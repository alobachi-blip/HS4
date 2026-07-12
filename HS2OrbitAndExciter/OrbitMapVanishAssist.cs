using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using AIChara;
using BepInEx;
using HarmonyLib;
using IllusionUtility.GetUtility;
using Manager;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// §11 穿牆：把地圖 Collider 補進 <c>lstMapVanish</c>。
    /// 每張地圖（game mapID）只完整掃描一次，結果寫入
    /// <c>BepInEx/config/HS2OrbitAndExciter/map_vanish/map_{id}.json</c>；
    /// 之後開遊戲只讀快取＋FindLoop，不再掃整棵地圖。
    /// </summary>
    internal static class OrbitMapVanishAssist
    {
        [Serializable]
        private class CacheFile
        {
            public int mapId;
            public int version = 1;
            public CacheEntry[] entries = Array.Empty<CacheEntry>();
        }

        [Serializable]
        private class CacheEntry
        {
            public string collider = "";
            public string[] objects = Array.Empty<string>();
        }

        private const int CacheVersion = 1;

        private static FieldInfo? _lstMapVanishField;
        private static bool _resolved;
        private static int _injectedMapInstanceId = -1;
        private static int _injectedCameraInstanceId = -1;

        /// <summary>地圖就緒後：有快取則只套用；無則掃描一次並落盤。</summary>
        internal static void EnsureInjected(HScene? hScene)
        {
            if (hScene == null)
                return;

            var map = Traverse.Create(hScene).Field("objMap").GetValue<GameObject>();
            if (map == null)
                return;

            var ctrl = hScene.ctrlFlag?.cameraCtrl as CameraControl_Ver2;
            if (ctrl == null)
                return;

            int mapGoId = map.GetInstanceID();
            int camId = ctrl.GetInstanceID();
            if (mapGoId == _injectedMapInstanceId && camId == _injectedCameraInstanceId)
                return;

            EnsureResolved();
            if (_lstMapVanishField == null)
                return;

            if (!(_lstMapVanishField.GetValue(ctrl) is List<CameraControl_Ver2.VisibleObject> list))
                return;

            int gameMapId = -1;
            try
            {
                if (Singleton<HSceneManager>.IsInstance())
                    gameMapId = Singleton<HSceneManager>.Instance.mapID;
            }
            catch { /* ignore */ }

            var seen = new HashSet<string>();
            for (int i = 0; i < list.Count; i++)
            {
                var existing = list[i];
                if (existing != null && !string.IsNullOrEmpty(existing.nameCollider))
                    seen.Add(existing.nameCollider);
            }

            int added;
            string source;
            CacheFile? cache = gameMapId >= 0 ? TryLoadCache(gameMapId) : null;
            if (cache != null)
            {
                added = ApplyCache(map, list, seen, cache);
                source = "快取";
            }
            else
            {
                var excludeRoots = CollectCharacterRoots(hScene);
                added = ScanApply(map, excludeRoots, seen, list, out var built);
                if (gameMapId >= 0 && built.Count > 0)
                    SaveCache(gameMapId, built);
                source = "掃描並寫入快取";
            }

            _injectedMapInstanceId = mapGoId;
            _injectedCameraInstanceId = camId;

            try { ctrl.ConfigVanish = true; } catch { /* ignore */ }

            HS2OrbitAndExciter.Log?.LogInfo(
                $"Orbit: 地圖 vanish 補齊 +{added}（mapID={gameMapId}；{source}；清單總計 {list.Count}）");
            OrbitStateMachineLog.Event("環視", "vanish補齊",
                $"{{\"mapId\":{gameMapId},\"added\":{added},\"source\":\"{source}\",\"total\":{list.Count}}}");
        }

        private static int ScanApply(
            GameObject map,
            List<Transform> excludeRoots,
            HashSet<string> seen,
            List<CameraControl_Ver2.VisibleObject> list,
            out List<CacheEntry> built)
        {
            built = new List<CacheEntry>(256);
            var colliders = map.GetComponentsInChildren<Collider>(true);
            int added = 0;

            for (int i = 0; i < colliders.Length; i++)
            {
                var col = colliders[i];
                if (col == null || string.IsNullOrEmpty(col.name))
                    continue;
                if (IsUnderAny(col.transform, excludeRoots))
                    continue;
                if (!seen.Add(col.name))
                    continue;

                var vo = new CameraControl_Ver2.VisibleObject
                {
                    nameCollider = col.name,
                    isVisible = true,
                    delay = 0f
                };
                CollectHideTargets(col, vo.listObj);
                if (vo.listObj.Count == 0)
                    vo.listObj.Add(col.gameObject);

                var names = new string[vo.listObj.Count];
                for (int j = 0; j < vo.listObj.Count; j++)
                    names[j] = vo.listObj[j] != null ? vo.listObj[j].name : col.name;

                built.Add(new CacheEntry { collider = col.name, objects = names });
                list.Add(vo);
                added++;
            }

            return added;
        }

        private static int ApplyCache(
            GameObject map,
            List<CameraControl_Ver2.VisibleObject> list,
            HashSet<string> seen,
            CacheFile cache)
        {
            int added = 0;
            Transform root = map.transform;
            if (cache.entries == null)
                return 0;

            for (int i = 0; i < cache.entries.Length; i++)
            {
                var e = cache.entries[i];
                if (e == null || string.IsNullOrEmpty(e.collider))
                    continue;
                if (!seen.Add(e.collider))
                    continue;

                var vo = new CameraControl_Ver2.VisibleObject
                {
                    nameCollider = e.collider,
                    isVisible = true,
                    delay = 0f
                };

                if (e.objects != null)
                {
                    for (int j = 0; j < e.objects.Length; j++)
                    {
                        string n = e.objects[j];
                        if (string.IsNullOrEmpty(n))
                            continue;
                        Transform t = root.FindLoop(n);
                        if (t != null && !vo.listObj.Contains(t.gameObject))
                            vo.listObj.Add(t.gameObject);
                    }
                }

                if (vo.listObj.Count == 0)
                {
                    Transform tCol = root.FindLoop(e.collider);
                    if (tCol != null)
                        vo.listObj.Add(tCol.gameObject);
                }

                if (vo.listObj.Count == 0)
                    continue;

                list.Add(vo);
                added++;
            }

            return added;
        }

        private static string CacheDir() =>
            Path.Combine(Paths.ConfigPath, "HS2OrbitAndExciter", "map_vanish");

        private static string CachePath(int mapId) =>
            Path.Combine(CacheDir(), $"map_{mapId}.json");

        private static CacheFile? TryLoadCache(int mapId)
        {
            try
            {
                string path = CachePath(mapId);
                if (!File.Exists(path))
                    return null;
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrEmpty(json))
                    return null;
                var file = JsonUtility.FromJson<CacheFile>(json);
                if (file == null || file.version != CacheVersion || file.mapId != mapId)
                    return null;
                if (file.entries == null || file.entries.Length == 0)
                    return null;
                return file;
            }
            catch (Exception ex)
            {
                HS2OrbitAndExciter.Log?.LogWarning($"Orbit: 讀 vanish 快取失敗 mapID={mapId}: {ex.Message}");
                return null;
            }
        }

        private static void SaveCache(int mapId, List<CacheEntry> built)
        {
            try
            {
                Directory.CreateDirectory(CacheDir());
                var file = new CacheFile
                {
                    mapId = mapId,
                    version = CacheVersion,
                    entries = built.ToArray()
                };
                File.WriteAllText(CachePath(mapId), JsonUtility.ToJson(file, true), Encoding.UTF8);
                HS2OrbitAndExciter.Log?.LogInfo(
                    $"Orbit: vanish 快取已寫入 {CachePath(mapId)}（{built.Count} 筆）");
            }
            catch (Exception ex)
            {
                HS2OrbitAndExciter.Log?.LogWarning($"Orbit: 寫 vanish 快取失敗: {ex.Message}");
            }
        }

        private static void EnsureResolved()
        {
            if (_resolved)
                return;
            _resolved = true;
            _lstMapVanishField = AccessTools.Field(typeof(CameraControl_Ver2), "lstMapVanish");
            if (_lstMapVanishField == null)
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: 找不到 CameraControl_Ver2.lstMapVanish，穿牆補齊略過");
        }

        private static List<Transform> CollectCharacterRoots(HScene hScene)
        {
            var roots = new List<Transform>(8);
            AddChaRoots(OrbitHelpers.GetChaFemales(hScene), roots);
            AddChaRoots(OrbitHelpers.GetChaMales(hScene), roots);
            return roots;
        }

        private static void AddChaRoots(ChaControl[]? chas, List<Transform> roots)
        {
            if (chas == null)
                return;
            for (int i = 0; i < chas.Length; i++)
            {
                var cha = chas[i];
                if (cha == null)
                    continue;
                if (cha.transform != null)
                    roots.Add(cha.transform);
                if (cha.objBodyBone != null)
                    roots.Add(cha.objBodyBone.transform);
            }
        }

        private static bool IsUnderAny(Transform t, List<Transform> roots)
        {
            for (int i = 0; i < roots.Count; i++)
            {
                var r = roots[i];
                if (r == null)
                    continue;
                if (t == r || t.IsChildOf(r))
                    return true;
            }
            return false;
        }

        private static void CollectHideTargets(Collider col, List<GameObject> dst)
        {
            var renderers = col.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null)
                    continue;
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
                    continue;
                if (r is MeshRenderer || r is SkinnedMeshRenderer)
                {
                    if (!dst.Contains(r.gameObject))
                        dst.Add(r.gameObject);
                }
            }

            if (dst.Count == 0)
            {
                Transform p = col.transform.parent;
                for (int up = 0; up < 4 && p != null; up++, p = p.parent)
                {
                    var r = p.GetComponent<Renderer>();
                    if (r is MeshRenderer || r is SkinnedMeshRenderer)
                    {
                        dst.Add(p.gameObject);
                        break;
                    }
                }
            }
        }
    }
}
