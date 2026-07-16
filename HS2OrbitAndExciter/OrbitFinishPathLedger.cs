using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HS2OrbitAndExciter
{
    internal readonly struct OrbitFinishCandidate
    {
        internal readonly string Family;
        internal readonly string Path;
        internal readonly HSceneFlagCtrl.ClickKind Click;
        internal readonly int LengthPriority;

        internal OrbitFinishCandidate(string family, string path, HSceneFlagCtrl.ClickKind click, int lengthPriority)
        {
            Family = family;
            Path = path;
            Click = click;
            LengthPriority = lengthPriority;
        }
    }

    internal static class OrbitFinishPathLedger
    {
        private static readonly Dictionary<string, int> Counts =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> Totals =
            new Dictionary<string, int>(StringComparer.Ordinal);

        internal static bool TryPick(IReadOnlyList<OrbitFinishCandidate> candidates, out OrbitFinishCandidate pick)
        {
            pick = default;
            if (candidates == null || candidates.Count == 0)
                return false;

            bool hasPick = false;
            float bestRatio = float.MaxValue;
            int bestLength = int.MinValue;
            string bestPath = "";

            foreach (var candidate in candidates)
            {
                int count = Count(candidate.Family, candidate.Path);
                int total = Math.Max(1, Total(candidate.Family));
                float ratio = count / (float)total;
                bool better = !hasPick
                    || ratio < bestRatio
                    || (Math.Abs(ratio - bestRatio) < 0.0001f && candidate.LengthPriority > bestLength)
                    || (Math.Abs(ratio - bestRatio) < 0.0001f
                        && candidate.LengthPriority == bestLength
                        && string.CompareOrdinal(candidate.Path, bestPath) < 0);
                if (!better)
                    continue;

                pick = candidate;
                hasPick = true;
                bestRatio = ratio;
                bestLength = candidate.LengthPriority;
                bestPath = candidate.Path;
            }

            return hasPick;
        }

        internal static void MarkConsumed(string family, string path)
        {
            if (string.IsNullOrEmpty(family) || string.IsNullOrEmpty(path))
                return;
            string key = Key(family, path);
            Counts[key] = Count(family, path) + 1;
            Totals[family] = Total(family) + 1;
        }

        internal static int Count(string family, string path) =>
            Counts.TryGetValue(Key(family, path), out int value) ? value : 0;

        internal static int Total(string family) =>
            Totals.TryGetValue(family ?? "", out int value) ? value : 0;

        internal static string BuildCandidateJson(IReadOnlyList<OrbitFinishCandidate> candidates)
        {
            var sb = new StringBuilder(128);
            sb.Append('[');
            if (candidates != null)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var c = candidates[i];
                    int total = Math.Max(1, Total(c.Family));
                    float ratio = Count(c.Family, c.Path) / (float)total;
                    sb.Append("{\"family\":\"").Append(Esc(c.Family));
                    sb.Append("\",\"path\":\"").Append(Esc(c.Path));
                    sb.Append("\",\"click\":\"").Append(Esc(c.Click.ToString()));
                    sb.Append("\",\"count\":").Append(Count(c.Family, c.Path));
                    sb.Append(",\"total\":").Append(Total(c.Family));
                    sb.Append(",\"ratio\":").Append(ratio.ToString("R", CultureInfo.InvariantCulture));
                    sb.Append(",\"lengthPriority\":").Append(c.LengthPriority).Append('}');
                }
            }
            sb.Append(']');
            return sb.ToString();
        }

        internal static string BuildSummaryJson(string family)
        {
            var sb = new StringBuilder(128);
            sb.Append("{\"family\":\"").Append(Esc(family)).Append("\",\"total\":").Append(Total(family));
            sb.Append(",\"counts\":{");
            bool first = true;
            foreach (var pair in Counts)
            {
                string prefix = (family ?? "") + ":";
                if (!pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                if (!first) sb.Append(',');
                first = false;
                string path = pair.Key.Substring(prefix.Length);
                sb.Append('"').Append(Esc(path)).Append("\":").Append(pair.Value);
            }
            sb.Append("}}");
            return sb.ToString();
        }

        private static string Key(string family, string path) => (family ?? "") + ":" + (path ?? "");

        private static string Esc(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return value!.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }
}
