using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HS2DirectHLauncher
{
    internal sealed class CardPicker
    {
        private readonly Random _random = new Random(unchecked(Environment.TickCount * 397 ^ Guid.NewGuid().GetHashCode()));

        internal string[] PickTwo(
            string directory,
            bool recursive,
            Func<string, bool>? preferred,
            Func<string, bool>? accepted,
            params string[] fallbacks)
        {
            var candidates = FindCards(directory, recursive);
            candidates.AddRange(fallbacks.Where(FileReferenceExists));
            candidates = candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var preferredCards = preferred == null
                ? new List<string>()
                : candidates.Where(preferred).ToList();
            var otherCards = preferred == null
                ? candidates
                : candidates.Where(path => !preferred(path)).ToList();
            Shuffle(preferredCards);
            Shuffle(otherCards);

            var selected = new List<string>(2);
            foreach (string path in preferredCards.Concat(otherCards))
            {
                if (accepted != null && !accepted(path))
                    continue;
                selected.Add(path);
                if (selected.Count == 2)
                    break;
            }

            if (selected.Count == 0)
                return new[] { string.Empty, string.Empty };
            if (selected.Count == 1)
                return new[] { selected[0], selected[0] };

            return selected.ToArray();
        }

        private void Shuffle(List<string> values)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                string temp = values[i];
                values[i] = values[j];
                values[j] = temp;
            }
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
