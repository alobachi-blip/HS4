using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HS2OrbitRuntimeHotfix
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("com.hs2.orbitandexciter", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("KK_PregnancyPlus", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class OrbitRuntimeHotfixPlugin : BaseUnityPlugin
    {
        internal const string PluginGuid = "com.hs2.orbitruntimehotfix";
        internal const string PluginName = "HS2 Orbit Runtime Hotfix";
        internal const string PluginVersion = "1.0.0";

        private const string OrbitAssembly = "HS2OrbitAndExciter";
        private const string PregnancyController = "KK_PregnancyPlus.PregnancyPlusCharaController";

        private bool _pregnancyRepairComplete;
        private bool _vanishPatchInstalled;
        private bool _cameraPatchInstalled;
        private bool _cumflationPatchInstalled;
        private float _nextResolveAt;
        private ConfigEntry<bool>? _verifyFiveStepCumflation;
        private ConfigEntry<bool>? _inflateOnInside;
        private ConfigEntry<bool>? _deflateOnPoseLanding;

        private void Awake()
        {
            RendererOnlyVanishFallback.SetLogger(Logger);
            CumflationRuntime.SetLogger(Logger);
            _verifyFiveStepCumflation = Config.Bind(
                "Diagnostics",
                "VerifyFiveStepCumflation",
                false,
                "One H-scene diagnostic: reset then verify five consecutive PregnancyPlus growth levels.");
            _inflateOnInside = Config.Bind(
                "Cumflation",
                "InflateOnInside",
                true,
                "When true, every male finish grows the PregnancyPlus belly by one level.");
            _deflateOnPoseLanding = Config.Bind(
                "Cumflation",
                "DeflateOnPoseLanding",
                true,
                "When true, foreplay or female-female pose landing reduces the PregnancyPlus belly by one level.");
            CumflationRuntime.SetInflateOnInsideConfig(_inflateOnInside);
            CumflationRuntime.SetDeflateOnPoseLandingConfig(_deflateOnPoseLanding);
            InvokeRepeating(nameof(TryInitialize), 0.5f, 1f);
        }

        private void Update()
        {
            RendererOnlyVanishFallback.RestoreWhenOrbitStops();
            CumflationRuntime.TryStartFiveStepVerification(this, _verifyFiveStepCumflation);
        }

        private void TryInitialize()
        {
            if (Time.realtimeSinceStartup < _nextResolveAt)
                return;
            _nextResolveAt = Time.realtimeSinceStartup + 1f;

            if (!_pregnancyRepairComplete)
                _pregnancyRepairComplete = TryRepairPregnancyIntegration();
            if (!_vanishPatchInstalled)
                _vanishPatchInstalled = TryInstallRendererFallback();
            if (!_cameraPatchInstalled)
                _cameraPatchInstalled = TryInstallCameraStabilizer();
            if (!_cumflationPatchInstalled)
                _cumflationPatchInstalled = TryInstallCumflationDeflateFix();

            if (_pregnancyRepairComplete && _vanishPatchInstalled && _cameraPatchInstalled && _cumflationPatchInstalled)
                CancelInvoke(nameof(TryInitialize));
        }

        private bool TryRepairPregnancyIntegration()
        {
            if (FindLoadedType(PregnancyController) == null)
                return false;

            var assistType = FindLoadedType("HS2OrbitAndExciter.PregnancyPlusAssist", OrbitAssembly);
            if (assistType == null)
                return false;

            var resolved = AccessTools.Field(assistType, "_resolved");
            var raiseCap = AccessTools.Method(assistType, "TryRaiseMaxInflationLevel");
            if (resolved == null || raiseCap == null)
            {
                Logger.LogWarning("Orbit PregnancyPlus integration has an unexpected shape; hotfix skipped.");
                return true;
            }

            try
            {
                // Old Orbit caches the startup lookup before PregnancyPlus is
                // loaded.  Clear only that cache, then invoke its own resolver.
                resolved.SetValue(null, false);
                raiseCap.Invoke(null, null);
                if (resolved.GetValue(null) is bool ready && ready)
                {
                    Logger.LogInfo("Rebound Orbit to PregnancyPlus after plugin load.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not rebind Orbit to PregnancyPlus yet: " + ex.Message);
            }

            return false;
        }

        private bool TryInstallRendererFallback()
        {
            var vanishType = FindLoadedType("HS2OrbitAndExciter.OrbitMapVanishAssist", OrbitAssembly);
            var target = vanishType == null
                ? null
                : AccessTools.Method(vanishType, "ApplyDirectLineOfSight", new[] { typeof(Vector3), typeof(Vector3) });
            if (target == null)
                return false;

            try
            {
                new Harmony(PluginGuid).Patch(
                    target,
                    postfix: new HarmonyMethod(typeof(OrbitRuntimeHotfixPlugin), nameof(DirectLineOfSightPostfix)));
                RendererOnlyVanishFallback.Initialize(vanishType!);
                Logger.LogInfo("Installed render-only map-occlusion fallback.");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not patch Orbit map occlusion yet: " + ex.Message);
                return false;
            }
        }

        private bool TryInstallCameraStabilizer()
        {
            var controllerType = FindLoadedType("HS2OrbitAndExciter.OrbitController", OrbitAssembly);
            if (controllerType == null)
                return false;

            var bodyAxisCamera = AccessTools.Method(controllerType, "ApplyBodyAxisCamera");
            var boneFocusOnly = AccessTools.Method(controllerType, "ApplyBoneFocusOnly");
            if (bodyAxisCamera == null || boneFocusOnly == null)
            {
                Logger.LogWarning("Orbit camera focus methods were not found; stabilizer skipped.");
                return true;
            }

            try
            {
                var harmony = new Harmony(PluginGuid + ".camera");
                var postfix = new HarmonyMethod(typeof(OrbitRuntimeHotfixPlugin), nameof(StableFocusPostfix));
                harmony.Patch(bodyAxisCamera, postfix: postfix);
                harmony.Patch(boneFocusOnly, postfix: postfix);
                LockedFocusStabilizer.Initialize(controllerType);
                Logger.LogInfo("Installed locked body-relative camera focus stabilizer.");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not patch Orbit camera focus yet: " + ex.Message);
                return false;
            }
        }

        private bool TryInstallCumflationDeflateFix()
        {
            var assistType = FindLoadedType("HS2OrbitAndExciter.PregnancyPlusAssist", OrbitAssembly);
            var deflate = assistType == null ? null : AccessTools.Method(assistType, "TryDeflateOneLevel");
            var inflate = assistType == null ? null : AccessTools.Method(assistType, "TryInflateOnInside");
            var tick = assistType == null ? null : AccessTools.Method(assistType, "TickInsideFinish");
            if (deflate == null || inflate == null || tick == null)
                return false;

            try
            {
                var harmony = new Harmony(PluginGuid + ".cumflation");
                harmony.Patch(
                    deflate,
                    prefix: new HarmonyMethod(typeof(OrbitRuntimeHotfixPlugin), nameof(DeflateOneLevelPrefix)));
                harmony.Patch(
                    inflate,
                    prefix: new HarmonyMethod(typeof(OrbitRuntimeHotfixPlugin), nameof(InflateOnInsidePrefix)));
                harmony.Patch(
                    tick,
                    prefix: new HarmonyMethod(typeof(OrbitRuntimeHotfixPlugin), nameof(TickInsideFinishPrefix)));
                CumflationRuntime.Initialize();
                Logger.LogInfo("Installed orgasm-triggered PregnancyPlus growth and one-level deflate controls.");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not patch Orbit cumflation deflate yet: " + ex.Message);
                return false;
            }
        }

        private static void DirectLineOfSightPostfix(Vector3 origin, Vector3 target)
        {
            RendererOnlyVanishFallback.Apply(origin, target);
        }

        private static void StableFocusPostfix(object __instance, object[] __args)
        {
            LockedFocusStabilizer.Apply(__instance, __args);
        }

        private static bool DeflateOneLevelPrefix(object[] __args, ref bool __result)
        {
            if (!CumflationRuntime.DeflateOnPoseLandingEnabled)
            {
                __result = false;
                return false;
            }
            __result = CumflationRuntime.TryDecreaseOneLevel(__args.Length > 0 ? __args[0] : null);
            // The original build calls HS2Inflation(true), which is a full reset.
            // Always suppress it, including while PregnancyPlus is still loading.
            return false;
        }

        private static bool InflateOnInsidePrefix(object[] __args, ref bool __result)
        {
            __result = CumflationRuntime.InflateOnInsideEnabled
                && CumflationRuntime.TryIncreaseOneLevel(__args.Length > 0 ? __args[0] : null);
            return false;
        }

        private static bool TickInsideFinishPrefix(object[] __args)
        {
            CumflationRuntime.TickInsideFinish(__args.Length > 0 ? __args[0] : null);
            return false;
        }

        private static Type? FindLoadedType(string fullName, string? assemblyName = null)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                var assembly = assemblies[i];
                if (assemblyName != null && !string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal))
                    continue;
                try
                {
                    var type = assembly.GetType(fullName, throwOnError: false);
                    if (type != null)
                        return type;
                }
                catch
                {
                    // Keep the next scheduled attempt available while another
                    // optional plugin is still loading.
                }
            }

            return null;
        }
    }

    /// <summary>
    /// The production Orbit DLL already locks its orientation axes to the body
    /// root, but it keeps overwriting TargetPos with the live selected bone. This
    /// adapter uses the existing locked focus local coordinate after the original
    /// camera write, so normal animation cannot shake the viewpoint.
    /// </summary>
    internal static class LockedFocusStabilizer
    {
        private static FieldInfo? _lockedBasisValid;
        private static FieldInfo? _lockedFemaleIdx;
        private static FieldInfo? _lockedFocusLocal;
        private static MethodInfo? _getBodyRoot;
        private static MethodInfo? _getChaFemales;
        private static PropertyInfo? _targetPosProperty;
        private static FieldInfo? _targetPosField;
        private static FieldInfo? _transBaseField;

        internal static void Initialize(Type controllerType)
        {
            _lockedBasisValid = AccessTools.Field(controllerType, "_lockedBasisValid");
            _lockedFemaleIdx = AccessTools.Field(controllerType, "_lockedFemaleIdx");
            _lockedFocusLocal = AccessTools.Field(controllerType, "_lockedFocusLocal");
            _getBodyRoot = AccessTools.Method(controllerType, "GetBodyRoot");
            var helpers = FindLoadedType("HS2OrbitAndExciter.OrbitHelpers", "HS2OrbitAndExciter");
            _getChaFemales = helpers == null ? null : AccessTools.Method(helpers, "GetChaFemales");
        }

        internal static void Apply(object controller, object[] args)
        {
            if (controller == null || args == null || args.Length < 2 || args[0] == null || args[1] == null ||
                _lockedBasisValid == null || _lockedFemaleIdx == null || _lockedFocusLocal == null ||
                _getBodyRoot == null || _getChaFemales == null)
                return;

            try
            {
                if (!(_lockedBasisValid.GetValue(controller) is bool locked) || !locked ||
                    !(_lockedFemaleIdx.GetValue(controller) is int femaleIdx) ||
                    !(_lockedFocusLocal.GetValue(controller) is Vector3 focusLocal))
                    return;

                object? females = _getChaFemales.Invoke(null, new[] { args[0] });
                if (females == null)
                    return;
                var body = _getBodyRoot.Invoke(null, new object[] { females, femaleIdx }) as Transform;
                if (body == null)
                    return;

                object cameraControl = args[1];
                var transBase = GetTransBase(cameraControl);
                if (transBase == null)
                    return;

                SetTargetPos(cameraControl, transBase.InverseTransformPoint(body.TransformPoint(focusLocal)));
            }
            catch
            {
                // The original camera writer remains authoritative if a future
                // Orbit build changes one of these internal members.
            }
        }

        private static Transform? GetTransBase(object cameraControl)
        {
            _transBaseField ??= AccessTools.Field(cameraControl.GetType(), "transBase");
            return _transBaseField?.GetValue(cameraControl) as Transform;
        }

        private static void SetTargetPos(object cameraControl, Vector3 value)
        {
            var type = cameraControl.GetType();
            _targetPosProperty ??= AccessTools.Property(type, "TargetPos");
            if (_targetPosProperty != null && _targetPosProperty.CanWrite)
            {
                _targetPosProperty.SetValue(cameraControl, value, null);
                return;
            }

            _targetPosField ??= AccessTools.Field(type, "TargetPos");
            _targetPosField?.SetValue(cameraControl, value);
        }

        private static Type? FindLoadedType(string fullName, string? assemblyName = null)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                var assembly = assemblies[i];
                if (assemblyName != null && !string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal))
                    continue;
                try
                {
                    var type = assembly.GetType(fullName, throwOnError: false);
                    if (type != null)
                        return type;
                }
                catch { }
            }

            return null;
        }
    }

    /// <summary>
    /// Adapts the deployed Orbit build to the actual PregnancyPlus HS2 API.
    /// HS2Inflation(true) means reset, so a one-level deflate must notify the
    /// controller at the preceding level instead.  It also owns the opt-in
    /// five-step in-game verification used to prove the increment path.
    /// </summary>
    internal static class CumflationRuntime
    {
        private const string OrbitAssembly = "HS2OrbitAndExciter";
        private const string PregnancyController = "KK_PregnancyPlus.PregnancyPlusCharaController";

        private static ManualLogSource? _log;
        private static Type? _controllerType;
        private static MethodInfo? _hs2Inflation;
        private static MethodInfo? _onInflationChanged;
        private static FieldInfo? _currentInflationLevel;
        private static FieldInfo? _initialized;
        private static MethodInfo? _getHScene;
        private static MethodInfo? _getChaFemales;
        private static PropertyInfo? _targetPregPlusSize;
        private static PropertyInfo? _currentInflationChange;
        private static PropertyInfo? _maxLevelEntry;
        private static FieldInfo? _maxLevelEntryField;
        private static bool _verificationRunning;
        private static bool _verificationStarted;
        private static bool _suppressDeflate;
        private static float _nextVerificationAt;
        private static ConfigEntry<bool>? _inflateOnInside;
        private static ConfigEntry<bool>? _deflateOnPoseLanding;
        private static FieldInfo? _orbitInflateOnInside;
        private static bool _checkedOrbitInflateSetting;
        private static FieldInfo? _orbitMaxLevel;
        private static bool _checkedOrbitMaxLevel;
        private static FieldInfo? _orbitInflateStep;
        private static bool _checkedOrbitInflateStep;
        private static FieldInfo? _orbitDeflateOnPoseLanding;
        private static bool _checkedOrbitDeflateSetting;
        private static object? _trackedHScene;
        private static int _lastInsideCount = -1;

        internal static void SetLogger(ManualLogSource logger) => _log = logger;

        internal static void SetInflateOnInsideConfig(ConfigEntry<bool> setting)
        {
            _inflateOnInside = setting;
        }

        internal static void SetDeflateOnPoseLandingConfig(ConfigEntry<bool> setting)
        {
            _deflateOnPoseLanding = setting;
        }

        internal static bool DeflateOnPoseLandingEnabled
        {
            get
            {
                if (TryReadOrbitDeflateSetting(out bool value))
                    return value;
                return _deflateOnPoseLanding?.Value ?? true;
            }
        }

        internal static bool InflateOnInsideEnabled
        {
            get
            {
                if (TryReadOrbitInflateSetting(out bool value))
                    return value;
                return _inflateOnInside?.Value ?? true;
            }
        }

        private static int AutomaticInflationMaxLevel => ReadOrbitIntSetting(
            "CumflationMaxLevel",
            ref _orbitMaxLevel,
            ref _checkedOrbitMaxLevel,
            18,
            1,
            60);

        private static int InflationStep => ReadOrbitIntSetting(
            "CumflationInflateStep",
            ref _orbitInflateStep,
            ref _checkedOrbitInflateStep,
            1,
            1,
            10);

        internal static void Initialize()
        {
            Resolve();
        }

        internal static bool TryDecreaseOneLevel(object? hScene)
        {
            if (_suppressDeflate)
            {
                _log?.LogInfo("[CumflationVerify] suppressed a pose-change deflate during verification.");
                return false;
            }

            if (!Resolve() || hScene == null || !TryGetControllers(hScene, out var controllers))
                return false;

            bool changed = false;
            int max = GetMaxLevel();
            for (int i = 0; i < controllers.Count; i++)
            {
                var controller = controllers[i];
                int current = GetLevel(controller);
                if (current <= 0)
                    continue;

                int next = current - 1;
                SetLevelAndNotify(controller, next, Math.Max(max, current));
                changed = true;
                _log?.LogInfo("[Cumflation] deflate level " + current + " -> " + next + ".");
            }

            return changed;
        }

        internal static bool TryIncreaseOneLevel(object? hScene)
        {
            if (!Resolve() || hScene == null || !TryGetControllers(hScene, out var controllers))
                return false;

            int maxLevel = AutomaticInflationMaxLevel;
            EnsurePregnancyPlusMaxLevel(maxLevel);
            int requestedSteps = InflationStep;
            bool changed = false;
            int appliedSteps = 0;
            for (int i = 0; i < controllers.Count; i++)
            {
                int current = GetLevel(controllers[i]);
                if (current < 0)
                    continue;

                int steps = Math.Min(requestedSteps, Math.Max(0, maxLevel - current));
                for (int step = 0; step < steps; step++)
                {
                    if (!TryInvokeInflation(controllers[i], false))
                        break;
                    changed = true;
                    appliedSteps++;
                }
            }

            if (changed)
                _log?.LogInfo(
                    "[Cumflation] orgasm increased belly by " + appliedSteps +
                    " level(s); automatic cap=" + maxLevel + ".");
            return changed;
        }

        internal static void TickInsideFinish(object? hScene)
        {
            if (hScene == null)
            {
                _trackedHScene = null;
                _lastInsideCount = -1;
                return;
            }

            if (!TryGetMaleFinishCount(hScene, out int maleFinishes))
                return;

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
            if (!InflateOnInsideEnabled)
                return;

            for (int i = 0; i < delta; i++)
                TryIncreaseOneLevel(hScene);
        }

        internal static void TryStartFiveStepVerification(MonoBehaviour host, ConfigEntry<bool>? requested)
        {
            if (requested == null || !requested.Value || _verificationRunning || _verificationStarted ||
                Time.realtimeSinceStartup < _nextVerificationAt || !Resolve())
                return;

            var hScene = GetHScene();
            if (hScene == null || !TryGetControllers(hScene, out var controllers) || controllers.Count == 0)
                return;

            _verificationStarted = true;
            _verificationRunning = true;
            host.StartCoroutine(VerifyFiveSteps(controllers, requested));
        }

        private static IEnumerator VerifyFiveSteps(List<Component> controllers, ConfigEntry<bool> requested)
        {
            _suppressDeflate = true;
            _log?.LogInfo("[CumflationVerify] starting controlled five-step PregnancyPlus check.");

            string? failure = null;
            for (int i = 0; i < controllers.Count; i++)
            {
                if (!TryInvokeInflation(controllers[i], true))
                {
                    failure = "could not reset PregnancyPlus controller";
                    break;
                }
            }

            yield return new WaitForSecondsRealtime(0.75f);
            for (int i = 0; i < controllers.Count && failure == null; i++)
            {
                int level = GetLevel(controllers[i]);
                if (level != 0)
                {
                    failure = "reset expected level 0, got " + level;
                    break;
                }
            }

            float? previousTarget = null;
            if (failure == null && controllers.Count > 0)
                previousTarget = ReadFloat(_targetPregPlusSize, controllers[0]);

            for (int step = 1; step <= 5 && failure == null; step++)
            {
                for (int i = 0; i < controllers.Count; i++)
                {
                    if (!TryInvokeInflation(controllers[i], false))
                    {
                        failure = "could not invoke growth at step " + step;
                        break;
                    }
                }

                if (failure != null)
                    break;
                yield return null;

                for (int i = 0; i < controllers.Count; i++)
                {
                    int level = GetLevel(controllers[i]);
                    if (level != step)
                    {
                        failure = "step " + step + " expected level " + step + ", got " + level;
                        break;
                    }
                }

                // PregnancyPlus animates at the configured speed.  Let the
                // target and mesh settle before accepting each next step.
                yield return new WaitForSecondsRealtime(3.5f);
                if (failure != null)
                    break;

                float? target = ReadFloat(_targetPregPlusSize, controllers[0]);
                float? current = ReadFloat(_currentInflationChange, controllers[0]);
                if (target.HasValue && previousTarget.HasValue && target.Value <= previousTarget.Value + 0.0001f)
                {
                    failure = "step " + step + " target size did not increase (" +
                              previousTarget.Value.ToString("F4") + " -> " + target.Value.ToString("F4") + ")";
                    break;
                }

                _log?.LogInfo("[CumflationVerify] step " + step + "/5 level=" + GetLevel(controllers[0]) +
                              " target=" + Format(target) + " current=" + Format(current));
                if (target.HasValue)
                    previousTarget = target;
            }

            _suppressDeflate = false;
            _verificationRunning = false;

            if (failure == null)
            {
                requested.Value = false;
                _log?.LogInfo("[CumflationVerify] PASS levels 0 -> 1 -> 2 -> 3 -> 4 -> 5; every target value increased.");
                yield break;
            }

            _log?.LogError("[CumflationVerify] FAILED: " + failure + "; will retry while the diagnostic config remains enabled.");
            _verificationStarted = false;
            _nextVerificationAt = Time.realtimeSinceStartup + 6f;
        }

        private static bool Resolve()
        {
            _controllerType ??= FindLoadedType(PregnancyController);
            if (_controllerType == null)
                return false;

            _hs2Inflation ??= AccessTools.Method(_controllerType, "HS2Inflation", new[] { typeof(bool) });
            _onInflationChanged ??= AccessTools.Method(
                _controllerType,
                "OnInflationChanged",
                new[] { typeof(float), typeof(int), typeof(int) });
            _currentInflationLevel ??= AccessTools.Field(_controllerType, "_currentInflationLevel");
            _initialized ??= AccessTools.Field(_controllerType, "initialized");
            _targetPregPlusSize ??= _controllerType.GetProperty("TargetPregPlusSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _currentInflationChange ??= _controllerType.GetProperty("CurrentInflationChange", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            var orbitType = FindLoadedType("HS2OrbitAndExciter.OrbitController", OrbitAssembly);
            _getHScene ??= orbitType == null ? null : AccessTools.Method(orbitType, "TryGetHScene");
            var helperType = FindLoadedType("HS2OrbitAndExciter.OrbitHelpers", OrbitAssembly);
            _getChaFemales ??= helperType == null ? null : AccessTools.Method(helperType, "GetChaFemales");

            var pluginType = FindLoadedType("KK_PregnancyPlus.PregnancyPlusPlugin");
            if (pluginType != null)
            {
                _maxLevelEntry ??= AccessTools.Property(pluginType, "HS2InflationMaxLevel");
                _maxLevelEntryField ??= AccessTools.Field(pluginType, "HS2InflationMaxLevel");
            }

            return _hs2Inflation != null && _onInflationChanged != null && _currentInflationLevel != null &&
                   _getHScene != null && _getChaFemales != null;
        }

        private static bool TryReadOrbitDeflateSetting(out bool value)
        {
            value = false;
            if (!_checkedOrbitDeflateSetting)
            {
                var orbitPluginType = FindLoadedType("HS2OrbitAndExciter.HS2OrbitAndExciter", OrbitAssembly);
                _orbitDeflateOnPoseLanding = orbitPluginType?.GetField(
                    "CumflationDeflateOnPoseLanding",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                _checkedOrbitDeflateSetting = true;
            }

            if (_orbitDeflateOnPoseLanding == null)
                return false;

            try
            {
                object? entry = _orbitDeflateOnPoseLanding.GetValue(null);
                object? configured = entry?.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry, null);
                if (configured is bool boolValue)
                {
                    value = boolValue;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static bool TryReadOrbitInflateSetting(out bool value)
        {
            value = false;
            if (!_checkedOrbitInflateSetting)
            {
                var orbitPluginType = FindLoadedType("HS2OrbitAndExciter.HS2OrbitAndExciter", OrbitAssembly);
                _orbitInflateOnInside = orbitPluginType?.GetField(
                    "CumflationInflateOnInside",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                _checkedOrbitInflateSetting = true;
            }

            if (_orbitInflateOnInside == null)
                return false;

            try
            {
                object? entry = _orbitInflateOnInside.GetValue(null);
                object? configured = entry?.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry, null);
                if (configured is bool boolValue)
                {
                    value = boolValue;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static int ReadOrbitIntSetting(
            string fieldName,
            ref FieldInfo? field,
            ref bool checkedSetting,
            int fallback,
            int min,
            int max)
        {
            if (!checkedSetting)
            {
                var orbitPluginType = FindLoadedType("HS2OrbitAndExciter.HS2OrbitAndExciter", OrbitAssembly);
                field = orbitPluginType?.GetField(
                    fieldName,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                checkedSetting = true;
            }

            try
            {
                object? entry = field?.GetValue(null);
                object? configured = entry?.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry, null);
                return configured == null
                    ? fallback
                    : Math.Max(min, Math.Min(max, Convert.ToInt32(configured)));
            }
            catch
            {
                return fallback;
            }
        }

        private static void EnsurePregnancyPlusMaxLevel(int requested)
        {
            try
            {
                object? entry = _maxLevelEntry?.GetValue(null, null) ?? _maxLevelEntryField?.GetValue(null);
                var property = entry?.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
                if (property == null || !property.CanRead || !property.CanWrite)
                    return;

                int current = Convert.ToInt32(property.GetValue(entry, null));
                if (current < requested)
                    property.SetValue(entry, requested, null);
            }
            catch { }
        }

        private static object? GetHScene()
        {
            try { return _getHScene?.Invoke(null, null); }
            catch { return null; }
        }

        private static bool TryGetMaleFinishCount(object hScene, out int count)
        {
            count = 0;
            try
            {
                var sceneType = hScene.GetType();
                object? ctrlFlag = sceneType.GetField("ctrlFlag", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(hScene)
                    ?? sceneType.GetProperty("ctrlFlag", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(hScene, null);
                if (ctrlFlag == null)
                    return false;

                var flagType = ctrlFlag.GetType();
                if (!TryReadCounter(flagType, ctrlFlag, "numInside", out int inside))
                    return false;

                TryReadCounter(flagType, ctrlFlag, "numOutSide", out int outside);
                TryReadCounter(flagType, ctrlFlag, "numDrink", out int drink);
                TryReadCounter(flagType, ctrlFlag, "numVomit", out int vomit);
                count = inside + outside + drink + vomit;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadCounter(Type flagType, object ctrlFlag, string name, out int value)
        {
            value = 0;
            try
            {
                object? raw = flagType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(ctrlFlag)
                    ?? flagType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(ctrlFlag, null);
                if (raw == null)
                    return false;
                value = Convert.ToInt32(raw);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetControllers(object hScene, out List<Component> controllers)
        {
            controllers = new List<Component>();
            try
            {
                if (_getChaFemales?.Invoke(null, new[] { hScene }) is not IEnumerable females)
                    return false;

                foreach (var female in females)
                {
                    if (female is not Component cha || _controllerType == null)
                        continue;
                    var controller = cha.GetComponent(_controllerType) ?? cha.GetComponentInChildren(_controllerType, true);
                    if (controller == null)
                        continue;
                    if (_initialized?.GetValue(controller) is bool initialized && !initialized)
                        continue;
                    controllers.Add(controller);
                }
            }
            catch
            {
                controllers.Clear();
            }

            return controllers.Count > 0;
        }

        private static int GetLevel(Component controller)
        {
            try { return Math.Max(0, Convert.ToInt32(_currentInflationLevel!.GetValue(controller))); }
            catch { return -1; }
        }

        private static void SetLevelAndNotify(Component controller, int level, int maxLevel)
        {
            _currentInflationLevel!.SetValue(controller, level);
            _onInflationChanged!.Invoke(controller, new object[] { (float)level, maxLevel, 0 });
        }

        private static bool TryInvokeInflation(Component controller, bool deflate)
        {
            try
            {
                _hs2Inflation!.Invoke(controller, new object[] { deflate });
                return true;
            }
            catch (Exception ex)
            {
                _log?.LogWarning("[CumflationVerify] HS2Inflation failed: " + ex.Message);
                return false;
            }
        }

        private static int GetMaxLevel()
        {
            try
            {
                object? entry = _maxLevelEntry?.GetValue(null, null) ?? _maxLevelEntryField?.GetValue(null);
                var value = entry?.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry, null);
                return Math.Max(6, Convert.ToInt32(value));
            }
            catch
            {
                return 18;
            }
        }

        private static float? ReadFloat(PropertyInfo? property, Component controller)
        {
            if (property == null)
                return null;
            try { return Convert.ToSingle(property.GetValue(controller, null)); }
            catch { return null; }
        }

        private static string Format(float? value)
        {
            return value.HasValue ? value.Value.ToString("F4") : "n/a";
        }

        private static Type? FindLoadedType(string fullName, string? assemblyName = null)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                var assembly = assemblies[i];
                if (assemblyName != null && !string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal))
                    continue;
                try
                {
                    var type = assembly.GetType(fullName, throwOnError: false);
                    if (type != null)
                        return type;
                }
                catch { }
            }

            return null;
        }
    }

    internal static class RendererOnlyVanishFallback
    {
        private const float SearchIntervalSeconds = 0.05f;
        private static readonly RaycastHit[] RayHits = new RaycastHit[64];

        private static ManualLogSource? _log;
        private static FieldInfo? _mapRootField;
        private static MethodInfo? _isDirectOccluderHidden;
        private static MethodInfo? _isOrbitActive;
        private static Type? _chaControlType;
        private static Renderer[]? _mapRenderers;
        private static int _mapRootId = -1;
        private static Renderer? _hiddenRenderer;
        private static bool _hiddenRendererOriginalEnabled;
        private static float _nextSearchAt;
        private static int _loggedMapRootId = -1;

        internal static void SetLogger(ManualLogSource logger) => _log = logger;

        internal static void Initialize(Type vanishType)
        {
            _mapRootField = AccessTools.Field(vanishType, "_injectedMapRoot");
            _isDirectOccluderHidden = AccessTools.Method(vanishType, "IsDirectOccluderHidden", new[] { typeof(Collider) });
            var orbitType = FindLoadedType("HS2OrbitAndExciter.OrbitController", "HS2OrbitAndExciter");
            _isOrbitActive = orbitType == null ? null : AccessTools.Method(orbitType, "IsOrbitActive");
        }

        internal static void RestoreWhenOrbitStops()
        {
            if (_isOrbitActive == null)
                return;

            try
            {
                if (_isOrbitActive.Invoke(null, null) is bool active && !active)
                    Restore();
            }
            catch
            {
                Restore();
            }
        }

        internal static void Apply(Vector3 origin, Vector3 target)
        {
            if (_mapRootField == null)
                return;

            var mapRoot = _mapRootField.GetValue(null) as GameObject;
            if (mapRoot == null)
            {
                Restore();
                return;
            }

            Vector3 delta = target - origin;
            float distance = delta.magnitude;
            if (distance <= 0.05f)
            {
                Restore();
                return;
            }

            Vector3 direction = delta / distance;
            var collider = FindNearestBlocker(origin, direction, distance);
            if (collider != null && IsAlreadyHidden(collider))
            {
                Restore();
                return;
            }

            var ray = new Ray(origin, direction);
            if (KeepCurrent(ray, distance))
                return;
            if (Time.unscaledTime < _nextSearchAt)
                return;
            _nextSearchAt = Time.unscaledTime + SearchIntervalSeconds;

            int rootId = mapRoot.GetInstanceID();
            if (_mapRenderers == null || _mapRootId != rootId)
            {
                _mapRenderers = mapRoot.GetComponentsInChildren<Renderer>(true);
                _mapRootId = rootId;
                _loggedMapRootId = -1;
            }

            Renderer? closest = null;
            float closestDistance = float.MaxValue;
            var renderers = _mapRenderers;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
                    continue;
                if (!Intersects(renderer, ray, distance, out float hitDistance) || hitDistance >= closestDistance)
                    continue;

                closest = renderer;
                closestDistance = hitDistance;
            }

            if (closest == null)
                return;

            _hiddenRenderer = closest;
            _hiddenRendererOriginalEnabled = closest.enabled;
            closest.enabled = false;
            if (_loggedMapRootId != rootId)
            {
                _loggedMapRootId = rootId;
                _log?.LogInfo("Renderer-only occlusion fallback active for " + closest.name + ".");
            }
        }

        private static Collider? FindNearestBlocker(Vector3 origin, Vector3 direction, float distance)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, RayHits, distance - 0.05f, ~0, QueryTriggerInteraction.Ignore);
            Collider? closest = null;
            float closestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var hit = RayHits[i];
                var collider = hit.collider;
                if (collider == null || IsCharacter(collider) || hit.distance >= closestDistance)
                    continue;
                closest = collider;
                closestDistance = hit.distance;
            }

            return closest;
        }

        private static bool IsAlreadyHidden(Collider collider)
        {
            if (_isDirectOccluderHidden == null)
                return false;
            try { return _isDirectOccluderHidden.Invoke(null, new object[] { collider }) is bool hidden && hidden; }
            catch { return false; }
        }

        private static bool IsCharacter(Collider collider)
        {
            _chaControlType ??= FindLoadedType("AIChara.ChaControl");
            try { return _chaControlType != null && collider.GetComponentInParent(_chaControlType) != null; }
            catch { return false; }
        }

        private static bool KeepCurrent(Ray ray, float distance)
        {
            if (_hiddenRenderer == null)
                return false;
            if (!_hiddenRenderer.gameObject.activeInHierarchy || !Intersects(_hiddenRenderer, ray, distance, out _))
            {
                Restore();
                return false;
            }

            _hiddenRenderer.enabled = false;
            return true;
        }

        private static bool Intersects(Renderer renderer, Ray ray, float distance, out float hitDistance)
        {
            hitDistance = 0f;
            return renderer.bounds.IntersectRay(ray, out hitDistance)
                && hitDistance > 0.05f
                && hitDistance < distance - 0.05f;
        }

        private static void Restore()
        {
            if (_hiddenRenderer != null)
                _hiddenRenderer.enabled = _hiddenRendererOriginalEnabled;
            _hiddenRenderer = null;
            _hiddenRendererOriginalEnabled = false;
        }

        private static Type? FindLoadedType(string fullName, string? assemblyName = null)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                var assembly = assemblies[i];
                if (assemblyName != null && !string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal))
                    continue;
                try
                {
                    var type = assembly.GetType(fullName, throwOnError: false);
                    if (type != null)
                        return type;
                }
                catch { }
            }

            return null;
        }
    }
}
