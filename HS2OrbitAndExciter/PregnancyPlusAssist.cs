using System;
using System.Collections.Generic;
using System.Reflection;
using AIChara;
using HarmonyLib;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// PregnancyPlus live shortcuts: Y/U are handled by Preg+ (step scaled by our patch).
    /// R reset often fails for HS2 H-scene belly (<c>HS2Inflation</c> level), so we force deflate on hotkey I.
    /// Optional orgasm-triggered belly grow via <c>HS2Inflation(false)</c> (menu default on).
    /// </summary>
    internal static class PregnancyPlusAssist
    {
        private static Type? _controllerType;
        private static MethodInfo? _hs2Inflation;
        private static MethodInfo? _resetInflation;
        private static MethodInfo? _meshInflateFloat;
        private static MethodInfo? _onInflationChanged;
        private static FieldInfo? _currentInflationLevel;
        private static FieldInfo? _inflationChange;
        private static FieldInfo? _infConfig;
        private static FieldInfo? _inflationSize;
        private static PropertyInfo? _targetPregPlusSize;
        private static PropertyInfo? _currentInflationChange;
        private static Type? _pluginType;
        private static PropertyInfo? _maxLevelEntryProperty;
        private static FieldInfo? _maxLevelEntryField;
        private static HScene? _trackedHScene;
        private static int _lastInsideCount = -1;
        private static bool _resolved;
        private static float _nextResolveAttemptAt;
        private static bool _unavailableLogged;
        private static bool _methodUnavailableLogged;

        private sealed class BellyRuntimeSnapshot
        {
            internal BellyRuntimeSnapshot(
                Component controller,
                int characterInstanceId,
                float inflationSize,
                float inflationChange,
                float targetSize,
                int currentLevel)
            {
                Controller = controller;
                CharacterInstanceId = characterInstanceId;
                InflationSize = inflationSize;
                InflationChange = inflationChange;
                TargetSize = targetSize;
                CurrentLevel = currentLevel;
            }

            internal Component Controller { get; }
            internal int CharacterInstanceId { get; }
            internal float InflationSize { get; }
            internal float InflationChange { get; }
            internal float TargetSize { get; }
            internal int CurrentLevel { get; }
        }

        // Orbit belly changes are H-session effects. Keep the original
        // PregnancyPlus runtime/card values so automatic grow/deflate/reset
        // cannot leak into a character card or survive the H scene.
        private static readonly Dictionary<int, BellyRuntimeSnapshot> RuntimeSnapshots =
            new Dictionary<int, BellyRuntimeSnapshot>();

        private const int DefaultInflationMaxLevel = 18;
        private const int DefaultInflationStep = 1;

        /// <summary>Grow the H-scene belly for any male or female orgasm.</summary>
        internal static bool TryInflateOnOrgasm(HScene? hScene)
        {
            if (HS2OrbitAndExciter.CumflationInflateOnInside?.Value != true)
                return false;
            if (hScene == null)
                return false;

            EnsureResolved();
            if (_controllerType == null || _meshInflateFloat == null)
                return false;

            var females = OrbitHelpers.GetChaFemales(hScene);
            if (females == null || females.Length == 0)
                return false;

            bool any = false;
            var seenFemaleIds = new HashSet<int>();
            for (int i = 0; i < females.Length; i++)
            {
                var cha = females[i];
                if (cha == null || !seenFemaleIds.Add(cha.GetInstanceID()))
                    continue;
                if (TryInflateOnCha(cha))
                    any = true;
            }

            if (any)
                HS2OrbitAndExciter.Log?.LogInfo(
                    $"Orbit: 高潮肚子變大 {InflationStep} 級（自動上限 {InflationMaxLevel} 級）");
            return any;
        }

        /// <summary>
        /// Compatibility entry point retained for the runtime hotfix and older
        /// callers.  Its behavior now follows the all-orgasm setting.
        /// </summary>
        internal static bool TryInflateOnInside(HScene? hScene) => TryInflateOnOrgasm(hScene);

        /// <summary>
        /// Track all native male-finish counters independently of VoiceTour.
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

            int maleFinishes = ctrl.numInside + ctrl.numOutSide + ctrl.numDrink + ctrl.numVomit;
            if (!ReferenceEquals(_trackedHScene, hScene))
            {
                _trackedHScene = hScene;
                _lastInsideCount = maleFinishes;
                return;
            }

            if (maleFinishes <= _lastInsideCount)
            {
                _lastInsideCount = maleFinishes;
                return;
            }

            int delta = maleFinishes - _lastInsideCount;
            _lastInsideCount = maleFinishes;
            if (HS2OrbitAndExciter.CumflationInflateOnInside?.Value != true)
                return;

            for (int i = 0; i < delta; i++)
                TryInflateOnOrgasm(hScene);
        }

        internal static void ResetInsideTracking()
        {
            _trackedHScene = null;
            _lastInsideCount = -1;
        }

        /// <summary>Ensure PregnancyPlus can reach the configured automatic-growth cap.</summary>
        internal static bool TryRaiseMaxInflationLevel()
        {
            EnsureResolved();
            // Automatic growth now uses a private visible-size cap.  Do not
            // assign PregnancyPlus' ConfigEntry here: ConfigEntry.Value saves
            // KK_PregnancyPlus.cfg immediately.
            return _resolved;
#pragma warning disable CS0162
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
                int requested = Math.Max(current, InflationMaxLevel);
                if (current < requested)
                {
                    valueProperty.SetValue(entry, requested, null);
                    HS2OrbitAndExciter.Log?.LogInfo(
                        $"Orbit: PregnancyPlus 肚子上限由 {current} 級提高至 {requested} 級");
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
                EnsureResolved();
                return _resolved
                    ? $"Auto belly cap: {InflationMaxLevel} levels (direct visible update)"
                    : "PregnancyPlus unavailable; automatic belly growth disabled";
#pragma warning disable CS0162
                TryRaiseMaxInflationLevel();
                int level = GetInflationMaxLevel();
                return level > 0
                    ? $"高潮自動上限：{InflationMaxLevel} 級；PregnancyPlus 可用上限：{level} 級"
                    : "PregnancyPlus 未載入：肚子上限未套用";
            }
        }

        internal static int InflationMaxLevel => Mathf.Clamp(
            HS2OrbitAndExciter.CumflationMaxLevel?.Value ?? DefaultInflationMaxLevel,
            1,
            60);

        internal static int InflationStep => Mathf.Clamp(
            HS2OrbitAndExciter.CumflationInflateStep?.Value ?? DefaultInflationStep,
            1,
            10);

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
            if (HS2OrbitAndExciter.CumflationDeflateOnPoseLanding?.Value != true)
                return false;
            if (hScene == null)
                return false;

            EnsureResolved();
            if (_controllerType == null || _meshInflateFloat == null)
                return false;

            var females = OrbitHelpers.GetChaFemales(hScene);
            if (females == null || females.Length == 0)
                return false;

            bool any = false;
            var seenFemaleIds = new HashSet<int>();
            for (int i = 0; i < females.Length; i++)
            {
                var cha = females[i];
                if (cha == null || !seenFemaleIds.Add(cha.GetInstanceID()))
                    continue;
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
            if (ctrl == null || _currentInflationLevel == null || _meshInflateFloat == null)
                return false;
            try
            {
                float before = ReadVisibleInflation(ctrl);
                int maxLevel = InflationMaxLevel;
                int current = VisibleSizeToLevel(before, maxLevel);
                if (current <= 0)
                    return false;

                int next = current - 1;
                return TryApplyDirectVisibleLevel(ctrl, cha, next, maxLevel, "OrbitPoseDeflate", before);
            }
            catch (Exception ex)
            {
                HS2OrbitAndExciter.Log?.LogWarning($"Orbit: PregnancyPlus visible deflate failed: {ex.Message}");
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
            var seenFemaleIds = new HashSet<int>();
            for (int i = 0; i < females.Length; i++)
            {
                var cha = females[i];
                if (cha == null || !seenFemaleIds.Add(cha.GetInstanceID()))
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
            if (ctrl == null || _currentInflationLevel == null || _meshInflateFloat == null)
                return false;

            try
            {
                int maxLevel = InflationMaxLevel;
                float before = ReadVisibleInflation(ctrl);
                int current = VisibleSizeToLevel(before, maxLevel);
                int steps = Math.Min(InflationStep, Math.Max(0, maxLevel - current));
                if (steps == 0)
                    return false;

                return TryApplyDirectVisibleLevel(ctrl, cha, current + steps, maxLevel, "OrbitOrgasm", before);
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

            if (!TryCaptureRuntimeBaseline(ctrl, cha))
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
                _meshInflateFloat.Invoke(ctrl, new object?[] { 0f, "OrbitRReset", null });
                ok = true;
            }

            _targetPregPlusSize?.SetValue(ctrl, 0f, null);
            _inflationChange?.SetValue(ctrl, 0f);

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
                if (_maxLevelEntryProperty == null)
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
            _meshInflateFloat = FindMeshInflateFloatMethod(_controllerType);
            _onInflationChanged = AccessTools.Method(
                _controllerType,
                "OnInflationChanged",
                new[] { typeof(float), typeof(int), typeof(int) });
            _currentInflationLevel = AccessTools.Field(_controllerType, "_currentInflationLevel");
            _inflationChange = AccessTools.Field(_controllerType, "_inflationChange");
            _infConfig = AccessTools.Field(_controllerType, "infConfig");
            if (_infConfig != null)
                _inflationSize = AccessTools.Field(_infConfig.FieldType, "inflationSize");
            _targetPregPlusSize = AccessTools.Property(_controllerType, "TargetPregPlusSize");
            _currentInflationChange = AccessTools.Property(_controllerType, "CurrentInflationChange");

            _resolved = _meshInflateFloat != null && _currentInflationLevel != null &&
                        _inflationChange != null && _infConfig != null && _inflationSize != null &&
                        _targetPregPlusSize != null;
            if (_resolved)
            {
                _unavailableLogged = false;
                _methodUnavailableLogged = false;
                HS2OrbitAndExciter.Log?.LogInfo("Orbit: PregnancyPlus integration ready.");
            }
            else if (!_methodUnavailableLogged)
            {
                _methodUnavailableLogged = true;
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: PregnancyPlus loaded but direct visible inflation API was not found; will retry.");
            }
        }

        private static MethodInfo? FindMeshInflateFloatMethod(Type controllerType)
        {
            var methods = controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                var method = methods[i];
                if (method.Name != "MeshInflate")
                    continue;

                var parameters = method.GetParameters();
                if (parameters.Length == 3 &&
                    parameters[0].ParameterType == typeof(float) &&
                    parameters[1].ParameterType == typeof(string))
                    return method;
            }

            return null;
        }

        /// <summary>
        /// Mirrors the known-good PregnancyPlus Y/U path: synchronize its
        /// target and current weights before requesting one mesh update.
        /// OrbitRuntimeHotfix also uses this method name as a capability marker.
        /// </summary>
        private static bool TryApplyDirectVisibleLevel(
            object controller,
            ChaControl cha,
            int level,
            int maxLevel,
            string reason,
            float before)
        {
            if (_meshInflateFloat == null || _currentInflationLevel == null ||
                _inflationChange == null || _targetPregPlusSize == null || maxLevel <= 0)
                return false;

            if (!(controller is Component component) || !TryCaptureRuntimeBaseline(component, cha))
                return false;

            try
            {
                int clampedLevel = Mathf.Clamp(level, 0, maxLevel);
                float size = Mathf.Clamp(clampedLevel * 40f / maxLevel, 0f, 40f);
                _currentInflationLevel.SetValue(controller, clampedLevel);
                _targetPregPlusSize.SetValue(controller, size, null);
                _inflationChange.SetValue(controller, size);
                _meshInflateFloat.Invoke(controller, new object?[] { size, reason, null });
                HS2OrbitAndExciter.Log?.LogInfo(
                    $"Orbit: PregnancyPlus visible belly {before:F2} -> {size:F2} " +
                    $"(level {clampedLevel}/{maxLevel}, {reason})");
                return true;
            }
            catch (Exception ex)
            {
                HS2OrbitAndExciter.Log?.LogWarning($"Orbit: PregnancyPlus visible belly update failed: {ex.Message}");
                return false;
            }
        }

        private static bool TryCaptureRuntimeBaseline(Component controller, ChaControl cha)
        {
            int instanceId = controller.GetInstanceID();
            if (RuntimeSnapshots.ContainsKey(instanceId))
                return true;

            if (_currentInflationLevel == null || _inflationChange == null ||
                _infConfig == null || _inflationSize == null || _targetPregPlusSize == null)
                return false;

            try
            {
                object? config = _infConfig.GetValue(controller);
                if (config == null)
                    return false;

                var snapshot = new BellyRuntimeSnapshot(
                    controller,
                    cha.GetInstanceID(),
                    Convert.ToSingle(_inflationSize.GetValue(config)),
                    Convert.ToSingle(_inflationChange.GetValue(controller)),
                    Convert.ToSingle(_targetPregPlusSize.GetValue(controller, null)),
                    Convert.ToInt32(_currentInflationLevel.GetValue(controller)));
                RuntimeSnapshots.Add(instanceId, snapshot);
                HS2OrbitAndExciter.Log?.LogInfo(
                    $"Orbit: belly session baseline {snapshot.InflationSize:F2} " +
                    $"(level {snapshot.CurrentLevel})");
                return true;
            }
            catch (Exception ex)
            {
                HS2OrbitAndExciter.Log?.LogWarning(
                    $"Orbit: belly session baseline capture failed; effect skipped: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Put the pre-H PregnancyPlus size into its serializable data just for
        /// OnCardBeingSaved. The mesh and the rest of the runtime state remain
        /// enlarged until the save callback completes.
        /// </summary>
        internal static bool TryPrepareBellyForSave(object controller, out float runtimeValue)
        {
            runtimeValue = 0f;
            if (!(controller is Component component)
                || !RuntimeSnapshots.TryGetValue(component.GetInstanceID(), out BellyRuntimeSnapshot snapshot)
                || _infConfig == null
                || _inflationSize == null)
                return false;

            try
            {
                object? config = _infConfig.GetValue(controller);
                if (config == null)
                    return false;

                float current = Convert.ToSingle(_inflationSize.GetValue(config));
                if (Mathf.Approximately(current, snapshot.InflationSize))
                    return false;

                runtimeValue = current;
                _inflationSize.SetValue(config, snapshot.InflationSize);
                HS2OrbitAndExciter.Log?.LogInfo(
                    $"Orbit: belly save guard {current:F2} -> {snapshot.InflationSize:F2}");
                return true;
            }
            catch (Exception ex)
            {
                HS2OrbitAndExciter.Log?.LogWarning($"Orbit: belly save guard failed: {ex.Message}");
                return false;
            }
        }

        internal static void RestoreBellyAfterSave(object controller, float runtimeValue)
        {
            if (float.IsNaN(runtimeValue) || _infConfig == null || _inflationSize == null)
                return;

            try
            {
                object? config = _infConfig.GetValue(controller);
                if (config != null)
                    _inflationSize.SetValue(config, runtimeValue);
            }
            catch (Exception ex)
            {
                HS2OrbitAndExciter.Log?.LogWarning(
                    $"Orbit: belly runtime restore after save failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Restore only the character whose ChaControl is about to be reused by
        /// G swap. Other females in a multi-female scene keep their own session
        /// growth and snapshots.
        /// </summary>
        internal static bool TryRestoreForCharacterSwap(ChaControl? cha, string reason)
        {
            if (cha == null || RuntimeSnapshots.Count == 0)
                return false;

            int characterInstanceId = cha.GetInstanceID();
            var controllerIds = new List<int>();
            foreach (var pair in RuntimeSnapshots)
            {
                if (pair.Value.CharacterInstanceId == characterInstanceId)
                    controllerIds.Add(pair.Key);
            }

            bool any = false;
            for (int i = 0; i < controllerIds.Count; i++)
            {
                int controllerId = controllerIds[i];
                BellyRuntimeSnapshot snapshot = RuntimeSnapshots[controllerId];
                // Remove first so a destroyed or otherwise broken controller
                // cannot leave a stale baseline for the replacement card.
                RuntimeSnapshots.Remove(controllerId);
                if (TryRestoreSnapshot(snapshot, reason))
                    any = true;
            }
            return any;
        }

        /// <summary>Restore all Orbit-owned PregnancyPlus changes at H teardown.</summary>
        internal static bool TryRestoreForLifecycle(string reason)
        {
            if (RuntimeSnapshots.Count == 0)
                return false;

            var snapshots = new List<BellyRuntimeSnapshot>(RuntimeSnapshots.Values);
            RuntimeSnapshots.Clear();
            bool any = false;
            for (int i = 0; i < snapshots.Count; i++)
            {
                BellyRuntimeSnapshot snapshot = snapshots[i];
                if (TryRestoreSnapshot(snapshot, reason))
                    any = true;
            }

            return any;
        }

        private static bool TryRestoreSnapshot(BellyRuntimeSnapshot snapshot, string reason)
        {
            try
            {
                Component controller = snapshot.Controller;
                object? config = _infConfig?.GetValue(controller);
                if (config != null && _inflationSize != null)
                    _inflationSize.SetValue(config, snapshot.InflationSize);
                _currentInflationLevel?.SetValue(controller, snapshot.CurrentLevel);
                _targetPregPlusSize?.SetValue(controller, snapshot.TargetSize, null);
                _inflationChange?.SetValue(controller, snapshot.InflationChange);

                // Rebuild the visible mesh at the original H-entry size.
                _meshInflateFloat?.Invoke(
                    controller,
                    new object?[] { snapshot.InflationSize, "OrbitLifecycleRestore", null });

                // MeshInflate owns inflationSize but not the HS2 runtime
                // counters; make those exact again after its synchronous setup.
                _currentInflationLevel?.SetValue(controller, snapshot.CurrentLevel);
                _targetPregPlusSize?.SetValue(controller, snapshot.TargetSize, null);
                _inflationChange?.SetValue(controller, snapshot.InflationChange);
                HS2OrbitAndExciter.Log?.LogInfo(
                    $"Orbit: belly lifecycle restore ({reason}) -> {snapshot.InflationSize:F2} " +
                    $"(level {snapshot.CurrentLevel})");
                return true;
            }
            catch (Exception ex)
            {
                HS2OrbitAndExciter.Log?.LogWarning(
                    $"Orbit: belly lifecycle restore ({reason}) failed: {ex.Message}");
                return false;
            }
        }

        private static int VisibleSizeToLevel(float size, int maxLevel)
        {
            if (maxLevel <= 0)
                return 0;
            return Mathf.Clamp(
                Mathf.FloorToInt(Mathf.Clamp(size, 0f, 40f) * maxLevel / 40f + 0.0001f),
                0,
                maxLevel);
        }

        private static float ReadVisibleInflation(object controller)
        {
            float visible = 0f;
            try
            {
                object? config = _infConfig?.GetValue(controller);
                if (config != null && _inflationSize != null)
                    visible = Math.Max(visible, Convert.ToSingle(_inflationSize.GetValue(config)));
            }
            catch { }

            try
            {
                object? target = _targetPregPlusSize?.GetValue(controller, null);
                if (target != null)
                    visible = Math.Max(visible, Convert.ToSingle(target));
            }
            catch { }

            try
            {
                object? current = _currentInflationChange?.GetValue(controller, null);
                if (current != null)
                    visible = Math.Max(visible, Convert.ToSingle(current));
            }
            catch { }

            return Mathf.Clamp(visible, 0f, 40f);
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
