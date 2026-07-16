using System;
using System.Collections.Generic;

namespace HS2OrbitAndExciter
{
    /// <summary>選池抽中後的流程線（契約 §1：動作線→閒置；窺視→窺視段）。</summary>
    internal enum PosePoolLine
    {
        Action,
        Peeping
    }

    /// <summary>選池一次抽取結果（當幀事件；非常駐狀態）。</summary>
    internal readonly struct PosePoolPick
    {
        internal readonly HScene.AnimationListInfo Info;
        internal readonly PosePoolLine Line;
        internal readonly bool RelaxedSessionDedup;

        internal PosePoolPick(HScene.AnimationListInfo info, PosePoolLine line, bool relaxedSessionDedup)
        {
            Info = info;
            Line = line;
            RelaxedSessionDedup = relaxedSessionDedup;
        }
    }

    /// <summary>
    /// §1 選池：混池（動作＋窺視）、本場去重、耗盡清空、空池放寬去重、窺視用 ActionCtrl。
    /// 重用 <see cref="OrbitShufflePool"/>；換角不清已用（C1）。
    /// </summary>
    internal static class OrbitPosePool
    {
        private static readonly HashSet<string> UsedPoseKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, HScene.AnimationListInfo> KeyToInfo =
            new Dictionary<string, HScene.AnimationListInfo>(StringComparer.Ordinal);

        private static PosePoolPick? _lastPick;
        private static int _coverageIndex;
        private static string _coverageSequenceCache = "";
        private static string[] _coverageSequence = Array.Empty<string>();
        private static bool _coverageStartLogged;
        private static readonly HashSet<string> CoverageMissingLogged =
            new HashSet<string>(StringComparer.Ordinal);

        internal static int UsedCount => UsedPoseKeys.Count;
        internal static PosePoolPick? LastPick => _lastPick;

        /// <summary>新 H 場景：清空本場已用。換角不要呼叫。</summary>
        internal static void OnHSceneEntered()
        {
            UsedPoseKeys.Clear();
            _lastPick = null;
            _coverageIndex = 0;
            _coverageStartLogged = false;
            CoverageMissingLogged.Clear();
        }

        internal static void ResetSession() => OnHSceneEntered();

        internal static void RefreshTraceStats(HSceneFlagCtrl? ctrlFlag)
        {
            var all = OrbitHelpers.GetAllPoseList();
            if (all.Count == 0)
            {
                OrbitPoseUnlockPolicy.RecordPosePoolStats(0, 0, 0);
                return;
            }
            RecordCandidateStats(ctrlFlag?.nowAnimationInfo, all, ctrlFlag);
        }

        /// <summary>
        /// 窺視判定：會進原版 <c>Peeping</c>（lstProc[5]）。
        /// 對齊 <c>HScene.ChangeModeCtrl</c>：ActionCtrl.Item1==3 且 Item2==6 → mode=5。
        /// 同一筆 <c>AnimationListInfo</c> 上 UI／LOG 顯示的是 <c>nameAnimation</c>（日文或譯名），
        /// 分流只認 ActionCtrl，不認顯示文字、不認硬編碼 id。
        /// </summary>
        internal static bool IsPeepingPose(HScene.AnimationListInfo? info)
        {
            if (info == null)
                return false;
            return info.ActionCtrl.Item1 == 3 && info.ActionCtrl.Item2 == 6;
        }

        internal static PosePoolLine ClassifyLine(HScene.AnimationListInfo info) =>
            IsPeepingPose(info) ? PosePoolLine.Peeping : PosePoolLine.Action;

        internal static string PoseKey(HScene.AnimationListInfo info) =>
            info.id.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// 抽下一姿。失敗回 null（日誌已打）；呼叫端留在原狀態、不換姿。
        /// </summary>
        internal static PosePoolPick? TryPick(
            HScene.AnimationListInfo? current,
            List<HScene.AnimationListInfo> all,
            HSceneFlagCtrl? ctrlFlag,
            string trigger)
        {
            _lastPick = null;
            if (all == null || all.Count == 0)
            {
                OrbitPoseUnlockPolicy.RecordPosePoolStats(0, 0, 0);
                Fail(trigger, "empty_table", current, relaxed: false);
                return null;
            }
            RecordCandidateStats(current, all, ctrlFlag);

            bool coveragePick = false;
            string coverageFamily = "";
            var pick = TryPickSmokeCoverage(current, all, ctrlFlag, preferUnused: true, out coverageFamily);
            if (pick != null)
                coveragePick = true;
            else
                pick = TryPickOnce(current, all, ctrlFlag, preferUnused: true);
            bool relaxed = false;
            if (pick == null)
            {
                // D2：放寬本場去重再試（仍排除當前＋虛脫過濾）
                relaxed = true;
                pick = TryPickSmokeCoverage(current, all, ctrlFlag, preferUnused: false, out coverageFamily);
                if (pick != null)
                    coveragePick = true;
                else
                    pick = TryPickOnce(current, all, ctrlFlag, preferUnused: false);
            }

            if (pick == null)
            {
                Fail(trigger, "no_candidate", current, relaxed);
                return null;
            }

            var info = pick;
            string key = PoseKey(info);
            UsedPoseKeys.Add(key);

            var result = new PosePoolPick(info, ClassifyLine(info), relaxed);
            _lastPick = result;

            string label = string.IsNullOrEmpty(info.nameAnimation) ? "?" : info.nameAnimation;
            string lineName = result.Line == PosePoolLine.Peeping ? "peeping" : "action";
            HS2OrbitAndExciter.Log?.LogInfo(
                $"Orbit: 選池 [{trigger}] → {label} id={info.id} 線={lineName}" +
                (relaxed ? " relaxed" : "") +
                $" 已用={UsedPoseKeys.Count}");
            OrbitStateMachineLog.Event(
                "選池",
                trigger,
                "{\"id\":" + info.id +
                ",\"line\":\"" + (result.Line == PosePoolLine.Peeping ? "peeping" : "action") +
                "\",\"family\":\"" + EscapeJson(ClassifyCoverageFamily(info)) +
                "\",\"coverageTarget\":\"" + EscapeJson(coveragePick ? coverageFamily : "") +
                "\",\"relaxed\":" + (relaxed ? "true" : "false") +
                ",\"used\":" + UsedPoseKeys.Count + "}");
            if (coveragePick)
            {
                OrbitStateMachineLog.Event(
                    "smoke",
                    "family_coverage_pick",
                    "{\"target\":\"" + EscapeJson(coverageFamily) +
                    "\",\"family\":\"" + EscapeJson(ClassifyCoverageFamily(info)) +
                    "\",\"id\":" + info.id +
                    ",\"name\":\"" + EscapeJson(info.nameAnimation) + "\"}");
            }
            return result;
        }

        private static HScene.AnimationListInfo? TryPickSmokeCoverage(
            HScene.AnimationListInfo? current,
            List<HScene.AnimationListInfo> all,
            HSceneFlagCtrl? ctrlFlag,
            bool preferUnused,
            out string targetFamily)
        {
            targetFamily = "";
            if (HS2OrbitAndExciter.EnableSmokeFamilyCoverage?.Value != true)
                return null;

            var sequence = GetCoverageSequence();
            if (sequence.Length == 0)
                return null;

            if (!_coverageStartLogged)
            {
                _coverageStartLogged = true;
                OrbitStateMachineLog.Event(
                    "smoke",
                    "family_coverage_start",
                    "{\"sequence\":\"" + EscapeJson(string.Join(",", sequence)) + "\"}");
            }

            for (int attempt = 0; attempt < sequence.Length; attempt++)
            {
                int index = (_coverageIndex + attempt) % sequence.Length;
                string target = sequence[index];
                var pick = TryPickCoverageTarget(current, all, ctrlFlag, target, preferUnused);
                if (pick == null)
                {
                    if (CoverageMissingLogged.Add(target + "|" + preferUnused))
                    {
                        OrbitStateMachineLog.Event(
                            "smoke",
                            "family_coverage_missing_candidate",
                            "{\"target\":\"" + EscapeJson(target) +
                            "\",\"preferUnused\":" + (preferUnused ? "true" : "false") + "}");
                    }
                    continue;
                }

                _coverageIndex = (index + 1) % sequence.Length;
                targetFamily = target;
                return pick;
            }

            return null;
        }

        private static HScene.AnimationListInfo? TryPickCoverageTarget(
            HScene.AnimationListInfo? current,
            List<HScene.AnimationListInfo> all,
            HSceneFlagCtrl? ctrlFlag,
            string targetFamily,
            bool preferUnused)
        {
            string? excludeKey = current != null ? PoseKey(current) : null;
            var candidates = new List<HScene.AnimationListInfo>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var item in all)
            {
                if (item == null)
                    continue;
                if (!OrbitHelpers.IsPoseAllowedUnderFaintness(item, ctrlFlag))
                    continue;
                if (!string.Equals(ClassifyCoverageFamily(item), targetFamily, StringComparison.Ordinal))
                    continue;

                string key = PoseKey(item);
                if (excludeKey != null && string.Equals(key, excludeKey, StringComparison.Ordinal))
                    continue;
                if (!seen.Add(key))
                    continue;
                if (preferUnused && UsedPoseKeys.Contains(key))
                    continue;
                candidates.Add(item);
            }

            if (candidates.Count == 0)
                return null;
            return candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        private static string[] GetCoverageSequence()
        {
            string configured = HS2OrbitAndExciter.SmokeFamilyCoverageSequence?.Value ?? "";
            if (string.Equals(configured, _coverageSequenceCache, StringComparison.Ordinal))
                return _coverageSequence;

            _coverageSequenceCache = configured;
            var tokens = configured.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var families = new List<string>(tokens.Length);
            foreach (string token in tokens)
            {
                string normalized = NormalizeCoverageFamily(token);
                if (!string.IsNullOrEmpty(normalized) && !families.Contains(normalized))
                    families.Add(normalized);
            }
            _coverageSequence = families.ToArray();
            return _coverageSequence;
        }

        private static string NormalizeCoverageFamily(string raw)
        {
            string value = (raw ?? "").Trim();
            if (value.Length == 0)
                return "";
            string compact = value.Replace("-", "_").Replace(" ", "_").ToUpperInvariant();
            switch (compact)
            {
                case "A":
                case "AIBU":
                case "A_AIBU":
                    return "A_Aibu";
                case "B":
                case "HOUSHI":
                case "B_HOUSHI":
                    return "B_Houshi";
                case "C":
                case "SONYU":
                case "C_SONYU":
                    return "C_Sonyu";
                case "D":
                case "MASTURBATION":
                case "D_MASTURBATION":
                    return "D_Masturbation";
                case "E":
                case "SPNKING":
                case "SPANKING":
                case "E_SPNKING":
                    return "E_Spnking";
                case "LES":
                case "A_LES":
                    return "A_Les";
                default:
                    return value;
            }
        }

        internal static string ClassifyCoverageFamily(HScene.AnimationListInfo? info)
        {
            if (info == null)
                return "Unknown";
            int action1 = info.ActionCtrl.Item1;
            int action2 = info.ActionCtrl.Item2;
            if (action1 == 0)
                return "A_Aibu";
            if (action1 == 1)
                return "B_Houshi";
            if (action1 == 2)
                return "C_Sonyu";
            if (action1 == 3)
            {
                switch (action2)
                {
                    case 0:
                    case 1:
                    case 7:
                        return "C_Sonyu";
                    case 2:
                        return "E_Spnking";
                    case 3:
                        return "A_Aibu";
                    case 4:
                    case 5:
                        return "D_Masturbation";
                    case 6:
                        return "Peeping";
                }
            }
            if (action1 == 4)
                return "A_Les";
            if (action1 == 5)
                return "B_MultiPlay";
            if (action1 == 6)
                return "C_MultiPlay";
            return "Unknown";
        }

        private static HScene.AnimationListInfo? TryPickOnce(
            HScene.AnimationListInfo? current,
            List<HScene.AnimationListInfo> all,
            HSceneFlagCtrl? ctrlFlag,
            bool preferUnused)
        {
            KeyToInfo.Clear();
            var keys = new List<string>(all.Count);
            string? excludeKey = current != null ? PoseKey(current) : null;

            foreach (var item in all)
            {
                if (item == null)
                    continue;
                if (!OrbitHelpers.IsPoseAllowedUnderFaintness(item, ctrlFlag))
                    continue;
                string key = PoseKey(item);
                if (excludeKey != null && string.Equals(key, excludeKey, StringComparison.Ordinal))
                    continue;
                if (KeyToInfo.ContainsKey(key))
                    continue;
                KeyToInfo[key] = item;
                keys.Add(key);
            }

            if (keys.Count == 0)
                return null;

            if (!preferUnused)
            {
                // 放寬：不看本場已用，直接在候選中亂數（仍排除當前）
                string key = keys[UnityEngine.Random.Range(0, keys.Count)];
                return KeyToInfo[key];
            }

            // B1：優先未用；耗盡則 ShufflePool 清空已用再抽（仍排除當前）
            string? pickedKey = OrbitShufflePool.Pick(keys, UsedPoseKeys, excludeKey);
            if (pickedKey == null)
                return null;
            return KeyToInfo.TryGetValue(pickedKey, out var info) ? info : null;
        }

        private static void RecordCandidateStats(
            HScene.AnimationListInfo? current,
            List<HScene.AnimationListInfo> all,
            HSceneFlagCtrl? ctrlFlag)
        {
            int total = 0;
            int afterUnlock = 0;
            int afterFaintness = 0;
            string? excludeKey = current != null ? PoseKey(current) : null;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var item in all)
            {
                if (item == null)
                    continue;
                total++;
                string key = PoseKey(item);
                if (excludeKey != null && string.Equals(key, excludeKey, StringComparison.Ordinal))
                    continue;
                if (!seen.Add(key))
                    continue;
                afterUnlock++;
                if (OrbitHelpers.IsPoseAllowedUnderFaintness(item, ctrlFlag))
                    afterFaintness++;
            }

            OrbitPoseUnlockPolicy.RecordPosePoolStats(total, afterUnlock, afterFaintness);
        }

        private static void Fail(
            string trigger,
            string reason,
            HScene.AnimationListInfo? current,
            bool relaxed)
        {
            int curId = current?.id ?? -1;
            HS2OrbitAndExciter.Log?.LogWarning(
                $"Orbit: 選池失敗 [{trigger}] reason={reason} current={curId}" +
                (relaxed ? " relaxed" : "") +
                " → 留在原處");
            OrbitStateMachineLog.Event(
                "選池",
                "fail_" + reason,
                "{\"trigger\":\"" + trigger +
                "\",\"current\":" + curId +
                ",\"relaxed\":" + (relaxed ? "true" : "false") +
                ",\"used\":" + UsedPoseKeys.Count + "}");
        }

        private static string EscapeJson(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return value!
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
        }
    }
}
