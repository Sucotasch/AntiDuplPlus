using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AntiDupl.NET.WinForms
{
    /// <summary>
    /// Состояние «разобрано» (T5, этап 6 плана дубль-поиска): множество ключей пар
    /// (тот же ключ, что у AutoSelector.GetKey — "путь1|путь2" без учёта порядка и регистра)
    /// в файле Resources.UserPath\processed.tsv, по строке на пару, пути через табуляцию.
    /// Ленивая загрузка при первом обращении, запись при каждом изменении (атомарно).
    /// Ключи — пути, поэтому после переноса/переименования папок старые отметки не совпадут:
    /// лечится пунктом «Очистить разобранные».
    /// </summary>
    public static class ProcessedPairs
    {
        private static HashSet<string> s_keys;

        public static string FileName
        {
            get { return Path.Combine(Resources.UserPath, "processed.tsv"); }
        }

        private static HashSet<string> Keys
        {
            get
            {
                if (s_keys == null) Load();
                return s_keys;
            }
        }

        /// <summary>Прочитать файл отметок (вызывается лениво).</summary>
        public static void Load()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(FileName))
                {
                    foreach (string line in File.ReadAllLines(FileName))
                    {
                        int tab = line.IndexOf('\t');
                        if (tab > 0 && tab < line.Length - 1)
                            keys.Add(line.Substring(0, tab) + "|" + line.Substring(tab + 1));
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            s_keys = keys;
        }

        public static bool IsProcessed(string key)
        {
            return !string.IsNullOrEmpty(key) && Keys.Contains(key);
        }

        public static int Count
        {
            get { return Keys.Count; }
        }

        /// <summary>Число отметок среди заданных ключей (счётчик «разобрано» для статусной строки).</summary>
        public static int CountIn(IEnumerable<string> keys)
        {
            int count = 0;
            if (keys == null) return 0;
            foreach (string key in keys)
                if (IsProcessed(key)) count++;
            return count;
        }

        /// <summary>Отметить пары как разобранные; возвращает число новых отметок.</summary>
        public static int Mark(IEnumerable<string> keys)
        {
            int added = 0;
            foreach (string key in keys)
                if (!string.IsNullOrEmpty(key) && Keys.Add(key)) added++;
            if (added > 0) Save();
            return added;
        }

        /// <summary>Снять отметки; возвращает число снятых.</summary>
        public static int Unmark(IEnumerable<string> keys)
        {
            int removed = 0;
            foreach (string key in keys)
                if (!string.IsNullOrEmpty(key) && Keys.Remove(key)) removed++;
            if (removed > 0) Save();
            return removed;
        }

        /// <summary>Снять все отметки; возвращает их число.</summary>
        public static int ClearAll()
        {
            int count = Keys.Count;
            Keys.Clear();
            Save();
            return count;
        }

        /// <summary>Записать файл (через .tmp + rename, чтобы обрыв не оставил мусор).</summary>
        public static void Save()
        {
            string fileName = FileName;
            string tmpName = fileName + ".tmp";
            try
            {
                using (var writer = new StreamWriter(tmpName, false, new UTF8Encoding(false)))
                {
                    foreach (string key in Keys)
                    {
                        int sep = key.IndexOf('|');
                        if (sep <= 0 || sep >= key.Length - 1) continue;
                        writer.Write(key.Substring(0, sep));
                        writer.Write('\t');
                        writer.WriteLine(key.Substring(sep + 1));
                    }
                }
                File.Move(tmpName, fileName, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
