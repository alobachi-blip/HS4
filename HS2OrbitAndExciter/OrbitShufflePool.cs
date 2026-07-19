using System;
using System.Collections.Generic;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>Prefer unused items; reset used set when exhausted; optional weighted pick.</summary>
    internal static class OrbitShufflePool
    {
        internal static string? Pick(
            IReadOnlyList<string> all,
            HashSet<string> used,
            string? excludePath,
            Func<string, float>? getWeight = null,
            Func<string, bool>? includePath = null,
            int maxIncludeChecks = int.MaxValue)
        {
            if (all == null || all.Count == 0)
                return null;

            var candidates = new List<string>();
            foreach (var path in all)
            {
                if (string.IsNullOrEmpty(path))
                    continue;
                if (excludePath != null && string.Equals(path, excludePath, StringComparison.OrdinalIgnoreCase))
                    continue;
                candidates.Add(path);
            }
            if (candidates.Count == 0)
                return null;

            var unused = new List<string>();
            foreach (var path in candidates)
            {
                if (!used.Contains(path))
                    unused.Add(path);
            }

            string? picked;
            if (includePath == null)
            {
                var pool = unused.Count > 0 ? unused : candidates;
                if (unused.Count == 0)
                    used.Clear();
                picked = WeightedPick(pool, getWeight);
            }
            else
            {
                int checksLeft = Math.Max(0, maxIncludeChecks);
                if (unused.Count > 0)
                {
                    picked = WeightedPickMatching(unused, getWeight, includePath, ref checksLeft);
                    if (picked == null && checksLeft > 0)
                    {
                        var usedCandidates = candidates.FindAll(path => used.Contains(path));
                        picked = WeightedPickMatching(usedCandidates, getWeight, includePath, ref checksLeft);
                        if (picked != null)
                            used.Clear();
                    }
                }
                else
                {
                    picked = WeightedPickMatching(candidates, getWeight, includePath, ref checksLeft);
                    if (picked != null)
                        used.Clear();
                }
            }

            if (picked != null)
                used.Add(picked);
            return picked;
        }

        /// <summary>
        /// Evaluate an expensive include predicate in weighted-random order,
        /// stopping after the caller's budget instead of scanning the full pool.
        /// </summary>
        private static string? WeightedPickMatching(
            IReadOnlyList<string> pool,
            Func<string, float>? getWeight,
            Func<string, bool> includePath,
            ref int checksLeft)
        {
            var remaining = new List<string>(pool);
            while (remaining.Count > 0 && checksLeft > 0)
            {
                string? candidate = WeightedPick(remaining, getWeight);
                if (candidate == null)
                    return null;
                remaining.Remove(candidate);
                checksLeft--;
                if (includePath(candidate))
                    return candidate;
            }
            return null;
        }

        private static string? WeightedPick(IReadOnlyList<string> pool, Func<string, float>? getWeight)
        {
            if (pool.Count == 0)
                return null;
            if (pool.Count == 1)
                return pool[0];

            float total = 0f;
            var weights = new float[pool.Count];
            for (int i = 0; i < pool.Count; i++)
            {
                float w = getWeight?.Invoke(pool[i]) ?? 1f;
                if (w < 0.01f)
                    w = 0.01f;
                weights[i] = w;
                total += w;
            }

            float r = UnityEngine.Random.Range(0f, total);
            for (int i = 0; i < pool.Count; i++)
            {
                r -= weights[i];
                if (r <= 0f)
                    return pool[i];
            }
            return pool[pool.Count - 1];
        }
    }
}
