using System;
using System.Collections;
using System.Collections.Generic;
using AIChara;
using Manager;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>G/H/J manual actions: swap female[0], random coordinate, random multi-slot wear.</summary>
    internal static class OrbitManualDirector
    {
        private const float ShortLivedOnStageSeconds = 30f;
        private const float LongStayBonusSeconds = 60f;
        private const float DislikedCharaWeight = 0.15f;
        private const float PreferredCharaWeight = 2.5f;
        private const float CharaReloadTimeoutSeconds = 15f;

        private static readonly HashSet<string> UsedCharas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> UsedCoordinates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Quick-swap strike 1: reduced weight for rest of session.</summary>
        private static readonly HashSet<string> DislikedCharas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Stayed on stage ≥60s before swap: boosted weight in G pool for rest of session.</summary>
        private static readonly HashSet<string> PreferredCharas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Quick-swap strike 2+: excluded from G pool for rest of session.</summary>
        private static readonly HashSet<string> ExcludedCharas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> QuickSwapStrikes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static string? _activeCharaPath;
        private static float _activeCharaSinceUnscaled = -1f;

        private static List<string>? _cachedCharas;
        private static List<string>? _cachedCoords;
        private static Dictionary<string, string>? _coordNameToPath;
        private static Dictionary<string, int>? _charaPersonalityByPath;
        private static readonly HashSet<string> KnownCharaPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> KnownCoordPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _busy;
        private static int _lastTrackedPoseId = -1;
        private static string? _activeBorrowedCameraName;

        internal static bool IsBusy => _busy;
        internal static bool IsCameraPaused => _busy;

        internal static void Reset()
        {
            _busy = false;
            ResetPoseCameraCycle();
        }

        private static void ResetPoseCameraCycle()
        {
            _lastTrackedPoseId = -1;
            _activeBorrowedCameraName = null;
        }

        /// <summary>Called when a new H scene instance is detected: incremental scan for new UserData png only.</summary>
        internal static void OnHSceneEntered()
        {
            EnsureFileCacheInitialized();
            MergeNewUserDataFiles();
            ResetPoseCameraCycle();
            OrbitOrgasmTattoo.OnHSceneEntered();
        }

        internal static bool CanAcceptHotkey(HScene? hScene)
        {
            if (hScene == null || _busy)
                return false;
            if (hScene.NowChangeAnim)
                return false;
            if (hScene.ctrlFlag?.selectAnimationListInfo != null)
                return false;
            if (Singleton<HSceneSprite>.IsInstance() && Singleton<HSceneSprite>.Instance.isFade)
                return false;
            return true;
        }

        internal static bool TrySwapFemale0(HScene hScene, OrbitController host)
        {
            if (!CanAcceptHotkey(hScene))
                return false;

            var cha = OrbitHelpers.GetChaFemales(hScene)?[0];
            if (cha == null)
                return false;

            EnsureFileCacheInitialized();
            var paths = GetEligibleCharaPaths();
            string? current = OrbitHelpers.GetUserDataFemaleCharaPath(cha);
            EnsureActiveCharaTracked(current);
            RewardIfLongStay(current);
            int currentPersonality = cha.chaFile.parameter2.personality;
            string? next = OrbitShufflePool.Pick(
                paths,
                UsedCharas,
                current,
                GetCharaWeight,
                path => TryGetKnownCharaPersonality(path, out int p) && p != currentPersonality);
            if (next == null)
            {
                HS2OrbitAndExciter.Log?.LogInfo("Orbit: G 無不同性格女角");
                return false;
            }

            host.StartCoroutine(SwapFemale0Routine(hScene, host, cha, current, next, currentPersonality));
            return true;
        }

        internal static bool TrySwapCoordinate(HScene hScene, OrbitController host)
        {
            if (!CanAcceptHotkey(hScene))
                return false;

            var cha = OrbitHelpers.GetChaFemales(hScene)?[0];
            if (cha == null)
                return false;

            EnsureFileCacheInitialized();
            var paths = GetUserDataFemaleCoordinatePaths();
            string? current = OrbitHelpers.GetCurrentCoordinatePath(cha, _coordNameToPath ?? new Dictionary<string, string>());
            string? next = OrbitShufflePool.Pick(paths, UsedCoordinates, current);
            if (next == null)
            {
                HS2OrbitAndExciter.Log?.LogInfo("Orbit: H 無可換衣");
                return false;
            }

            host.StartCoroutine(SwapCoordinateRoutine(hScene, host, cha, next));
            return true;
        }

        internal static bool TryRandomWear(HScene hScene)
        {
            if (!CanAcceptHotkey(hScene))
                return false;

            var chaFemales = OrbitHelpers.GetChaFemales(hScene);
            var chaMales = OrbitHelpers.GetChaMales(hScene);
            if (!OrbitHelpers.ApplyRandomWearSlots(chaFemales, chaMales))
                return false;

            HS2OrbitAndExciter.Log?.LogInfo("Orbit: J 亂數穿著");
            OrbitController.NotifyManualHotkeyCompleted(hScene);
            return true;
        }

        internal static bool TryChangePose(HScene hScene)
        {
            if (!CanAcceptHotkey(hScene))
                return false;

            if (!OrbitPoseDirector.RequestHotkeyPoseChange(hScene))
            {
                HS2OrbitAndExciter.Log?.LogInfo("Orbit: L 無可換姿");
                return false;
            }

            var next = hScene.ctrlFlag?.selectAnimationListInfo;
            string label = next == null || string.IsNullOrEmpty(next.nameAnimation)
                ? "?"
                : next.nameAnimation;
            HS2OrbitAndExciter.Log?.LogInfo($"Orbit: L 換姿 {label}");
            return true;
        }

        internal static bool TryCyclePoseCamera(HScene hScene, OrbitController host)
        {
            if (!CanAcceptCameraHotkey(hScene))
                return false;

            SyncPoseCameraAnchor(hScene);

            var all = OrbitHelpers.GetAllPoseList();
            var cameras = OrbitHelpers.GetDistinctPoseCameraList(all);
            if (cameras.Count == 0)
            {
                HS2OrbitAndExciter.Log?.LogInfo("Orbit: K 無可用姿勢鏡頭");
                return false;
            }

            string? anchor = _activeBorrowedCameraName ?? hScene.ctrlFlag?.nowAnimationInfo?.nameCamera;
            var next = OrbitHelpers.PickNextPoseCamera(cameras, anchor);
            if (next == null)
                return false;

            host.ApplyBorrowedPoseCamera(hScene, next);
            _activeBorrowedCameraName = next.nameCamera;

            string label = string.IsNullOrEmpty(next.nameAnimation) ? next.nameCamera : next.nameAnimation;
            HS2OrbitAndExciter.Log?.LogInfo($"Orbit: K 鏡頭 {label}");
            return true;
        }

        private static bool CanAcceptCameraHotkey(HScene? hScene)
        {
            if (hScene == null || _busy)
                return false;
            if (hScene.NowChangeAnim)
                return false;
            if (Singleton<HSceneSprite>.IsInstance() && Singleton<HSceneSprite>.Instance.isFade)
                return false;
            return true;
        }

        private static void SyncPoseCameraAnchor(HScene hScene)
        {
            var now = hScene.ctrlFlag?.nowAnimationInfo;
            if (now == null)
                return;
            if (now.id != _lastTrackedPoseId)
            {
                _lastTrackedPoseId = now.id;
                _activeBorrowedCameraName = null;
            }
        }

        private static float GetCharaWeight(string path)
        {
            if (DislikedCharas.Contains(path))
                return DislikedCharaWeight;
            if (PreferredCharas.Contains(path))
                return PreferredCharaWeight;
            return 1f;
        }

        private static void EnsureActiveCharaTracked(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            if (!string.Equals(path, _activeCharaPath, StringComparison.OrdinalIgnoreCase) || _activeCharaSinceUnscaled < 0f)
            {
                _activeCharaPath = path;
                _activeCharaSinceUnscaled = Time.unscaledTime;
            }
        }

        private static void PenalizeIfQuickSwapAway(string? oldPath)
        {
            if (string.IsNullOrEmpty(oldPath))
                return;
            if (!string.Equals(oldPath, _activeCharaPath, StringComparison.OrdinalIgnoreCase))
                return;
            if (_activeCharaSinceUnscaled < 0f)
                return;
            if (Time.unscaledTime - _activeCharaSinceUnscaled >= ShortLivedOnStageSeconds)
                return;

            int strikes = QuickSwapStrikes.TryGetValue(oldPath!, out int n) ? n + 1 : 1;
            QuickSwapStrikes[oldPath!] = strikes;
            if (strikes >= 2)
            {
                ExcludedCharas.Add(oldPath!);
                DislikedCharas.Remove(oldPath!);
                PreferredCharas.Remove(oldPath!);
                HS2OrbitAndExciter.Log?.LogInfo($"Orbit: G 排除 {System.IO.Path.GetFileName(oldPath)}（快換×{strikes}）");
            }
            else
            {
                DislikedCharas.Add(oldPath!);
            }
        }

        private static void RewardIfLongStay(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            if (!string.Equals(path, _activeCharaPath, StringComparison.OrdinalIgnoreCase))
                return;
            if (_activeCharaSinceUnscaled < 0f)
                return;
            if (Time.unscaledTime - _activeCharaSinceUnscaled < LongStayBonusSeconds)
                return;
            if (!PreferredCharas.Add(path!))
                return;

            DislikedCharas.Remove(path!);
            HS2OrbitAndExciter.Log?.LogInfo($"Orbit: G 加權 {System.IO.Path.GetFileName(path)}（停留≥{LongStayBonusSeconds:F0}s）");
        }

        private static void SetActiveCharaAfterSwap(string newPath)
        {
            _activeCharaPath = newPath;
            _activeCharaSinceUnscaled = Time.unscaledTime;
        }

        private static IEnumerator SwapFemale0Routine(
            HScene hScene,
            OrbitController host,
            ChaControl cha,
            string? oldPath,
            string newPath,
            int oldPersonality)
        {
            _busy = true;
            PenalizeIfQuickSwapAway(oldPath);

            if (!cha.chaFile.LoadCharaFile(newPath, 1))
            {
                HS2OrbitAndExciter.Log?.LogWarning($"Orbit: G LoadCharaFile 失敗 {newPath}");
                _busy = false;
                yield break;
            }

            cha.ChangeNowCoordinate();
            cha.Reload();

            float deadline = Time.unscaledTime + CharaReloadTimeoutSeconds;
            while (!cha.loadEnd && Time.unscaledTime < deadline)
                yield return null;

            if (!cha.loadEnd)
            {
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: G Reload 逾時");
                _busy = false;
                yield break;
            }

            cha.SetClothesStateAll(0);

            if (Singleton<HSceneManager>.IsInstance())
            {
                var hm = Singleton<HSceneManager>.Instance;
                hm.Personality[0] = cha.chaFile.parameter2.personality;
                hm.pngFemales[0] = newPath;
            }

            yield return OrbitHelpers.ReinitFemale0VoiceAndFeel(hScene, cha);

            HS2OrbitAndExciter.Log?.LogInfo(
                $"Orbit: G 換角 {System.IO.Path.GetFileName(newPath)}（性格 {oldPersonality}→{cha.chaFile.parameter2.personality}）");
            SetActiveCharaAfterSwap(newPath);

            OrbitOrgasmTattoo.ClearStamps();

            if (OrbitBehaviorHub.IsOrbitAssistActive())
            {
                var ctrl = hScene.ctrlFlag?.cameraCtrl as CameraControl_Ver2;
                if (ctrl != null)
                    host.InternalRebindAfterPoseChange(hScene, ctrl);
            }
            else
            {
                OrbitController.RequestViewReapply();
            }

            OrbitController.NotifyManualHotkeyCompleted(hScene);
            _busy = false;
        }

        private static IEnumerator SwapCoordinateRoutine(
            HScene hScene,
            OrbitController host,
            ChaControl cha,
            string nextPath)
        {
            _busy = true;

            if (!cha.ChangeNowCoordinate(nextPath, reload: true))
            {
                HS2OrbitAndExciter.Log?.LogWarning($"Orbit: H ChangeNowCoordinate 失敗 {nextPath}");
                _busy = false;
                yield break;
            }

            float deadline = Time.unscaledTime + CharaReloadTimeoutSeconds;
            while (!cha.loadEnd && Time.unscaledTime < deadline)
                yield return null;

            if (!cha.loadEnd)
            {
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: H Reload 逾時");
                _busy = false;
                yield break;
            }

            cha.SetClothesStateAll(0);
            HS2OrbitAndExciter.Log?.LogInfo($"Orbit: H 換衣 {System.IO.Path.GetFileName(nextPath)}");

            if (OrbitBehaviorHub.IsOrbitAssistActive())
            {
                var ctrl = hScene.ctrlFlag?.cameraCtrl as CameraControl_Ver2;
                if (ctrl != null)
                    host.InternalRebindAfterPoseChange(hScene, ctrl);
            }
            else
            {
                OrbitController.RequestViewReapply();
            }

            OrbitController.NotifyManualHotkeyCompleted(hScene);
            _busy = false;
        }

        private static readonly string[] EmptyPaths = Array.Empty<string>();

        private static IReadOnlyList<string> GetEligibleCharaPaths()
        {
            if (_cachedCharas == null || _cachedCharas.Count == 0)
                return EmptyPaths;
            if (ExcludedCharas.Count == 0)
                return _cachedCharas;
            var list = new List<string>(_cachedCharas.Count);
            foreach (string path in _cachedCharas)
            {
                if (!ExcludedCharas.Contains(path))
                    list.Add(path);
            }
            return list;
        }

        private static IReadOnlyList<string> GetUserDataFemaleCoordinatePaths()
        {
            return _cachedCoords != null ? _cachedCoords : EmptyPaths;
        }

        private static void EnsureFileCacheInitialized()
        {
            if (_cachedCharas != null)
                return;
            _cachedCharas = OrbitHelpers.ListUserDataPngFiles("chara/female/");
            _cachedCoords = OrbitHelpers.ListUserDataPngFiles("coordinate/female/");
            _coordNameToPath = OrbitHelpers.BuildCoordinateNameIndex(_cachedCoords);
            _charaPersonalityByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            KnownCharaPaths.Clear();
            KnownCoordPaths.Clear();
            foreach (string p in _cachedCharas)
            {
                KnownCharaPaths.Add(p);
                IndexCharaPersonality(p);
            }
            foreach (string p in _cachedCoords)
                KnownCoordPaths.Add(p);
            HS2OrbitAndExciter.Log?.LogInfo(
                $"Orbit: 初掃女角 {_cachedCharas.Count}、coordinate {_cachedCoords.Count}");
        }

        private static bool TryGetKnownCharaPersonality(string path, out int personality)
        {
            personality = 0;
            if (_charaPersonalityByPath != null && _charaPersonalityByPath.TryGetValue(path, out personality))
                return true;

            if (!TryReadCharaPersonality(path, out personality))
                return false;

            _charaPersonalityByPath ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _charaPersonalityByPath[path] = personality;
            return true;
        }

        private static int GetCharaPersonality(string path)
        {
            return TryGetKnownCharaPersonality(path, out int personality) ? personality : -1;
        }

        private static void IndexCharaPersonality(string path)
        {
            if (_charaPersonalityByPath == null)
                return;
            if (TryReadCharaPersonality(path, out int personality))
                _charaPersonalityByPath[path] = personality;
        }

        private static bool TryReadCharaPersonality(string path, out int personality)
        {
            personality = 0;
            if (string.IsNullOrEmpty(path))
                return false;

            var chaFile = new ChaFileControl();
            if (!chaFile.LoadCharaFile(path, 1))
                return false;

            personality = chaFile.parameter2.personality;
            return true;
        }

        private static void MergeNewUserDataFiles()
        {
            if (_cachedCharas == null || _cachedCoords == null || _coordNameToPath == null)
                return;

            int newCharas = MergeNewPaths("chara/female/", _cachedCharas, KnownCharaPaths);
            int newCoords = 0;
            foreach (string path in OrbitHelpers.ListUserDataPngFiles("coordinate/female/"))
            {
                if (!KnownCoordPaths.Add(path))
                    continue;
                _cachedCoords.Add(path);
                OrbitHelpers.TryIndexCoordinatePath(path, _coordNameToPath);
                newCoords++;
            }

            if (newCharas > 0 || newCoords > 0)
            {
                HS2OrbitAndExciter.Log?.LogInfo(
                    $"Orbit: H 場景新增 女角 {newCharas}、coordinate {newCoords}（現共 {_cachedCharas.Count}/{_cachedCoords.Count}）");
            }
        }

        private static int MergeNewPaths(string relativeDir, List<string> cache, HashSet<string> known)
        {
            int added = 0;
            foreach (string path in OrbitHelpers.ListUserDataPngFiles(relativeDir))
            {
                if (!known.Add(path))
                    continue;
                cache.Add(path);
                if (relativeDir.StartsWith("chara/female", StringComparison.OrdinalIgnoreCase))
                    IndexCharaPersonality(path);
                added++;
            }
            return added;
        }

        internal readonly struct ManualHudStats
        {
            internal readonly int CharaPool;
            internal readonly int Disliked;
            internal readonly int Excluded;
            internal readonly int Preferred;
            internal readonly float OnStageSeconds;
            internal readonly bool OnStageTracked;

            internal ManualHudStats(int charaPool, int disliked, int excluded, int preferred, float onStageSeconds, bool onStageTracked)
            {
                CharaPool = charaPool;
                Disliked = disliked;
                Excluded = excluded;
                Preferred = preferred;
                OnStageSeconds = onStageSeconds;
                OnStageTracked = onStageTracked;
            }
        }

        /// <summary>Compact G-pool stats for HUD (no scan; uses existing cache).</summary>
        internal static ManualHudStats GetHudStats()
        {
            int pool = 0;
            if (_cachedCharas != null)
            {
                foreach (string path in _cachedCharas)
                {
                    if (!ExcludedCharas.Contains(path))
                        pool++;
                }
            }

            float onStage = -1f;
            bool tracked = _activeCharaSinceUnscaled >= 0f && !string.IsNullOrEmpty(_activeCharaPath);
            if (tracked)
                onStage = Time.unscaledTime - _activeCharaSinceUnscaled;

            return new ManualHudStats(pool, DislikedCharas.Count, ExcludedCharas.Count, PreferredCharas.Count, onStage, tracked);
        }
    }
}
