using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    internal static class OrbitFinishDirector
    {
        private const float PendingTimeoutSeconds = 8f;
        private const float BlockLogIntervalSeconds = 2f;

        private static string _pendingFamily = "";
        private static string _pendingPath = "";
        private static float _pendingSinceUnscaled = -1f;
        private static float _nextBlockedLogUnscaled;
        private static int _lastPendingPoseId = -1;

        internal static void Reset()
        {
            _pendingFamily = "";
            _pendingPath = "";
            _pendingSinceUnscaled = -1f;
            _nextBlockedLogUnscaled = 0f;
            _lastPendingPoseId = -1;
        }

        internal static void Tick(HScene? hScene)
        {
            if (hScene == null)
            {
                Reset();
                return;
            }

            var ctrl = hScene.ctrlFlag;
            if (ctrl == null)
            {
                Reset();
                return;
            }

            TickPending(hScene, ctrl);

            if (HS2OrbitAndExciter.EnableOrbitFinishDirector?.Value != true)
                return;
            if (!OrbitBehaviorHub.IsOrbitAssistActive())
                return;
            if (OrbitFsmCellClassifier.Classify(hScene) != OrbitFsmCell.ActionBridge)
                return;
            if (hScene.NowChangeAnim || OrbitPosePool.IsPeepingPose(ctrl.nowAnimationInfo))
                return;
            if (ConfirmDialog.active)
                return;
            if (ctrl.click != HSceneFlagCtrl.ClickKind.None)
            {
                LogBlocked("external_click", ctrl);
                return;
            }
            if (!string.IsNullOrEmpty(_pendingPath))
                return;
            if (!TryGetHSceneSprite(hScene, out var sprite) || sprite == null)
            {
                LogBlocked("no_sprite", ctrl);
                return;
            }

            string family = ResolveFinishFamily(hScene, ctrl);
            if (family == "")
                return;
            if (family == "B" && ctrl.feel_m < 0.75f)
                return;
            if (family == "C" && ctrl.feel_m < 0.75f)
                return;

            string clip = GetLayer0StateName(hScene);
            var candidates = BuildCandidates(family, hScene, ctrl, sprite, clip);
            OrbitStateMachineLog.Event(
                "finish",
                "candidate",
                "{\"family\":\"" + family +
                "\",\"clip\":\"" + Esc(clip) +
                "\",\"feel_f\":" + ctrl.feel_f.ToString("R", CultureInfo.InvariantCulture) +
                ",\"feel_m\":" + ctrl.feel_m.ToString("R", CultureInfo.InvariantCulture) +
                ",\"bhsAutoFinishEnabled\":" + (OrbitBhsCompat.Snapshot().AutoFinishEnabled ? "true" : "false") +
                ",\"candidates\":" + OrbitFinishPathLedger.BuildCandidateJson(candidates) + "}");

            if (!OrbitFinishPathLedger.TryPick(candidates, out var pick))
            {
                LogBlocked("no_candidate", ctrl);
                return;
            }

            ctrl.click = pick.Click;
            _pendingFamily = pick.Family;
            _pendingPath = pick.Path;
            _pendingSinceUnscaled = Time.unscaledTime;
            _lastPendingPoseId = ctrl.nowAnimationInfo?.id ?? -1;
            OrbitStateMachineLog.Event(
                "finish",
                "set_click",
                "{\"family\":\"" + pick.Family +
                "\",\"path\":\"" + pick.Path +
                "\",\"click\":\"" + pick.Click +
                "\",\"poseId\":" + _lastPendingPoseId +
                ",\"ledger\":" + OrbitFinishPathLedger.BuildSummaryJson(pick.Family) + "}");
        }

        private static void TickPending(HScene hScene, HSceneFlagCtrl ctrl)
        {
            if (string.IsNullOrEmpty(_pendingPath))
                return;

            string clip = GetLayer0StateName(hScene);
            bool consumed = clip.StartsWith("Orgasm", StringComparison.Ordinal)
                || clip.StartsWith("D_Orgasm", StringComparison.Ordinal)
                || clip == "Drink"
                || clip == "Vomit"
                || clip.EndsWith("_A", StringComparison.Ordinal)
                || OrbitFsmCellClassifier.Classify(hScene) == OrbitFsmCell.AfterIdle;
            if (consumed)
            {
                OrbitFinishPathLedger.MarkConsumed(_pendingFamily, _pendingPath);
                OrbitStateMachineLog.Event(
                    "finish",
                    "consumed",
                    "{\"family\":\"" + _pendingFamily +
                    "\",\"path\":\"" + _pendingPath +
                    "\",\"clip\":\"" + Esc(clip) +
                    "\",\"ledger\":" + OrbitFinishPathLedger.BuildSummaryJson(_pendingFamily) + "}");
                ResetPending();
                return;
            }

            bool poseChanged = ctrl.nowAnimationInfo != null && ctrl.nowAnimationInfo.id != _lastPendingPoseId;
            bool timedOut = _pendingSinceUnscaled > 0f && Time.unscaledTime - _pendingSinceUnscaled > PendingTimeoutSeconds;
            if (poseChanged || timedOut)
            {
                OrbitStateMachineLog.Event(
                    "finish",
                    "blocked",
                    "{\"reason\":\"" + (poseChanged ? "pose_changed" : "timeout") +
                    "\",\"family\":\"" + _pendingFamily +
                    "\",\"path\":\"" + _pendingPath +
                    "\",\"clip\":\"" + Esc(clip) + "\"}");
                ResetPending();
            }
        }

        private static void ResetPending()
        {
            _pendingFamily = "";
            _pendingPath = "";
            _pendingSinceUnscaled = -1f;
            _lastPendingPoseId = -1;
        }

        private static List<OrbitFinishCandidate> BuildCandidates(string family, HScene hScene, HSceneFlagCtrl ctrl, HSceneSprite sprite, string clip)
        {
            int modeCtrl = TryReadHSceneInt(hScene, "modeCtrl", -1);
            var candidates = new List<OrbitFinishCandidate>(4);
            if (family == "B")
            {
                bool slot1 = IsFinishVisible(sprite, 1);
                bool slot3 = IsFinishVisible(sprite, 3);
                bool slot4 = IsFinishVisible(sprite, 4);
                bool drink = slot1 && modeCtrl != 0 && !(!slot4 && slot1 && modeCtrl == 1);
                bool vomit = slot3;
                bool outside = slot4 || (slot1 && (modeCtrl == 0 || modeCtrl == 1));
                if (drink)
                    candidates.Add(new OrbitFinishCandidate("B", "drink", HSceneFlagCtrl.ClickKind.FinishDrink, 2));
                if (vomit)
                    candidates.Add(new OrbitFinishCandidate("B", "vomit", HSceneFlagCtrl.ClickKind.FinishVomit, 3));
                if (outside)
                    candidates.Add(new OrbitFinishCandidate("B", "outSide", HSceneFlagCtrl.ClickKind.FinishOutSide, 1));
            }
            else if (family == "C")
            {
                int action1 = ctrl.nowAnimationInfo?.ActionCtrl.Item1 ?? -1;
                bool canInside = IsSonyuInsideConsumable(action1, modeCtrl);
                bool sixnine = action1 == 3 && modeCtrl == 0;
                bool downState = clip.StartsWith("D_", StringComparison.Ordinal);
                bool outOrSameAllowed = !sixnine || !downState;
                bool inOLoop = clip == "OLoop" || clip == "D_OLoop";

                if (IsFinishVisible(sprite, 1) && canInside)
                    candidates.Add(new OrbitFinishCandidate("C", "maleInside", HSceneFlagCtrl.ClickKind.FinishInSide, 4));
                if (IsFinishVisible(sprite, 5) && outOrSameAllowed)
                    candidates.Add(new OrbitFinishCandidate("C", "maleOutside", HSceneFlagCtrl.ClickKind.FinishOutSide, 1));
                if (IsFinishVisible(sprite, 2) && outOrSameAllowed && inOLoop)
                    candidates.Add(new OrbitFinishCandidate("C", "same", HSceneFlagCtrl.ClickKind.FinishSame, 3));
            }
            return candidates;
        }

        private static bool IsSonyuInsideConsumable(int action1, int modeCtrl)
        {
            bool shokushu = action1 == 3 && (modeCtrl == 1 || modeCtrl == 7);
            return (action1 == 2 && modeCtrl == 0) || shokushu;
        }

        private static string ResolveFinishFamily(HScene hScene, HSceneFlagCtrl ctrl)
        {
            int mode = TryReadHSceneInt(hScene, "mode", -1);
            int modeCtrl = TryReadHSceneInt(hScene, "modeCtrl", -1);
            if (mode == 1)
                return "B";
            if (mode == 2)
                return "C";
            if ((mode == 7 || mode == 8) && (modeCtrl == 1 || modeCtrl == 2))
                return "B";
            if ((mode == 7 || mode == 8) && (modeCtrl == 3 || modeCtrl == 4))
                return "C";

            int action = ctrl.nowAnimationInfo?.ActionCtrl.Item1 ?? -1;
            if (action == 1 || action == 5)
                return "B";
            if (action == 2 || action == 6)
                return "C";
            return "";
        }

        private static void LogBlocked(string reason, HSceneFlagCtrl ctrl)
        {
            if (Time.unscaledTime < _nextBlockedLogUnscaled)
                return;
            _nextBlockedLogUnscaled = Time.unscaledTime + BlockLogIntervalSeconds;
            OrbitStateMachineLog.Event(
                "finish",
                "blocked",
                "{\"reason\":\"" + Esc(reason) +
                "\",\"click\":\"" + Esc(ctrl.click.ToString()) +
                "\",\"feel_f\":" + ctrl.feel_f.ToString("R", CultureInfo.InvariantCulture) +
                ",\"feel_m\":" + ctrl.feel_m.ToString("R", CultureInfo.InvariantCulture) + "}");
        }

        private static bool TryGetHSceneSprite(HScene hScene, out HSceneSprite? sprite)
        {
            sprite = null;
            try
            {
                sprite = Traverse.Create(hScene).Field("sprite").GetValue<HSceneSprite>();
                return sprite != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsFinishVisible(HSceneSprite sprite, int slot)
        {
            try { return sprite.IsFinishVisible(slot); }
            catch { return false; }
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

        private static readonly string[] StateNames =
        {
            "Orgasm", "D_Orgasm", "Orgasm_IN", "Orgasm_OUT", "OrgasmF_IN", "OrgasmM_IN",
            "OrgasmM_OUT", "OrgasmS_IN", "D_OrgasmF_IN", "D_OrgasmM_IN",
            "Drink", "Vomit", "Drink_A", "Vomit_A",
            "Orgasm_A", "Orgasm_IN_A", "Orgasm_OUT_A", "OrgasmM_OUT_A",
            "D_Orgasm_A", "D_Orgasm_IN_A", "D_Orgasm_OUT_A", "D_OrgasmM_OUT_A"
        };

        private static string Esc(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return value!.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }
}
