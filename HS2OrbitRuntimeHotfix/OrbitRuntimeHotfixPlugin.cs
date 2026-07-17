using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
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
        private float _nextResolveAt;

        private void Awake()
        {
            RendererOnlyVanishFallback.SetLogger(Logger);
            InvokeRepeating(nameof(TryInitialize), 0.5f, 1f);
        }

        private void Update()
        {
            RendererOnlyVanishFallback.RestoreWhenOrbitStops();
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

            if (_pregnancyRepairComplete && _vanishPatchInstalled && _cameraPatchInstalled)
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

        private static void DirectLineOfSightPostfix(Vector3 origin, Vector3 target)
        {
            RendererOnlyVanishFallback.Apply(origin, target);
        }

        private static void StableFocusPostfix(object __instance, object[] __args)
        {
            LockedFocusStabilizer.Apply(__instance, __args);
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
