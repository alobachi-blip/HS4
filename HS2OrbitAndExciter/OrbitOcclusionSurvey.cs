using System;
using System.Collections.Generic;
using HarmonyLib;
using Manager;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// Diagnostic-only center-line occlusion survey. Character renderers and
    /// partial silhouette/edge coverage are intentionally outside its scope.
    /// It compares the camera transform used by the current production ray with
    /// the transform CameraControl_Ver2 will apply later in LateUpdate.
    /// </summary>
    internal static class OrbitOcclusionSurvey
    {
        private const float SampleIntervalSeconds = 0.1f;
        private const float TransitionOriginDeltaMeters = 0.25f;
        private const float RayEndPaddingMeters = 0.05f;
        private const float InsideProbeRadiusMeters = 0.02f;
        private const int MaxRendererSearchDepth = 8;
        private const int MaxRenderersPerSearchNode = 256;

        private sealed class VisualHit
        {
            internal Collider? Collider;
            internal Renderer? Renderer;
            internal string Signature = "";
            internal string Name = "";
            internal float Distance;
            internal bool Visible;
            internal bool IsTrigger;
        }

        private sealed class FrameSample
        {
            internal HScene? HScene;
            internal CameraControl_Ver2? Control;
            internal Transform? CameraTransform;
            internal Vector3 ActualOrigin;
            internal Vector3 FinalOrigin;
            internal Vector3 Target;
            internal int FocusIndex;
            internal int MapId;
            internal List<VisualHit> BeforeActual = new List<VisualHit>();
            internal List<VisualHit> BeforeFinal = new List<VisualHit>();
            internal List<VisualHit> BeforeTriggers = new List<VisualHit>();
            internal List<VisualHit> Inside = new List<VisualHit>();
            internal VisualHit? RendererOnlyBefore;
        }

        private static FrameSample? _pending;
        private static float _nextSampleAt;
        private static float _nextSummaryAt;
        private static int _samples;
        private static int _issueSamples;
        private static readonly Dictionary<string, int> CategoryCounts =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private static GameObject? _cachedMapRoot;
        private static int _cachedMapRootId = -1;
        private static Renderer[] _cachedMapRenderers = Array.Empty<Renderer>();

        internal static bool Enabled => HS2OrbitAndExciter.EnableOcclusionSurvey?.Value == true;

        internal static void Reset()
        {
            if (_samples > 0)
                LogSummary("reset");
            _pending = null;
            _nextSampleAt = 0f;
            _nextSummaryAt = 0f;
            _samples = 0;
            _issueSamples = 0;
            CategoryCounts.Clear();
            _cachedMapRoot = null;
            _cachedMapRootId = -1;
            _cachedMapRenderers = Array.Empty<Renderer>();
        }

        internal static void Prepare(
            HScene hScene,
            CameraControl_Ver2 control,
            Camera camera,
            Vector3 target,
            int focusIndex)
        {
            _pending = null;
            if (!Enabled || hScene == null || control == null || camera == null ||
                Time.unscaledTime < _nextSampleAt)
            {
                return;
            }

            _nextSampleAt = Time.unscaledTime + SampleIntervalSeconds;
            _pending = new FrameSample
            {
                HScene = hScene,
                Control = control,
                CameraTransform = camera.transform,
                ActualOrigin = camera.transform.position,
                FinalOrigin = PredictFinalCameraPosition(control),
                Target = target,
                FocusIndex = focusIndex,
                MapId = GetMapId()
            };
        }

        /// <summary>Called after production has restored the previous frame's direct hides.</summary>
        internal static void CaptureBeforeAssist(Vector3 productionOrigin, Vector3 productionTarget)
        {
            var frame = _pending;
            if (frame == null)
                return;

            frame.ActualOrigin = productionOrigin;
            frame.Target = productionTarget;
            frame.BeforeActual = TraceVisibleObjects(
                frame.ActualOrigin, frame.Target, frame.CameraTransform,
                QueryTriggerInteraction.Ignore);
            frame.BeforeFinal = TraceVisibleObjects(
                frame.FinalOrigin, frame.Target, frame.CameraTransform,
                QueryTriggerInteraction.Ignore);
            frame.BeforeTriggers = TraceVisibleObjects(
                frame.FinalOrigin, frame.Target, frame.CameraTransform,
                QueryTriggerInteraction.Collide);
            frame.Inside = FindContainingVisuals(frame.FinalOrigin, frame.Target, frame.CameraTransform);

            if (CountVisible(frame.BeforeFinal) == 0)
                frame.RendererOnlyBefore = FindRendererOnlyBlocker(frame);
        }

        /// <summary>Called after the production method and any Harmony postfix fallback returned.</summary>
        internal static void Finish()
        {
            var frame = _pending;
            _pending = null;
            if (frame == null)
                return;

            _samples++;
            var afterFinal = TraceVisibleObjects(
                frame.FinalOrigin, frame.Target, frame.CameraTransform,
                QueryTriggerInteraction.Ignore);
            var afterTriggers = TraceVisibleObjects(
                frame.FinalOrigin, frame.Target, frame.CameraTransform,
                QueryTriggerInteraction.Collide);
            VisualHit? rendererOnlyAfter = CountVisible(afterFinal) == 0
                ? FindRendererOnlyBlocker(frame)
                : null;

            var categories = new List<string>(6);
            float originDelta = Vector3.Distance(frame.ActualOrigin, frame.FinalOrigin);
            VisualHit? oldFirst = FirstVisible(frame.BeforeActual);
            VisualHit? finalFirst = FirstVisible(frame.BeforeFinal);
            VisualHit? afterFirst = FirstVisible(afterFinal);

            if (originDelta >= TransitionOriginDeltaMeters && afterFirst != null &&
                !SameVisual(oldFirst, finalFirst))
            {
                AddCategory(categories, "transition_stale_origin");
            }

            if (CountVisible(frame.BeforeFinal) >= 2 && CountVisible(afterFinal) >= 1)
                AddCategory(categories, "multi_layer_remaining");

            if (finalFirst != null && ContainsVisible(afterFinal, finalFirst.Signature))
            {
                AddCategory(categories, "first_blocker_remained");
                if (finalFirst.Collider != null &&
                    OrbitMapVanishAssist.IsDirectOccluderHidden(finalFirst.Collider))
                {
                    AddCategory(categories, "false_hidden_mapping");
                }
            }

            if (HasVisible(frame.Inside))
                AddCategory(categories, "camera_inside_geometry");

            if (HasVisibleTriggerMissingFromIgnore(afterTriggers, afterFinal))
                AddCategory(categories, "trigger_only_blocker");

            if (frame.RendererOnlyBefore != null && frame.RendererOnlyBefore.Visible &&
                rendererOnlyAfter != null && rendererOnlyAfter.Visible &&
                frame.RendererOnlyBefore.Signature == rendererOnlyAfter.Signature)
            {
                AddCategory(categories, "renderer_without_collider");
            }

            if (categories.Count > 0)
            {
                _issueSamples++;
                for (int i = 0; i < categories.Count; i++)
                {
                    string category = categories[i];
                    CategoryCounts.TryGetValue(category, out int count);
                    CategoryCounts[category] = count + 1;
                }

                OrbitStateMachineLog.Event(
                    "occlusion_survey",
                    "sample",
                    "{\"mapId\":" + frame.MapId
                    + ",\"focusIndex\":" + frame.FocusIndex
                    + ",\"originDelta\":" + F3(originDelta)
                    + ",\"beforeActual\":" + CountVisible(frame.BeforeActual)
                    + ",\"beforeFinal\":" + CountVisible(frame.BeforeFinal)
                    + ",\"afterFinal\":" + CountVisible(afterFinal)
                    + ",\"oldFirst\":\"" + Esc(NameOf(oldFirst)) + "\""
                    + ",\"finalFirst\":\"" + Esc(NameOf(finalFirst)) + "\""
                    + ",\"afterFirst\":\"" + Esc(NameOf(afterFirst)) + "\""
                    + ",\"insideFirst\":\"" + Esc(NameOf(FirstVisible(frame.Inside))) + "\""
                    + ",\"rendererOnly\":\"" + Esc(NameOf(rendererOnlyAfter)) + "\""
                    + ",\"categories\":[" + JsonStringList(categories) + "]}");
            }

            if (_nextSummaryAt <= 0f || Time.unscaledTime >= _nextSummaryAt)
            {
                _nextSummaryAt = Time.unscaledTime + 5f;
                LogSummary("periodic");
            }
        }

        private static Vector3 PredictFinalCameraPosition(CameraControl_Ver2 control)
        {
            Quaternion rotation = Quaternion.Euler(control.CameraAngle);
            Vector3 targetWorld = control.TargetPos;
            if (control.transBase != null)
            {
                rotation = control.transBase.rotation * rotation;
                targetWorld = control.transBase.TransformPoint(control.TargetPos);
            }
            return rotation * control.CameraDir + targetWorld;
        }

        private static List<VisualHit> TraceVisibleObjects(
            Vector3 origin,
            Vector3 target,
            Transform? cameraTransform,
            QueryTriggerInteraction triggerMode)
        {
            var result = new List<VisualHit>(8);
            Vector3 delta = target - origin;
            float distance = delta.magnitude;
            if (distance <= RayEndPaddingMeters)
                return result;

            var direction = delta / distance;
            var ray = new Ray(origin, direction);
            var hits = Physics.RaycastAll(
                origin, direction, distance - RayEndPaddingMeters, ~0, triggerMode);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < hits.Length; i++)
            {
                var collider = hits[i].collider;
                if (ShouldIgnoreCollider(collider, cameraTransform))
                    continue;
                var visual = ResolveVisual(collider, ray, distance, hits[i].distance);
                if (visual == null || !seen.Add(visual.Signature))
                    continue;
                visual.Collider = collider;
                visual.Distance = hits[i].distance;
                visual.IsTrigger = collider.isTrigger;
                result.Add(visual);
            }
            return result;
        }

        private static List<VisualHit> FindContainingVisuals(
            Vector3 origin,
            Vector3 target,
            Transform? cameraTransform)
        {
            var result = new List<VisualHit>(4);
            var colliders = Physics.OverlapSphere(
                origin, InsideProbeRadiusMeters, ~0, QueryTriggerInteraction.Collide);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Vector3 delta = target - origin;
            float distance = Mathf.Max(delta.magnitude, 0.1f);
            var ray = new Ray(origin, delta.sqrMagnitude > 1e-6f ? delta.normalized : Vector3.forward);
            for (int i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (ShouldIgnoreCollider(collider, cameraTransform))
                    continue;
                var visual = ResolveVisual(collider, ray, distance, 0f);
                if (visual == null || !seen.Add(visual.Signature))
                    continue;
                visual.Collider = collider;
                visual.IsTrigger = collider.isTrigger;
                result.Add(visual);
            }
            return result;
        }

        private static VisualHit? ResolveVisual(
            Collider collider,
            Ray ray,
            float maxDistance,
            float colliderHitDistance)
        {
            Transform? node = collider.transform;
            Renderer? best = null;
            float bestError = float.MaxValue;
            float bestRayDistance = colliderHitDistance;

            for (int depth = 0; depth < MaxRendererSearchDepth && node != null; depth++, node = node.parent)
            {
                var renderers = node.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > MaxRenderersPerSearchNode)
                    continue;

                for (int i = 0; i < renderers.Length; i++)
                {
                    var renderer = renderers[i];
                    if (!IsSurveyRenderer(renderer))
                        continue;
                    if (!renderer.bounds.IntersectRay(ray, out float rayDistance) ||
                        rayDistance > maxDistance - RayEndPaddingMeters)
                    {
                        continue;
                    }

                    float error = Mathf.Abs(rayDistance - colliderHitDistance);
                    if (best == null || error < bestError)
                    {
                        best = renderer;
                        bestError = error;
                        bestRayDistance = rayDistance;
                    }
                }

                if (best != null)
                    break;
            }

            if (best == null)
                return null;

            return new VisualHit
            {
                Renderer = best,
                Signature = "r:" + best.GetInstanceID(),
                Name = best.name ?? collider.name ?? "unknown",
                Distance = bestRayDistance,
                Visible = best.enabled && best.gameObject.activeInHierarchy
            };
        }

        private static VisualHit? FindRendererOnlyBlocker(FrameSample frame)
        {
            EnsureMapRendererCache(frame.HScene);
            Vector3 delta = frame.Target - frame.FinalOrigin;
            float distance = delta.magnitude;
            if (distance <= RayEndPaddingMeters)
                return null;

            var ray = new Ray(frame.FinalOrigin, delta / distance);
            Renderer? closest = null;
            float closestDistance = float.MaxValue;
            for (int i = 0; i < _cachedMapRenderers.Length; i++)
            {
                var renderer = _cachedMapRenderers[i];
                if (!IsSurveyRenderer(renderer) || !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }
                if (!renderer.bounds.IntersectRay(ray, out float hitDistance) ||
                    hitDistance <= RayEndPaddingMeters ||
                    hitDistance >= distance - RayEndPaddingMeters ||
                    hitDistance >= closestDistance)
                {
                    continue;
                }
                closest = renderer;
                closestDistance = hitDistance;
            }

            if (closest == null)
                return null;
            return new VisualHit
            {
                Renderer = closest,
                Signature = "r:" + closest.GetInstanceID(),
                Name = closest.name ?? "unknown",
                Distance = closestDistance,
                Visible = true
            };
        }

        private static void EnsureMapRendererCache(HScene? hScene)
        {
            GameObject? mapRoot = null;
            try
            {
                if (hScene != null)
                    mapRoot = Traverse.Create(hScene).Field("objMap").GetValue<GameObject>();
            }
            catch { }

            int id = mapRoot != null ? mapRoot.GetInstanceID() : -1;
            if (id == _cachedMapRootId && ReferenceEquals(mapRoot, _cachedMapRoot))
                return;
            _cachedMapRoot = mapRoot;
            _cachedMapRootId = id;
            _cachedMapRenderers = mapRoot != null
                ? mapRoot.GetComponentsInChildren<Renderer>(true)
                : Array.Empty<Renderer>();
        }

        private static bool ShouldIgnoreCollider(Collider? collider, Transform? cameraTransform)
        {
            if (collider == null)
                return true;
            if (cameraTransform != null &&
                (collider.transform == cameraTransform || collider.transform.IsChildOf(cameraTransform)))
            {
                return true;
            }
            try
            {
                return collider.GetComponentInParent<AIChara.ChaControl>() != null;
            }
            catch { return false; }
        }

        private static bool IsSurveyRenderer(Renderer? renderer)
        {
            if (renderer == null || renderer is ParticleSystemRenderer ||
                renderer is TrailRenderer || renderer is LineRenderer)
            {
                return false;
            }
            try
            {
                return renderer.GetComponentInParent<AIChara.ChaControl>() == null;
            }
            catch { return true; }
        }

        private static bool HasVisibleTriggerMissingFromIgnore(
            List<VisualHit> collide,
            List<VisualHit> ignore)
        {
            var ignored = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < ignore.Count; i++)
                if (ignore[i].Visible) ignored.Add(ignore[i].Signature);
            for (int i = 0; i < collide.Count; i++)
            {
                var hit = collide[i];
                if (hit.Visible && hit.IsTrigger && !ignored.Contains(hit.Signature))
                    return true;
            }
            return false;
        }

        private static void AddCategory(List<string> categories, string category)
        {
            if (!categories.Contains(category))
                categories.Add(category);
        }

        private static int CountVisible(List<VisualHit> hits)
        {
            int count = 0;
            for (int i = 0; i < hits.Count; i++)
                if (hits[i].Visible) count++;
            return count;
        }

        private static bool HasVisible(List<VisualHit> hits) => FirstVisible(hits) != null;

        private static VisualHit? FirstVisible(List<VisualHit> hits)
        {
            for (int i = 0; i < hits.Count; i++)
                if (hits[i].Visible) return hits[i];
            return null;
        }

        private static bool ContainsVisible(List<VisualHit> hits, string signature)
        {
            for (int i = 0; i < hits.Count; i++)
                if (hits[i].Visible && hits[i].Signature == signature) return true;
            return false;
        }

        private static bool SameVisual(VisualHit? a, VisualHit? b) =>
            a != null && b != null && a.Signature == b.Signature;

        private static string NameOf(VisualHit? hit) => hit?.Name ?? "";

        private static int GetMapId()
        {
            try
            {
                return Singleton<HSceneManager>.IsInstance()
                    ? Singleton<HSceneManager>.Instance.mapID
                    : -1;
            }
            catch { return -1; }
        }

        private static void LogSummary(string reason)
        {
            var keys = new List<string>(CategoryCounts.Keys);
            keys.Sort(StringComparer.Ordinal);
            var parts = new List<string>(keys.Count);
            for (int i = 0; i < keys.Count; i++)
                parts.Add("\"" + Esc(keys[i]) + "\":" + CategoryCounts[keys[i]]);
            OrbitStateMachineLog.Event(
                "occlusion_survey",
                "summary",
                "{\"reason\":\"" + Esc(reason) + "\",\"samples\":" + _samples
                + ",\"issueSamples\":" + _issueSamples
                + ",\"counts\":{" + string.Join(",", parts.ToArray()) + "}}");
        }

        private static string JsonStringList(List<string> values)
        {
            var result = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
                result[i] = "\"" + Esc(values[i]) + "\"";
            return string.Join(",", result);
        }

        private static string F3(float value) =>
            value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

        private static string Esc(string value) =>
            (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
