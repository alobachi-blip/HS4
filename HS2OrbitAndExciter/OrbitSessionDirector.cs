using System;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    internal static class OrbitSessionDirector
    {
        private const float ScriptPhaseSeconds = 4f;
        private const float WeakSpeed = 0.55f;
        private const float StrongSpeed = 1.25f;
        private const float PullWheel = -0.25f;
        private const float SpankWheel = 0.12f;
        private const float SpankFirstPulseDelaySeconds = 0.6f;
        private const float SpankPulseIntervalSeconds = 1.15f;
        private const float SpankFinishReadyFeel = 0.70f;
        private const float SpankFinishActionNorm = 0.70f;

        private static int _lastHSceneId = -1;
        private static int _lastPoseId = -1;
        private static int _sessionIndex;
        private static float _scriptStartedUnscaled = -1f;
        private static string _lastStep = "";
        private static bool _pullInjectedForPose;
        private static float _nextSpankPulseUnscaled;
        private static int _spankPulseCount;
        private static bool _spankFinishFeelForcedForPose;
        private static bool _spankPoolRequestedForPose;

        internal static void Reset()
        {
            _lastHSceneId = -1;
            _lastPoseId = -1;
            _scriptStartedUnscaled = -1f;
            _lastStep = "";
            _pullInjectedForPose = false;
            _nextSpankPulseUnscaled = 0f;
            _spankPulseCount = 0;
            _spankFinishFeelForcedForPose = false;
            _spankPoolRequestedForPose = false;
        }

        internal static void Tick(HScene? hScene)
        {
            if (hScene == null || HS2OrbitAndExciter.EnableOrbitSessionDirector?.Value != true)
                return;
            if (!OrbitBehaviorHub.IsOrbitAssistActive())
                return;
            var ctrl = hScene.ctrlFlag;
            if (ctrl?.nowAnimationInfo == null)
                return;

            EnsureSession(hScene, ctrl.nowAnimationInfo.id);
            if (OrbitFsmCellClassifier.Classify(hScene) != OrbitFsmCell.ActionBridge)
                return;

            string family = ResolveFamily(hScene, ctrl);
            string clip = GetLayer0StateName(hScene);
            if (family == "A" || family == "B" || family == "C")
                TickWsScript(ctrl, clip, family);
            else if (family == "E")
            {
                if (TickSpankingCompletionHandoff(hScene, ctrl, clip))
                    return;
                TickSpankingFinishAssist(hScene, ctrl, clip);
            }
        }

        internal static bool TryInjectInsideAfterWheel(ref float wheel)
        {
            if (HS2OrbitAndExciter.EnableOrbitSessionDirector?.Value != true)
                return false;
            if (!OrbitBehaviorHub.IsOrbitAssistActive())
                return false;
            var hScene = OrbitController.TryGetHScene();
            if (hScene?.ctrlFlag?.nowAnimationInfo == null)
                return false;
            EnsureSession(hScene, hScene.ctrlFlag.nowAnimationInfo.id);
            string clip = GetLayer0StateName(hScene);
            if (clip != "Orgasm_IN_A" && clip != "D_Orgasm_IN_A")
                return false;
            if (_pullInjectedForPose)
                return false;

            wheel = PullWheel;
            _pullInjectedForPose = true;
            OrbitStateMachineLog.Event(
                "ina",
                "pull_click",
                "{\"clip\":\"" + Esc(clip) +
                "\",\"wheel\":" + PullWheel.ToString("R", CultureInfo.InvariantCulture) +
                ",\"poseId\":" + hScene.ctrlFlag.nowAnimationInfo.id + "}");
            return true;
        }

        internal static bool TryForceInsideAfterAutoPull()
        {
            if (HS2OrbitAndExciter.EnableOrbitSessionDirector?.Value != true)
                return false;
            if (!OrbitBehaviorHub.IsOrbitAssistActive())
                return false;
            var hScene = OrbitController.TryGetHScene();
            if (hScene?.ctrlFlag?.nowAnimationInfo == null)
                return false;
            EnsureSession(hScene, hScene.ctrlFlag.nowAnimationInfo.id);
            string clip = GetLayer0StateName(hScene);
            if (clip != "Orgasm_IN_A" && clip != "D_Orgasm_IN_A")
                return false;
            if (_pullInjectedForPose)
                return false;

            _pullInjectedForPose = true;
            OrbitStateMachineLog.Event(
                "ina",
                "pull_click",
                "{\"clip\":\"" + Esc(clip) +
                "\",\"input\":\"auto\"" +
                ",\"poseId\":" + hScene.ctrlFlag.nowAnimationInfo.id + "}");
            return true;
        }

        internal static float GetSpankingWheelAxis(string axisName)
        {
            float real = Input.GetAxis(axisName);
            if (axisName != "Mouse ScrollWheel")
                return real;
            if (Mathf.Abs(real) > 0.001f)
                return real;
            if (HS2OrbitAndExciter.EnableOrbitSessionDirector?.Value != true)
                return real;
            if (!OrbitBehaviorHub.IsOrbitAssistActive())
                return real;

            var hScene = OrbitController.TryGetHScene();
            var ctrl = hScene?.ctrlFlag;
            if (hScene == null || ctrl?.nowAnimationInfo == null || hScene.NowChangeAnim)
                return real;
            if (ctrl.selectAnimationListInfo != null)
                return real;

            EnsureSession(hScene, ctrl.nowAnimationInfo.id);
            if (ResolveFamily(hScene, ctrl) != "E")
                return real;
            if (_spankFinishFeelForcedForPose)
                return real;

            string clip = GetLayer0StateName(hScene);
            if (!IsSpankingWaitingClip(clip))
                return real;
            if (Time.unscaledTime < _nextSpankPulseUnscaled)
                return real;

            _nextSpankPulseUnscaled = Time.unscaledTime + SpankPulseIntervalSeconds;
            _spankPulseCount++;
            OrbitStateMachineLog.Event(
                "spank",
                "click",
                "{\"input\":\"wheel\"" +
                ",\"clip\":\"" + Esc(clip) +
                "\",\"wheel\":" + SpankWheel.ToString("R", CultureInfo.InvariantCulture) +
                ",\"pulse\":" + _spankPulseCount +
                ",\"poseId\":" + ctrl.nowAnimationInfo.id + "}");
            return SpankWheel;
        }

        private static void EnsureSession(HScene hScene, int poseId)
        {
            int hSceneId = hScene.GetInstanceID();
            if (hSceneId == _lastHSceneId && poseId == _lastPoseId)
                return;

            _lastHSceneId = hSceneId;
            _lastPoseId = poseId;
            _sessionIndex++;
            _scriptStartedUnscaled = Time.unscaledTime;
            _lastStep = "";
            _pullInjectedForPose = false;
            _nextSpankPulseUnscaled = Time.unscaledTime + SpankFirstPulseDelaySeconds;
            _spankPulseCount = 0;
            _spankFinishFeelForcedForPose = false;
            _spankPoolRequestedForPose = false;
            OrbitStateMachineLog.Event(
                "session/script",
                "start",
                "{\"sessionIndex\":" + _sessionIndex +
                ",\"poseId\":" + poseId +
                ",\"strongFirst\":" + ((_sessionIndex % 2) == 1 ? "true" : "false") + "}");
        }

        private static void TickWsScript(HSceneFlagCtrl ctrl, string clip, string family)
        {
            if (!IsWsControllableClip(clip))
                return;

            float elapsed = Mathf.Max(0f, Time.unscaledTime - _scriptStartedUnscaled);
            bool strongFirst = (_sessionIndex % 2) == 1;
            string step;
            float targetSpeed;
            if (elapsed < ScriptPhaseSeconds)
            {
                step = strongFirst ? "S" : "W";
                targetSpeed = strongFirst ? StrongSpeed : WeakSpeed;
            }
            else if (elapsed < ScriptPhaseSeconds * 2f)
            {
                step = strongFirst ? "W" : "S";
                targetSpeed = strongFirst ? WeakSpeed : StrongSpeed;
            }
            else
            {
                step = "done";
                targetSpeed = ctrl.speed;
            }

            if (step == "done")
                return;

            ctrl.speed = targetSpeed;
            if (step == _lastStep)
                return;
            _lastStep = step;
            OrbitStateMachineLog.Event(
                "ws",
                "script_step",
                "{\"family\":\"" + family +
                "\",\"step\":\"" + step +
                "\",\"clip\":\"" + Esc(clip) +
                "\",\"speed\":" + targetSpeed.ToString("R", CultureInfo.InvariantCulture) +
                ",\"elapsed\":" + elapsed.ToString("R", CultureInfo.InvariantCulture) + "}");
        }

        private static bool IsWsControllableClip(string clip)
        {
            return clip == "WLoop" || clip == "SLoop" || clip == "D_WLoop" || clip == "D_SLoop";
        }

        private static bool IsSpankingWaitingClip(string clip)
        {
            return clip == "WIdle" || clip == "SIdle";
        }

        private static void TickSpankingFinishAssist(HScene hScene, HSceneFlagCtrl ctrl, string clip)
        {
            if (hScene.NowChangeAnim || ctrl.selectAnimationListInfo != null)
                return;
            if (clip != "SAction" && clip != "D_Action")
                return;
            if (!_spankFinishFeelForcedForPose && ctrl.feel_f < SpankFinishReadyFeel)
                return;

            float norm = GetLayer0NormalizedTime(hScene);
            if (norm < SpankFinishActionNorm)
                return;

            float before = ctrl.feel_f;
            ctrl.feel_f = 1f;
            Patches.ExciterState.ForceNextOrgasmTrigger();
            if (_spankFinishFeelForcedForPose)
                return;

            _spankFinishFeelForcedForPose = true;
            OrbitStateMachineLog.Event(
                "spank",
                "finish_feel",
                "{\"clip\":\"" + Esc(clip) +
                "\",\"norm\":" + norm.ToString("R", CultureInfo.InvariantCulture) +
                ",\"feelBefore\":" + before.ToString("R", CultureInfo.InvariantCulture) +
                ",\"feelAfter\":1" +
                ",\"poseId\":" + ctrl.nowAnimationInfo.id + "}");
        }

        private static bool TickSpankingCompletionHandoff(HScene hScene, HSceneFlagCtrl ctrl, string clip)
        {
            if (!_spankFinishFeelForcedForPose || _spankPoolRequestedForPose)
                return false;
            if (!IsSpankingWaitingClip(clip))
                return false;
            if (hScene.NowChangeAnim || ctrl.selectAnimationListInfo != null || ctrl.nowOrgasm)
                return true;

            bool requested = OrbitFsmFlow.RequestSelectPoolImmediate(hScene, "spank_complete");
            _spankPoolRequestedForPose = requested;
            string reason = requested ? OrbitAssistReasons.None : OrbitPoseDirector.LastHotkeyFailReason;
            OrbitStateMachineLog.Event(
                "spank",
                requested ? "handoff_pool" : "handoff_blocked",
                "{\"clip\":\"" + Esc(clip) +
                "\",\"reason\":\"" + Esc(reason) +
                "\",\"poseId\":" + ctrl.nowAnimationInfo.id + "}");
            return requested;
        }

        private static string ResolveFamily(HScene hScene, HSceneFlagCtrl ctrl)
        {
            string actionFamily = OrbitPosePool.ClassifyCoverageFamily(ctrl.nowAnimationInfo);
            switch (actionFamily)
            {
                case "A_Aibu":
                case "A_Les":
                    return "A";
                case "B_Houshi":
                case "B_MultiPlay":
                    return "B";
                case "C_Sonyu":
                case "C_MultiPlay":
                    return "C";
                case "D_Masturbation":
                    return "D";
                case "E_Spnking":
                    return "E";
            }

            int mode = TryReadHSceneInt(hScene, "mode", -1);
            int modeCtrl = TryReadHSceneInt(hScene, "modeCtrl", -1);
            if (mode == 0 || mode == 6)
                return "A";
            if (mode == 1)
                return "B";
            if (mode == 2)
                return "C";
            if (mode == 3)
                return "E";
            if ((mode == 7 || mode == 8) && modeCtrl == 0)
                return "A";
            if ((mode == 7 || mode == 8) && (modeCtrl == 1 || modeCtrl == 2))
                return "B";
            if ((mode == 7 || mode == 8) && (modeCtrl == 3 || modeCtrl == 4))
                return "C";
            return "";
        }

        private static int TryReadHSceneInt(HScene hScene, string fieldName, int fallback)
        {
            try { return Traverse.Create(hScene).Field(fieldName).GetValue<int>(); }
            catch { return fallback; }
        }

        private static string GetLayer0StateName(HScene hScene)
        {
            var cha = OrbitHelpers.GetChaFemales(hScene)?[0];
            var anim = OrbitHelpers.TryGetFemaleAnimBody(cha);
            if (anim == null)
                return "?";
            try
            {
                var state = anim.GetCurrentAnimatorStateInfo(0);
                foreach (string name in StateNames)
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

        private static float GetLayer0NormalizedTime(HScene hScene)
        {
            var cha = OrbitHelpers.GetChaFemales(hScene)?[0];
            var anim = OrbitHelpers.TryGetFemaleAnimBody(cha);
            if (anim == null)
                return 0f;
            try
            {
                return anim.GetCurrentAnimatorStateInfo(0).normalizedTime;
            }
            catch
            {
                return 0f;
            }
        }

        private static readonly string[] StateNames =
        {
            "Idle", "D_Idle", "WIdle", "SIdle",
            "WAction", "SAction", "D_Action",
            "WLoop", "SLoop", "D_WLoop", "D_SLoop", "OLoop", "D_OLoop",
            "Orgasm", "D_Orgasm",
            "Orgasm_IN_A", "D_Orgasm_IN_A", "D_Orgasm_A",
            "Pull", "D_Pull", "Drop", "D_Drop"
        };

        private static string Esc(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return value!.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }
}
