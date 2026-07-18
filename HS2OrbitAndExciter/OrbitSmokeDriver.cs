using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Actor;
using AIChara;
using HarmonyLib;
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
        private bool _orbitAssistLogged;
        private float _nextOrbitAssistAttemptUnscaled;
        private readonly HashSet<string> _stageKeyframes = new HashSet<string>();
        private float _nextStageKeyframeUnscaled;
        private bool _cumflationVerificationStarted;
        private const int MaxStageKeyframes = 32;

        private void Update()
        {
            if (HS2OrbitAndExciter.EnableDirectHSmokeDriver?.Value != true)
                return;

            if (_requested)
            {
                TryEnableDirectHOrbitAssist();
                TryLogDirectHActive();
                TryLogDirectHKeyframe();
                TryLogStageKeyframe();
                TryStartCumflationScreenshotVerification();
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

        private void TryEnableDirectHOrbitAssist()
        {
            if (_orbitAssistLogged || Time.unscaledTime < _nextOrbitAssistAttemptUnscaled)
                return;
            if (HS2OrbitAndExciter.EnableDirectHSmokeOrbitAssist?.Value != true)
                return;

            var hScene = OrbitController.TryGetHScene();
            if (hScene?.ctrlFlag?.cameraCtrl == null || hScene.ctrlFlag.nowAnimationInfo == null)
                return;

            var info = hScene.ctrlFlag.nowAnimationInfo;
            if (info.id < 0)
                return;

            var action = info.ActionCtrl;
            if (action.Item1 < 0 || action.Item2 < 0)
                return;

            _nextOrbitAssistAttemptUnscaled = Time.unscaledTime + 2f;
            bool ok = OrbitController.SetOrbitAssistActive(true, "direct_h_smoke");
            OrbitStateMachineLog.Event("smoke", ok ? "direct_h_orbit_on" : "direct_h_orbit_fail",
                "{\"mode\":" + action.Item1 +
                ",\"modeCtrl\":" + action.Item2 +
                ",\"nowAnim\":\"" + Esc(info.nameAnimation) + "#id" + info.id + ";down" + info.nDownPtn + "\"" +
                ",\"orbit\":" + (OrbitController.IsOrbitActive() ? "true" : "false") + "}");
            _orbitAssistLogged = ok;
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

        private void TryLogStageKeyframe()
        {
            if (!_activeLogged || Scene.IsFadeNow)
                return;
            if (HS2OrbitAndExciter.EnableSmokeKeyframeScreenshots?.Value != true)
                return;
            if (_stageKeyframes.Count >= MaxStageKeyframes || Time.unscaledTime < _nextStageKeyframeUnscaled)
                return;

            var hScene = OrbitController.TryGetHScene();
            var ctrl = hScene?.ctrlFlag;
            if (hScene == null || ctrl?.nowAnimationInfo == null)
                return;

            string clip = GetLayer0StateName(hScene, out float clipNorm);
            string cell = OrbitFsmCellClassifier.Classify(hScene).ToString();
            string family = ResolveFamily(hScene, ctrl);
            if (cell == "Unknown" || clip == "?")
                return;

            string key = family + "|" + cell + "|" + clip;
            if (_stageKeyframes.Contains(key))
                return;

            _nextStageKeyframeUnscaled = Time.unscaledTime + 0.75f;
            _stageKeyframes.Add(key);
            string marker = "stage_" + SanitizeMarker(family + "_" + cell + "_" + clip);
            string screenshot = CaptureKeyframe(marker, out string screenshotError);
            OrbitStateMachineLog.Event("smoke", "stage_keyframe",
                "{\"keyframe\":\"" + Esc(marker) + "\""
                + ",\"sessionFamily\":\"" + Esc(family) + "\""
                + ",\"fsmCell\":\"" + Esc(cell) + "\""
                + ",\"clip\":\"" + Esc(clip) + "\""
                + ",\"clipNorm\":" + clipNorm.ToString("R", CultureInfo.InvariantCulture)
                + ",\"poseId\":" + ctrl.nowAnimationInfo.id
                + ",\"poseName\":\"" + Esc(ctrl.nowAnimationInfo.nameAnimation) + "\""
                + ",\"screenshot\":\"" + Esc(screenshot) + "\""
                + ",\"screenshotError\":\"" + Esc(screenshotError) + "\"}");
        }

        private void TryStartCumflationScreenshotVerification()
        {
            if (_cumflationVerificationStarted || !_activeLogged || Scene.IsFadeNow)
                return;
            if (HS2OrbitAndExciter.EnableCumflationScreenshotVerification?.Value != true)
                return;
            if (Time.unscaledTime < _activeSinceUnscaled + 4f)
                return;

            _cumflationVerificationStarted = true;
            StartCoroutine(CaptureCumflationBeforeAfter());
        }

        private IEnumerator CaptureCumflationBeforeAfter()
        {
            var hScene = OrbitController.TryGetHScene();
            if (hScene?.ctrlFlag == null)
                yield break;

            string before = CaptureKeyframe("cumflation_before", out string beforeError);
            yield return new WaitForEndOfFrame();
            yield return new WaitForSecondsRealtime(1f);

            // Use the native female-orgasm entry point so the screenshot test
            // covers the same Harmony postfix used by real gameplay.
            hScene.ctrlFlag.AddOrgasm();
            OrbitStateMachineLog.Event("smoke", "cumflation_trigger",
                "{\"source\":\"HSceneFlagCtrl.AddOrgasm\"}");

            float settle = Mathf.Clamp(
                HS2OrbitAndExciter.CumflationScreenshotSettleSeconds?.Value ?? 8f,
                2f,
                30f);
            yield return new WaitForSecondsRealtime(settle);
            yield return new WaitForEndOfFrame();

            string after = CaptureKeyframe("cumflation_after", out string afterError);
            OrbitStateMachineLog.Event("smoke", "cumflation_screenshots",
                "{\"before\":\"" + Esc(before) + "\""
                + ",\"after\":\"" + Esc(after) + "\""
                + ",\"beforeError\":\"" + Esc(beforeError) + "\""
                + ",\"afterError\":\"" + Esc(afterError) + "\""
                + ",\"settleSeconds\":" + settle.ToString("R", CultureInfo.InvariantCulture) + "}");

            if (HS2OrbitAndExciter.EnableSessionOnlySaveVerification?.Value == true)
            {
                // HScene.EndProcADV saves its live character card in place. Never let
                // this diagnostic touch a normal card: the caller must first make an
                // explicitly named disposable copy under UserData/chara/female.
                string cardName = OrbitHelpers.GetChaFemales(hScene)?[0]?.chaFile?.charaFileName ?? "";
                if (!cardName.StartsWith("__codex_session_only_card_test", StringComparison.OrdinalIgnoreCase))
                {
                    OrbitStateMachineLog.Event("smoke", "session_only_save_refused",
                        "{\"card\":\"" + Esc(cardName) + "\"}");
                    yield break;
                }

                // CaptureScreenshot writes asynchronously. Give the after frame time to
                // reach disk before asking vanilla HScene to run its ordinary save/exit.
                yield return new WaitForSecondsRealtime(1f);
                OrbitStateMachineLog.Event("smoke", "session_only_save_request",
                    "{\"card\":\"" + Esc(cardName) + "\"}");
                hScene.ctrlFlag.click = HSceneFlagCtrl.ClickKind.SceneEnd;
            }
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

        private static string GetLayer0StateName(HScene hScene, out float normalizedTime)
        {
            normalizedTime = -1f;
            var cha = OrbitHelpers.GetChaFemales(hScene)?[0];
            var anim = OrbitHelpers.TryGetFemaleAnimBody(cha);
            if (anim == null)
                return "?";
            try
            {
                var state = anim.GetCurrentAnimatorStateInfo(0);
                normalizedTime = state.normalizedTime;
                foreach (string name in StageStateNames)
                {
                    if (state.IsName(name))
                        return name;
                }
                return "h=" + state.fullPathHash;
            }
            catch
            {
                return "?";
            }
        }

        private static string ResolveFamily(HScene hScene, HSceneFlagCtrl ctrl)
        {
            int mode = TryReadHSceneInt(hScene, "mode", -1);
            int modeCtrl = TryReadHSceneInt(hScene, "modeCtrl", -1);

            if (OrbitPosePool.IsPeepingPose(ctrl.nowAnimationInfo) || mode == 5)
                return "Peeping";

            string actionFamily = OrbitPosePool.ClassifyCoverageFamily(ctrl.nowAnimationInfo);
            if (actionFamily != "Unknown" && actionFamily != "Peeping")
                return actionFamily;

            switch (mode)
            {
                case 0: return "A_Aibu";
                case 1: return "B_Houshi";
                case 2: return "C_Sonyu";
                case 3: return "E_Spnking";
                case 4: return "D_Masturbation";
                case 6: return "A_Les";
                case 7:
                case 8:
                    if (modeCtrl == 0) return "A_MultiPlay";
                    if (modeCtrl == 1 || modeCtrl == 2) return "B_MultiPlay";
                    if (modeCtrl == 3 || modeCtrl == 4) return "C_MultiPlay";
                    return "MultiPlay";
                default:
                    return "Unknown";
            }
        }

        private static int TryReadHSceneInt(HScene hScene, string fieldName, int fallback)
        {
            try
            {
                return Traverse.Create(hScene).Field(fieldName).GetValue<int>();
            }
            catch
            {
                return fallback;
            }
        }

        private static string SanitizeMarker(string marker)
        {
            if (string.IsNullOrEmpty(marker))
                return "stage";
            var chars = marker.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char ch = chars[i];
                if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-'))
                    chars[i] = '_';
            }
            string sanitized = new string(chars).Trim('_');
            if (sanitized.Length > 96)
                sanitized = sanitized.Substring(0, 96);
            return string.IsNullOrEmpty(sanitized) ? "stage" : sanitized;
        }

        private static readonly string[] StageStateNames =
        {
            "Idle", "D_Idle", "WIdle", "SIdle", "Insert", "D_Insert",
            "WLoop", "SLoop", "OLoop", "D_WLoop", "D_SLoop", "D_OLoop", "MLoop",
            "WAction", "SAction", "D_Action",
            "Orgasm", "D_Orgasm", "Orgasm_IN", "Orgasm_OUT",
            "OrgasmF_IN", "D_OrgasmF_IN", "OrgasmM_IN", "D_OrgasmM_IN",
            "OrgasmM_OUT", "OrgasmS_IN",
            "Orgasm_IN_A", "D_Orgasm_IN_A", "Pull", "D_Pull", "Drop", "D_Drop",
            "Orgasm_A", "Orgasm_OUT_A", "Drink", "Vomit", "Drink_A", "Vomit_A", "OrgasmM_OUT_A",
            "D_Orgasm_A", "D_Orgasm_OUT_A", "D_OrgasmM_OUT_A"
        };

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
