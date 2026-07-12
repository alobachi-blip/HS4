using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using AIChara;
using BepInEx;
using HarmonyLib;
using Manager;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// H-session voice tour: advance voice banks by female orgasm / houshi male finish;
    /// persist stage per character; restore FemaleState on exit (card stats untouched).
    /// </summary>
    internal static class OrbitVoiceTour
    {
        internal readonly struct StageDef
        {
            internal StageDef(ChaFileDefine.State state, int phase, int stateNum, string labelZh)
            {
                State = state;
                Phase = phase;
                StateNum = stateNum;
                LabelZh = labelZh;
            }

            internal ChaFileDefine.State State { get; }
            /// <summary>CheckPhase override: 0 shy, 1 experienced, 2 dependence, 4 broken.</summary>
            internal int Phase { get; }
            /// <summary>-1 = N/A; else Favor/Enjoyment/Slavery/Aversion value for start-voice tier.</summary>
            internal int StateNum { get; }
            internal string LabelZh { get; }
        }

        internal static readonly StageDef[] Stages =
        {
            new StageDef(ChaFileDefine.State.Blank, 0, -1, "通常·青澀"),
            new StageDef(ChaFileDefine.State.Favor, 0, 50, "好意低·青澀"),
            new StageDef(ChaFileDefine.State.Favor, 0, 100, "好意高·青澀"),
            new StageDef(ChaFileDefine.State.Favor, 1, 100, "好意·熟練"),
            new StageDef(ChaFileDefine.State.Enjoyment, 0, 50, "享樂低·青澀"),
            new StageDef(ChaFileDefine.State.Enjoyment, 1, 100, "享樂·熟練"),
            new StageDef(ChaFileDefine.State.Slavery, 0, 50, "隷属低·青澀"),
            new StageDef(ChaFileDefine.State.Slavery, 1, 100, "隷属·熟練"),
            new StageDef(ChaFileDefine.State.Aversion, 0, 50, "嫌悪低·青澀"),
            new StageDef(ChaFileDefine.State.Aversion, 1, 100, "嫌悪·熟練"),
            new StageDef(ChaFileDefine.State.Dependence, 2, -1, "依存"),
            new StageDef(ChaFileDefine.State.Broken, 4, -1, "壊れ"),
        };

        private static readonly Dictionary<string, int> ProgressByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static bool _progressLoaded;
        private static bool _active;
        private static bool _hasSnapshot;
        private static string? _charKey;
        private static int _stageIndex;
        private static int _hitsInStage;
        private static int _lastHoushiMaleSum = -1;
        private static int _lastInsideSum = -1;
        private static string _lastTrigger = "—";

        private static ChaFileDefine.State[]? _snapStates;
        private static readonly int[] _snapFavor = new int[2];
        private static readonly int[] _snapEnjoyment = new int[2];
        private static readonly int[] _snapSlavery = new int[2];
        private static readonly int[] _snapAversion = new int[2];

        private static FieldInfo? _femaleStateField;

        internal static bool Enabled => HS2OrbitAndExciter.VoiceTourEnabled?.Value ?? true;
        internal static bool PersistProgress => HS2OrbitAndExciter.VoiceTourPersistProgress?.Value ?? true;
        internal static bool ResetOnNewH => HS2OrbitAndExciter.VoiceTourResetOnNewH?.Value ?? false;
        internal static bool CountHoushiMale => HS2OrbitAndExciter.VoiceTourCountHoushiMaleFinish?.Value ?? true;
        internal static bool Loop => HS2OrbitAndExciter.VoiceTourLoop?.Value ?? true;
        internal static int HitsPerStage
        {
            get
            {
                int v = HS2OrbitAndExciter.VoiceTourHitsPerStage?.Value ?? 1;
                return v < 1 ? 1 : v;
            }
        }

        internal static bool IsActive => _active && Enabled;
        internal static int StageIndex => _stageIndex;
        internal static int StageCount => Stages.Length;
        internal static int HitsInStage => _hitsInStage;
        internal static string? CharKey => _charKey;
        internal static string LastTrigger => _lastTrigger;
        internal static string CurrentLabelZh =>
            _stageIndex >= 0 && _stageIndex < Stages.Length ? Stages[_stageIndex].LabelZh : "—";

        internal static int? TryGetForcedPhase()
        {
            if (!IsActive || _stageIndex < 0 || _stageIndex >= Stages.Length)
                return null;
            return Stages[_stageIndex].Phase;
        }

        internal static void OnHSceneEntered(HScene? hScene)
        {
            if (!Enabled || hScene == null)
            {
                ClearSession(restore: false);
                return;
            }

            EnsureProgressLoaded();
            var cha = OrbitHelpers.GetChaFemales(hScene)?[0];
            string key = ResolveCharKey(cha);
            if (string.IsNullOrEmpty(key))
                key = "unknown";

            // Leaving previous H without null gap (swap): restore then re-enter.
            if (_active && !string.Equals(_charKey, key, StringComparison.OrdinalIgnoreCase))
                RestoreSnapshot();

            _charKey = key;
            if (ResetOnNewH)
                _stageIndex = 0;
            else if (ProgressByKey.TryGetValue(key, out int saved))
                _stageIndex = ClampStage(saved);
            else
                _stageIndex = 0;

            _hitsInStage = 0;
            _lastTrigger = "進H";
            _lastHoushiMaleSum = -1;
            _lastInsideSum = -1;
            Snapshot(hScene);
            ApplyStage();
            _active = true;
            SaveProgressForCurrent();
            HS2OrbitAndExciter.Log?.LogInfo($"[VoiceTour] enter key={key} stage={_stageIndex}/{Stages.Length} ({CurrentLabelZh})");
        }

        internal static void OnHSceneExited()
        {
            if (!_active && !_hasSnapshot)
                return;
            RestoreSnapshot();
            SaveProgressForCurrent();
            FlushProgressFile();
            ClearSession(restore: false);
        }

        internal static void OnFemaleOrgasm()
        {
            if (!IsActive) return;
            _lastTrigger = "女高潮";
            RegisterHit();
        }

        internal static void Tick(HScene? hScene)
        {
            if (hScene == null)
            {
                if (_active)
                    OnHSceneExited();
                return;
            }

            if (!Enabled)
            {
                if (_active)
                {
                    RestoreSnapshot();
                    ClearSession(restore: false);
                }
                return;
            }

            if (!_active)
            {
                OnHSceneEntered(hScene);
                return;
            }

            // Character swap mid-H (G): rebind progress.
            var cha = OrbitHelpers.GetChaFemales(hScene)?[0];
            string key = ResolveCharKey(cha);
            if (!string.IsNullOrEmpty(key) && !string.Equals(key, _charKey, StringComparison.OrdinalIgnoreCase))
            {
                OnHSceneEntered(hScene);
                return;
            }

            EnsureStageAppliedIfDrifted();

            var ctrl = hScene.ctrlFlag;
            if (ctrl == null) return;

            // 插入內射：numInside 增加即推進
            int inside = ctrl.numInside;
            if (_lastInsideSum < 0)
                _lastInsideSum = inside;
            else if (inside > _lastInsideSum)
            {
                int delta = inside - _lastInsideSum;
                _lastInsideSum = inside;
                for (int i = 0; i < delta; i++)
                {
                    _lastTrigger = "內射";
                    RegisterHit();
                }
                PregnancyPlusAssist.TryInflateOnInside(hScene);
            }
            else
                _lastInsideSum = inside;

            if (!CountHoushiMale)
                return;

            var info = ctrl.nowAnimationInfo;
            if (info == null) return;
            // aibu=0 houshi=1 sonyu=2 …
            if (info.ActionCtrl.Item1 != 1)
            {
                _lastHoushiMaleSum = ctrl.numOutSide + ctrl.numDrink;
                return;
            }

            int sum = ctrl.numOutSide + ctrl.numDrink;
            if (_lastHoushiMaleSum < 0)
            {
                _lastHoushiMaleSum = sum;
                return;
            }

            if (sum > _lastHoushiMaleSum)
            {
                int delta = sum - _lastHoushiMaleSum;
                _lastHoushiMaleSum = sum;
                for (int i = 0; i < delta; i++)
                {
                    _lastTrigger = "侍奉射精";
                    RegisterHit();
                    // §16～18：男射也觸發刺青／胸／噴
                    OrbitBehaviorHub.NotifyOrgasmEvent(ctrl, "男射精");
                }
            }
            else
            {
                _lastHoushiMaleSum = sum;
            }
        }

        internal static void ResetCurrentCharacterProgress()
        {
            EnsureProgressLoaded();
            string? key = _charKey;
            if (string.IsNullOrEmpty(key))
                return;
            ProgressByKey[key!] = 0;
            _stageIndex = 0;
            _hitsInStage = 0;
            _lastTrigger = "重置";
            if (_active)
                ApplyStage();
            FlushProgressFile();
        }

        private static void RegisterHit()
        {
            OrbitBehaviorHub.NotifyVoiceTourHit(_lastTrigger);

            _hitsInStage++;
            if (_hitsInStage < HitsPerStage)
            {
                SaveProgressForCurrent();
                return;
            }

            _hitsInStage = 0;
            int next = _stageIndex + 1;
            if (next >= Stages.Length)
                next = Loop ? 0 : Stages.Length - 1;
            _stageIndex = next;
            ApplyStage();
            SaveProgressForCurrent();
            FlushProgressFile();
            HS2OrbitAndExciter.Log?.LogInfo($"[VoiceTour] advance → {_stageIndex} {CurrentLabelZh} via {_lastTrigger}");
        }

        private static void Snapshot(HScene hScene)
        {
            var mgr = Singleton<HSceneManager>.Instance;
            if (mgr == null) return;

            EnsureFields();
            var states = _femaleStateField?.GetValue(mgr) as ChaFileDefine.State[];
            if (states == null || states.Length == 0) return;

            _snapStates = (ChaFileDefine.State[])states.Clone();
            var nums = mgr.FemaleStateNum;
            for (int i = 0; i < 2 && i < nums.Length; i++)
            {
                var d = nums[i];
                _snapFavor[i] = d != null && d.TryGetValue(ChaFileDefine.State.Favor, out int f) ? f : 0;
                _snapEnjoyment[i] = d != null && d.TryGetValue(ChaFileDefine.State.Enjoyment, out int e) ? e : 0;
                _snapSlavery[i] = d != null && d.TryGetValue(ChaFileDefine.State.Slavery, out int s) ? s : 0;
                _snapAversion[i] = d != null && d.TryGetValue(ChaFileDefine.State.Aversion, out int a) ? a : 0;
            }
            _hasSnapshot = true;
        }

        private static void RestoreSnapshot()
        {
            if (!_hasSnapshot || _snapStates == null) return;
            var mgr = Singleton<HSceneManager>.Instance;
            if (mgr == null) return;

            EnsureFields();
            var states = _femaleStateField?.GetValue(mgr) as ChaFileDefine.State[];
            if (states != null)
            {
                for (int i = 0; i < states.Length && i < _snapStates.Length; i++)
                    states[i] = _snapStates[i];
            }

            var nums = mgr.FemaleStateNum;
            for (int i = 0; i < 2 && i < nums.Length; i++)
            {
                var d = nums[i];
                if (d == null) continue;
                d[ChaFileDefine.State.Favor] = _snapFavor[i];
                d[ChaFileDefine.State.Enjoyment] = _snapEnjoyment[i];
                d[ChaFileDefine.State.Slavery] = _snapSlavery[i];
                d[ChaFileDefine.State.Aversion] = _snapAversion[i];
            }
            _hasSnapshot = false;
        }

        private static void EnsureStageAppliedIfDrifted()
        {
            if (_stageIndex < 0 || _stageIndex >= Stages.Length) return;
            var mgr = Singleton<HSceneManager>.Instance;
            if (mgr == null) return;
            EnsureFields();
            var states = _femaleStateField?.GetValue(mgr) as ChaFileDefine.State[];
            if (states == null || states.Length == 0) return;
            if (states[0] != Stages[_stageIndex].State)
                ApplyStage();
        }

        private static void ApplyStage()
        {
            if (_stageIndex < 0 || _stageIndex >= Stages.Length) return;
            var mgr = Singleton<HSceneManager>.Instance;
            if (mgr == null) return;

            EnsureFields();
            var def = Stages[_stageIndex];
            var states = _femaleStateField?.GetValue(mgr) as ChaFileDefine.State[];
            if (states != null)
            {
                for (int i = 0; i < states.Length; i++)
                    states[i] = def.State;
            }

            var nums = mgr.FemaleStateNum;
            for (int i = 0; i < nums.Length; i++)
            {
                var d = nums[i];
                if (d == null) continue;
                int n = def.StateNum >= 0 ? def.StateNum : 0;
                switch (def.State)
                {
                    case ChaFileDefine.State.Favor:
                        d[ChaFileDefine.State.Favor] = n;
                        break;
                    case ChaFileDefine.State.Enjoyment:
                        d[ChaFileDefine.State.Enjoyment] = n;
                        break;
                    case ChaFileDefine.State.Slavery:
                        d[ChaFileDefine.State.Slavery] = n;
                        break;
                    case ChaFileDefine.State.Aversion:
                        d[ChaFileDefine.State.Aversion] = n;
                        break;
                }
            }
        }

        private static void ClearSession(bool restore)
        {
            if (restore)
                RestoreSnapshot();
            _active = false;
            _hasSnapshot = false;
            _snapStates = null;
            _lastHoushiMaleSum = -1;
            _lastInsideSum = -1;
        }

        private static void SaveProgressForCurrent()
        {
            string? key = _charKey;
            if (string.IsNullOrEmpty(key)) return;
            ProgressByKey[key!] = _stageIndex;
        }

        private static int ClampStage(int s)
        {
            if (s < 0) return 0;
            if (s >= Stages.Length) return Stages.Length - 1;
            return s;
        }

        private static string ResolveCharKey(ChaControl? cha)
        {
            if (cha == null) return "unknown";
            string? path = OrbitHelpers.GetUserDataFemaleCharaPath(cha);
            if (!string.IsNullOrEmpty(path))
                return Path.GetFileNameWithoutExtension(path);
            try
            {
                string name = cha.chaFile?.charaFileName ?? "";
                if (!string.IsNullOrEmpty(name))
                    return Path.GetFileNameWithoutExtension(name);
            }
            catch { /* ignore */ }
            try
            {
                string fn = cha.chaFile?.parameter?.fullname ?? "";
                int p = cha.fileParam2 != null ? cha.fileParam2.personality : -1;
                if (!string.IsNullOrEmpty(fn))
                    return $"{fn}_{p}";
            }
            catch { /* ignore */ }
            return "unknown";
        }

        private static void EnsureFields()
        {
            if (_femaleStateField != null) return;
            _femaleStateField = AccessTools.Field(typeof(HSceneManager), "femaleState");
        }

        private static string ProgressPath
        {
            get
            {
                string dir = Path.Combine(Paths.ConfigPath);
                return Path.Combine(dir, "HS2OrbitAndExciter.VoiceTour.json");
            }
        }

        private static void EnsureProgressLoaded()
        {
            if (_progressLoaded) return;
            _progressLoaded = true;
            try
            {
                string path = ProgressPath;
                if (!File.Exists(path)) return;
                string json = File.ReadAllText(path, Encoding.UTF8);
                ParseSimpleJson(json);
            }
            catch (Exception ex)
            {
                HS2OrbitAndExciter.Log?.LogWarning($"[VoiceTour] load progress failed: {ex.Message}");
            }
        }

        private static void FlushProgressFile()
        {
            if (!PersistProgress) return;
            try
            {
                var sb = new StringBuilder();
                sb.Append("{\n");
                bool first = true;
                foreach (var kv in ProgressByKey)
                {
                    if (!first) sb.Append(",\n");
                    first = false;
                    sb.Append("  ").Append(JsonString(kv.Key)).Append(": ").Append(kv.Value);
                }
                sb.Append("\n}\n");
                File.WriteAllText(ProgressPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                HS2OrbitAndExciter.Log?.LogWarning($"[VoiceTour] save progress failed: {ex.Message}");
            }
        }

        private static string JsonString(string s)
        {
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        /// <summary>Minimal {"key": n, ...} parser (no nested objects).</summary>
        private static void ParseSimpleJson(string json)
        {
            ProgressByKey.Clear();
            int i = 0;
            while (i < json.Length)
            {
                int q1 = json.IndexOf('"', i);
                if (q1 < 0) break;
                int q2 = json.IndexOf('"', q1 + 1);
                if (q2 < 0) break;
                string key = json.Substring(q1 + 1, q2 - q1 - 1).Replace("\\\"", "\"").Replace("\\\\", "\\");
                int colon = json.IndexOf(':', q2 + 1);
                if (colon < 0) break;
                int j = colon + 1;
                while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
                int start = j;
                while (j < json.Length && (char.IsDigit(json[j]) || json[j] == '-')) j++;
                if (start < j && int.TryParse(json.Substring(start, j - start), out int val))
                    ProgressByKey[key] = ClampStage(val);
                i = j;
            }
        }
    }
}
