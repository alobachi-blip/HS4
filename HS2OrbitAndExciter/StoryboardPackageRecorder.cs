using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using AIChara;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// Records Storyboard Package v1 assets while orbit is active.
    /// Runtime writes are guarded so package output stays outside the HS2 game tree.
    /// </summary>
    internal static class StoryboardPackageRecorder
    {
        private const int CaptureWidth = 1280;
        private const int CaptureHeight = 720;
        private const string PackageSchema = "storyboard_package_v1";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static bool _sessionActive;
        private static string _sessionDir = "";
        private static string _storyboardPath = "";
        private static int _shotIndex;
        private static int _writtenShots;
        private static int _hSceneId = -1;
        private static ActiveShot? _shot;
        private static bool _rawSequenceStarted;
        private static int _rawFramesWritten;
        private static float _lastWarnUnscaled = -999f;

        internal static string StatusText { get; private set; } = "Storyboard: idle";

        internal static void NotifyOrbitToggled(bool active, MonoBehaviour host, HScene? hScene)
        {
            if (!active)
            {
                StopSession(closeCurrentShot: true, host: host, hScene: hScene, reason: "orbit_off");
                return;
            }

            if (HS2OrbitAndExciter.StoryboardPackageEnabled?.Value == true && hScene != null)
                EnsureSession(host, hScene);
        }

        internal static void Tick(
            MonoBehaviour host,
            HScene hScene,
            CameraControl_Ver2 ctrl,
            bool cameraSpinning,
            int orbitPhase,
            float orbitDegrees,
            int rotationCount,
            int roundTripCount,
            int focusIndex,
            OrbitBodyAxis.OrbitAxisMode axisMode,
            float tiltDegrees,
            float zoomMultiplier)
        {
            if (HS2OrbitAndExciter.StoryboardPackageEnabled?.Value != true)
            {
                StopSession(closeCurrentShot: true, host: host, hScene: hScene, reason: "disabled");
                return;
            }

            if (!OrbitBehaviorHub.IsOrbitAssistActive())
            {
                StopSession(closeCurrentShot: true, host: host, hScene: hScene, reason: "assist_inactive");
                return;
            }

            EnsureSession(host, hScene);
            if (!_sessionActive)
                return;

            if (_hSceneId != hScene.GetInstanceID())
            {
                StopSession(closeCurrentShot: false, host: host, hScene: hScene, reason: "hscene_changed");
                EnsureSession(host, hScene);
                if (!_sessionActive)
                    return;
            }

            float now = Time.unscaledTime;
            if (_shot == null)
            {
                BeginShot(host, hScene, ctrl, now, orbitPhase, orbitDegrees, rotationCount, roundTripCount, focusIndex, axisMode, tiltDegrees, zoomMultiplier);
                return;
            }

            _shot.EndPhase = orbitPhase;
            _shot.EndOrbitDegrees = orbitDegrees;
            _shot.EndRotationCount = rotationCount;
            _shot.EndRoundTripCount = roundTripCount;
            _shot.EndFocusIndex = focusIndex;
            _shot.EndAxisMode = axisMode;
            _shot.EndTiltDegrees = tiltDegrees;
            _shot.EndZoomMultiplier = zoomMultiplier;
            _shot.SawCameraSpin |= cameraSpinning;

            float duration = ClampShotDuration(HS2OrbitAndExciter.StoryboardShotDurationSeconds?.Value ?? 4f);
            if (now - _shot.StartUnscaled >= duration)
            {
                CompleteShot(host, hScene, ctrl, now);
                BeginShot(host, hScene, ctrl, now, orbitPhase, orbitDegrees, rotationCount, roundTripCount, focusIndex, axisMode, tiltDegrees, zoomMultiplier);
            }
        }

        private static void EnsureSession(MonoBehaviour host, HScene hScene)
        {
            if (_sessionActive)
                return;

            if (!TryBuildSessionPath(out string sessionDir, out string error))
            {
                WarnThrottled("[Storyboard] " + error);
                StatusText = "Storyboard: blocked - " + error;
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.Combine(sessionDir, "frames"));
                Directory.CreateDirectory(Path.Combine(sessionDir, "frames_raw"));
                Directory.CreateDirectory(Path.Combine(sessionDir, "prompts"));
                Directory.CreateDirectory(Path.Combine(sessionDir, "jobs"));
                _sessionDir = sessionDir;
                _storyboardPath = Path.Combine(sessionDir, "storyboard.ndjson");
                _shotIndex = 0;
                _writtenShots = 0;
                _rawSequenceStarted = false;
                _rawFramesWritten = 0;
                _hSceneId = hScene.GetInstanceID();
                _shot = null;
                _sessionActive = true;
                StatusText = "Storyboard: recording -> " + _sessionDir;
                HS2OrbitAndExciter.Log?.LogInfo("[Storyboard] package recording -> " + _sessionDir);
            }
            catch (Exception ex)
            {
                _sessionActive = false;
                StatusText = "Storyboard: failed - " + ex.Message;
                WarnThrottled("[Storyboard] failed to create package: " + ex.Message);
            }
        }

        private static bool TryBuildSessionPath(out string sessionDir, out string error)
        {
            sessionDir = "";
            error = "";
            string root = HS2OrbitAndExciter.StoryboardPackageOutputRoot?.Value ?? "";
            if (string.IsNullOrWhiteSpace(root))
            {
                error = "OutputRoot is empty";
                return false;
            }

            try
            {
                string fullRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root.Trim()));
                string gameRoot = SafeFullPath(Paths.GameRootPath);
                string bepinexRoot = SafeFullPath(Paths.BepInExRootPath);
                string dataRoot = SafeFullPath(Application.dataPath);

                if (IsSameOrChild(fullRoot, gameRoot) || IsSameOrChild(fullRoot, bepinexRoot) || IsSameOrChild(fullRoot, dataRoot))
                {
                    error = "OutputRoot must stay outside HS2/BepInEx";
                    return false;
                }

                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                sessionDir = Path.Combine(fullRoot, stamp + "_storyboard_v1");
                return true;
            }
            catch (Exception ex)
            {
                error = "invalid OutputRoot: " + ex.Message;
                return false;
            }
        }

        private static string SafeFullPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "";
            try { return Path.GetFullPath(path); }
            catch { return ""; }
        }

        private static bool IsSameOrChild(string candidate, string parent)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(parent))
                return false;
            char sep = Path.DirectorySeparatorChar;
            string c = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + sep;
            string p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + sep;
            return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
        }

        private static void BeginShot(
            MonoBehaviour host,
            HScene hScene,
            CameraControl_Ver2 ctrl,
            float now,
            int orbitPhase,
            float orbitDegrees,
            int rotationCount,
            int roundTripCount,
            int focusIndex,
            OrbitBodyAxis.OrbitAxisMode axisMode,
            float tiltDegrees,
            float zoomMultiplier)
        {
            _shotIndex++;
            string shotId = "shot_" + _shotIndex.ToString("0000", CultureInfo.InvariantCulture);
            int seed = Guid.NewGuid().GetHashCode() & 0x7fffffff;
            var shot = new ActiveShot
            {
                ShotId = shotId,
                Seed = seed,
                StartUnscaled = now,
                StartFrameRel = "frames/" + shotId + "_start.png",
                EndFrameRel = "frames/" + shotId + "_end.png",
                StartPhase = orbitPhase,
                StartOrbitDegrees = orbitDegrees,
                StartRotationCount = rotationCount,
                StartRoundTripCount = roundTripCount,
                StartFocusIndex = focusIndex,
                StartAxisMode = axisMode,
                StartTiltDegrees = tiltDegrees,
                StartZoomMultiplier = zoomMultiplier,
                EndPhase = orbitPhase,
                EndOrbitDegrees = orbitDegrees,
                EndRotationCount = rotationCount,
                EndRoundTripCount = roundTripCount,
                EndFocusIndex = focusIndex,
                EndAxisMode = axisMode,
                EndTiltDegrees = tiltDegrees,
                EndZoomMultiplier = zoomMultiplier,
                SawCameraSpin = false
            };
            _shot = shot;
            QueueCapture(host, ctrl, shot.StartFrameRel);
            TryStartRawSequence(host, ctrl);
            StatusText = "Storyboard: recording " + shot.ShotId + " -> " + _sessionDir;
        }

        private static void CompleteShot(MonoBehaviour host, HScene hScene, CameraControl_Ver2 ctrl, float now)
        {
            var shot = _shot;
            if (shot == null || !_sessionActive)
                return;

            float duration = Mathf.Clamp(now - shot.StartUnscaled, 3f, 6f);
            bool endFrame = HS2OrbitAndExciter.StoryboardCaptureEndFrame?.Value != false;
            if (endFrame)
                QueueCapture(host, ctrl, shot.EndFrameRel);

            try
            {
                string prompt = BuildPromptText(hScene, shot, duration, endFrame);
                string promptRel = "prompts/" + shot.ShotId + ".txt";
                string jobRel = "jobs/" + shot.ShotId + ".json";
                File.WriteAllText(RelToAbs(promptRel), prompt, Utf8NoBom);
                File.WriteAllText(RelToAbs(jobRel), BuildJobJson(shot, duration, endFrame, promptRel), Utf8NoBom);
                File.AppendAllText(_storyboardPath, BuildStoryboardLine(hScene, shot, duration, endFrame) + "\n", Utf8NoBom);
                _writtenShots++;
                StatusText = "Storyboard: " + _writtenShots + " shot(s) -> " + _sessionDir;
            }
            catch (Exception ex)
            {
                WarnThrottled("[Storyboard] failed to write shot metadata: " + ex.Message);
                StatusText = "Storyboard: write failed - " + ex.Message;
            }

            _shot = null;
        }

        private static void StopSession(bool closeCurrentShot, MonoBehaviour host, HScene? hScene, string reason)
        {
            if (!_sessionActive)
                return;

            if (closeCurrentShot && _shot != null && hScene != null)
            {
                var ctrl = hScene.ctrlFlag?.cameraCtrl as CameraControl_Ver2;
                if (ctrl != null && Time.unscaledTime - _shot.StartUnscaled >= 3f)
                    CompleteShot(host, hScene, ctrl, Time.unscaledTime);
            }

            string rawSummary = _rawFramesWritten > 0 ? ", raw_frames=" + _rawFramesWritten : "";
            HS2OrbitAndExciter.Log?.LogInfo("[Storyboard] stopped (" + reason + "), shots=" + _writtenShots + rawSummary + ", dir=" + _sessionDir);
            StatusText = _writtenShots > 0
                ? "Storyboard: stopped, " + _writtenShots + " shot(s)" + rawSummary + " -> " + _sessionDir
                : "Storyboard: idle";
            _sessionActive = false;
            _shot = null;
            _hSceneId = -1;
            _rawSequenceStarted = false;
        }

        private static void QueueCapture(MonoBehaviour host, CameraControl_Ver2 ctrl, string relPath)
        {
            if (host == null || ctrl == null || string.IsNullOrEmpty(_sessionDir))
                return;
            Camera cam = ctrl.thisCamera != null ? ctrl.thisCamera : Camera.main;
            if (cam == null)
            {
                WarnThrottled("[Storyboard] no camera for " + relPath);
                return;
            }
            host.StartCoroutine(CaptureCameraPng(cam, RelToAbs(relPath), relPath));
        }

        private static void TryStartRawSequence(MonoBehaviour host, CameraControl_Ver2 ctrl)
        {
            if (_rawSequenceStarted
                || HS2OrbitAndExciter.StoryboardRawSequenceEnabled?.Value != true
                || host == null
                || ctrl == null
                || string.IsNullOrEmpty(_sessionDir))
                return;

            Camera cam = ctrl.thisCamera != null ? ctrl.thisCamera : Camera.main;
            if (cam == null)
            {
                WarnThrottled("[Storyboard] no camera for frames_raw");
                return;
            }

            _rawSequenceStarted = true;
            int fps = Mathf.Clamp(HS2OrbitAndExciter.StoryboardFps?.Value ?? 24, 12, 60);
            float seconds = Mathf.Clamp(HS2OrbitAndExciter.StoryboardRawSequenceSeconds?.Value ?? 2f, 1f, 6f);
            int frameCount = Mathf.Max(1, Mathf.RoundToInt(seconds * fps));
            string sessionAtStart = _sessionDir;
            string rawDir = RelToAbs("frames_raw");
            host.StartCoroutine(CaptureRawSequence(cam, rawDir, sessionAtStart, fps, frameCount));
        }

        private static IEnumerator CaptureCameraPng(Camera camera, string absPath, string relPath)
        {
            yield return new WaitForEndOfFrame();
            if (camera == null)
                yield break;

            CaptureCameraPngNow(camera, absPath, relPath);
        }

        private static IEnumerator CaptureRawSequence(Camera camera, string rawDir, string sessionAtStart, int fps, int frameCount)
        {
            float start = Time.unscaledTime;
            int progressStep = Mathf.Max(1, fps / 2);
            try
            {
                Directory.CreateDirectory(rawDir);
            }
            catch (Exception ex)
            {
                WarnThrottled("[Storyboard] failed to create frames_raw: " + ex.Message);
                yield break;
            }

            for (int i = 1; i <= frameCount; i++)
            {
                if (!IsSameSession(sessionAtStart) || camera == null)
                    yield break;

                yield return new WaitForEndOfFrame();

                if (!IsSameSession(sessionAtStart) || camera == null)
                    yield break;

                string fileName = "source_" + i.ToString("0000", CultureInfo.InvariantCulture) + ".png";
                string relPath = "frames_raw/" + fileName;
                string absPath = Path.Combine(rawDir, fileName);
                if (CaptureCameraPngNow(camera, absPath, relPath))
                {
                    _rawFramesWritten = i;
                    if (i == 1 || i == frameCount || i % progressStep == 0)
                        StatusText = "Storyboard: raw frames " + i + "/" + frameCount + " -> " + _sessionDir;
                }

                float nextAt = start + (i / (float)fps);
                while (Time.unscaledTime < nextAt)
                {
                    if (!IsSameSession(sessionAtStart))
                        yield break;
                    yield return null;
                }
            }

            HS2OrbitAndExciter.Log?.LogInfo("[Storyboard] raw sequence captured " + _rawFramesWritten + " frame(s) -> " + Path.Combine(sessionAtStart, "frames_raw"));
        }

        private static bool CaptureCameraPngNow(Camera camera, string absPath, string relPath)
        {
            if (camera == null)
                return false;

            RenderTexture? rt = null;
            RenderTexture? previousTarget = camera.targetTexture;
            RenderTexture? previousActive = RenderTexture.active;
            Texture2D? tex = null;
            try
            {
                string? dir = Path.GetDirectoryName(absPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                rt = RenderTexture.GetTemporary(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGB24, mipChain: false);
                tex.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0);
                tex.Apply(updateMipmaps: false);
                File.WriteAllBytes(absPath, tex.EncodeToPNG());
                return true;
            }
            catch (Exception ex)
            {
                WarnThrottled("[Storyboard] capture failed for " + relPath + ": " + ex.Message);
                return false;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                if (tex != null)
                    UnityEngine.Object.Destroy(tex);
                if (rt != null)
                    RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static bool IsSameSession(string sessionAtStart) =>
            _sessionActive && string.Equals(_sessionDir, sessionAtStart, StringComparison.OrdinalIgnoreCase);

        private static string BuildStoryboardLine(HScene hScene, ActiveShot shot, float duration, bool endFrame)
        {
            int fps = Mathf.Clamp(HS2OrbitAndExciter.StoryboardFps?.Value ?? 24, 12, 60);
            string negative = NegativePrompt();
            var sb = new StringBuilder(2048);
            sb.Append('{');
            JsonProp(sb, "schema", PackageSchema);
            sb.Append(',');
            JsonProp(sb, "shot_id", shot.ShotId);
            sb.Append(",\"frame\":{");
            JsonProp(sb, "start", shot.StartFrameRel);
            sb.Append(',');
            if (endFrame) JsonProp(sb, "end", shot.EndFrameRel); else sb.Append("\"end\":null");
            sb.Append('}');
            sb.Append(",\"duration\":").Append(duration.ToString("0.###", CultureInfo.InvariantCulture));
            sb.Append(",\"fps\":").Append(fps);
            sb.Append(",\"resolution\":{\"width\":").Append(CaptureWidth).Append(",\"height\":").Append(CaptureHeight).Append('}');
            sb.Append(",\"camera\":").Append(BuildCameraJson(shot));
            sb.Append(",\"subjects\":").Append(BuildSubjectsJson(hScene));
            sb.Append(',');
            JsonProp(sb, "action", BuildActionText(hScene));
            sb.Append(',');
            JsonProp(sb, "motion_prompt", BuildMotionPrompt(shot, duration));
            sb.Append(',');
            JsonProp(sb, "continuity_notes", BuildContinuityNotes(shot, endFrame));
            sb.Append(',');
            JsonProp(sb, "negative_prompt", negative);
            sb.Append(',');
            JsonProp(sb, "model_target", HS2OrbitAndExciter.StoryboardModelTarget?.Value ?? "Wan2GP/ComfyUI/FramePack");
            sb.Append(",\"seed\":").Append(shot.Seed);
            sb.Append('}');
            return sb.ToString();
        }

        private static string BuildJobJson(ActiveShot shot, float duration, bool endFrame, string promptRel)
        {
            int fps = Mathf.Clamp(HS2OrbitAndExciter.StoryboardFps?.Value ?? 24, 12, 60);
            var sb = new StringBuilder(2048);
            sb.Append("{\n");
            JsonPropLine(sb, "schema", PackageSchema, comma: true, indent: 2);
            JsonPropLine(sb, "shot_id", shot.ShotId, comma: true, indent: 2);
            JsonPropLine(sb, "model_target", HS2OrbitAndExciter.StoryboardModelTarget?.Value ?? "Wan2GP/ComfyUI/FramePack", comma: true, indent: 2);
            sb.Append("  \"input\": {\n");
            JsonPropLine(sb, "start_frame", shot.StartFrameRel, comma: true, indent: 4);
            if (endFrame)
                JsonPropLine(sb, "end_frame", shot.EndFrameRel, comma: false, indent: 4);
            else
                sb.Append("    \"end_frame\": null\n");
            sb.Append("  },\n");
            sb.Append("  \"prompt_file\": ").Append(Json(promptRel)).Append(",\n");
            sb.Append("  \"generation\": {\"duration\": ").Append(duration.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(", \"fps\": ").Append(fps)
                .Append(", \"width\": ").Append(CaptureWidth)
                .Append(", \"height\": ").Append(CaptureHeight)
                .Append(", \"seed\": ").Append(shot.Seed).Append("},\n");
            sb.Append("  \"wan2gp\": {\"task\": \"i2v\", \"first_frame\": ").Append(Json(shot.StartFrameRel))
                .Append(", \"last_frame\": ").Append(endFrame ? Json(shot.EndFrameRel) : "null").Append("},\n");
            sb.Append("  \"comfyui\": {\"workflow\": \"TODO\", \"load_image_start\": ")
                .Append(Json(shot.StartFrameRel)).Append(", \"load_image_end\": ")
                .Append(endFrame ? Json(shot.EndFrameRel) : "null").Append("},\n");
            sb.Append("  \"framepack\": {\"mode\": \"image_to_video\", \"start_frame\": ")
                .Append(Json(shot.StartFrameRel)).Append(", \"end_frame\": ")
                .Append(endFrame ? Json(shot.EndFrameRel) : "null").Append("}\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        private static string BuildPromptText(HScene hScene, ActiveShot shot, float duration, bool endFrame)
        {
            var sb = new StringBuilder(1600);
            sb.AppendLine("Fictional adult consensual stylized 3D scene sourced from the HS2 reference frame.");
            sb.AppendLine("Duration: " + duration.ToString("0.0", CultureInfo.InvariantCulture) + " seconds, " + (HS2OrbitAndExciter.StoryboardFps?.Value ?? 24) + " fps, 1280x720.");
            sb.AppendLine("Start frame: " + shot.StartFrameRel + ".");
            if (endFrame)
                sb.AppendLine("End frame: " + shot.EndFrameRel + ".");
            sb.AppendLine("Subjects: " + BuildSubjectsText(hScene) + ".");
            sb.AppendLine("Action: " + BuildActionText(hScene));
            sb.AppendLine("Camera motion: " + BuildCameraMotionText(shot, duration));
            sb.AppendLine("Character motion: Continue the current in-game animation rhythm with natural body motion, breathing, hair and cloth follow-through; do not introduce a new pose or new person inside the shot.");
            sb.AppendLine("Continuity: " + BuildContinuityNotes(shot, endFrame));
            sb.AppendLine("Negative prompt: " + NegativePrompt());
            return sb.ToString();
        }

        private static string BuildCameraJson(ActiveShot shot)
        {
            var sb = new StringBuilder(512);
            sb.Append('{');
            JsonProp(sb, "movement", BuildCameraMotionText(shot, ClampShotDuration(HS2OrbitAndExciter.StoryboardShotDurationSeconds?.Value ?? 4f)));
            sb.Append(',');
            JsonProp(sb, "focus", FocusLabel(shot.StartFocusIndex));
            sb.Append(',');
            JsonProp(sb, "axis", shot.StartAxisMode.ToString());
            sb.Append(",\"start_phase\":").Append(shot.StartPhase);
            sb.Append(",\"end_phase\":").Append(shot.EndPhase);
            sb.Append(",\"start_orbit_degrees\":").Append(shot.StartOrbitDegrees.ToString("0.###", CultureInfo.InvariantCulture));
            sb.Append(",\"end_orbit_degrees\":").Append(shot.EndOrbitDegrees.ToString("0.###", CultureInfo.InvariantCulture));
            sb.Append(",\"tilt_degrees\":").Append(shot.StartTiltDegrees.ToString("0.###", CultureInfo.InvariantCulture));
            sb.Append(",\"zoom_multiplier\":").Append(shot.StartZoomMultiplier.ToString("0.###", CultureInfo.InvariantCulture));
            sb.Append('}');
            return sb.ToString();
        }

        private static string BuildMotionPrompt(ActiveShot shot, float duration)
        {
            return BuildCameraMotionText(shot, duration)
                   + " Character motion continues from the exact start frame for "
                   + duration.ToString("0.0", CultureInfo.InvariantCulture)
                   + " seconds; preserve the same animation rhythm, contact points, body proportions, outfit state, lighting, and composition.";
        }

        private static string BuildCameraMotionText(ActiveShot shot, float duration)
        {
            if (!shot.SawCameraSpin)
                return "Hold the current orbit framing with only subtle stabilization; no cut, no sudden zoom, no camera reset.";

            if (shot.StartAxisMode == OrbitBodyAxis.OrbitAxisMode.WorldVertical)
                return "Small horizon-locked world-vertical orbit around " + FocusLabel(shot.StartFocusIndex)
                       + ", from " + shot.StartOrbitDegrees.ToString("0", CultureInfo.InvariantCulture)
                       + " degrees to " + shot.EndOrbitDegrees.ToString("0", CultureInfo.InvariantCulture)
                       + " degrees over " + duration.ToString("0.0", CultureInfo.InvariantCulture)
                       + " seconds. Keep the room horizon upright, preserve the exact character silhouette, and avoid roll, flip, zoom jump, or reframing.";

            return "Smooth body-axis orbit around " + FocusLabel(shot.StartFocusIndex)
                   + ", axis " + shot.StartAxisMode
                   + ", from " + shot.StartOrbitDegrees.ToString("0", CultureInfo.InvariantCulture)
                   + " degrees to " + shot.EndOrbitDegrees.ToString("0", CultureInfo.InvariantCulture)
                   + " degrees over " + duration.ToString("0.0", CultureInfo.InvariantCulture)
                   + " seconds, with stable focus tracking and no abrupt reframing.";
        }

        private static string BuildContinuityNotes(ActiveShot shot, bool endFrame)
        {
            string end = endFrame
                ? " End near the provided end frame and preserve the same camera path."
                : " End by continuing the same motion arc from the start frame.";
            return "Use the start frame as the exact first-frame identity, scene, lighting, outfit, body shape, hair, accessories, and composition reference."
                   + end
                   + " Keep all content fictional, adult, and consensual; do not add minors, real people, extra characters, UI, text, or watermarks.";
        }

        private static string BuildActionText(HScene hScene)
        {
            string anim = hScene.ctrlFlag?.nowAnimationInfo != null
                ? hScene.ctrlFlag.nowAnimationInfo.nameAnimation + " id=" + hScene.ctrlFlag.nowAnimationInfo.id
                : "current H-scene animation";
            string state = TryLayer0StateName(hScene);
            float speed = 0f;
            try
            {
                if (hScene.ctrlFlag != null)
                    speed = (float)(Traverse.Create(hScene.ctrlFlag).Field("speed").GetValue() ?? 0f);
            }
            catch { /* ignore */ }
            return anim + "; animator_state=" + state + "; playback_speed=" + speed.ToString("0.###", CultureInfo.InvariantCulture)
                   + ". Continue this action without changing pose category during the shot.";
        }

        private static string BuildSubjectsJson(HScene hScene)
        {
            var sb = new StringBuilder(512);
            sb.Append('[');
            bool first = true;
            AppendCharacters(sb, OrbitHelpers.GetChaFemales(hScene), "female", ref first);
            AppendCharacters(sb, OrbitHelpers.GetChaMales(hScene), "male", ref first);
            sb.Append(']');
            return sb.ToString();
        }

        private static string BuildSubjectsText(HScene hScene)
        {
            string text = "";
            AppendSubjectNames(ref text, OrbitHelpers.GetChaFemales(hScene), "female");
            AppendSubjectNames(ref text, OrbitHelpers.GetChaMales(hScene), "male");
            return string.IsNullOrEmpty(text) ? "current fictional adult HS2 characters" : text;
        }

        private static void AppendCharacters(StringBuilder sb, ChaControl[]? chars, string rolePrefix, ref bool first)
        {
            if (chars == null)
                return;
            for (int i = 0; i < chars.Length; i++)
            {
                var cha = chars[i];
                if (cha == null || cha.objBodyBone == null)
                    continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append('{');
                JsonProp(sb, "role", rolePrefix + i);
                sb.Append(',');
                JsonProp(sb, "name", CharacterName(cha, rolePrefix + i));
                sb.Append(',');
                JsonProp(sb, "card", Path.GetFileName(OrbitHelpers.GetUserDataFemaleCharaPath(cha) ?? ""));
                sb.Append(',');
                JsonProp(sb, "continuity", "fictional adult consensual character; preserve identity, body shape, hair, outfit, accessories, and skin shading");
                sb.Append('}');
            }
        }

        private static void AppendSubjectNames(ref string text, ChaControl[]? chars, string rolePrefix)
        {
            if (chars == null)
                return;
            for (int i = 0; i < chars.Length; i++)
            {
                var cha = chars[i];
                if (cha == null || cha.objBodyBone == null)
                    continue;
                if (text.Length > 0)
                    text += ", ";
                text += rolePrefix + i + " " + CharacterName(cha, rolePrefix + i);
            }
        }

        private static string CharacterName(ChaControl cha, string fallback)
        {
            try
            {
                string name = cha.chaFile?.parameter?.fullname ?? "";
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
                string file = cha.chaFile?.charaFileName ?? "";
                if (!string.IsNullOrWhiteSpace(file))
                    return file;
            }
            catch { /* ignore */ }
            return fallback;
        }

        private static string TryLayer0StateName(HScene hScene)
        {
            var cha = OrbitHelpers.GetChaFemales(hScene);
            if (cha == null || cha.Length == 0 || cha[0] == null)
                return "unknown";
            var anim = OrbitHelpers.TryGetFemaleAnimBody(cha[0]);
            if (anim == null)
                return "unknown";
            try
            {
                var st = anim.GetCurrentAnimatorStateInfo(0);
                foreach (string name in ProbeStateNames)
                {
                    if (st.IsName(name))
                        return name;
                }
                return "hash=" + st.fullPathHash + ";normalized=" + st.normalizedTime.ToString("0.###", CultureInfo.InvariantCulture);
            }
            catch { return "unknown"; }
        }

        private static readonly string[] ProbeStateNames =
        {
            "Idle", "D_Idle", "WIdle", "SIdle", "Insert", "D_Insert",
            "WLoop", "SLoop", "OLoop", "D_WLoop", "D_SLoop", "D_OLoop", "MLoop",
            "WAction", "SAction", "D_Action",
            "OrgasmF_IN", "D_OrgasmF_IN", "OrgasmM_IN", "D_OrgasmM_IN",
            "Orgasm_A", "Orgasm_IN_A", "Orgasm_OUT_A", "Drink_A", "Vomit_A", "OrgasmM_OUT_A",
            "D_Orgasm_A", "D_Orgasm_OUT_A", "D_Orgasm_IN_A", "D_OrgasmM_OUT_A"
        };

        private static string FocusLabel(int focusIndex)
        {
            switch (focusIndex)
            {
                case 0: return "female0 head";
                case 1: return "female0 chest";
                case 2: return "female0 pelvis";
                case 3: return "female1 head";
                case 4: return "female1 chest";
                case 5: return "female1 pelvis";
                default: return "current body focus";
            }
        }

        private static string NegativePrompt() =>
            "minor, underage, childlike, real person, unauthorized likeness, non-consensual, coercion, violence, injury, identity drift, extra people, extra limbs, anatomy distortion, face drift, outfit change, hair change, scene cut, camera jump, UI overlay, subtitles, text, watermark, logo, low resolution, blur, flicker";

        private static float ClampShotDuration(float seconds) => Mathf.Clamp(seconds, 3f, 6f);

        private static string RelToAbs(string rel) =>
            Path.Combine(_sessionDir, rel.Replace('/', Path.DirectorySeparatorChar));

        private static void JsonProp(StringBuilder sb, string name, string? value)
        {
            sb.Append(Json(name)).Append(':').Append(Json(value ?? ""));
        }

        private static void JsonPropLine(StringBuilder sb, string name, string? value, bool comma, int indent)
        {
            sb.Append(' ', indent);
            JsonProp(sb, name, value);
            if (comma) sb.Append(',');
            sb.Append('\n');
        }

        private static string Json(string? s)
        {
            string value = s ?? "";
            if (value.Length == 0)
                return "\"\"";
            var sb = new StringBuilder(value.Length + 8);
            sb.Append('"');
            foreach (char ch in value)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 32)
                            sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static void WarnThrottled(string message)
        {
            if (Time.unscaledTime - _lastWarnUnscaled < 5f)
                return;
            _lastWarnUnscaled = Time.unscaledTime;
            HS2OrbitAndExciter.Log?.LogWarning(message);
        }

        private sealed class ActiveShot
        {
            internal string ShotId = "";
            internal int Seed;
            internal float StartUnscaled;
            internal string StartFrameRel = "";
            internal string EndFrameRel = "";
            internal int StartPhase;
            internal int EndPhase;
            internal float StartOrbitDegrees;
            internal float EndOrbitDegrees;
            internal int StartRotationCount;
            internal int EndRotationCount;
            internal int StartRoundTripCount;
            internal int EndRoundTripCount;
            internal int StartFocusIndex;
            internal int EndFocusIndex;
            internal OrbitBodyAxis.OrbitAxisMode StartAxisMode;
            internal OrbitBodyAxis.OrbitAxisMode EndAxisMode;
            internal float StartTiltDegrees;
            internal float EndTiltDegrees;
            internal float StartZoomMultiplier;
            internal float EndZoomMultiplier;
            internal bool SawCameraSpin;
        }
    }
}
