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
        private static Dictionary<string, string> s_sideCache = new Dictionary<string, string>();

        public static IReadOnlyDictionary<string, string> SideCache => s_sideCache;

        public class BatchResult
        {
            public int Succeeded { get; set; }
            public int Failed { get; set; }
            public List<string> FailedPaths { get; set; } = new List<string>();
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
            s_sideCache.Clear();
            int affected = 0;

            for (int i = 0; i < results.Length; i++)
            {
                var r = results[i];
                if (r.type != CoreDll.ResultType.DuplImagePair) continue;

                AutoSelectSide side = DetermineSide(r, criteria, poolMap);

                if (side != AutoSelectSide.DontCare)
                {
                    string targetPath = (side == AutoSelectSide.First) ? r.first.path : r.second.path;
                    s_sideCache[GetKey(r)] = targetPath;
                    affected++;
                }
            }

            return affected;
        }

        /// <summary>
        /// Set the targeted side for a result (manual toggle).
        /// </summary>
        public static void SetSide(CoreResult r, AutoSelectSide side)
        {
            if (side == AutoSelectSide.DontCare)
                s_sideCache.Remove(GetKey(r));
            else
                s_sideCache[GetKey(r)] = (side == AutoSelectSide.First) ? r.first.path : r.second.path;
        }

        /// <summary>
        /// Clear the targeted side for a result.
        /// </summary>
        public static void ClearSide(CoreResult r)
        {
            s_sideCache.Remove(GetKey(r));
        }

        /// <summary>
        /// Invert side cache: swap targeted image to the other one in each pair.
        /// </summary>
        public static int InvertSides(CoreLib core)
        {
            var results = core.GetResult(0, 1000000);
            if (results == null || results.Length == 0) return 0;

            var newCache = new Dictionary<string, string>();
            int inverted = 0;

            for (int i = 0; i < results.Length; i++)
            {
                var r = results[i];
                string key = GetKey(r);
                string oldPath;
                if (s_sideCache.TryGetValue(key, out oldPath))
                {
                    // Swap: if was targeting first, now target second, and vice versa
                    string newPath = string.Equals(oldPath, r.first?.path, StringComparison.OrdinalIgnoreCase)
                        ? r.second.path
                        : r.first.path;
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
        /// Execute actions on all marked results (delete or move the targeted side).
        /// For delete: removes the targeted image.
        /// For move: moves the targeted image to the specified folder.
        /// </summary>
        public static BatchResult ExecuteBatch(CoreLib core, bool delete, string targetFolder = null)
        {
            var results = core.GetResult(0, 1000000);
            if (results == null || results.Length == 0)
                return new BatchResult();

            int succeeded = 0, failed = 0;
            var failedPaths = new List<string>();
            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = results.Length - 1; i >= 0; i--)
            {
                var r = results[i];
                string key = GetKey(r);
                string targetPath;
                if (!s_sideCache.TryGetValue(key, out targetPath)) continue;
                if (r.type != CoreDll.ResultType.DuplImagePair) continue;
                if (!processed.Add(targetPath)) continue;

                core.SetCurrent(i);
                bool ok = false;

                if (delete)
                {
                    if (string.Equals(targetPath, r.first?.path, StringComparison.OrdinalIgnoreCase))
                        ok = core.ApplyToResult(CoreDll.LocalActionType.DeleteFirst, CoreDll.TargetType.Current);
                    else
                        ok = core.ApplyToResult(CoreDll.LocalActionType.DeleteSecond, CoreDll.TargetType.Current);
                }
                else if (!string.IsNullOrEmpty(targetFolder))
                {
                    string sourcePath = targetPath;
                    if (!string.IsNullOrEmpty(sourcePath) && System.IO.File.Exists(sourcePath))
                    {
                        try
                        {
                            string destPath = System.IO.Path.Combine(targetFolder, System.IO.Path.GetFileName(sourcePath));
                            if (System.IO.File.Exists(destPath))
                            {
                                string name = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
                                string ext = System.IO.Path.GetExtension(sourcePath);
                                int counter = 1;
                                while (System.IO.File.Exists(destPath))
                                {
                                    destPath = System.IO.Path.Combine(targetFolder, $"{name}_{counter}{ext}");
                                    counter++;
                                }
                            }
                            System.IO.File.Move(sourcePath, destPath);
                            ok = true;
                        }
                        catch (IOException) { failedPaths.Add(sourcePath); }
                        catch (UnauthorizedAccessException) { failedPaths.Add(sourcePath); }
                    }
                }

                if (ok) succeeded++; else failed++;
            }

            s_sideCache.Clear();

            if (succeeded > 0)
            {
                core.Clear(CoreDll.FileType.Temporary);
                core.CheckImageData();
            }

            return new BatchResult { Succeeded = succeeded, Failed = failed, FailedPaths = failedPaths };
        }

        /// <summary>
        /// Determine which side of the pair to target based on criteria.
        /// AND logic: ALL active criteria must agree on the same side.
        /// If any active criterion says DontCare or conflicts → result is DontCare.
        /// </summary>
        public static AutoSelectSide DetermineSide(CoreResult r, AutoSelectCriteria criteria, Dictionary<string, int> poolMap)
        {
            AutoSelectSide result = AutoSelectSide.DontCare;
            bool firstCriterion = true;

            // Time
            if (criteria.TimeSide != AutoSelectSide.DontCare)
            {
                AutoSelectSide side = AutoSelectSide.DontCare;
                if (r.first.time < r.second.time)
                    side = AutoSelectSide.First;
                else if (r.second.time < r.first.time)
                    side = AutoSelectSide.Second;

                if (criteria.TimeSide == AutoSelectSide.Second && side != AutoSelectSide.DontCare)
                    side = (side == AutoSelectSide.First) ? AutoSelectSide.Second : AutoSelectSide.First;

                if (firstCriterion) { result = side; firstCriterion = false; }
                else if (side != AutoSelectSide.DontCare && result != side) return AutoSelectSide.DontCare;
            }

            // Size
            if (criteria.SizeSide != AutoSelectSide.DontCare)
            {
                AutoSelectSide side = AutoSelectSide.DontCare;
                if (r.first.size < r.second.size)
                    side = AutoSelectSide.First;
                else if (r.second.size < r.first.size)
                    side = AutoSelectSide.Second;

                if (criteria.SizeSide == AutoSelectSide.Second && side != AutoSelectSide.DontCare)
                    side = (side == AutoSelectSide.First) ? AutoSelectSide.Second : AutoSelectSide.First;

                if (firstCriterion) { result = side; firstCriterion = false; }
                else if (side != AutoSelectSide.DontCare && result != side) return AutoSelectSide.DontCare;
            }

            // Quality
            if (criteria.QualitySide != AutoSelectSide.DontCare)
            {
                double q1 = r.first.blockiness + r.first.blurring;
                double q2 = r.second.blockiness + r.second.blurring;

                AutoSelectSide side = AutoSelectSide.DontCare;
                if (q1 > q2)
                    side = AutoSelectSide.First;
                else if (q2 > q1)
                    side = AutoSelectSide.Second;

                if (criteria.QualitySide == AutoSelectSide.Second && side != AutoSelectSide.DontCare)
                    side = (side == AutoSelectSide.First) ? AutoSelectSide.Second : AutoSelectSide.First;

                if (firstCriterion) { result = side; firstCriterion = false; }
                else if (side != AutoSelectSide.DontCare && result != side) return AutoSelectSide.DontCare;
            }

            // Resolution
            if (criteria.ResolutionSide != AutoSelectSide.DontCare)
            {
                long res1 = (long)r.first.width * r.first.height;
                long res2 = (long)r.second.width * r.second.height;

                AutoSelectSide side = AutoSelectSide.DontCare;
                if (res1 < res2)
                    side = AutoSelectSide.First;
                else if (res2 < res1)
                    side = AutoSelectSide.Second;

                if (criteria.ResolutionSide == AutoSelectSide.Second && side != AutoSelectSide.DontCare)
                    side = (side == AutoSelectSide.First) ? AutoSelectSide.Second : AutoSelectSide.First;

                if (firstCriterion) { result = side; firstCriterion = false; }
                else if (side != AutoSelectSide.DontCare && result != side) return AutoSelectSide.DontCare;
            }

            // Pool
            if (criteria.PoolSide != AutoSelectSide.DontCare)
            {
                int pool1 = GetPool(r.first, poolMap);
                int pool2 = GetPool(r.second, poolMap);

                AutoSelectSide side = AutoSelectSide.DontCare;
                if (criteria.PoolSide == AutoSelectSide.First)
                {
                    if (pool1 == 1) side = AutoSelectSide.First;
                    else if (pool2 == 1) side = AutoSelectSide.Second;
                }
                else if (criteria.PoolSide == AutoSelectSide.Second)
                {
                    if (pool1 == 2) side = AutoSelectSide.First;
                    else if (pool2 == 2) side = AutoSelectSide.Second;
                }

                if (firstCriterion) { result = side; firstCriterion = false; }
                else if (side != AutoSelectSide.DontCare && result != side) return AutoSelectSide.DontCare;
            }

            return result;
        }

        private static int GetPool(CoreImageInfo image, Dictionary<string, int> poolMap)
        {
            if (image == null || string.IsNullOrEmpty(image.path)) return 0;

            string imgPath = image.path.ToLowerInvariant();
            int bestPool = 0;
            int bestLen = 0;

            foreach (var kv in poolMap)
            {
                string dbPath = kv.Key.ToLowerInvariant();
                if (imgPath.StartsWith(dbPath) && dbPath.Length > bestLen)
                {
                    bestPool = kv.Value;
                    bestLen = dbPath.Length;
                }
            }

            return bestPool;
        }
    }
}
