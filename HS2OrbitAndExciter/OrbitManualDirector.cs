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
        private const float ShortLivedDepartSeconds = 30f;
        private const float ShortLivedWeight = 0.15f;
        private const float FileListCacheSeconds = 30f;
        private const float CharaReloadTimeoutSeconds = 15f;

        private static readonly HashSet<string> UsedCharas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> UsedCoordinates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, float> DepartedAtUnscaled = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        private static List<string>? _cachedCharas;
        private static List<string>? _cachedCoords;
        private static float _cacheBuiltAt = -999f;
        private static bool _busy;

        internal static bool IsBusy => _busy;
        internal static bool IsCameraPaused => _busy;

        internal static void Reset()
        {
            _busy = false;
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

            var paths = GetUserDataFemaleCharaPaths();
            string? current = OrbitHelpers.GetUserDataFemaleCharaPath(cha);
            string? next = OrbitShufflePool.Pick(paths, UsedCharas, current, GetCharaWeight);
            if (next == null)
            {
                HS2OrbitAndExciter.Log?.LogInfo("Orbit: G 無可換女角");
                return false;
            }

            host.StartCoroutine(SwapFemale0Routine(hScene, host, cha, current, next));
            return true;
        }

        internal static bool TrySwapCoordinate(HScene hScene)
        {
            if (!CanAcceptHotkey(hScene))
                return false;

            var cha = OrbitHelpers.GetChaFemales(hScene)?[0];
            if (cha == null)
                return false;

            var paths = GetUserDataFemaleCoordinatePaths();
            string? current = OrbitHelpers.GetCurrentCoordinatePath(cha, paths);
            string? next = OrbitShufflePool.Pick(paths, UsedCoordinates, current);
            if (next == null)
            {
                HS2OrbitAndExciter.Log?.LogInfo("Orbit: H 無可換衣");
                return false;
            }

            cha.ChangeNowCoordinate(next, reload: true);
            cha.SetClothesStateAll(0);
            HS2OrbitAndExciter.Log?.LogInfo($"Orbit: H 換衣 {System.IO.Path.GetFileName(next)}");
            OrbitController.RequestViewReapply();
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
            return true;
        }

        private static float GetCharaWeight(string path)
        {
            if (DepartedAtUnscaled.TryGetValue(path, out float departedAt)
                && Time.unscaledTime - departedAt < ShortLivedDepartSeconds)
                return ShortLivedWeight;
            return 1f;
        }

        private static void MarkDeparted(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            DepartedAtUnscaled[path!] = Time.unscaledTime;
        }

        private static IEnumerator SwapFemale0Routine(
            HScene hScene,
            OrbitController host,
            ChaControl cha,
            string? oldPath,
            string newPath)
        {
            _busy = true;
            MarkDeparted(oldPath);

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

            HS2OrbitAndExciter.Log?.LogInfo($"Orbit: G 換角 {System.IO.Path.GetFileName(newPath)}");

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

            _busy = false;
        }

        private static readonly string[] EmptyPaths = Array.Empty<string>();

        private static IReadOnlyList<string> GetUserDataFemaleCharaPaths()
        {
            RefreshFileCacheIfNeeded();
            return _cachedCharas != null ? _cachedCharas : EmptyPaths;
        }

        private static IReadOnlyList<string> GetUserDataFemaleCoordinatePaths()
        {
            RefreshFileCacheIfNeeded();
            return _cachedCoords != null ? _cachedCoords : EmptyPaths;
        }

        private static void RefreshFileCacheIfNeeded()
        {
            if (_cachedCharas != null && Time.unscaledTime - _cacheBuiltAt < FileListCacheSeconds)
                return;

            _cachedCharas = OrbitHelpers.ListUserDataPngFiles("chara/female/");
            _cachedCoords = OrbitHelpers.ListUserDataPngFiles("coordinate/female/");
            _cacheBuiltAt = Time.unscaledTime;
        }
    }
}
