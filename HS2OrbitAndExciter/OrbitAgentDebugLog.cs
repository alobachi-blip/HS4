using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// Compare-mode NDJSON: one file per versioned deploy DLL under <c>d:\HS4\orbit-compare\</c>.
    /// Lines include dllBase, semver, runId, unscaledTime for diffing old vs new builds.
    /// </summary>
    internal static class OrbitAgentDebugLog
    {
        private const string CompareDir = @"d:\HS4\orbit-compare";
        private const string SessionTag = "b93d2d";

        private static readonly string RunId = Guid.NewGuid().ToString("N");
        private static string? _logFilePath;
        private static string _dllBase = "unknown";
        private static bool _bootLineWritten;

        internal static void EnsureInit()
        {
            if (_logFilePath != null) return;
            try
            {
                _dllBase = PluginBuildIdentity.AssemblyFileName;
                if (string.IsNullOrEmpty(_dllBase)) _dllBase = "unknown.dll";
                string safe = SanitizeFileName(Path.GetFileNameWithoutExtension(_dllBase));
                if (string.IsNullOrEmpty(safe)) safe = "unknown";
                Directory.CreateDirectory(CompareDir);
                _logFilePath = Path.Combine(CompareDir, safe + ".ndjson");
            }
            catch
            {
                _logFilePath = Path.Combine(CompareDir, "fallback.ndjson");
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "log";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (Array.IndexOf(invalid, c) >= 0) sb.Append('_');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        internal static string JsonEscape(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t = s!;
            return t.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        internal static void Write(string hypothesisId, string location, string message, string dataJsonObject = "{}")
        {
            EnsureInit();
            try
            {
                if (!_bootLineWritten)
                {
                    _bootLineWritten = true;
                    WriteCore("boot", "OrbitAgentDebugLog", "logger_ready",
                        "{\"dllBase\":\"" + JsonEscape(_dllBase) + "\",\"compareDir\":\"" + JsonEscape(CompareDir) + "\"}");
                }
                WriteCore(hypothesisId, location, message, dataJsonObject);
            }
            catch
            {
                // ignore
            }
        }

        private static void WriteCore(string hypothesisId, string location, string message, string dataJsonObject)
        {
            long ts = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            double u = Time.unscaledTime;
            string line =
                "{\"sessionTag\":\"" + SessionTag + "\",\"runId\":\"" + RunId + "\",\"dllBase\":\"" + JsonEscape(_dllBase) +
                "\",\"pluginVersion\":\"" + JsonEscape(PluginBuildIdentity.SemanticVersion) + "\",\"timestamp\":" + ts +
                ",\"unscaledTime\":" + u.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                ",\"hypothesisId\":\"" + JsonEscape(hypothesisId) + "\",\"location\":\"" + JsonEscape(location) +
                "\",\"message\":\"" + JsonEscape(message) + "\",\"data\":" + dataJsonObject + "}\n";
            File.AppendAllText(_logFilePath!, line);
        }
    }
}
