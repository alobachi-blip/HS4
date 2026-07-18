using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace HS2DirectHLeanProfile
{
    public static class DirectHLeanProfilePatcher
    {
        private const string RunMarkerFileName = "HS2DirectHLauncher.run";
        private const string FullPluginsMarkerFileName = "HS2DirectHLauncher.fullplugins";
        private const string LeanPluginDirectoryName = "directh_plugins";

        public static IEnumerable<string> TargetDLLs => Array.Empty<string>();

        public static void Initialize()
        {
            string markerPath = Path.Combine(Paths.ConfigPath, RunMarkerFileName);
            if (!File.Exists(markerPath))
                return;

            string fullPluginsMarkerPath = Path.Combine(Paths.ConfigPath, FullPluginsMarkerFileName);
            if (File.Exists(fullPluginsMarkerPath))
            {
                try
                {
                    File.Delete(fullPluginsMarkerPath);
                }
                catch
                {
                    // It is only a one-shot mode marker; a later lean launch also removes it.
                }
                return;
            }

            ManualLogSource log = Logger.CreateLogSource("Direct-H Lean Profile");
            try
            {
                string bepinRoot = EnsureTrailingSeparator(Path.GetFullPath(Paths.BepInExRootPath));
                string leanPath = Path.GetFullPath(Path.Combine(Paths.BepInExRootPath, LeanPluginDirectoryName));
                if (!EnsureTrailingSeparator(leanPath).StartsWith(bepinRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Lean plugin path escaped the BepInEx root.");
                if (!Directory.Exists(leanPath))
                    throw new DirectoryNotFoundException("Lean plugin directory was not prepared: " + leanPath);
                if (Directory.GetFiles(leanPath, "HS2DirectHLauncher*.dll", SearchOption.AllDirectories).Length == 0)
                    throw new FileNotFoundException("Lean profile does not contain HS2DirectHLauncher.");

                PropertyInfo? pluginPath = typeof(Paths).GetProperty(
                    nameof(Paths.PluginPath),
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo? setter = pluginPath?.GetSetMethod(nonPublic: true);
                if (setter == null)
                    throw new MissingMethodException("BepInEx.Paths.PluginPath setter was not found.");

                setter.Invoke(null, new object[] { leanPath });
                log.LogInfo("Direct-H marker found; using lean plugin path: " + leanPath);
            }
            catch (Exception ex)
            {
                log.LogError("Could not activate the Direct-H lean profile; full plugin path remains active: " + ex);
            }
        }

        public static void Patch(ref AssemblyDefinition assembly)
        {
            // This preloader component only switches Paths.PluginPath.
        }

        private static string EnsureTrailingSeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }
    }
}
