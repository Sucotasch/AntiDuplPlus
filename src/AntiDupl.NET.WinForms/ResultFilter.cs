using System;

using AntiDupl.NET.Core;

namespace AntiDupl.NET.WinForms
{
    /// <summary>
    /// Фильтр результатов (T1, этап 6 плана дубль-поиска). Работает по ключу пары
    /// (AutoSelector.GetKey), поэтому одинаково применим и к строкам списка, и к разметке
    /// с пакетными операциями: пары, скрытые фильтром, авто-выбор и batch не затрагивают.
    /// </summary>
    public class ResultFilter
    {
        /// <summary>Скрывать пары, отмеченные как разобранные (T5).</summary>
        public bool HideProcessed;

        /// <summary>Показывать только неразмеченные пары (без цели).</summary>
        public bool OnlyUnmarked;

        /// <summary>Подстрока пути любой из сторон; пусто — по тексту не фильтровать.</summary>
        public string Text = "";

        public bool IsActive
        {
            get { return HideProcessed || OnlyUnmarked || !string.IsNullOrEmpty(Text); }
        }

        public bool MatchesKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return true;

            if (HideProcessed && ProcessedPairs.IsProcessed(key)) return false;
            if (OnlyUnmarked && AutoSelector.IsMarkedKey(key)) return false;
            if (!string.IsNullOrEmpty(Text))
            {
                // '|' не может встречаться в пути Windows, поэтому разбор ключа безопасен.
                int sep = key.IndexOf('|');
                string first = sep >= 0 ? key.Substring(0, sep) : key;
                string second = sep >= 0 ? key.Substring(sep + 1) : "";
                if (first.IndexOf(Text, StringComparison.OrdinalIgnoreCase) < 0 &&
                    second.IndexOf(Text, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }
            return true;
        }

        public bool Matches(CoreResult r)
        {
            return r == null || MatchesKey(AutoSelector.GetKey(r));
        }

        public void Reset()
        {
            HideProcessed = false;
            OnlyUnmarked = false;
            Text = "";
        }
    }
}
