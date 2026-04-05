using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// Version and optional git/date from versioned deploy DLL name (see Directory.Build.targets).
    /// </summary>
    public static class PluginBuildIdentity
    {
        private static readonly Regex VersionedDll =
            new Regex(@"^HS2OrbitAndExciter_([a-fA-F0-9]{8})_(\d{8})(_dirty)?$", RegexOptions.CultureInvariant);

        public static string SemanticVersion => PluginInfo.PLUGIN_VERSION;

        public static string AssemblyFileName
        {
            get
            {
                try
                {
                    var loc = Assembly.GetExecutingAssembly().Location;
                    return string.IsNullOrEmpty(loc) ? PluginInfo.PLUGIN_NAME + ".dll" : Path.GetFileName(loc);
                }
                catch
                {
                    return PluginInfo.PLUGIN_NAME + ".dll";
                }
            }
        }

        /// <summary>Parses <c>HS2OrbitAndExciter_&lt;git&gt;_&lt;yyyyMMdd&gt;[_dirty].dll</c> basename.</summary>
        public static bool TryParseVersionedFileName(string? assemblyPath, out string gitRev, out string utcDate, out bool dirty)
        {
            gitRev = "";
            utcDate = "";
            dirty = false;
            if (string.IsNullOrEmpty(assemblyPath)) return false;
            var baseName = Path.GetFileNameWithoutExtension(assemblyPath);
            var m = VersionedDll.Match(baseName);
            if (!m.Success) return false;
            gitRev = m.Groups[1].Value;
            utcDate = m.Groups[2].Value;
            dirty = m.Groups[3].Success;
            return true;
        }

        public static string[] GetGuiLines()
        {
            const string displayName = "環視與興奮條";
            try
            {
                var path = Assembly.GetExecutingAssembly().Location ?? "";
                var lines = new[]
                {
                    "建置識別（測試對照用）",
                    $"{displayName}　版本 {SemanticVersion}",
                    $"檔案名稱：{AssemblyFileName}"
                };
                if (TryParseVersionedFileName(path, out var git, out var date, out var dirty))
                {
                    return new[]
                    {
                        lines[0],
                        lines[1],
                        $"原始碼版本：{git}　建置日期：{date}（世界協調時間）" + (dirty ? "　（含未提交變更）" : ""),
                        lines[2]
                    };
                }
                return new[]
                {
                    lines[0],
                    lines[1],
                    "（檔名未含版本編號；建置時可產出帶日期與版號的檔名以利對照）",
                    lines[2]
                };
            }
            catch
            {
                return new[] { displayName, "版本 " + SemanticVersion };
            }
        }
    }
}
