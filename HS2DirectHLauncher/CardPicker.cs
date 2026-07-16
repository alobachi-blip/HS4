using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HS2DirectHLauncher
{
    internal sealed class CardPicker
    {
        private readonly Random _random = new Random(unchecked(Environment.TickCount * 397 ^ Guid.NewGuid().GetHashCode()));

        internal string[] PickTwo(string directory, bool recursive, params string[] fallbacks)
        {
            var candidates = FindCards(directory, recursive);
            if (candidates.Count == 0)
            {
                candidates.AddRange(fallbacks.Where(FileReferenceExists));
                candidates = candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }

            if (candidates.Count == 0)
                return new[] { string.Empty, string.Empty };
            if (candidates.Count == 1)
                return new[] { candidates[0], candidates[0] };

            int first = _random.Next(candidates.Count);
            int second = _random.Next(candidates.Count - 1);
            if (second >= first)
                second++;
            return new[] { candidates[first], candidates[second] };
        }

        private static List<string> FindCards(string directory, bool recursive)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return new List<string>();

            try
            {
                return Directory.EnumerateFiles(
                        directory,
                        "*.png",
                        recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                    .Where(path => new FileInfo(path).Length > 0)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private static bool FileReferenceExists(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   (File.Exists(path) || !Path.IsPathRooted(path));
        }
    }
}
