using System;
using System.Collections.Generic;
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
        // Side cache: result index → which side to act on
        private static Dictionary<int, AutoSelectSide> s_sideCache = new Dictionary<int, AutoSelectSide>();

        public static IReadOnlyDictionary<int, AutoSelectSide> SideCache => s_sideCache;

        /// <summary>
        /// Apply auto-select criteria to all results.
        /// Returns the number of results where a side was marked.
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
                if (!criteria.IncludeDefects && r.type == CoreDll.ResultType.DefectImage) continue;

                AutoSelectSide side = DetermineSide(r, criteria, poolMap);

                if (side != AutoSelectSide.DontCare)
                {
                    s_sideCache[i] = side;
                    affected++;
                }
            }

            return affected;
        }

        /// <summary>
        /// Get the targeted side for a result index.
        /// </summary>
        public static AutoSelectSide GetSide(int resultIndex)
        {
            AutoSelectSide side;
            s_sideCache.TryGetValue(resultIndex, out side);
            return side;
        }

        /// <summary>
        /// Set the targeted side for a result index (manual toggle).
        /// </summary>
        public static void SetSide(int resultIndex, AutoSelectSide side)
        {
            s_sideCache[resultIndex] = side;
        }

        /// <summary>
        /// Clear the targeted side for a result index.
        /// </summary>
        public static void ClearSide(int resultIndex)
        {
            s_sideCache.Remove(resultIndex);
        }

        /// <summary>
        /// Invert side cache: First ↔ Second.
        /// </summary>
        public static int InvertSides(CoreLib core)
        {
            var results = core.GetResult(0, 1000000);
            if (results == null || results.Length == 0) return 0;

            var newCache = new Dictionary<int, AutoSelectSide>();
            int inverted = 0;

            for (int i = 0; i < results.Length; i++)
            {
                AutoSelectSide oldSide;
                if (s_sideCache.TryGetValue(i, out oldSide))
                {
                    newCache[i] = (oldSide == AutoSelectSide.First) 
                        ? AutoSelectSide.Second 
                        : AutoSelectSide.First;
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
        public static int Execute(CoreLib core, bool delete, string targetFolder = null)
        {
            var results = core.GetResult(0, 1000000);
            if (results == null || results.Length == 0) return 0;

            int executed = 0;
            // Process in reverse to maintain index stability
            for (int i = results.Length - 1; i >= 0; i--)
            {
                AutoSelectSide side;
                if (!s_sideCache.TryGetValue(i, out side)) continue;
                if (results[i].type != CoreDll.ResultType.DuplImagePair) continue;

                core.SetCurrent(i);

                if (delete)
                {
                    if (side == AutoSelectSide.First)
                        core.ApplyToResult(CoreDll.LocalActionType.DeleteFirst, CoreDll.TargetType.Current);
                    else
                        core.ApplyToResult(CoreDll.LocalActionType.DeleteSecond, CoreDll.TargetType.Current);
                }
                else if (!string.IsNullOrEmpty(targetFolder))
                {
                    // Move to user-specified folder
                    string sourcePath = (side == AutoSelectSide.First) 
                        ? results[i].first.path 
                        : results[i].second.path;
                    
                    if (!string.IsNullOrEmpty(sourcePath) && System.IO.File.Exists(sourcePath))
                    {
                        string destPath = System.IO.Path.Combine(targetFolder, System.IO.Path.GetFileName(sourcePath));
                        // Handle name conflicts
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
                    }
                }

                executed++;
            }

            s_sideCache.Clear();
            return executed;
        }

        /// <summary>
        /// Determine which side of the pair to target based on criteria.
        /// </summary>
        public static AutoSelectSide DetermineSide(CoreResult r, AutoSelectCriteria criteria, Dictionary<string, int> poolMap)
        {
            AutoSelectSide result = AutoSelectSide.DontCare;

            // Time
            if (criteria.TimeSide != AutoSelectSide.DontCare)
            {
                if (r.first.time < r.second.time)
                    result = AutoSelectSide.First;
                else if (r.second.time < r.first.time)
                    result = AutoSelectSide.Second;

                if (criteria.TimeSide == AutoSelectSide.Second && result != AutoSelectSide.DontCare)
                    result = (result == AutoSelectSide.First) ? AutoSelectSide.Second : AutoSelectSide.First;
            }

            // Size
            if (criteria.SizeSide != AutoSelectSide.DontCare)
            {
                AutoSelectSide side;
                if (r.first.size < r.second.size)
                    side = AutoSelectSide.First;
                else if (r.second.size < r.first.size)
                    side = AutoSelectSide.Second;
                else
                    side = AutoSelectSide.DontCare;

                if (criteria.SizeSide == AutoSelectSide.Second && side != AutoSelectSide.DontCare)
                    side = (side == AutoSelectSide.First) ? AutoSelectSide.Second : AutoSelectSide.First;

                if (side != AutoSelectSide.DontCare)
                    result = side;
            }

            // Quality
            if (criteria.QualitySide != AutoSelectSide.DontCare)
            {
                double q1 = r.first.blockiness + r.first.blurring;
                double q2 = r.second.blockiness + r.second.blurring;

                AutoSelectSide side;
                if (q1 > q2)
                    side = AutoSelectSide.First;
                else if (q2 > q1)
                    side = AutoSelectSide.Second;
                else
                    side = AutoSelectSide.DontCare;

                if (criteria.QualitySide == AutoSelectSide.Second && side != AutoSelectSide.DontCare)
                    side = (side == AutoSelectSide.First) ? AutoSelectSide.Second : AutoSelectSide.First;

                if (side != AutoSelectSide.DontCare)
                    result = side;
            }

            // Resolution
            if (criteria.ResolutionSide != AutoSelectSide.DontCare)
            {
                long res1 = (long)r.first.width * r.first.height;
                long res2 = (long)r.second.width * r.second.height;

                AutoSelectSide side;
                if (res1 < res2)
                    side = AutoSelectSide.First;
                else if (res2 < res1)
                    side = AutoSelectSide.Second;
                else
                    side = AutoSelectSide.DontCare;

                if (criteria.ResolutionSide == AutoSelectSide.Second && side != AutoSelectSide.DontCare)
                    side = (side == AutoSelectSide.First) ? AutoSelectSide.Second : AutoSelectSide.First;

                if (side != AutoSelectSide.DontCare)
                    result = side;
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

                if (side != AutoSelectSide.DontCare)
                    result = side;
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
