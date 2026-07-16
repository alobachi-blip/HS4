using System;
using System.IO;
using BepInEx;

namespace HS2OrbitAndExciter
{
    internal readonly struct OrbitBhsCompatSnapshot
    {
        internal readonly bool Installed;
        internal readonly bool ConfigFound;
        internal readonly bool AutoFinishEnabled;
        internal readonly bool OffsetApplied;
        internal readonly bool SolverEnabled;
        internal readonly string PluginPath;
        internal readonly string ConfigPath;

        internal OrbitBhsCompatSnapshot(
            bool installed,
            bool configFound,
            bool autoFinishEnabled,
            bool offsetApplied,
            bool solverEnabled,
            string pluginPath,
            string configPath)
        {
            Installed = installed;
            ConfigFound = configFound;
            AutoFinishEnabled = autoFinishEnabled;
            OffsetApplied = offsetApplied;
            SolverEnabled = solverEnabled;
            PluginPath = pluginPath;
            ConfigPath = configPath;
        }
    }

    internal static class OrbitBhsCompat
    {
        private const float RefreshIntervalSeconds = 5f;
        private static OrbitBhsCompatSnapshot _snapshot;
        private static float _nextRefreshUnscaled;
        private static bool _hasSnapshot;
        private static string _lastSignature = "";

        internal static OrbitBhsCompatSnapshot Snapshot()
        {
            if (!_hasSnapshot || UnityEngine.Time.unscaledTime >= _nextRefreshUnscaled)
                Refresh();
            return _snapshot;
        }

        internal static void LogIfChanged()
        {
            var snap = Snapshot();
            string signature = snap.Installed + "|" + snap.ConfigFound + "|" +
                snap.AutoFinishEnabled + "|" + snap.OffsetApplied + "|" + snap.SolverEnabled;
            if (signature == _lastSignature)
                return;
            _lastSignature = signature;
            OrbitStateMachineLog.Event(
                "bhs",
                "compat",
                "{\"installed\":" + Bool(snap.Installed) +
                ",\"configFound\":" + Bool(snap.ConfigFound) +
                ",\"bhsAutoFinishEnabled\":" + Bool(snap.AutoFinishEnabled) +
                ",\"bhsOffsetApplied\":" + Bool(snap.OffsetApplied) +
                ",\"bhsSolverEnabled\":" + Bool(snap.SolverEnabled) +
                ",\"pluginPath\":\"" + Esc(snap.PluginPath) +
                "\",\"configPath\":\"" + Esc(snap.ConfigPath) + "\"}");
        }

        private static void Refresh()
        {
            _nextRefreshUnscaled = UnityEngine.Time.unscaledTime + RefreshIntervalSeconds;
            string pluginPath = FindPluginPath();
            string configPath = FindConfigPath();
            bool installed = !string.IsNullOrEmpty(pluginPath);
            bool configFound = File.Exists(configPath);
            bool autoFinish = false;
            bool offset = false;
            bool solver = false;

            if (configFound)
            {
                try
                {
                    string[] lines = File.ReadAllLines(configPath);
                    autoFinish = IsEnabledEnum(ReadSetting(lines, "Auto finish"));
                    offset = IsEnabledBool(ReadSetting(lines, "Apply saved offsets"));
                    bool animationFixer = IsEnabledBool(ReadSetting(lines, "Enable Animation Fixer"));
                    bool brokenTables = IsEnabledBool(ReadSetting(lines, "Fix broken Animation Tables"));
                    bool kiss = IsEnabledBool(ReadSetting(lines, "Fix Kiss Animations"));
                    solver = animationFixer && (brokenTables || kiss);
                }
                catch (Exception ex)
                {
                    HS2OrbitAndExciter.Log?.LogDebug($"Orbit BHS config read skipped: {ex.Message}");
                }
            }

            _snapshot = new OrbitBhsCompatSnapshot(
                installed,
                configFound,
                installed && autoFinish,
                installed && offset,
                installed && solver,
                pluginPath,
                configPath);
            _hasSnapshot = true;
        }

        private static string FindPluginPath()
        {
            try
            {
                string plugins = Path.Combine(Paths.BepInExRootPath, "plugins");
                if (!Directory.Exists(plugins))
                    return "";
                string[] matches = Directory.GetFiles(plugins, "HS2_BetterHScenes.dll", SearchOption.AllDirectories);
                return matches.Length > 0 ? matches[0] : "";
            }
            catch
            {
                return "";
            }
        }

        private static string FindConfigPath()
        {
            string path = Path.Combine(Paths.BepInExRootPath, "config", "HS2_BetterHScenes.cfg");
            return path;
        }

        private static string ReadSetting(string[] lines, string key)
        {
            string prefix = key + " =";
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;
                if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                return line.Substring(prefix.Length).Trim();
            }
            return "";
        }

        private static bool IsEnabledBool(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("enabled", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("1", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEnabledEnum(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            return !(value.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("disabled", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("0", StringComparison.OrdinalIgnoreCase));
        }

        private static string Bool(bool value) => value ? "true" : "false";

        private static string Esc(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return value!.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }
}
