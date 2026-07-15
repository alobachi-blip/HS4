using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Actor;
using AIChara;
using Illusion.Game;
using Manager;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// Smoke-only scene shortcut. Disabled by default; tools/run_orbit_smoke.ps1
    /// can enable it temporarily to avoid title -> selection -> map UI latency.
    /// </summary>
    internal sealed class OrbitSmokeDriver : MonoBehaviour
    {
        private bool _requested;
        private bool _activeLogged;
        private bool _keyframeLogged;
        private float _activeSinceUnscaled;
        private int _activeMode = -1;
        private int _activeModeCtrl = -1;
        private string _activeNowAnim = "";
        private float _nextAttemptUnscaled;
        private float _nextActiveCheckUnscaled;
        private int _attempts;

        private void Update()
        {
            if (HS2OrbitAndExciter.EnableDirectHSmokeDriver?.Value != true)
                return;

            if (_requested)
            {
                TryLogDirectHActive();
                TryLogDirectHKeyframe();
                return;
            }

            if (HSceneManager.isHScene || OrbitController.TryGetHScene() != null)
                return;

            float delay = Mathf.Max(0f, HS2OrbitAndExciter.DirectHSmokeDelaySeconds?.Value ?? 8f);
            if (Time.unscaledTime < delay || Time.unscaledTime < _nextAttemptUnscaled)
                return;

            _attempts++;
            _nextAttemptUnscaled = Time.unscaledTime + 2f;

            if (!CanRequestDirectH(out string reason))
            {
                OrbitStateMachineLog.Event("smoke", "direct_h_wait", "{\"reason\":\"" + Esc(reason) + "\",\"attempt\":" + _attempts + "}");
                return;
            }

            try
            {
                RequestDirectH();
                _requested = true;
            }
            catch (Exception ex)
            {
                OrbitStateMachineLog.Event("smoke", "direct_h_fail", "{\"error\":\"" + Esc(ex.Message) + "\"}");
                HS2OrbitAndExciter.Log?.LogWarning("[OrbitSmoke] Direct H smoke jump failed: " + ex.Message);
                _requested = true;
            }
        }

        private static bool CanRequestDirectH(out string reason)
        {
            if (!Singleton<Game>.IsInstance())
            {
                reason = "no_game";
                return false;
            }
            if (!Singleton<HSceneManager>.IsInstance())
            {
                reason = "no_hscene_manager";
                return false;
            }
            if (!Singleton<Character>.IsInstance())
            {
                reason = "no_character";
                return false;
            }
            if (Scene.IsFadeNow)
            {
                reason = "scene_fade";
                return false;
            }
            reason = "ready";
            return true;
        }

        private static void RequestDirectH()
        {
            var game = Singleton<Game>.Instance;
            var manager = Singleton<HSceneManager>.Instance;

            int mapId = HS2OrbitAndExciter.DirectHSmokeMapId?.Value ?? 3;
            int eventNo = HS2OrbitAndExciter.DirectHSmokeEventNo?.Value ?? -1;
            string femalePath = HS2OrbitAndExciter.DirectHSmokeFemaleCardPath?.Value ?? "";
            string secondFemalePath = HS2OrbitAndExciter.DirectHSmokeSecondFemaleCardPath?.Value ?? "";
            string malePath = HS2OrbitAndExciter.DirectHSmokeMaleCardPath?.Value ?? "";
            if (string.IsNullOrWhiteSpace(malePath) && game.saveData?.playerChara != null)
                malePath = game.saveData.playerChara.FileName ?? "";

            game.eventNo = eventNo;
            game.peepKind = -1;
            game.isConciergeAngry = false;
            game.mapNo = mapId;
            game.heroineList = BuildHeroineList(femalePath, secondFemalePath);
            if (game.saveData != null)
                game.saveData.BeforeFemaleName = string.Empty;

            manager.mapID = mapId;
            manager.player = null;
            manager.females[0] = null;
            manager.females[1] = null;
            manager.pngFemales[0] = femalePath;
            manager.pngFemales[1] = secondFemalePath;
            manager.pngMale = malePath;
            manager.bFutanari = false;
            manager.pngMaleSecond = "";
            manager.bFutanariSecond = false;
            manager.SecondSitori = false;

            OrbitStateMachineLog.Event("smoke", "direct_h_load",
                "{\"mapId\":" + mapId + ",\"eventNo\":" + eventNo
                + ",\"female\":\"" + Esc(femalePath)
                + "\",\"secondFemale\":\"" + Esc(secondFemalePath)
                + "\",\"male\":\"" + Esc(malePath) + "\"}");

            Scene.LoadReserve(new Scene.Data
            {
                levelName = "HScene",
                fadeType = FadeCanvas.Fade.In
            }, isLoadingImageDraw: true);
        }

        private void TryLogDirectHActive()
        {
            if (_activeLogged || Time.unscaledTime < _nextActiveCheckUnscaled)
                return;

            _nextActiveCheckUnscaled = Time.unscaledTime + 0.5f;
            var hScene = OrbitController.TryGetHScene();
            if (hScene?.ctrlFlag?.nowAnimationInfo == null)
                return;

            var info = hScene.ctrlFlag.nowAnimationInfo;
            if (info.id < 0)
                return;

            var action = info.ActionCtrl;
            if (action.Item1 < 0 || action.Item2 < 0)
                return;

            _activeMode = action.Item1;
            _activeModeCtrl = action.Item2;
            _activeNowAnim = Esc(info.nameAnimation) + "#id" + info.id + ";down" + info.nDownPtn;
            _activeSinceUnscaled = Time.unscaledTime;

            OrbitStateMachineLog.Event("smoke", "direct_h_active",
                "{\"keyframe\":\"direct_h_active\""
                + ",\"mode\":" + _activeMode
                + ",\"modeCtrl\":" + _activeModeCtrl
                + ",\"nowAnim\":\"" + _activeNowAnim + "\"}");
            _activeLogged = true;
        }

        private void TryLogDirectHKeyframe()
        {
            if (!_activeLogged || _keyframeLogged)
                return;
            if (Time.unscaledTime < _activeSinceUnscaled + 3f || Scene.IsFadeNow)
                return;

            string screenshot = CaptureKeyframe("direct_h_keyframe", out string screenshotError);
            OrbitStateMachineLog.Event("smoke", "direct_h_keyframe",
                "{\"keyframe\":\"direct_h_keyframe\""
                + ",\"mode\":" + _activeMode
                + ",\"modeCtrl\":" + _activeModeCtrl
                + ",\"nowAnim\":\"" + _activeNowAnim + "\""
                + ",\"screenshot\":\"" + Esc(screenshot) + "\""
                + ",\"screenshotError\":\"" + Esc(screenshotError) + "\"}");
            _keyframeLogged = true;
        }

        private static string CaptureKeyframe(string marker, out string error)
        {
            error = "";
            try
            {
                string configured = HS2OrbitAndExciter.SmokeKeyframeDirectory?.Value ?? "";
                string dir = string.IsNullOrWhiteSpace(configured)
                    ? DefaultKeyframeDirectory()
                    : configured;

                Directory.CreateDirectory(dir);
                string file = marker + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + ".png";
                string path = Path.Combine(dir, file);
                ScreenCapture.CaptureScreenshot(path, 1);
                return path;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                HS2OrbitAndExciter.Log?.LogWarning("[OrbitSmoke] Keyframe screenshot failed: " + ex.Message);
                return "";
            }
        }

        private static string DefaultKeyframeDirectory()
        {
            string? tracePath = OrbitStateMachineLog.LogPath;
            string root = !string.IsNullOrWhiteSpace(tracePath)
                ? Path.GetDirectoryName(tracePath) ?? Application.dataPath
                : Application.dataPath;
            return Path.Combine(root, "OrbitSmokeKeyframes");
        }

        private static List<Heroine> BuildHeroineList(string femalePath, string secondFemalePath)
        {
            var list = new List<Heroine>();
            var first = TryLoadHeroine(femalePath);
            if (first != null)
                list.Add(first);
            var second = TryLoadHeroine(secondFemalePath);
            if (second != null)
                list.Add(second);
            return list;
        }

        private static Heroine? TryLoadHeroine(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            try
            {
                var file = new ChaFileControl();
                return file.LoadCharaFile(path, 1)
                    ? new Heroine(file, isRandomize: false)
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static string Esc(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value!.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }
}
