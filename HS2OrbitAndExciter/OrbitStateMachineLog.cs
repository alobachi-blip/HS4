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
        private static readonly string RunId = Guid.NewGuid().ToString("N").Substring(0, 8);
        private static string? _path;
        private static float _nextSnapUnscaled;
        private static string _lastSuppress = "";
        private static string _lastDirector = "";
        private static bool _lastFaint;
        private static bool _lastNowOrgasm;
        private static bool _lastNowChangeAnim;
        private static bool _lastBusy;
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

            var ctrl = hScene.ctrlFlag;
            bool faint = ctrl != null && ctrl.isFaintness;
            bool nowOrgasm = ctrl != null && ctrl.nowOrgasm;
            bool nowChange = hScene.NowChangeAnim;
            bool busy = OrbitManualDirector.IsBusy;
            string director = OrbitPoseDirector.DebugStateName;
            OrbitBehaviorHub.CanAutoAdvance(ctrl, out string suppress);
            // Log field "suppress" = CanAutoAdvance reason (none = allowed).

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

        private static string BuildSnapshotJson(HScene hScene, string suppress, string director)
        {
            var ctrl = hScene.ctrlFlag;
            var cha = OrbitHelpers.GetChaFemales(hScene)?[0];
            var anim = OrbitHelpers.TryGetFemaleAnimBody(cha);
            string clip = "?";
            float clipNorm = -1f;
            if (anim != null)
            {
                try
                {
                    var st = anim.GetCurrentAnimatorStateInfo(0);
                    clipNorm = st.normalizedTime;
                    // Hash-only; name via short IsName probe of common states is heavy — store hash + loop flags.
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
            }

            int mode = TryReadHSceneInt(hScene, "mode", -1);
            int modeCtrl = TryReadHSceneInt(hScene, "modeCtrl", -1);

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
            bool finishVisibleKnown = TryGetHSceneSprite(hScene, out var sprite);
            bool[] finishVisible = new bool[5];
            if (finishVisibleKnown && sprite != null)
            {
                for (int i = 0; i < finishVisible.Length; i++)
                    finishVisible[i] = TryIsFinishVisible(sprite, i + 1);
            }

            var sb = new StringBuilder(768);
            sb.Append('{');
            sb.Append("\"suppress\":\"").Append(Esc(suppress)).Append('"');
            sb.Append(",\"director\":\"").Append(Esc(director)).Append('"');
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
            sb.Append(",\"clipNorm\":").Append(clipNorm.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"nowAnim\":\"").Append(nowName).Append('"');
            sb.Append(",\"selAnim\":\"").Append(selName).Append('"');
            sb.Append(",\"actCtrl\":\"").Append(actCtrl).Append('"');
            sb.Append(",\"finishVisibleKnown\":").Append(finishVisibleKnown ? "true" : "false");
            sb.Append(",\"finishVisible\":[");
            for (int i = 0; i < finishVisible.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(finishVisible[i] ? "true" : "false");
            }
            sb.Append(']');
            sb.Append(",\"peeping\":").Append(OrbitPosePool.IsPeepingPose(ctrl?.nowAnimationInfo) ? "true" : "false");
            sb.Append(",\"orgasmQuiet\":").Append(OrbitBehaviorHub.RemainingOrgasmQuietSeconds().ToString("0.##", CultureInfo.InvariantCulture));
            sb.Append('}');
            return sb.ToString();
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
