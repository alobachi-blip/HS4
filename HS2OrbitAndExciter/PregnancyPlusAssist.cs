using System;
using System.Reflection;
using AIChara;
using HarmonyLib;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// PregnancyPlus live shortcuts: Y/U are handled by Preg+ (step scaled by our patch).
    /// R reset often fails for HS2 H-scene belly (<c>HS2Inflation</c> level), so we force deflate on hotkey I.
    /// Optional inside-finish belly grow via <c>HS2Inflation(false)</c> (menu default on).
    /// </summary>
    internal static class PregnancyPlusAssist
    {
        private static Type? _controllerType;
        private static MethodInfo? _hs2Inflation;
        private static MethodInfo? _resetInflation;
        private static MethodInfo? _meshInflateFloat;
        private static FieldInfo? _currentInflationLevel;
        private static FieldInfo? _infConfig;
        private static FieldInfo? _inflationSize;
        private static Type? _pluginType;
        private static PropertyInfo? _maxLevelEntryProperty;
        private static FieldInfo? _maxLevelEntryField;
        private static HScene? _trackedHScene;
        private static int _lastInsideCount = -1;
        private static bool _resolved;
        private static float _nextResolveAttemptAt;
        private static bool _unavailableLogged;
        private static bool _methodUnavailableLogged;

        private const int OriginalHs2InflationMaxLevel = 6;
        private const int InflationMaxLevelMultiplier = 3;

        /// <summary>Grow H-scene belly one level on inside finish (cumflation).</summary>
        internal static bool TryInflateOnInside(HScene? hScene)
        {
            if (HS2OrbitAndExciter.CumflationEnabled?.Value != true)
                return false;
            if (hScene == null)
                return false;

            EnsureResolved();
            if (_controllerType == null || _hs2Inflation == null)
                return false;

            // PregnancyPlus clips HS2Inflation(false) before it animates the mesh.
            // Raise the original six-level cap to 18 before invoking it.
            TryRaiseMaxInflationLevel();

            var females = OrbitHelpers.GetChaFemales(hScene);
            if (females == null || females.Length == 0)
                return false;

            bool any = false;
            for (int i = 0; i < females.Length; i++)
            {
                var cha = females[i];
                if (cha == null)
                    continue;
                if (TryInflateOnCha(cha))
                    any = true;
            }

            if (any)
                HS2OrbitAndExciter.Log?.LogInfo("Orbit: 內射肚子變大（PregnancyPlus HS2Inflation）");
            return any;
        }

        /// <summary>
        /// Track HS2's cumulative inside-finish counter independently of VoiceTour.
        /// This keeps the menu toggle functional even when voice-tour progression is off.
        /// </summary>
        internal static void TickInsideFinish(HScene? hScene)
        {
            if (hScene == null)
            {
                ResetInsideTracking();
                return;
            }

            var ctrl = hScene.ctrlFlag;
            if (ctrl == null)
                return;

            int inside = ctrl.numInside;
            if (!ReferenceEquals(_trackedHScene, hScene))
            {
                _trackedHScene = hScene;
                _lastInsideCount = inside;
                return;
            }

            if (inside <= _lastInsideCount)
            {
                _lastInsideCount = inside;
                return;
            }

            int delta = inside - _lastInsideCount;
            _lastInsideCount = inside;
            if (HS2OrbitAndExciter.CumflationEnabled?.Value != true)
                return;

            for (int i = 0; i < delta; i++)
                TryInflateOnInside(hScene);
        }

        internal static void ResetInsideTracking()
        {
            _trackedHScene = null;
            _lastInsideCount = -1;
        }

        /// <summary>Ensure the PregnancyPlus HS2 inflation cap is at least three times its original default.</summary>
        internal static bool TryRaiseMaxInflationLevel()
        {
            EnsureResolved();
            if (_pluginType == null)
                return false;

            object? entry = null;
            try
            {
                entry = _maxLevelEntryProperty?.GetValue(null, null)
                    ?? _maxLevelEntryField?.GetValue(null);
            }
            catch
            {
                return false;
            }

            if (entry == null)
                return false;

            var valueProperty = entry.GetType().GetProperty(
                "Value",
                BindingFlags.Instance | BindingFlags.Public);
            if (valueProperty == null || !valueProperty.CanRead || !valueProperty.CanWrite)
                return false;

            try
            {
                int current = Convert.ToInt32(valueProperty.GetValue(entry, null));
                int requested = Math.Max(current, OriginalHs2InflationMaxLevel * InflationMaxLevelMultiplier);
                if (current < requested)
                {
                    valueProperty.SetValue(entry, requested, null);
                    HS2OrbitAndExciter.Log?.LogInfo(
                        $"Orbit: PregnancyPlus 肚子上限由 {current} 級提高至 {requested} 級（原始上限 3 倍）");
                }
                return true;
            }
            catch (Exception ex)
            {
                HS2OrbitAndExciter.Log?.LogWarning($"Orbit: PregnancyPlus 肚子上限套用失敗: {ex.Message}");
                return false;
            }
        }

        internal static string InflationCapStatus
        {
            get
            {
                TryRaiseMaxInflationLevel();
                int level = GetInflationMaxLevel();
                return level > 0
                    ? $"PregnancyPlus 肚子上限：{level} 級（原始 6 級的 3 倍）"
                    : "PregnancyPlus 未載入：肚子上限未套用";
            }
        }

        private static int GetInflationMaxLevel()
        {
            EnsureResolved();
            if (_pluginType == null)
                return 0;

            try
            {
                object? entry = _maxLevelEntryProperty?.GetValue(null, null)
                    ?? _maxLevelEntryField?.GetValue(null);
                if (entry == null)
                    return 0;

                var valueProperty = entry.GetType().GetProperty(
                    "Value",
                    BindingFlags.Instance | BindingFlags.Public);
                return valueProperty == null
                    ? 0
                    : Math.Max(0, Convert.ToInt32(valueProperty.GetValue(entry, null)));
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>§19：愛撫／女女落地縮腹一級（與內射對稱）。</summary>
        internal static bool TryDeflateOneLevel(HScene? hScene)
        {
            if (HS2OrbitAndExciter.CumflationEnabled?.Value != true)
                return false;
            if (hScene == null)
                return false;

            EnsureResolved();
            if (_controllerType == null || _hs2Inflation == null)
                return false;

            var females = OrbitHelpers.GetChaFemales(hScene);
            if (females == null || females.Length == 0)
                return false;

            bool any = false;
            for (int i = 0; i < females.Length; i++)
            {
                var cha = females[i];
                if (cha == null)
                    continue;
                // HS2Inflation(true) = 降一級（Preg+ API）
                if (TryDeflateOnCha(cha))
                    any = true;
            }

            if (any)
                HS2OrbitAndExciter.Log?.LogInfo("Orbit: 愛撫／女女落地，肚子降一級");
            return any;
        }

        private static bool TryDeflateOnCha(ChaControl cha)
        {
            var ctrl = cha.GetComponent(_controllerType!) ?? cha.GetComponentInChildren(_controllerType!, true);
            if (ctrl == null)
                return false;
            try
            {
                _hs2Inflation!.Invoke(ctrl, new object[] { true });
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryResetBelly(HScene? hScene)
        {
            if (hScene == null)
                return false;

            EnsureResolved();
            if (_controllerType == null)
                return false;

            var females = OrbitHelpers.GetChaFemales(hScene);
            if (females == null || females.Length == 0)
                return false;

            bool any = false;
            for (int i = 0; i < females.Length; i++)
            {
                var cha = females[i];
                if (cha == null)
                    continue;
                if (TryResetOnCha(cha))
                    any = true;
            }

            if (any)
                HS2OrbitAndExciter.Log?.LogInfo("Orbit: I 清腹（PregnancyPlus）");
            return any;
        }

        private static bool TryInflateOnCha(ChaControl cha)
        {
            var ctrl = cha.GetComponent(_controllerType!) ?? cha.GetComponentInChildren(_controllerType!, true);
            if (ctrl == null)
                return false;

            try
            {
                // HS2Inflation(false) = +1 level (same as Preg+ Allow cumflation).
                _hs2Inflation!.Invoke(ctrl, new object[] { false });
                return true;
            }
            catch (Exception ex)
            {
                HS2OrbitAndExciter.Log?.LogWarning($"Orbit: HS2Inflation 失敗: {ex.Message}");
                return false;
            }
        }

        private static bool TryResetOnCha(ChaControl cha)
        {
            var ctrl = cha.GetComponent(_controllerType!) ?? cha.GetComponentInChildren(_controllerType!, true);
            if (ctrl == null)
                return false;

            bool ok = false;

            if (_currentInflationLevel != null)
            {
                _currentInflationLevel.SetValue(ctrl, 0);
                ok = true;
            }

            if (_hs2Inflation != null)
            {
                _hs2Inflation.Invoke(ctrl, new object[] { true });
                ok = true;
            }

            if (_infConfig != null && _inflationSize != null)
            {
                var cfg = _infConfig.GetValue(ctrl);
                if (cfg != null)
                {
                    _inflationSize.SetValue(cfg, 0f);
                    ok = true;
                }
            }

            if (_resetInflation != null)
            {
                _resetInflation.Invoke(ctrl, null);
                ok = true;
            }
            else if (_meshInflateFloat != null)
            {
                _meshInflateFloat.Invoke(ctrl, new object[] { 0f, "OrbitRReset" });
                ok = true;
            }

            return ok;
        }

        private static void EnsureResolved()
        {
            if (_resolved)
                return;

            // PregnancyPlus is optional.  A transient startup-order miss must
            // not permanently disable cumflation for the rest of this session.
            if (Time.realtimeSinceStartup < _nextResolveAttemptAt)
                return;
            _nextResolveAttemptAt = Time.realtimeSinceStartup + 1f;

            _controllerType = FindLoadedType("KK_PregnancyPlus.PregnancyPlusCharaController");
            _pluginType = FindLoadedType("KK_PregnancyPlus.PregnancyPlusPlugin");
            if (_pluginType != null)
            {
                _maxLevelEntryProperty = AccessTools.Property(_pluginType, "HS2InflationMaxLevel");
                _maxLevelEntryField = AccessTools.Field(_pluginType, "HS2InflationMaxLevel");
            }
            if (_controllerType == null)
            {
                if (!_unavailableLogged)
                {
                    _unavailableLogged = true;
                    HS2OrbitAndExciter.Log?.LogWarning("Orbit: PregnancyPlus unavailable; belly grow/reset will retry after it loads.");
                }
                return;
            }

            _hs2Inflation = AccessTools.Method(_controllerType, "HS2Inflation", new[] { typeof(bool) });
            _resetInflation = AccessTools.Method(_controllerType, "ResetInflation");
            _meshInflateFloat = AccessTools.Method(_controllerType, "MeshInflate", new[] { typeof(float), typeof(string) });
            _currentInflationLevel = AccessTools.Field(_controllerType, "_currentInflationLevel");
            _infConfig = AccessTools.Field(_controllerType, "infConfig");
            if (_infConfig != null)
                _inflationSize = AccessTools.Field(_infConfig.FieldType, "inflationSize");

            _resolved = _hs2Inflation != null;
            if (_resolved)
            {
                _unavailableLogged = false;
                _methodUnavailableLogged = false;
                HS2OrbitAndExciter.Log?.LogInfo("Orbit: PregnancyPlus integration ready.");
            }
            else if (!_methodUnavailableLogged)
            {
                _methodUnavailableLogged = true;
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: PregnancyPlus loaded but HS2Inflation was not found; will retry.");
            }
        }

        private static Type? FindLoadedType(string fullName)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    var type = assemblies[i].GetType(fullName, throwOnError: false);
                    if (type != null)
                        return type;
                }
                catch
                {
                    // An unrelated mod can transiently fail reflection while its
                    // assembly is loading.  Keep the next retry available.
                }
            }

            return null;
        }
    }
}
