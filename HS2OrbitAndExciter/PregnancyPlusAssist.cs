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
        private static bool _resolved;

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
            _resolved = true;

            _controllerType = AccessTools.TypeByName("KK_PregnancyPlus.PregnancyPlusCharaController");
            if (_controllerType == null)
            {
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: PregnancyPlus 未載入，肚子膨脹／清腹略過");
                return;
            }

            _hs2Inflation = AccessTools.Method(_controllerType, "HS2Inflation", new[] { typeof(bool) });
            _resetInflation = AccessTools.Method(_controllerType, "ResetInflation");
            _meshInflateFloat = AccessTools.Method(_controllerType, "MeshInflate", new[] { typeof(float), typeof(string) });
            _currentInflationLevel = AccessTools.Field(_controllerType, "_currentInflationLevel");
            _infConfig = AccessTools.Field(_controllerType, "infConfig");
            if (_infConfig != null)
                _inflationSize = AccessTools.Field(_infConfig.FieldType, "inflationSize");
        }
    }
}
