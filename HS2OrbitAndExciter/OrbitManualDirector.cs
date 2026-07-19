using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AIChara;
using Illusion.Game;
using Manager;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>G/H/J manual actions: swap female[0], random coordinate, random multi-slot wear.</summary>
    internal static class OrbitManualDirector
    {
        private const float LongStayBonusSeconds = 60f;
        private const float DislikedCharaWeight = 0.15f;
        private const float PreferredCharaWeight = 2.5f;
        private const float CharaReloadTimeoutSeconds = 15f;
        private const float MapReloadTimeoutSeconds = 30f;
        private const int CharaPersonalityCheckBudget = 16;

        private static readonly HashSet<string> UsedCharas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> UsedCoordinates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<int> UsedMaps = new HashSet<int>();
        /// <summary>Manual Shift+G: reduced weight for rest of session.</summary>
        private static readonly HashSet<string> DislikedCharas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Stayed on stage ≥60s before swap: boosted weight in G pool for rest of session.</summary>
        private static readonly HashSet<string> PreferredCharas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static string? _activeCharaPath;
        private static float _activeCharaSinceUnscaled = -1f;

        private static List<string>? _cachedCharas;
        private static List<string>? _cachedCoords;
        private static Dictionary<string, string>? _coordNameToPath;
        private static Dictionary<string, int>? _charaPersonalityByPath;
        private static MethodInfo? _messagePackDeserializeBytes;
        private static readonly HashSet<string> UnreadableCharaPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            UsedMaps.Clear();
        }

        private static void ResetPoseCameraCycle()
        {
            _lastTrackedPoseId = -1;
            _activeBorrowedCameraName = null;
        }

        /// <summary>Called when a new H scene instance is detected: incremental scan for new UserData png only.</summary>
        internal static void OnHSceneEntered(HScene? hScene = null)
        {
            EnsureFileCacheInitialized();
            MergeNewUserDataFiles();
            ResetPoseCameraCycle();
            // §1 C1：換角不清本場已用；僅新 H 場清空
            OrbitPosePool.OnHSceneEntered();
            OrbitFsmFlow.OnHSceneEntered();
            OrbitOrgasmTattoo.OnHSceneEntered();
            // Protect against a scene transition that skips the controller's
            // normal no-HScene frame (and therefore skips the usual cleanup).
            OrbitOrgasmBustGrowth.TryRestoreForLifecycle("new_h_scene");
            PregnancyPlusAssist.TryRestoreForLifecycle("new_h_scene");
            OrbitOrgasmBustGrowth.ResetHud();
            OrbitOrgasmNippleSpray.Reset();
            HScene? scene = hScene ?? OrbitController.TryGetHScene();
            if (scene != null)
            {
                OrbitOrgasmBustGrowth.CaptureBaseline(OrbitHelpers.GetChaFemales(scene)?[0]);
                OrbitVoiceTour.OnHSceneEntered(scene);
            }
        }

        internal static bool CanAcceptHotkey(HScene? hScene)
        {
            if (hScene == null || _busy)
                return false;
            if (hScene.NowChangeAnim)
                return false;
            if (hScene.ctrlFlag?.selectAnimationListInfo != null)
                return false;
            // §6a：不擋 nowOrgasm／窺視播片
            if (Singleton<HSceneSprite>.IsInstance() && Singleton<HSceneSprite>.Instance.isFade)
                return false;
            try
            {
                if (ConfirmDialog.active)
                    return false;
            }
            catch { /* ConfirmDialog 未載入時略過 */ }
            return true;
        }

        internal static string DescribeHotkeyBlockReason(HScene? hScene)
        {
            if (hScene == null) return OrbitAssistReasons.NoHScene;
            if (_busy) return OrbitAssistReasons.ManualBusy;
            if (hScene.NowChangeAnim) return OrbitAssistReasons.Changing;
            if (hScene.ctrlFlag?.selectAnimationListInfo != null) return OrbitAssistReasons.PoseQueued;
            if (Singleton<HSceneSprite>.IsInstance() && Singleton<HSceneSprite>.Instance.isFade)
                return OrbitAssistReasons.SpriteFade;
            try
            {
                if (ConfirmDialog.active)
                    return OrbitAssistReasons.ConfirmDialog;
            }
            catch { /* ignore */ }
            if (OrbitPoseDirector.Phase == DirectorState.Rebinding) return OrbitAssistReasons.Rebinding;
            if (OrbitPoseDirector.IsPoseChangeInFlight) return OrbitAssistReasons.PoseQueued;
            return OrbitAssistReasons.None;
        }

        internal static bool TrySwapFemale0(HScene hScene, OrbitController host, bool lowerCurrentWeight = false)
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
            if (lowerCurrentWeight)
                LowerCurrentCharaWeight(current);
            int currentPersonality = cha.chaFile.parameter2.personality;
            string? next = OrbitShufflePool.Pick(
                paths,
                UsedCharas,
                current,
                GetCharaWeight,
                path => TryGetKnownCharaPersonality(path, out int p) && p != currentPersonality,
                maxIncludeChecks: CharaPersonalityCheckBudget);
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
            EnsureCoordinateNameIndexInitialized();
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

        internal static bool TrySwapScene(HScene hScene, OrbitController host)
        {
            if (!CanAcceptHotkey(hScene)
                || hScene.ctrlFlag?.nowAnimationInfo == null
                || hScene.ctrlFlag.nowOrgasm
                || OrbitPoseDirector.IsPoseChangeInFlight
                || !Singleton<HSceneManager>.IsInstance()
                || BaseMap.infoTable == null)
                return false;

            int current = Singleton<HSceneManager>.Instance.mapID;
            var candidates = BaseMap.infoTable.Values
                .Where(map => map != null && map.No >= 0 && map.No != current && (map.Draw == 0 || map.Draw == 2))
                .Select(map => map.No)
                .Distinct()
                .ToList();
            if (candidates.Count == 0)
                return false;

            var unused = candidates.Where(id => !UsedMaps.Contains(id)).ToList();
            if (unused.Count == 0)
            {
                UsedMaps.Clear();
                unused = candidates;
            }

            int next = unused[UnityEngine.Random.Range(0, unused.Count)];
            host.StartCoroutine(SwapSceneRoutine(hScene, host, current, next));
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
            // §6c／§1：L＝手動選池（各格）
            string block = DescribeHotkeyBlockReason(hScene);
            if (block == OrbitAssistReasons.PoseQueued
                && !hScene.NowChangeAnim
                && OrbitBehaviorHub.TryKickQueuedChangeAnimation(hScene))
            {
                HS2OrbitAndExciter.Log?.LogInfo("Orbit: L kick 卡住換姿");
                return true;
            }
            if (block == OrbitAssistReasons.Changing
                && OrbitBehaviorHub.TryResolveAppliedPoseChange(hScene))
            {
                HS2OrbitAndExciter.Log?.LogInfo("Orbit: L 解除 Changing 黏旗");
                OrbitPoseLandedPolicy.OnPoseLanded(hScene, PoseLandedSource.Resolve);
                block = DescribeHotkeyBlockReason(hScene);
            }
            if (block != OrbitAssistReasons.None)
            {
                HS2OrbitAndExciter.Log?.LogInfo($"Orbit: L 被擋 {block}");
                return false;
            }

            return OrbitFsmFlow.HandleL(hScene);
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

        private static void LowerCurrentCharaWeight(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            PreferredCharas.Remove(path!);
            if (DislikedCharas.Add(path!))
                HS2OrbitAndExciter.Log?.LogInfo(
                    $"Orbit: Shift+G 降權 {System.IO.Path.GetFileName(path)}（出現率×{DislikedCharaWeight:0.##}）");
            else
                HS2OrbitAndExciter.Log?.LogInfo(
                    $"Orbit: Shift+G 已降權 {System.IO.Path.GetFileName(path)}");
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

            // G reuses the same ChaControl (and usually the same PregnancyPlus
            // controller) for the next card. Restore and forget all temporary
            // body growth before LoadCharaFile overwrites the old character;
            // otherwise the old belly snapshot can be applied to the new card.
            OrbitOrgasmBustGrowth.TryRestoreForLifecycle("g_character_swap");
            PregnancyPlusAssist.TryRestoreForCharacterSwap(cha, "g_character_swap");

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
            OrbitOrgasmBustGrowth.ResetHud();
            OrbitOrgasmNippleSpray.Reset();
            OrbitOrgasmBustGrowth.CaptureBaseline(cha);

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

            // Coordinate reload rebuilds body; wait a few frames so bones/parents exist.
            yield return host.StartCoroutine(OrbitOrgasmTattoo.ReapplyAfterReloadRoutine(cha));

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

        private static IEnumerator SwapSceneRoutine(
            HScene hScene,
            OrbitController host,
            int previousMapId,
            int nextMapId)
        {
            _busy = true;
            HS2OrbitAndExciter.Log?.LogInfo($"Orbit: F 換場景 {previousMapId} -> {nextMapId}");
            OrbitStateMachineLog.Event("map", "change_start",
                "{\"from\":" + previousMapId + ",\"to\":" + nextMapId + "}");

            try
            {
                BaseMap.Change(nextMapId, FadeCanvas.Fade.None, false);
            }
            catch (System.Exception ex)
            {
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: F BaseMap.Change 失敗: " + ex.Message);
                _busy = false;
                yield break;
            }

            float deadline = Time.unscaledTime + MapReloadTimeoutSeconds;
            while ((BaseMap.isMapLoading || BaseMap.no != nextMapId) && Time.unscaledTime < deadline)
                yield return null;

            GameObject mapRoot = BaseMap.mapRoot;
            if (BaseMap.isMapLoading || BaseMap.no != nextMapId || mapRoot == null)
            {
                HS2OrbitAndExciter.Log?.LogWarning($"Orbit: F 換場景逾時 map={nextMapId}");
                _busy = false;
                yield break;
            }

            var manager = Singleton<HSceneManager>.Instance;
            var ctrlFlag = hScene.ctrlFlag;
            var hPointCtrl = HarmonyLib.Traverse.Create(hScene).Field("hPointCtrl").GetValue<HPointCtrl>();
            var hPointList = mapRoot.GetComponentInChildren<HPointList>();
            if (ctrlFlag == null || hPointCtrl == null || hPointList == null)
            {
                HS2OrbitAndExciter.Log?.LogWarning($"Orbit: F 新場景缺少 HPoint map={nextMapId}");
                _busy = false;
                yield break;
            }

            manager.mapID = nextMapId;
            if (Singleton<Game>.IsInstance())
                Singleton<Game>.Instance.mapNo = nextMapId;
            HarmonyLib.Traverse.Create(hScene).Field("objMap").SetValue(mapRoot);
            HarmonyLib.Traverse.Create(hScene).Field("Bath").SetValue(nextMapId == 4 || nextMapId == 52 || nextMapId == 53);
            HarmonyLib.Traverse.Create(hScene).Field("Room").SetValue(nextMapId == 3);

            hPointList.Init();
            HSceneManager.HResourceTables.HPointInitData(hPointList, mapRoot);
            hPointCtrl.HPointList = hPointList;
            hPointCtrl.InitHPoint();
            SingletonInitializer<BaseMap>.instance.MobObjectsVisible(Singleton<Game>.IsInstance() && Singleton<Game>.Instance.eventNo == 52);
            ctrlFlag.cameraCtrl?.loadVanishExcelData("list/map/", nextMapId, mapRoot);

            var currentAnimation = ctrlFlag.nowAnimationInfo;
            ctrlFlag.selectAnimationListInfo = null;
            ctrlFlag.nPlace = -1;
            ctrlFlag.HPointID = -1;
            ctrlFlag.nowHPoint = null;
            if (currentAnimation != null)
                yield return host.StartCoroutine(hScene.ChangeAnimation(
                    currentAnimation,
                    _isForceResetCamera: true,
                    _isForceLoopAction: true,
                    _UseFade: false));

            OrbitFloorNormal.ResetCache();
            OrbitMapVanishAssist.EnsureInjected(hScene);
            OrbitController.RequestViewReapply();
            UsedMaps.Add(nextMapId);
            OrbitStateMachineLog.Event("map", "change_end",
                "{\"from\":" + previousMapId + ",\"to\":" + nextMapId + "}");
            OrbitController.NotifyManualHotkeyCompleted(hScene);
            _busy = false;
        }

        private static readonly string[] EmptyPaths = Array.Empty<string>();

        private static IReadOnlyList<string> GetEligibleCharaPaths()
        {
            if (_cachedCharas == null || _cachedCharas.Count == 0)
                return EmptyPaths;
            return _cachedCharas;
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
            _coordNameToPath = null;
            _charaPersonalityByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            UnreadableCharaPaths.Clear();
            KnownCharaPaths.Clear();
            KnownCoordPaths.Clear();
            foreach (string p in _cachedCharas)
                KnownCharaPaths.Add(p);
            foreach (string p in _cachedCoords)
                KnownCoordPaths.Add(p);
            HS2OrbitAndExciter.Log?.LogInfo(
                $"Orbit: 初掃女角 {_cachedCharas.Count}、coordinate {_cachedCoords.Count}");
        }

        private static void EnsureCoordinateNameIndexInitialized()
        {
            if (_coordNameToPath != null)
                return;
            _coordNameToPath = OrbitHelpers.BuildCoordinateNameIndex(_cachedCoords ?? new List<string>());
        }

        private static bool TryGetKnownCharaPersonality(string path, out int personality)
        {
            personality = 0;
            if (_charaPersonalityByPath != null && _charaPersonalityByPath.TryGetValue(path, out personality))
                return true;
            if (UnreadableCharaPaths.Contains(path))
                return false;

            if (!TryReadCharaPersonality(path, out personality))
            {
                UnreadableCharaPaths.Add(path);
                return false;
            }

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
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite);
                using var reader = new BinaryReader(stream);

                long pngSize = PngFile.GetPngSize(reader);
                if (pngSize > 0)
                    stream.Seek(pngSize, SeekOrigin.Current);
                if (stream.Length - stream.Position < sizeof(int))
                    return false;

                reader.ReadInt32(); // product number
                reader.ReadString(); // card marker
                reader.ReadString(); // card version
                reader.ReadInt32(); // language
                reader.ReadString(); // user ID
                reader.ReadString(); // data ID

                int headerSize = reader.ReadInt32();
                if (headerSize <= 0 || headerSize > stream.Length - stream.Position)
                    return false;

                BlockHeader? header = DeserializeMessagePack<BlockHeader>(reader.ReadBytes(headerSize));
                if (header == null)
                    return false;
                reader.ReadInt64(); // total block-data size
                long dataStart = stream.Position;
                BlockHeader.Info info = header.SearchInfo(ChaFileParameter2.BlockName);
                if (info == null)
                {
                    // Pre-HS2 cards have no Parameter2 block; a full game load
                    // leaves ChaFileParameter2 at its default personality 0.
                    personality = 0;
                    return true;
                }
                if (info.pos < 0
                    || info.size <= 0
                    || info.size > int.MaxValue
                    || info.pos > stream.Length - dataStart)
                    return false;

                long blockStart = dataStart + info.pos;
                if (info.size > stream.Length - blockStart)
                    return false;

                stream.Seek(blockStart, SeekOrigin.Begin);
                ChaFileParameter2? parameter = DeserializeMessagePack<ChaFileParameter2>(
                    reader.ReadBytes((int)info.size));
                if (parameter == null)
                    return false;
                personality = parameter.personality;
                return true;
            }
            catch (Exception ex)
            {
                HS2OrbitAndExciter.Log?.LogWarning(
                    $"Orbit: G skip unreadable card {Path.GetFileName(path)}: {ex.Message}");
                return false;
            }
        }

        private static T? DeserializeMessagePack<T>(byte[] bytes) where T : class
        {
            if (_messagePackDeserializeBytes == null)
            {
                Type? serializer = HarmonyLib.AccessTools.TypeByName(
                    "MessagePack.MessagePackSerializer");
                _messagePackDeserializeBytes = serializer?
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(method =>
                    {
                        if (method.Name != "Deserialize" || !method.IsGenericMethodDefinition)
                            return false;
                        ParameterInfo[] parameters = method.GetParameters();
                        return parameters.Length == 1 && parameters[0].ParameterType == typeof(byte[]);
                    });
            }

            if (_messagePackDeserializeBytes == null)
                return null;
            return _messagePackDeserializeBytes
                .MakeGenericMethod(typeof(T))
                .Invoke(null, new object[] { bytes }) as T;
        }

        private static void MergeNewUserDataFiles()
        {
            if (_cachedCharas == null || _cachedCoords == null)
                return;

            int newCharas = MergeNewPaths("chara/female/", _cachedCharas, KnownCharaPaths);
            int newCoords = 0;
            foreach (string path in OrbitHelpers.ListUserDataPngFiles("coordinate/female/"))
            {
                if (!KnownCoordPaths.Add(path))
                    continue;
                _cachedCoords.Add(path);
                if (_coordNameToPath != null)
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
                added++;
            }
            return added;
        }

        internal readonly struct ManualHudStats
        {
            internal readonly int CharaPool;
            internal readonly int Disliked;
            internal readonly int Preferred;
            internal readonly float OnStageSeconds;
            internal readonly bool OnStageTracked;

            internal ManualHudStats(int charaPool, int disliked, int preferred, float onStageSeconds, bool onStageTracked)
            {
                CharaPool = charaPool;
                Disliked = disliked;
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
                pool = _cachedCharas.Count;

            float onStage = -1f;
            bool tracked = _activeCharaSinceUnscaled >= 0f && !string.IsNullOrEmpty(_activeCharaPath);
            if (tracked)
                onStage = Time.unscaledTime - _activeCharaSinceUnscaled;

            return new ManualHudStats(pool, DislikedCharas.Count, PreferredCharas.Count, onStage, tracked);
        }
    }
}
