using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AntiDupl.NET.Core;
using AntiDupl.NET.Core.Original;

namespace AntiDupl.NET.WinForms
{
    /// <summary>
    /// Tracks which side of each result pair is targeted for action.
    /// Used by Auto-Select to mark first/second for deletion or move.
    /// </summary>
    public static class AutoSelector
    {
        // Side cache: normalized path pair → targeted image path (or null for DontCare)
        // P2-12: written by batch threads (Apply/BatchRunAtOnce), read by the UI
        // thread (GetTargetPath/GetTargetIndex during painting) — a plain
        // Dictionary can corrupt or throw when a resize coincides with a read.
        private static System.Collections.Concurrent.ConcurrentDictionary<string, string> s_sideCache =
            new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

        public static IReadOnlyDictionary<string, string> SideCache => s_sideCache;

        /// <summary>
        /// Защищённые пути («Не удалять», этап 5 плана дубль-поиска). Заполняется UI из
        /// CoreOptions.protectedPath; нативный движок об этой категории не знает.
        /// </summary>
        public static CorePathWithSubFolder[] ProtectedPaths;

        /// <summary>
        /// Снимок разметки перед последним Apply — для пункта «Отменить разметку» (T4').
        /// </summary>
        private static Dictionary<string, string> s_lastSnapshot;

        public class BatchResult
        {
            public int Succeeded { get; set; }
            public int Failed { get; set; }
            public int SkippedProtected { get; set; }
            public List<string> FailedPaths { get; set; } = new List<string>();
        }

        /// <summary>
        /// Передать в AutoSelector актуальный список защищённых путей (managed-категория
        /// «Не удалять» из CoreOptions) перед разметкой и пакетными операциями.
        /// </summary>
        public static void SyncProtectedPaths(CoreOptions options)
        {
            ProtectedPaths = options != null ? options.protectedPath : null;
        }

        /// <summary>
        /// Файл лежит под защищённым путём: совпадение по полному пути, а для записей
        /// с включённой вложенностью — по префиксу с границей разделителя (как в GetPool).
        /// </summary>
        public static bool IsProtected(string path)
        {
            CorePathWithSubFolder[] paths = ProtectedPaths;
            if (paths == null || string.IsNullOrEmpty(path)) return false;

            for (int i = 0; i < paths.Length; i++)
            {
                if (paths[i] == null || string.IsNullOrEmpty(paths[i].path)) continue;
                if (string.Equals(path, paths[i].path, StringComparison.OrdinalIgnoreCase)) return true;
                if (!paths[i].enableSubFolder) continue;

                string dir = paths[i].path;
                if (path.Length > dir.Length &&
                    path.StartsWith(dir, StringComparison.OrdinalIgnoreCase) &&
                    (path[dir.Length] == System.IO.Path.DirectorySeparatorChar ||
                     path[dir.Length] == System.IO.Path.AltDirectorySeparatorChar))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Активный фильтр результатов (этап 6; null или неактивный — фильтра нет).
        /// Скрытые фильтром пары не размечаются, не инвертируются и не обрабатываются batch.
        /// </summary>
        public static ResultFilter Filter;

        /// <summary>Передать в AutoSelector текущий фильтр результатов (этап 6).</summary>
        public static void SyncFilter(Options options)
        {
            Filter = options != null ? options.resultFilter : null;
        }

        /// <summary>Есть ли пометка у пары с таким ключом (для фильтра и счётчиков).</summary>
        public static bool IsMarkedKey(string key)
        {
            return !string.IsNullOrEmpty(key) && s_sideCache.ContainsKey(key);
        }

        /// <summary>Видима ли пара при текущем фильтре (для разметки и batch).</summary>
        public static bool IsVisibleKey(string key)
        {
            return Filter == null || !Filter.IsActive || Filter.MatchesKey(key);
        }

        /// <summary>
        /// Помечен ли файл как цель хотя бы одной видимой пары (чекбокс группового вида).
        /// </summary>
        public static bool IsMarkedPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            foreach (var kv in s_sideCache)
            {
                if (string.Equals(kv.Value, path, StringComparison.OrdinalIgnoreCase) && IsVisibleKey(kv.Key))
                    return true;
            }
            return false;
        }

        private static bool IsVisible(CoreResult r)
        {
            return Filter == null || !Filter.IsActive || Filter.Matches(r);
        }

        /// <summary>Только видимые результаты — инвариант «оставить файл» считается по ним.</summary>
        private static CoreResult[] VisibleResults(CoreResult[] results)
        {
            if (Filter == null || !Filter.IsActive) return results;
            var visible = new List<CoreResult>(results.Length);
            for (int i = 0; i < results.Length; i++)
                if (results[i] != null && IsVisible(results[i]))
                    visible.Add(results[i]);
            return visible.ToArray();
        }

        /// <summary>
        /// Generate a stable cache key from a result pair (order-independent).
        /// </summary>
        public static string GetKey(CoreResult r)
        {
            string p1 = r.first?.path ?? "";
            string p2 = r.second?.path ?? "";
            return string.Compare(p1, p2, StringComparison.OrdinalIgnoreCase) <= 0
                ? p1 + "|" + p2
                : p2 + "|" + p1;
        }

        /// <summary>
        /// Get the targeted image path for a result, or null if not targeted.
        /// </summary>
        public static string GetTargetPath(CoreResult r)
        {
            string path;
            s_sideCache.TryGetValue(GetKey(r), out path);
            return path;
        }

        // P2-8: the cache value is always the targeted path itself, and the cache is
        // pruned against the live result list (ResultsListView.GetResults), so the
        // counters/long-path checks below are pure cache computations — no full
        // GetResult marshalling per menu open anymore.

        /// <summary>
        /// Remove cache entries whose key does not match any current result pair.
        /// Called after the result list has been (re)loaded, so counters and batch
        /// execution never see markings from a previous search or from pairs that
        /// were removed from the results by single-row actions.
        /// </summary>
        public static void PruneStaleEntries(CoreResult[] results)
        {
            if (s_sideCache.IsEmpty) return;
            if (results == null || results.Length == 0) { s_sideCache.Clear(); return; }

            var liveKeys = new HashSet<string>(results.Length, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < results.Length; i++)
            {
                var r = results[i];
                if (r == null || r.type != CoreDll.ResultType.DuplImagePair) continue;
                liveKeys.Add(GetKey(r));
            }

            foreach (var key in s_sideCache.Keys)
            {
                if (!liveKeys.Contains(key))
                    s_sideCache.TryRemove(key, out _);
            }
        }


        /// <summary>
        /// Get which position (0=first, 1=second, -1=none) the targeted image is in.
        /// </summary>
        public static int GetTargetIndex(CoreResult r)
        {
            string targetPath = GetTargetPath(r);
            if (targetPath == null) return -1;
            if (string.Equals(targetPath, r.first?.path, StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(targetPath, r.second?.path, StringComparison.OrdinalIgnoreCase)) return 1;
            return -1;
        }

        /// <summary>
        /// Apply auto-select criteria to all results.
        /// </summary>
        public static int Apply(CoreLib core, AutoSelectCriteria criteria)
        {
            if (!criteria.HasCriteria) return 0;

            var results = core.GetResult(0, 1000000);
            if (results == null || results.Length == 0) return 0;

            var poolMap = Forms.DatabaseManagerForm.GetPoolAssignments();
            // T4': снимок состояния до разметки — для «Отменить разметку».
            s_lastSnapshot = new Dictionary<string, string>(s_sideCache, StringComparer.OrdinalIgnoreCase);
            // Этап 6: при активном фильтре снимаются пометки только у видимых пар, скрытые
            // фильтром сохраняют свои пометки (и batch их не тронет).
            if (Filter == null || !Filter.IsActive)
            {
                s_sideCache.Clear();
            }
            else
            {
                foreach (var key in s_sideCache.Keys)
                    if (Filter.MatchesKey(key))
                        s_sideCache.TryRemove(key, out _);
            }
            int affected = 0;

            for (int i = 0; i < results.Length; i++)
            {
                var r = results[i];
                if (r.type != CoreDll.ResultType.DuplImagePair) continue;
                if (!IsVisible(r)) continue;

                AutoSelectSide side = DetermineSide(r, criteria, poolMap);

                if (side != AutoSelectSide.DontCare)
                {
                    string targetPath = (side == AutoSelectSide.First) ? r.first.path : r.second.path;
                    s_sideCache[GetKey(r)] = targetPath;
                    affected++;
                }
            }

            // T2/T3: инвариант «в каждой группе остаётся файл» — поверх попарной разметки.
            // Этап 6: считается по видимым парам (скрытые фильтром не размечаются).
            EnforceKeepOne(VisibleResults(results), criteria, poolMap, s_sideCache);

            return affected;
        }

        /// <summary>
        /// «Отменить разметку» (T4'): восстанавливает снимок, сделанный перед последним Apply.
        /// Возвращает число восстановленных пометок (0, если снимка нет).
        /// </summary>
        public static int UndoMarking()
        {
            Dictionary<string, string> snapshot = s_lastSnapshot;
            if (snapshot == null) return 0;
            s_lastSnapshot = null;

            var restored = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
            foreach (var kv in snapshot)
                restored[kv.Key] = kv.Value;
            s_sideCache = restored;
            return restored.Count;
        }

        /// <summary>
        /// «Оставить одну» (T1'): в группе помечаются к удалению все файлы, кроме keepPath.
        /// Нативный SelectionType.SelectAllButThis сам файлы не удаляет (batch читает пометки
        /// результатов, а не флаги группы), поэтому разметка строится обходом графа группы от
        /// оставляемого файла: каждому незакрытому файлу достаётся своя родительская пара —
        /// у каждой пары ровно одна цель, и все файлы группы, кроме оставляемого, покрыты.
        /// Возвращает число помеченных пар.
        /// </summary>
        public static int KeepOnlyOne(CoreLib core, CoreImageInfo[] groupImages, string keepPath)
        {
            if (core == null || groupImages == null || string.IsNullOrEmpty(keepPath)) return 0;

            var groupPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < groupImages.Length; i++)
                if (groupImages[i] != null && !string.IsNullOrEmpty(groupImages[i].path))
                    groupPaths.Add(groupImages[i].path);
            if (!groupPaths.Contains(keepPath)) return 0;

            var results = core.GetResult(0, 1000000);
            if (results == null || results.Length == 0) return 0;

            // Смежность по парам, обе стороны которых лежат в группе.
            var adjacent = new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < results.Length; i++)
            {
                var r = results[i];
                if (r == null || r.type != CoreDll.ResultType.DuplImagePair) continue;
                if (r.first == null || r.second == null) continue;
                if (!groupPaths.Contains(r.first.path) || !groupPaths.Contains(r.second.path)) continue;

                string key = GetKey(r);
                // Этап 6: скрытые фильтром пары не входят в граф группы — иначе T1' поставил бы
                // пометку, которую batch не станет выполнять.
                if (!IsVisibleKey(key)) continue;
                List<KeyValuePair<string, string>> list;
                if (!adjacent.TryGetValue(r.first.path, out list)) { list = new List<KeyValuePair<string, string>>(); adjacent[r.first.path] = list; }
                list.Add(new KeyValuePair<string, string>(key, r.second.path));
                if (!adjacent.TryGetValue(r.second.path, out list)) { list = new List<KeyValuePair<string, string>>(); adjacent[r.second.path] = list; }
                list.Add(new KeyValuePair<string, string>(key, r.first.path));
            }

            // Старые пометки пар группы снимаются: BFS проставит их заново там, где нужно.
            foreach (var kv in adjacent)
                foreach (var edge in kv.Value)
                    s_sideCache.TryRemove(edge.Key, out _);

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { keepPath };
            var queue = new Queue<string>();
            queue.Enqueue(keepPath);
            int marked = 0;

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                List<KeyValuePair<string, string>> list;
                if (!adjacent.TryGetValue(current, out list)) continue;

                for (int i = 0; i < list.Count; i++)
                {
                    string other = list[i].Value;
                    if (!visited.Add(other)) continue;
                    s_sideCache[list[i].Key] = other;
                    queue.Enqueue(other);
                    marked++;
                }
            }

            return marked;
        }

        /// <summary>
        /// Отметить/снять файл во всех видимых парах его группы (чекбокс группового вида).
        /// Цель пары — один файл, поэтому отметка ставит файл целью всех его пар, а снятие
        /// убирает только пары, целью которых был именно он.
        /// </summary>
        public static int SetPathMarked(CoreResult[] results, CoreGroup group, string path, bool marked)
        {
            if (results == null || group == null || group.images == null || string.IsNullOrEmpty(path))
                return 0;

            var groupPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < group.images.Length; i++)
                if (group.images[i] != null && !string.IsNullOrEmpty(group.images[i].path))
                    groupPaths.Add(group.images[i].path);
            if (!groupPaths.Contains(path)) return 0;

            int changed = 0;
            for (int i = 0; i < results.Length; i++)
            {
                var r = results[i];
                if (r == null || r.type != CoreDll.ResultType.DuplImagePair) continue;
                if (r.first == null || r.second == null) continue;
                if (!groupPaths.Contains(r.first.path) || !groupPaths.Contains(r.second.path)) continue;

                // Цель пары может быть только одним из её файлов: иначе пометка файла A
                // задела бы пары B-C, которые удаление A не разрешает.
                bool hasPath = string.Equals(r.first.path, path, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(r.second.path, path, StringComparison.OrdinalIgnoreCase);
                if (!hasPath) continue;

                string key = GetKey(r);
                if (marked)
                {
                    // Скрытые фильтром пары batch не обрабатывает — не помечаем и их.
                    if (!IsVisibleKey(key)) continue;
                    s_sideCache[key] = path;
                    changed++;
                }
                else
                {
                    string value;
                    if (s_sideCache.TryGetValue(key, out value) &&
                        string.Equals(value, path, StringComparison.OrdinalIgnoreCase))
                    {
                        s_sideCache.TryRemove(key, out _);
                        changed++;
                    }
                }
            }
            return changed;
        }

        /// <summary>
        /// Число помеченных файлов, лежащих в защищённых путях (для сообщения до batch).
        /// </summary>
        public static int CountMarkedProtected(CoreLib core)
        {
            int count = 0;
            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in s_sideCache)
            {
                if (!IsVisibleKey(kv.Key)) continue;
                if (kv.Value != null && processed.Add(kv.Value) && IsProtected(kv.Value)) count++;
            }
            return count;
        }

        /// <summary>
        /// Set the targeted side for a result (manual toggle).
        /// </summary>
        public static void SetSide(CoreResult r, AutoSelectSide side)
        {
            if (side == AutoSelectSide.DontCare)
                s_sideCache.TryRemove(GetKey(r), out _);
            else
                s_sideCache[GetKey(r)] = (side == AutoSelectSide.First) ? r.first.path : r.second.path;
        }

        /// <summary>
        /// Clear the targeted side for a result.
        /// </summary>
        public static void ClearSide(CoreResult r)
        {
            s_sideCache.TryRemove(GetKey(r), out _);
        }

        /// <summary>
        /// Invert side cache: swap targeted image to the other one in each pair.
        /// P2-8: works purely on cache keys — the key is the order-independent
        /// "p1|p2" pair, and '|' cannot occur in a Windows path, so the targeted
        /// side is always the *other* component of the same key. No GetResult.
        /// </summary>
        public static int InvertSides(CoreLib core)
        {
            int inverted = 0;
            var newCache = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

            foreach (var kv in s_sideCache)
            {
                string key = kv.Key;
                // Этап 6: скрытые фильтром пометки переносятся как есть — инверсия их не трогает.
                if (!IsVisibleKey(key)) { newCache[key] = kv.Value; continue; }
                int sep = key.IndexOf('|');
                if (sep < 0 || sep == key.Length - 1) continue; // malformed key — drop
                string p1 = key.Substring(0, sep);
                string p2 = key.Substring(sep + 1);

                string newPath =
                    string.Equals(kv.Value, p1, StringComparison.OrdinalIgnoreCase) ? p2 :
                    string.Equals(kv.Value, p2, StringComparison.OrdinalIgnoreCase) ? p1 :
                    null; // value no longer matches either side — drop the marking
                if (newPath != null)
                {
                    newCache[key] = newPath;
                    inverted++;
                }
            }

            s_sideCache = newCache;
            return inverted;
        }

        /// <summary>
        /// Clear all side markings (Deselect All).
        /// </summary>
        public static int ClearAll(CoreLib core)
        {
            int count = s_sideCache.Count;
            s_sideCache.Clear();
            return count;
        }

        /// <summary>
        /// <summary>
        /// Number of results currently marked for action.
        /// P2-8: cache values ARE the targeted paths, and the cache is pruned
        /// against the live result list — count distinct values, no GetResult.
        /// </summary>
        public static int CountMarked(CoreLib core)
        {
            int count = 0;
            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in s_sideCache)
            {
                if (!IsVisibleKey(kv.Key)) continue;
                if (kv.Value != null && processed.Add(kv.Value)) count++;
            }
            return count;
        }

        /// <summary>
        /// True if any marked target has a path longer than MAX_PATH (260).
        /// P2-8: pure cache computation — values are the target paths.
        /// </summary>
        public static bool HasLongPaths(CoreLib core)
        {
            const int MAX_PATH = 260;
            foreach (var kv in s_sideCache)
            {
                if (!IsVisibleKey(kv.Key)) continue;
                if (kv.Value != null && kv.Value.Length > MAX_PATH) return true;
            }
            return false;
        }

        /// Execute actions on all marked results (delete or move the targeted side).
        /// For delete: removes the targeted image.
        /// For move: moves the targeted image to the specified folder.
        /// </summary>
        public static BatchResult ExecuteBatch(CoreLib core, bool delete, string targetFolder = null, bool markProcessed = false)
        {
            var results = core.GetResult(0, 1000000);
            if (results == null || results.Length == 0)
                return new BatchResult();

            int skippedProtected = 0;
            var targetByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var protectedSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < results.Length; i++)
            {
                var r = results[i];
                if (r.type != CoreDll.ResultType.DuplImagePair) continue;
                string key = GetKey(r);
                // Этап 6: скрытые фильтром пары не обрабатываются, их пометки сохраняются.
                if (!IsVisibleKey(key)) continue;
                string targetPath;
                if (!s_sideCache.TryGetValue(key, out targetPath) || string.IsNullOrEmpty(targetPath)) continue;
                // Ручные действия защитой не блокируются, пакетные — блокируются (этап 5).
                if (IsProtected(targetPath)) { if (protectedSeen.Add(targetPath)) skippedProtected++; continue; }
                targetByKey[key] = targetPath;
                targets.Add(targetPath);
            }

            if (delete)
            {
                ApplyBySelection(core, targets, true, CoreDll.LocalActionType.DeleteFirst);
                ApplyBySelection(core, targets, false, CoreDll.LocalActionType.DeleteSecond);
            }
            else if (!string.IsNullOrEmpty(targetFolder))
            {
                MoveTargets(targets, targetFolder);
                ApplyBySelection(core, targets, true, CoreDll.LocalActionType.MarkRemovedFirst);
                ApplyBySelection(core, targets, false, CoreDll.LocalActionType.MarkRemovedSecond);
            }

            // Учёт по файловой системе: файла нет — операция удалась; иначе — неудача (S13:
            // пометки неудачных пар остаются для повторной попытки).
            int succeeded = 0, failed = 0;
            var failedPaths = new List<string>();
            foreach (string targetPath in targets)
            {
                if (!System.IO.File.Exists(targetPath)) succeeded++;
                else { failed++; failedPaths.Add(targetPath); }
            }
            var succeededKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in targetByKey)
                if (!System.IO.File.Exists(kv.Value))
                    succeededKeys.Add(kv.Key);

            // Remove only the keys that were successfully acted upon, so that a partial
            // failure keeps the marking for the remaining rows (S13).
            foreach (var key in succeededKeys)
                s_sideCache.TryRemove(key, out _);

            // Этап 6: авто-отметка «разобрано» после успешных операций (опция, по умолчанию выкл.).
            if (markProcessed && succeededKeys.Count > 0)
                ProcessedPairs.Mark(succeededKeys);

            if (succeeded > 0)
            {
                core.Clear(CoreDll.FileType.Temporary);
                core.CheckImageData();
            }

            return new BatchResult { Succeeded = succeeded, Failed = failed, FailedPaths = failedPaths, SkippedProtected = skippedProtected };
        }

        /// <summary>
        /// Применить действие к результатам, у которых нужная сторона — из targets, через выборку ядра
        /// (TargetType.Selected): движок сам перебирает результаты, поэтому сдвиг индексов после
        /// удаления/пометки (RemoveDeleted, adUndoRedoEngine.cpp:110) безопасен. Вызовы по индексу
        /// (SetCurrent + TargetType.Current) после первой же операции бьют по чужой паре — G6.2.
        /// </summary>
        private static void ApplyBySelection(CoreLib core, HashSet<string> targets, bool firstSide, CoreDll.LocalActionType action)
        {
            if (targets.Count == 0) return;
            CoreResult[] live = core.GetResult(0, core.GetResultSize());
            if (live == null || live.Length == 0) return;

            core.SetSelection(0, (uint)live.Length, false);
            int selected = 0;
            for (int i = 0; i < live.Length; i++)
            {
                if (live[i].type != CoreDll.ResultType.DuplImagePair) continue;
                string path = firstSide ? live[i].first?.path : live[i].second?.path;
                if (path == null || !targets.Contains(path)) continue;
                core.SetSelection((uint)i, 1, true);
                selected++;
            }
            if (selected == 0) return;
            core.ApplyToResult(action, CoreDll.TargetType.Selected);
        }

        /// <summary>Перемещает целевые файлы в папку (имена не перетираются) — по путям, без индексов ядра.</summary>
        private static void MoveTargets(HashSet<string> targets, string targetFolder)
        {
            foreach (string sourcePath in targets)
            {
                if (!System.IO.File.Exists(sourcePath)) continue; // файла уже нет — не ошибка
                try
                {
                    System.IO.File.Move(sourcePath, UniqueDestinationPath(targetFolder, sourcePath));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static string UniqueDestinationPath(string folder, string sourcePath)
        {
            string destPath = System.IO.Path.Combine(folder, System.IO.Path.GetFileName(sourcePath));
            if (!System.IO.File.Exists(destPath)) return destPath;

            string name = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
            string ext = System.IO.Path.GetExtension(sourcePath);
            int counter = 1;
            while (System.IO.File.Exists(destPath))
            {
                destPath = System.IO.Path.Combine(folder, $"{name}_{counter}{ext}");
                counter++;
            }
            return destPath;
        }

        /// <summary>
        /// Determine which side of the pair to target based on criteria.
        /// Quality criteria (Resolution, Size, Quality) form a priority cascade:
        /// the first criterion that decides (is not a tie) wins; ties fall through.
        /// When every active quality criterion ties, the pair is treated as "don't care"
        /// and the first image is selected so that the user is never left choosing manually.
        /// Time and Pool are standalone modes, not part of the quality cascade:
        ///   - Time: older/newer; a tie selects the first image.
        ///   - Pool: selects the image from the requested pool; if neither matches,
        ///     nothing is selected (DontCare).
        /// </summary>
        public static AutoSelectSide DetermineSide(CoreResult r, AutoSelectCriteria criteria, Dictionary<string, int> poolMap)
        {
            AutoSelectSide side = DetermineSideRaw(r, criteria, poolMap);
            if (side == AutoSelectSide.DontCare) return side;

            // Защищённый файл никогда не выбирается целевым; если защищены обе стороны —
            // пара не помечается.
            if (!IsProtected(PathOf(r, side))) return side;
            AutoSelectSide other = Opposite(side);
            return IsProtected(PathOf(r, other)) ? AutoSelectSide.DontCare : other;
        }

        private static string PathOf(CoreResult r, AutoSelectSide side)
        {
            return (side == AutoSelectSide.First) ? r.first?.path : r.second?.path;
        }

        /// <summary>
        /// Инвариант «в каждой группе остаётся хотя бы один файл» (T2/T3, план 8.1.1).
        /// Строит компоненты графа дублей по парам результатов; если в компоненте помечены все
        /// файлы — снимает пометку с «лучшего» по тому же каскаду критериев, что и DetermineSide
        /// (полное равенство решается тай-брейком по пути). Возвращает число снятых пометок.
        /// </summary>
        public static int EnforceKeepOne(CoreResult[] results, AutoSelectCriteria criteria,
            Dictionary<string, int> poolMap, IDictionary<string, string> sideCache)
        {
            if (results == null || results.Length == 0 || sideCache == null || sideCache.Count == 0)
                return 0;

            var nodeIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var parent = new List<int>();
            var images = new List<CoreImageInfo>();

            int GetNode(CoreImageInfo image)
            {
                if (image == null || string.IsNullOrEmpty(image.path)) return -1;
                int id;
                if (nodeIndex.TryGetValue(image.path, out id)) return id;
                id = parent.Count;
                nodeIndex[image.path] = id;
                parent.Add(id);
                images.Add(image);
                return id;
            }

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            for (int i = 0; i < results.Length; i++)
            {
                CoreResult r = results[i];
                if (r == null || r.type != CoreDll.ResultType.DuplImagePair) continue;
                int a = GetNode(r.first);
                int b = GetNode(r.second);
                if (a < 0 || b < 0) continue;
                a = Find(a);
                b = Find(b);
                if (a != b) parent[b] = a;
            }
            if (parent.Count == 0) return 0;

            // path → ключи кэша, в которых этот путь является целью.
            var markedKeys = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in sideCache)
            {
                if (kv.Value == null) continue;
                List<string> keys;
                if (!markedKeys.TryGetValue(kv.Value, out keys))
                {
                    keys = new List<string>();
                    markedKeys[kv.Value] = keys;
                }
                keys.Add(kv.Key);
            }
            if (markedKeys.Count == 0) return 0;

            var components = new Dictionary<int, List<int>>();
            for (int i = 0; i < parent.Count; i++)
            {
                int root = Find(i);
                List<int> members;
                if (!components.TryGetValue(root, out members))
                {
                    members = new List<int>();
                    components[root] = members;
                }
                members.Add(i);
            }

            int unmarked = 0;
            foreach (var members in components.Values)
            {
                if (members.Count < 2) continue;

                bool allMarked = true;
                for (int i = 0; i < members.Count; i++)
                {
                    if (!markedKeys.ContainsKey(images[members[i]].path))
                    {
                        allMarked = false;
                        break;
                    }
                }
                if (!allMarked) continue;

                CoreImageInfo keep = images[members[0]];
                for (int i = 1; i < members.Count; i++)
                {
                    if (BetterForKeep(images[members[i]], keep, criteria, poolMap))
                        keep = images[members[i]];
                }

                List<string> keys;
                if (!markedKeys.TryGetValue(keep.path, out keys)) continue;
                for (int i = 0; i < keys.Count; i++)
                {
                    string target;
                    if (sideCache.TryGetValue(keys[i], out target) &&
                        string.Equals(target, keep.path, StringComparison.OrdinalIgnoreCase))
                        sideCache.Remove(keys[i]);
                }
                markedKeys.Remove(keep.path);
                unmarked++;
            }

            return unmarked;
        }

        /// <summary>
        /// Какой из двух файлов лучше оставить: зеркало каскада DetermineSide (критерий,
        /// помечающий файл к удалению, для «оставить» означает обратную сторону).
        /// </summary>
        private static bool BetterForKeep(CoreImageInfo a, CoreImageInfo b, AutoSelectCriteria criteria,
            Dictionary<string, int> poolMap)
        {
            int cmp = 0;

            if (criteria.ResolutionSide != AutoSelectSide.DontCare)
            {
                cmp = Math.Sign((long)a.width * a.height - (long)b.width * b.height);
                if (criteria.ResolutionSide != AutoSelectSide.First) cmp = -cmp;
            }
            if (cmp == 0 && criteria.SizeSide != AutoSelectSide.DontCare)
            {
                cmp = a.size.CompareTo(b.size);
                if (criteria.SizeSide != AutoSelectSide.First) cmp = -cmp;
            }
            if (cmp == 0 && criteria.QualitySide != AutoSelectSide.DontCare &&
                a.blockiness >= 0 && a.blurring >= 0 && b.blockiness >= 0 && b.blurring >= 0)
            {
                cmp = (a.blockiness + a.blurring).CompareTo(b.blockiness + b.blurring);
                if (criteria.QualitySide == AutoSelectSide.First) cmp = -cmp;
            }
            if (cmp == 0 && criteria.TimeSide != AutoSelectSide.DontCare)
            {
                cmp = a.time.CompareTo(b.time);
                if (criteria.TimeSide == AutoSelectSide.Second) cmp = -cmp;
            }
            if (cmp == 0 && criteria.PoolSide != AutoSelectSide.DontCare && poolMap != null)
            {
                int want = criteria.PoolSide == AutoSelectSide.First ? 1 : 2;
                bool aInPool = GetPool(a, poolMap) == want;
                bool bInPool = GetPool(b, poolMap) == want;
                if (aInPool != bInPool) cmp = aInPool ? -1 : 1;
            }

            if (cmp != 0) return cmp > 0;
            return string.Compare(a.path, b.path, StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static AutoSelectSide DetermineSideRaw(CoreResult r, AutoSelectCriteria criteria, Dictionary<string, int> poolMap)
        {
            bool hasQualityCriterion =
                criteria.ResolutionSide != AutoSelectSide.DontCare ||
                criteria.SizeSide != AutoSelectSide.DontCare ||
                criteria.QualitySide != AutoSelectSide.DontCare;

            if (hasQualityCriterion)
            {
                // Quality cascade: Resolution → Size → Quality.
                AutoSelectSide side;

                if (criteria.ResolutionSide != AutoSelectSide.DontCare)
                {
                    side = LowerResolutionSide(r);
                    if (side != AutoSelectSide.DontCare)
                        return (criteria.ResolutionSide == AutoSelectSide.First) ? side : Opposite(side);
                }

                if (criteria.SizeSide != AutoSelectSide.DontCare)
                {
                    side = SmallerSizeSide(r);
                    if (side != AutoSelectSide.DontCare)
                        return (criteria.SizeSide == AutoSelectSide.First) ? side : Opposite(side);
                }

                if (criteria.QualitySide != AutoSelectSide.DontCare)
                {
                    side = WorseQualitySide(r);
                    if (side != AutoSelectSide.DontCare)
                        return (criteria.QualitySide == AutoSelectSide.First) ? side : Opposite(side);
                }

                // All active quality criteria tied → "don't care" → select first.
                return AutoSelectSide.First;
            }

            // Standalone modes: Time and Pool (not part of the quality cascade).
            if (criteria.TimeSide != AutoSelectSide.DontCare)
            {
                AutoSelectSide side = OlderSide(r);
                if (side != AutoSelectSide.DontCare)
                    return (criteria.TimeSide == AutoSelectSide.First) ? side : Opposite(side);
            }

            if (criteria.PoolSide != AutoSelectSide.DontCare)
            {
                AutoSelectSide side = PoolMatchSide(r, criteria.PoolSide, poolMap);
                if (side != AutoSelectSide.DontCare)
                    return side;
                // Requested pool matched by neither image → nothing to select.
                return AutoSelectSide.DontCare;
            }

            // Time tied or no criterion active → select first.
            return AutoSelectSide.First;
        }

        private static AutoSelectSide Opposite(AutoSelectSide side)
        {
            return (side == AutoSelectSide.First) ? AutoSelectSide.Second : AutoSelectSide.First;
        }

        private static AutoSelectSide OlderSide(CoreResult r)
        {
            if (r.first.time < r.second.time) return AutoSelectSide.First;
            if (r.second.time < r.first.time) return AutoSelectSide.Second;
            return AutoSelectSide.DontCare;
        }

        private static AutoSelectSide SmallerSizeSide(CoreResult r)
        {
            if (r.first.size < r.second.size) return AutoSelectSide.First;
            if (r.second.size < r.first.size) return AutoSelectSide.Second;
            return AutoSelectSide.DontCare;
        }

        private static AutoSelectSide WorseQualitySide(CoreResult r)
        {
            if (r.first.blockiness < 0 || r.first.blurring < 0 ||
                r.second.blockiness < 0 || r.second.blurring < 0)
                return AutoSelectSide.DontCare;

            double q1 = r.first.blockiness + r.first.blurring;
            double q2 = r.second.blockiness + r.second.blurring;
            if (q1 > q2) return AutoSelectSide.First;
            if (q2 > q1) return AutoSelectSide.Second;
            return AutoSelectSide.DontCare;
        }

        private static AutoSelectSide LowerResolutionSide(CoreResult r)
        {
            long res1 = (long)r.first.width * r.first.height;
            long res2 = (long)r.second.width * r.second.height;
            if (res1 < res2) return AutoSelectSide.First;
            if (res2 < res1) return AutoSelectSide.Second;
            return AutoSelectSide.DontCare;
        }

        private static AutoSelectSide PoolMatchSide(CoreResult r, AutoSelectSide requestedPool, Dictionary<string, int> poolMap)
        {
            int pool1 = GetPool(r.first, poolMap);
            int pool2 = GetPool(r.second, poolMap);

            if (requestedPool == AutoSelectSide.First)
            {
                if (pool1 == 1) return AutoSelectSide.First;
                if (pool2 == 1) return AutoSelectSide.Second;
            }
            else
            {
                if (pool1 == 2) return AutoSelectSide.First;
                if (pool2 == 2) return AutoSelectSide.Second;
            }
            return AutoSelectSide.DontCare;
        }

        private static int GetPool(CoreImageInfo image, Dictionary<string, int> poolMap)
        {
            if (image == null || string.IsNullOrEmpty(image.path)) return 0;

            string imgPath = image.path;
            int bestPool = 0;
            int bestLen = 0;

            foreach (var kv in poolMap)
            {
                string dbPath = kv.Key;
                if (dbPath.Length > bestLen &&
                    imgPath.StartsWith(dbPath, StringComparison.OrdinalIgnoreCase) &&
                    (imgPath.Length == dbPath.Length || imgPath[dbPath.Length] == System.IO.Path.DirectorySeparatorChar ||
                     imgPath[dbPath.Length] == System.IO.Path.AltDirectorySeparatorChar))
                {
                    bestPool = kv.Value;
                    bestLen = dbPath.Length;
                }
            }

            return bestPool;
        }
    }
}
