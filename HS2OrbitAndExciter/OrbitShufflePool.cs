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
            Func<string, float>? getWeight = null)
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

            var pool = unused.Count > 0 ? unused : candidates;
            if (unused.Count == 0)
                used.Clear();

            string? picked = WeightedPick(pool, getWeight);
            if (picked != null)
                used.Add(picked);
            return picked;
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
