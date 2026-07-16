using System;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// NDJSON state-machine log for diagnosing orbit assist / pose / faintness deadlocks.
    /// File: <c>BepInEx/LogOutput/HS2OrbitAndExciter_fsm.ndjson</c>
    /// </summary>
    internal static class OrbitStateMachineLog
    {
        private const float SnapIntervalSeconds = 0.5f;
        private const float SessionStateIntervalSeconds = 0.5f;
        private static readonly string RunId = Guid.NewGuid().ToString("N").Substring(0, 8);
        private static string? _path;
        private static float _nextSnapUnscaled;
        private static float _nextSessionStateUnscaled;
        private static string _lastSuppress = "";
        private static string _lastDirector = "";
        private static bool _lastFaint;
        private static bool _lastNowOrgasm;
        private static bool _lastNowChangeAnim;
        private static bool _lastBusy;
        private static int _lastHSceneId = -1;
        private static int _lastSessionPoseId = -999;
        private static int _sessionIndex;
        private static string _lastSessionStateSignature = "";
        private static bool _bootWritten;

        internal static bool Enabled => HS2OrbitAndExciter.EnableStateMachineTrace?.Value == true;

        internal static string? LogPath
        {
            get
            {
                EnsurePath();
                return _path;
            }
        }

        internal static void EnsurePath()
        {
            if (_path != null) return;
            try
            {
                string dir = Path.Combine(Paths.BepInExRootPath, "LogOutput");
                Directory.CreateDirectory(dir);
                _path = Path.Combine(dir, "HS2OrbitAndExciter_fsm.ndjson");
            }
            catch
            {
                _path = Path.Combine(Application.dataPath, "HS2OrbitAndExciter_fsm.ndjson");
            }
        }

        internal static void Boot()
        {
            if (!Enabled) return;
            if (_bootWritten) return;
            _bootWritten = true;
            Write("boot", "OrbitStateMachineLog", "logger_ready",
                "{\"dll\":\"" + Esc(PluginBuildIdentity.AssemblyFileName) +
                "\",\"path\":\"" + Esc(LogPath) + "\"}");
            HS2OrbitAndExciter.Log?.LogInfo($"[FSM] state log → {LogPath}");
        }

        internal static void Event(string kind, string message, string dataJson = "{}")
        {
            if (!Enabled) return;
            Write(kind, "event", message, dataJson);
        }

        internal static void Tick(HScene? hScene)
        {
            if (!Enabled) return;
            Boot();
            if (hScene == null) return;
            OrbitBhsCompat.LogIfChanged();

            var ctrl = hScene.ctrlFlag;
            bool faint = ctrl != null && ctrl.isFaintness;
            bool nowOrgasm = ctrl != null && ctrl.nowOrgasm;
            bool nowChange = hScene.NowChangeAnim;
            bool busy = OrbitManualDirector.IsBusy;
            string director = OrbitPoseDirector.DebugStateName;
            OrbitBehaviorHub.CanAutoAdvance(ctrl, out string suppress);
            // Log field "suppress" = CanAutoAdvance reason (none = allowed).
            TickSessionTrace(hScene, suppress, director);

            if (suppress != _lastSuppress)
            {
                Write("gate", "suppress_change", suppress,
                    BuildSnapshotJson(hScene, suppress, director));
                _lastSuppress = suppress;
            }
            if (director != _lastDirector)
            {
                Write("director", "state_change", director,
                    BuildSnapshotJson(hScene, suppress, director));
                _lastDirector = director;
            }
            if (faint != _lastFaint)
            {
                Write("faint", faint ? "enter" : "exit", faint ? "isFaintness=1" : "isFaintness=0",
                    BuildSnapshotJson(hScene, suppress, director));
                _lastFaint = faint;
            }
            if (nowOrgasm != _lastNowOrgasm)
            {
                Write("orgasm", nowOrgasm ? "enter" : "exit", "nowOrgasm",
                    BuildSnapshotJson(hScene, suppress, director));
                _lastNowOrgasm = nowOrgasm;
            }
            if (nowChange != _lastNowChangeAnim)
            {
                Write("anim", nowChange ? "change_start" : "change_end", "NowChangeAnim",
                    BuildSnapshotJson(hScene, suppress, director));
                _lastNowChangeAnim = nowChange;
            }
            if (busy != _lastBusy)
            {
                Write("manual", busy ? "busy" : "idle", "ManualDirector",
                    BuildSnapshotJson(hScene, suppress, director));
                _lastBusy = busy;
            }

            if (Time.unscaledTime < _nextSnapUnscaled) return;
            _nextSnapUnscaled = Time.unscaledTime + SnapIntervalSeconds;
            Write("SNAP", "tick", "snapshot", BuildSnapshotJson(hScene, suppress, director));
        }

        internal static void Hotkey(string key, bool ok, string detail)
        {
            if (!Enabled) return;
            Write("hotkey", key, ok ? "ok" : "fail",
                "{\"ok\":" + (ok ? "true" : "false") + ",\"detail\":\"" + Esc(detail) + "\"}");
        }

        private static void TickSessionTrace(HScene hScene, string suppress, string director)
        {
            int hSceneId = hScene.GetInstanceID();
            var ctrl = hScene.ctrlFlag;
            int poseId = ctrl?.nowAnimationInfo?.id ?? -1;
            string signature = BuildSessionStateSignature(hScene, suppress, director);
            bool newScene = hSceneId != _lastHSceneId;
            bool newPose = poseId != _lastSessionPoseId;

            if (newScene || newPose)
            {
                _sessionIndex++;
                _lastHSceneId = hSceneId;
                _lastSessionPoseId = poseId;
                _lastSessionStateSignature = "";
                _nextSessionStateUnscaled = 0f;
                Write("session/start", "trace", newScene ? "hscene" : "pose",
                    BuildSnapshotJson(hScene, suppress, director));
            }

            if (Time.unscaledTime < _nextSessionStateUnscaled
                && string.Equals(signature, _lastSessionStateSignature, StringComparison.Ordinal))
                return;

            _nextSessionStateUnscaled = Time.unscaledTime + SessionStateIntervalSeconds;
            _lastSessionStateSignature = signature;
            Write("session/state", "trace", "state", BuildSnapshotJson(hScene, suppress, director));
        }

        private static string BuildSessionStateSignature(HScene hScene, string suppress, string director)
        {
            var ctrl = hScene.ctrlFlag;
            int mode = TryReadHSceneInt(hScene, "mode", -1);
            int modeCtrl = TryReadHSceneInt(hScene, "modeCtrl", -1);
            string clip = TryGetLayer0State(hScene, out float _, out int hash);
            int poseId = ctrl?.nowAnimationInfo?.id ?? -1;
            int selId = ctrl?.selectAnimationListInfo?.id ?? -1;
            string click = ctrl != null ? ctrl.click.ToString() : "";
            bool[] finishVisible = TryReadFinishVisible(hScene, out bool finishKnown);
            var cell = OrbitFsmCellClassifier.Classify(hScene);

            var sb = new StringBuilder(160);
            sb.Append(mode).Append('|').Append(modeCtrl).Append('|').Append(cell).Append('|');
            sb.Append(poseId).Append('|').Append(selId).Append('|').Append(clip).Append('|').Append(hash).Append('|');
            sb.Append(ctrl != null && ctrl.isFaintness ? "F" : "f").Append('|');
            sb.Append(ctrl != null && ctrl.nowOrgasm ? "O" : "o").Append('|');
            sb.Append(hScene.NowChangeAnim ? "C" : "c").Append('|');
            sb.Append(click).Append('|').Append(suppress).Append('|').Append(director).Append('|').Append(finishKnown ? "V" : "v");
            for (int i = 0; i < finishVisible.Length; i++)
                sb.Append(finishVisible[i] ? '1' : '0');
            return sb.ToString();
        }

        private static string BuildSnapshotJson(HScene hScene, string suppress, string director)
        {
            var ctrl = hScene.ctrlFlag;
            string clip = TryGetLayer0State(hScene, out float clipNorm, out int clipHash);

            int mode = TryReadHSceneInt(hScene, "mode", -1);
            int modeCtrl = TryReadHSceneInt(hScene, "modeCtrl", -1);
            int actionCtrl1 = -1;
            int actionCtrl2 = -1;

            string nowName = ctrl?.nowAnimationInfo != null
                ? Esc(ctrl.nowAnimationInfo.nameAnimation) + "#id" + ctrl.nowAnimationInfo.id + ";down" + ctrl.nowAnimationInfo.nDownPtn
                : "";
            string selName = ctrl?.selectAnimationListInfo != null
                ? Esc(ctrl.selectAnimationListInfo.nameAnimation) + "#id" + ctrl.selectAnimationListInfo.id + ";down" + ctrl.selectAnimationListInfo.nDownPtn
                : "";
            string actCtrl = "";
            if (ctrl?.nowAnimationInfo != null)
            {
                var ac = ctrl.nowAnimationInfo.ActionCtrl;
                actionCtrl1 = ac.Item1;
                actionCtrl2 = ac.Item2;
                actCtrl = ac.Item1 + "," + ac.Item2;
            }

            float speed = 0f;
            try
            {
                if (ctrl != null)
                    speed = (float)(HarmonyLib.Traverse.Create(ctrl).Field("speed").GetValue() ?? 0f);
            }
            catch { /* ignore */ }

            float feelF = ctrl?.feel_f ?? -1f;
            float feelM = ctrl?.feel_m ?? -1f;
            string click = ctrl != null ? ctrl.click.ToString() : "";
            int clickValue = ctrl != null ? (int)ctrl.click : -999;
            bool[] finishVisible = TryReadFinishVisible(hScene, out bool finishVisibleKnown);
            OrbitPosePool.RefreshTraceStats(ctrl);
            var posePoolStats = OrbitPoseUnlockPolicy.LastPosePoolStats;
            var bhs = OrbitBhsCompat.Snapshot();
            var fsmCell = OrbitFsmCellClassifier.Classify(hScene);
            bool peeping = OrbitPosePool.IsPeepingPose(ctrl?.nowAnimationInfo);
            string sessionFamily = DescribeSessionFamily(mode, modeCtrl, actionCtrl1, actionCtrl2, peeping);

            var sb = new StringBuilder(1400);
            sb.Append('{');
            sb.Append("\"sessionIndex\":").Append(_sessionIndex);
            sb.Append(",\"suppress\":\"").Append(Esc(suppress)).Append('"');
            sb.Append(",\"director\":\"").Append(Esc(director)).Append('"');
            sb.Append(",\"fsmCell\":\"").Append(Esc(fsmCell.ToString())).Append('"');
            sb.Append(",\"sessionFamily\":\"").Append(Esc(sessionFamily)).Append('"');
            sb.Append(",\"mode\":").Append(mode);
            sb.Append(",\"modeCtrl\":").Append(modeCtrl);
            sb.Append(",\"orbit\":").Append(OrbitBehaviorHub.IsOrbitAssistActive() ? "true" : "false");
            sb.Append(",\"faint\":").Append(ctrl != null && ctrl.isFaintness ? "true" : "false");
            sb.Append(",\"faintType\":").Append(ctrl?.FaintnessType ?? -999);
            sb.Append(",\"nowOrgasm\":").Append(ctrl != null && ctrl.nowOrgasm ? "true" : "false");
            sb.Append(",\"nowChangeAnim\":").Append(hScene.NowChangeAnim ? "true" : "false");
            sb.Append(",\"manualBusy\":").Append(OrbitManualDirector.IsBusy ? "true" : "false");
            sb.Append(",\"inputForcus\":").Append(ctrl != null && ctrl.inputForcus ? "true" : "false");
            sb.Append(",\"isAutoAction\":").Append(ctrl != null && ctrl.isAutoActionChange ? "true" : "false");
            sb.Append(",\"speed\":").Append(speed.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"feel_f\":").Append(feelF.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"feel_m\":").Append(feelM.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"click\":\"").Append(Esc(click)).Append('"');
            sb.Append(",\"clickValue\":").Append(clickValue);
            sb.Append(",\"inActionLoop\":").Append(OrbitHelpers.IsFirstFemaleInActionLoop(hScene) ? "true" : "false");
            sb.Append(",\"clip\":\"").Append(Esc(clip)).Append('"');
            sb.Append(",\"clipHash\":").Append(clipHash);
            sb.Append(",\"clipNorm\":").Append(clipNorm.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"nowAnim\":\"").Append(nowName).Append('"');
            sb.Append(",\"nowAnimId\":").Append(ctrl?.nowAnimationInfo?.id ?? -1);
            sb.Append(",\"nowAnimName\":\"").Append(Esc(ctrl?.nowAnimationInfo?.nameAnimation)).Append('"');
            sb.Append(",\"nowAnimDown\":").Append(ctrl?.nowAnimationInfo?.nDownPtn ?? -1);
            sb.Append(",\"selAnim\":\"").Append(selName).Append('"');
            sb.Append(",\"selAnimId\":").Append(ctrl?.selectAnimationListInfo?.id ?? -1);
            sb.Append(",\"selAnimName\":\"").Append(Esc(ctrl?.selectAnimationListInfo?.nameAnimation)).Append('"');
            sb.Append(",\"selAnimDown\":").Append(ctrl?.selectAnimationListInfo?.nDownPtn ?? -1);
            sb.Append(",\"actCtrl\":\"").Append(actCtrl).Append('"');
            sb.Append(",\"actionCtrl1\":").Append(actionCtrl1);
            sb.Append(",\"actionCtrl2\":").Append(actionCtrl2);
            sb.Append(",\"finishVisibleKnown\":").Append(finishVisibleKnown ? "true" : "false");
            sb.Append(",\"finishVisible\":[");
            for (int i = 0; i < finishVisible.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(finishVisible[i] ? "true" : "false");
            }
            sb.Append(']');
            sb.Append(",\"posePool\":{\"total\":").Append(posePoolStats.Total);
            sb.Append(",\"afterUnlock\":").Append(posePoolStats.AfterUnlock);
            sb.Append(",\"afterFaintness\":").Append(posePoolStats.AfterFaintness).Append('}');
            sb.Append(",\"poseUnlock\":{\"enabled\":")
                .Append(HS2OrbitAndExciter.EnableSafePoseUnlock?.Value != false ? "true" : "false");
            sb.Append(",\"motionLimitRelaxed\":").Append(OrbitPoseUnlockPolicy.RelaxedMotionLimitCount);
            sb.Append(",\"recoverRelaxed\":").Append(OrbitPoseUnlockPolicy.RelaxedMotionLimitRecoverCount);
            sb.Append(",\"autoRelaxed\":").Append(OrbitPoseUnlockPolicy.RelaxedAutoMotionLimitCount);
            sb.Append(",\"unsafeRejects\":").Append(OrbitPoseUnlockPolicy.UnsafeRejectCount);
            sb.Append(",\"errors\":").Append(OrbitPoseUnlockPolicy.ErrorCount).Append('}');
            sb.Append(",\"bhsInstalled\":").Append(bhs.Installed ? "true" : "false");
            sb.Append(",\"bhsConfigFound\":").Append(bhs.ConfigFound ? "true" : "false");
            sb.Append(",\"bhsAutoFinishEnabled\":").Append(bhs.AutoFinishEnabled ? "true" : "false");
            sb.Append(",\"bhsOffsetApplied\":").Append(bhs.OffsetApplied ? "true" : "false");
            sb.Append(",\"bhsSolverEnabled\":").Append(bhs.SolverEnabled ? "true" : "false");
            sb.Append(",\"peeping\":").Append(peeping ? "true" : "false");
            sb.Append(",\"orgasmQuiet\":").Append(OrbitBehaviorHub.RemainingOrgasmQuietSeconds().ToString("0.##", CultureInfo.InvariantCulture));
            sb.Append('}');
            return sb.ToString();
        }

        private static string TryGetLayer0State(HScene hScene, out float normalizedTime, out int fullPathHash)
        {
            normalizedTime = -1f;
            fullPathHash = 0;
            var cha = OrbitHelpers.GetChaFemales(hScene)?[0];
            var anim = OrbitHelpers.TryGetFemaleAnimBody(cha);
            string clip = "?";
            if (anim == null)
                return clip;
            try
            {
                var st = anim.GetCurrentAnimatorStateInfo(0);
                normalizedTime = st.normalizedTime;
                fullPathHash = st.fullPathHash;
                clip = "h=" + st.fullPathHash + ";norm=" + st.normalizedTime.ToString("0.###", CultureInfo.InvariantCulture);
                foreach (string n in ProbeStateNames)
                {
                    if (st.IsName(n))
                    {
                        clip = n;
                        break;
                    }
                }
            }
            catch { /* ignore */ }
            return clip;
        }

        private static bool[] TryReadFinishVisible(HScene hScene, out bool known)
        {
            bool[] finishVisible = new bool[5];
            known = TryGetHSceneSprite(hScene, out var sprite);
            if (known && sprite != null)
            {
                for (int i = 0; i < finishVisible.Length; i++)
                    finishVisible[i] = TryIsFinishVisible(sprite, i + 1);
            }
            return finishVisible;
        }

        private static string DescribeSessionFamily(int mode, int modeCtrl, int actionCtrl1, int actionCtrl2, bool peeping)
        {
            if (peeping || mode == 5)
                return "Peeping";

            string actionFamily = DescribeActionFamily(actionCtrl1, actionCtrl2);
            if (actionFamily != "Unknown")
                return actionFamily;

            switch (mode)
            {
                case 0:
                    return "A_Aibu";
                case 1:
                    return "B_Houshi";
                case 2:
                    return "C_Sonyu";
                case 3:
                    return "E_Spnking";
                case 4:
                    return "D_Masturbation";
                case 6:
                    return "A_Les";
                case 7:
                case 8:
                    if (modeCtrl == 0)
                        return "A_MultiPlay";
                    if (modeCtrl == 1 || modeCtrl == 2)
                        return "B_MultiPlay";
                    if (modeCtrl == 3 || modeCtrl == 4)
                        return "C_MultiPlay";
                    return "MultiPlay";
                default:
                    return "Unknown";
            }
        }

        private static string DescribeActionFamily(int actionCtrl1, int actionCtrl2)
        {
            if (actionCtrl1 == 0)
                return "A_Aibu";
            if (actionCtrl1 == 1)
                return "B_Houshi";
            if (actionCtrl1 == 2)
                return "C_Sonyu";
            if (actionCtrl1 == 3)
            {
                if (actionCtrl2 == 0 || actionCtrl2 == 1 || actionCtrl2 == 7)
                    return "C_Sonyu";
                if (actionCtrl2 == 2)
                    return "E_Spnking";
                if (actionCtrl2 == 3)
                    return "A_Aibu";
                if (actionCtrl2 == 4 || actionCtrl2 == 5)
                    return "D_Masturbation";
                if (actionCtrl2 == 6)
                    return "Peeping";
            }
            if (actionCtrl1 == 4)
                return "A_Les";
            if (actionCtrl1 == 5)
                return "B_MultiPlay";
            if (actionCtrl1 == 6)
                return "C_MultiPlay";
            return "Unknown";
        }

        private static int TryReadHSceneInt(HScene hScene, string fieldName, int fallback)
        {
            try
            {
                return HarmonyLib.Traverse.Create(hScene).Field(fieldName).GetValue<int>();
            }
            catch
            {
                return fallback;
            }
        }

        private static bool TryGetHSceneSprite(HScene hScene, out HSceneSprite? sprite)
        {
            sprite = null;
            try
            {
                sprite = HarmonyLib.Traverse.Create(hScene).Field("sprite").GetValue<HSceneSprite>();
                return sprite != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryIsFinishVisible(HSceneSprite sprite, int slot)
        {
            try
            {
                return sprite.IsFinishVisible(slot);
            }
            catch
            {
                return false;
            }
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

        private static void Write(string hypothesisId, string location, string message, string dataJson)
        {
            if (!Enabled) return;
            EnsurePath();
            try
            {
                long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                string line =
                    "{\"runId\":\"" + RunId +
                    "\",\"dll\":\"" + Esc(PluginBuildIdentity.AssemblyFileName) +
                    "\",\"ts\":" + ts +
                    ",\"ut\":" + Time.unscaledTime.ToString("R", CultureInfo.InvariantCulture) +
                    ",\"id\":\"" + Esc(hypothesisId) +
                    "\",\"loc\":\"" + Esc(location) +
                    "\",\"msg\":\"" + Esc(message) +
                    "\",\"data\":" + dataJson + "}\n";
                File.AppendAllText(_path!, line, Encoding.UTF8);
            }
            catch
            {
                // never break gameplay for logging
            }
        }

        private static string Esc(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s!.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }
}
