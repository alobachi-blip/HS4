using System;
using System.Collections.Generic;
using Manager;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>Diagnostic run: sample visual occlusion during 20 orbit rotations.</summary>
    internal static class OrbitOcclusion20Test
    {
        private const int TargetRotations = 20;
        private const float SampleInterval = 0.1f;
        private const float TestDistanceBodyHeights = 6f;
        private static readonly RaycastHit[] Hits = new RaycastHit[64];

        private static bool _running;
        private static bool _finished;
        private static float _nextSample;
        private static int _sampleCount;
        private static int _blockedSamples;
        private static int _framingOutSamples;
        private static int _maxHiddenRenderers;
        private static string _firstBlocker = "";
        private static bool _searchingForOccluder;
        private static bool _occluderConfirmed;

        internal static bool Enabled => HS2OrbitAndExciter.EnableOcclusion20CircleTest?.Value == true;
        internal static bool IsRunning => _running && !_finished;

        internal static void TickProduction(HScene hScene, CameraControl_Ver2 ctrl, int focusIndex)
        {
            if (hScene == null || ctrl == null) return;
            if (Enabled && !_running && !_finished)
                Arm(hScene, ctrl, focusIndex);
            if (Enabled && _searchingForOccluder)
                SetTestDistance(hScene, ctrl, focusIndex);
            Camera? camera = ctrl.thisCamera != null ? ctrl.thisCamera : Camera.main;
            var focus = GetFocusWorld(OrbitHelpers.GetChaFemales(hScene), focusIndex);
            if (camera != null && focus.HasValue)
                OrbitMapVanishAssist.ApplyDirectLineOfSight(
                    PredictFinalCameraPosition(ctrl),
                    focus.Value,
                    camera);
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

        internal static void Arm(HScene hScene, CameraControl_Ver2 ctrl, int focusIndex)
        {
            if (!Enabled)
                return;

            SetTestDistance(hScene, ctrl, focusIndex);

            _running = true;
            _searchingForOccluder = true;
            _occluderConfirmed = false;
            _finished = false;
            ResetLeg();
            OrbitStateMachineLog.Event("occlusion20", "start",
                "{\"targetRotations\":20,\"sampleInterval\":0.1,\"phase\":\"search_occluder\",\"distanceBodyHeights\":6}");
            HS2OrbitAndExciter.Log?.LogInfo("[Occlusion20] SEARCH: extending camera to 6 body heights; 20-circle test waits for a real occluder.");
        }

        internal static void Reset()
        {
            OrbitOcclusionSurvey.Reset();
            _running = false;
            _finished = false;
            _sampleCount = 0;
            _blockedSamples = 0;
            _framingOutSamples = 0;
            _maxHiddenRenderers = 0;
            _firstBlocker = "";
            _searchingForOccluder = false;
            _occluderConfirmed = false;
        }

        internal static void Sample(HScene hScene, CameraControl_Ver2 ctrl, int focusIndex)
        {
            if (hScene == null || ctrl == null)
                return;

            Camera? camera = ctrl.thisCamera != null ? ctrl.thisCamera : Camera.main;
            var females = OrbitHelpers.GetChaFemales(hScene);
            Vector3? focus = GetFocusWorld(females, focusIndex);
            if (camera == null || !focus.HasValue)
                return;

            OrbitOcclusionSurvey.Prepare(hScene, ctrl, camera, focus.Value, focusIndex);

            // Capture whether a physical map blocker existed before the direct
            // occlusion assist changes its visual state.
            bool rawOccluder = TryFindBlocker(camera.transform.position, focus.Value,
                out string rawBlocker, out _);
            TickProduction(hScene, ctrl, focusIndex);
            OrbitOcclusionSurvey.Finish();

            if (!Enabled || !_running || _finished || Time.unscaledTime < _nextSample)
                return;
            _nextSample = Time.unscaledTime + SampleInterval;

            Vector3 viewport = camera.WorldToViewportPoint(focus.Value);
            bool framingOut = viewport.z <= 0f
                || viewport.x < 0.01f || viewport.x > 0.99f
                || viewport.y < 0.01f || viewport.y > 0.99f;
            bool occluderAfterHide = TryFindBlocker(camera.transform.position, focus.Value,
                out string blocker, out bool hiddenByVanish);
            bool blocked = occluderAfterHide && !hiddenByVanish;

            if (_searchingForOccluder)
            {
                if (!rawOccluder)
                    return;
                _searchingForOccluder = false;
                _occluderConfirmed = true;
                OrbitStateMachineLog.Event("occlusion20", "occluder_confirmed",
                    "{\"blocker\":\"" + Escape(rawBlocker) + "\",\"hiddenAfterAssist\":" + Bool(hiddenByVanish) + "}");
                HS2OrbitAndExciter.Log?.LogInfo("[Occlusion20] OCCLUDER CONFIRMED: " + rawBlocker + "; starting 20-circle measurement.");
                ResetLeg();
            }

            _sampleCount++;
            if (framingOut)
                _framingOutSamples++;
            if (blocked)
            {
                _blockedSamples++;
                if (string.IsNullOrEmpty(_firstBlocker))
                    _firstBlocker = blocker;
            }
            _maxHiddenRenderers = Mathf.Max(_maxHiddenRenderers, OrbitMapVanishAssist.HiddenRendererCount);

            if (OrbitStateMachineLog.Enabled && (blocked || framingOut))
            {
                OrbitStateMachineLog.Event("occlusion20", "sample_warning",
                    "{\"blocked\":" + Bool(blocked)
                    + ",\"hiddenByVanish\":" + Bool(hiddenByVanish)
                    + ",\"framingOut\":" + Bool(framingOut)
                    + ",\"blocker\":\"" + Escape(blocker)
                    + "\",\"hiddenRenderers\":" + OrbitMapVanishAssist.HiddenRendererCount + "}");
            }
        }

        internal static void OnRotationBoundary(int rotationCount)
        {
            if (!Enabled || !_running || _finished || _searchingForOccluder || !_occluderConfirmed)
                return;

            bool legPass = _blockedSamples == 0 && _framingOutSamples == 0;
            string data = "{\"rotation\":" + rotationCount
                + ",\"samples\":" + _sampleCount
                + ",\"blockedSamples\":" + _blockedSamples
                + ",\"framingOutSamples\":" + _framingOutSamples
                + ",\"maxHiddenRenderers\":" + _maxHiddenRenderers
                + ",\"firstBlocker\":\"" + Escape(_firstBlocker)
                + "\",\"pass\":" + Bool(legPass) + "}";
            OrbitStateMachineLog.Event("occlusion20", "rotation", data);
            HS2OrbitAndExciter.Log?.LogInfo(
                "[Occlusion20] rotation=" + rotationCount
                + " samples=" + _sampleCount
                + " blocked=" + _blockedSamples
                + " framingOut=" + _framingOutSamples
                + " hiddenMax=" + _maxHiddenRenderers
                + " blocker=" + (_firstBlocker.Length == 0 ? "none" : _firstBlocker)
                + " pass=" + legPass);

            if (rotationCount >= TargetRotations)
            {
                _finished = true;
                _running = false;
                OrbitBehaviorHub.ToggleOrbitCameraSpinning();
                OrbitStateMachineLog.Event("occlusion20", "complete",
                    "{\"rotations\":20,\"pass\":" + Bool(legPass) + ",\"occluderConfirmed\":true}");
                HS2OrbitAndExciter.Log?.LogInfo(
                    "[Occlusion20] COMPLETE pass=" + legPass
                    + " rotations=20. Camera spinning paused.");
            }
            else
            {
                ResetLeg();
            }
        }

        private static void ResetLeg()
        {
            _nextSample = 0f;
            _sampleCount = 0;
            _blockedSamples = 0;
            _framingOutSamples = 0;
            _maxHiddenRenderers = 0;
            _firstBlocker = "";
        }

        private static void SetTestDistance(HScene hScene, CameraControl_Ver2 ctrl, int focusIndex)
        {
            var females = OrbitHelpers.GetChaFemales(hScene);
            int femaleIndex = focusIndex < 3 ? 0 : 1;
            float bodyHeight = females != null && femaleIndex < females.Length
                ? OrbitHelpers.GetBodyHeight(females, femaleIndex)
                : 1.7f;
            ctrl.CameraDir = new Vector3(0f, 0f, -bodyHeight * TestDistanceBodyHeights);
        }

        private static Vector3? GetFocusWorld(AIChara.ChaControl[]? females, int focusIndex)
        {
            if (females == null || focusIndex < 0)
                return null;
            int female = focusIndex < 3 ? 0 : 1;
            if (female >= females.Length)
                return null;
            string bone = (focusIndex % 3) switch
            {
                0 => OrbitHelpers.BoneHead,
                1 => OrbitHelpers.BoneChest,
                _ => OrbitHelpers.BonePelvis
            };
            return OrbitHelpers.GetBonePosition(females, female, bone);
        }

        private static bool TryFindBlocker(Vector3 origin, Vector3 target,
            out string blocker, out bool hiddenByVanish)
        {
            blocker = "";
            hiddenByVanish = false;
            Vector3 delta = target - origin;
            float distance = delta.magnitude;
            if (distance <= 0.05f)
                return false;

            int count = Physics.RaycastNonAlloc(origin, delta / distance, Hits,
                distance - 0.05f, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            bool found = false;
            bool foundHidden = false;
            for (int i = 0; i < count; i++)
            {
                var hit = Hits[i];
                var col = hit.collider;
                if (col == null || hit.distance >= nearest || IsCharacter(col))
                    continue;
                nearest = hit.distance;
                found = true;
                foundHidden = AreRenderersHidden(col);
                blocker = col.name ?? "unknown";
            }

            hiddenByVanish = foundHidden;
            return found;
        }

        private static bool IsCharacter(Collider col)
        {
            try { return col.GetComponentInParent<AIChara.ChaControl>() != null; }
            catch { return false; }
        }

        private static bool AreRenderersHidden(Collider col)
        {
            if (OrbitMapVanishAssist.IsDirectOccluderHidden(col))
                return true;
            var renderers = col.GetComponentsInChildren<Renderer>(true);
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;
                found = true;
                if (renderer.enabled)
                    return false;
            }
            var parent = col.transform;
            for (int depth = 0; depth < 8 && parent != null; depth++, parent = parent.parent)
            {
                var parentRenderers = parent.GetComponents<Renderer>();
                for (int i = 0; i < parentRenderers.Length; i++)
                {
                    var renderer = parentRenderers[i];
                    if (renderer == null) continue;
                    found = true;
                    if (renderer.enabled) return false;
                }
            }
            return found;
        }

        private static string Bool(bool value) => value ? "true" : "false";
        private static string Escape(string value) =>
            (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
