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

        internal static int UsedCount => UsedPoseKeys.Count;
        internal static PosePoolPick? LastPick => _lastPick;

        /// <summary>新 H 場景：清空本場已用。換角不要呼叫。</summary>
        internal static void OnHSceneEntered()
        {
            UsedPoseKeys.Clear();
            _lastPick = null;
        }

        internal static void ResetSession() => OnHSceneEntered();

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
                Fail(trigger, "empty_table", current, relaxed: false);
                return null;
            }

            var pick = TryPickOnce(current, all, ctrlFlag, preferUnused: true);
            bool relaxed = false;
            if (pick == null)
            {
                // D2：放寬本場去重再試（仍排除當前＋虛脫過濾）
                relaxed = true;
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
            string lineName = result.Line == PosePoolLine.Peeping ? "窺視" : "動作線";
            HS2OrbitAndExciter.Log?.LogInfo(
                $"Orbit: 選池 [{trigger}] → {label} id={info.id} 線={lineName}" +
                (relaxed ? "（放寬去重）" : "") +
                $" 已用={UsedPoseKeys.Count}");
            OrbitStateMachineLog.Event(
                "選池",
                trigger,
                "{\"id\":" + info.id +
                ",\"line\":\"" + (result.Line == PosePoolLine.Peeping ? "peeping" : "action") +
                "\",\"relaxed\":" + (relaxed ? "true" : "false") +
                ",\"used\":" + UsedPoseKeys.Count + "}");
            return result;
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

        private static void Fail(
            string trigger,
            string reason,
            HScene.AnimationListInfo? current,
            bool relaxed)
        {
            int curId = current?.id ?? -1;
            HS2OrbitAndExciter.Log?.LogWarning(
                $"Orbit: 選池失敗 [{trigger}] reason={reason} current={curId}" +
                (relaxed ? "（已放寬去重）" : "") +
                " → 留在原處");
            OrbitStateMachineLog.Event(
                "選池",
                "fail_" + reason,
                "{\"trigger\":\"" + trigger +
                "\",\"current\":" + curId +
                ",\"relaxed\":" + (relaxed ? "true" : "false") +
                ",\"used\":" + UsedPoseKeys.Count + "}");
        }
    }
}
