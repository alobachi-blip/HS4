using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using IllusionUtility.GetUtility;
using Manager;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// §11 穿牆：把地圖 Collider 補進 <c>lstMapVanish</c>。
    /// 每張地圖（game mapID）只完整掃描一次，結果寫入手寫 JSON 快取
    /// <c>BepInEx/config/HS2OrbitAndExciter/map_vanish/map_{id}.json</c>；
    /// 之後開遊戲只讀快取＋依名尋找，不再掃整棵地圖。
    /// （不改原版 Excel／abdata 場景檔；快取即「跑過一次」的持久結果。）
    /// </summary>
    internal static class OrbitMapVanishAssist
    {
        private sealed class CacheFile
        {
            public int mapId;
            public int version = CacheVersion;
            public List<CacheEntry> entries = new List<CacheEntry>();
        }

        private sealed class CacheEntry
        {
            public string collider = "";
            public List<string> objects = new List<string>();
        }

        /// <summary>
        /// v4：道具根改為「逐層往上爬、每層都重新統計牽連 Renderer 數」，
        /// 超過 <see cref="MaxHideRendererCount"/> 就停在上一層，避免一碰到牆／門
        /// 就把整棟建築或整張地圖背景一起 vanish（v3 的 bug：一路爬到大容器節點）。
        /// </summary>
        private const int CacheVersion = 4;
        private const int MaxDirectPhysicalOccluders = 8;
        private const int MaxDirectRendererOccluders = 8;
        private const float DirectInsideProbeRadius = 0.02f;
        private const float DirectRayPadding = 0.05f;
        private const float MinimumViewportSpan = 0.015f;
        private const int DirectRaycastBufferSize = 128;
        private const int DirectOverlapBufferSize = 64;

        private struct DirectRendererHit
        {
            internal Renderer Renderer;
            internal float Distance;
        }

        private sealed class RaycastDistanceComparer : IComparer<RaycastHit>
        {
            internal static readonly RaycastDistanceComparer Instance = new RaycastDistanceComparer();
            public int Compare(RaycastHit x, RaycastHit y) => x.distance.CompareTo(y.distance);
        }

        private sealed class DirectRendererDistanceComparer : IComparer<DirectRendererHit>
        {
            internal static readonly DirectRendererDistanceComparer Instance = new DirectRendererDistanceComparer();
            public int Compare(DirectRendererHit x, DirectRendererHit y) => x.Distance.CompareTo(y.Distance);
        }

        private static FieldInfo? _lstMapVanishField;
        private static bool _resolved;
        private static int _injectedMapInstanceId = -1;
        private static int _injectedCameraInstanceId = -1;
        private static GameObject? _injectedMapRoot;
        private static readonly HashSet<CameraControl_Ver2.VisibleObject> _injectedEntries =
            new HashSet<CameraControl_Ver2.VisibleObject>();
        private static readonly Dictionary<Renderer, HashSet<CameraControl_Ver2.VisibleObject>> _hiddenByRenderer =
            new Dictionary<Renderer, HashSet<CameraControl_Ver2.VisibleObject>>();
        private static readonly Dictionary<Renderer, bool> _originalRendererEnabled =
            new Dictionary<Renderer, bool>();
        private static readonly HashSet<Renderer> _directOccludersThisFrame =
            new HashSet<Renderer>();
        private static readonly Dictionary<Renderer, bool> _directOriginalRendererEnabled =
            new Dictionary<Renderer, bool>();
        private static readonly HashSet<CameraControl_Ver2.VisibleObject> _directEntries =
            new HashSet<CameraControl_Ver2.VisibleObject>();
        private static readonly HashSet<string> _directColliderNames =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<GameObject, bool> _directInactiveObjects =
            new Dictionary<GameObject, bool>();
        private static int _directMapRendererRootId = -1;
        private static Renderer[] _directMapRenderers = Array.Empty<Renderer>();
        private static readonly RaycastHit[] DirectRaycastHits = new RaycastHit[DirectRaycastBufferSize];
        private static readonly Collider[] DirectOverlapColliders = new Collider[DirectOverlapBufferSize];
        private static readonly HashSet<int> DirectSeenColliderIds = new HashSet<int>();
        private static readonly List<DirectRendererHit> DirectRendererCandidates =
            new List<DirectRendererHit>(32);
        private static readonly List<Renderer> DirectHierarchyRenderers = new List<Renderer>(64);
        private static readonly List<Renderer> DirectUsableRenderers = new List<Renderer>(64);
        private static readonly List<Renderer> VisibilityRenderers = new List<Renderer>(64);
        private static readonly List<Renderer?> EmptyRenderers = new List<Renderer?>();

        internal static bool IsDirectOccluderHidden(Collider col) =>
            col != null && _directColliderNames.Contains(col.name);

        internal static int HiddenRendererCount => _hiddenByRenderer.Count;

        /// <summary>Hide the actual renderer hit between camera and character. This is
        /// independent of vanilla's camera-trigger based vanish list.</summary>
        internal static void BeginDirectOcclusionFrame()
        {
            foreach (var entry in _directEntries)
                SetInjectedEntryVisibility(entry, true);
            _directEntries.Clear();
            _directColliderNames.Clear();
            foreach (var pair in _directInactiveObjects)
                if (pair.Key != null) pair.Key.SetActive(pair.Value);
            _directInactiveObjects.Clear();
            foreach (var renderer in _directOccludersThisFrame)
            {
                if (renderer != null && _directOriginalRendererEnabled.TryGetValue(renderer, out bool wasEnabled))
                    renderer.enabled = wasEnabled;
            }
            _directOccludersThisFrame.Clear();
        }

        internal static void HideDirectOccluder(Collider col)
        {
            if (col == null || IsNonOccludingVisualName(col.name)) return;
            foreach (var entry in _injectedEntries)
            {
                if (entry != null && string.Equals(entry.nameCollider, col.name, StringComparison.Ordinal))
                {
                    SetInjectedEntryVisibility(entry, false);
                    _directEntries.Add(entry);
                    if (entry.listObj != null)
                    {
                        foreach (var go in entry.listObj)
                        {
                            if (go == null || go.name.EndsWith("_col", StringComparison.OrdinalIgnoreCase) ||
                                go.name.EndsWith("_meta", StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (!_directInactiveObjects.ContainsKey(go))
                                _directInactiveObjects[go] = go.activeSelf;
                            go.SetActive(false);
                        }
                    }
                    _directColliderNames.Add(col.name);
                    return;
                }
            }
            if (_injectedMapRoot != null)
            {
                string visualName = col.name;
                if (visualName.EndsWith("_col", StringComparison.OrdinalIgnoreCase))
                    visualName = visualName.Substring(0, visualName.Length - 4);
                else if (visualName.EndsWith("_meta", StringComparison.OrdinalIgnoreCase))
                    visualName = visualName.Substring(0, visualName.Length - 5);
                var visual = FindNamedTransform(_injectedMapRoot, visualName);
                if (visual != null)
                {
                    DirectHierarchyRenderers.Clear();
                    visual.GetComponentsInChildren(true, DirectHierarchyRenderers);
                    for (int i = 0; i < DirectHierarchyRenderers.Count; i++)
                    {
                        var r = DirectHierarchyRenderers[i];
                        if (r == null || r.GetComponentInParent<AIChara.ChaControl>() != null)
                            continue;
                        HideDirectRenderer(r);
                    }
                    if (DirectHierarchyRenderers.Count > 0)
                        _directColliderNames.Add(col.name);
                    return;
                }
            }
            Transform node = col.transform;
            for (int depth = 0; depth < 8 && node != null; depth++, node = node.parent)
            {
                DirectHierarchyRenderers.Clear();
                DirectUsableRenderers.Clear();
                node.GetComponentsInChildren(true, DirectHierarchyRenderers);
                for (int i = 0; i < DirectHierarchyRenderers.Count; i++)
                {
                    var r = DirectHierarchyRenderers[i];
                    if (r != null && !(r is ParticleSystemRenderer) &&
                        !(r is TrailRenderer) && !(r is LineRenderer) &&
                        r.GetComponentInParent<AIChara.ChaControl>() == null)
                    {
                        DirectUsableRenderers.Add(r);
                        if (DirectUsableRenderers.Count > 64)
                            break;
                    }
                }
                if (DirectUsableRenderers.Count == 0 || DirectUsableRenderers.Count > 64)
                    continue;
                for (int i = 0; i < DirectUsableRenderers.Count; i++)
                    HideDirectRenderer(DirectUsableRenderers[i]);
                _directColliderNames.Add(col.name);
                return;
            }
        }

        internal static void ApplyDirectLineOfSight(Vector3 origin, Vector3 target, Camera? camera = null)
        {
            BeginDirectOcclusionFrame();
            OrbitOcclusionSurvey.CaptureBeforeAssist(origin, target);
            Vector3 delta = target - origin;
            float distance = delta.magnitude;
            if (distance <= DirectRayPadding) return;

            HideContainingGeometry(origin);

            var direction = delta / distance;
            RaycastHit[] hits = DirectRaycastHits;
            int hitCount = Physics.RaycastNonAlloc(
                origin,
                direction,
                hits,
                distance - DirectRayPadding,
                ~0,
                QueryTriggerInteraction.Collide);
            if (hitCount >= hits.Length)
            {
                hits = Physics.RaycastAll(
                    origin,
                    direction,
                    distance - DirectRayPadding,
                    ~0,
                    QueryTriggerInteraction.Collide);
                hitCount = hits.Length;
            }
            Array.Sort(hits, 0, hitCount, RaycastDistanceComparer.Instance);
            DirectSeenColliderIds.Clear();
            int physicalHidden = 0;
            for (int i = 0; i < hitCount; i++)
            {
                var col = hits[i].collider;
                if (col == null || col.GetComponentInParent<AIChara.ChaControl>() != null)
                    continue;
                if (!DirectSeenColliderIds.Add(col.GetInstanceID()))
                    continue;
                HideDirectOccluder(col);
                physicalHidden++;
                if (physicalHidden >= MaxDirectPhysicalOccluders)
                    break;
            }

            HideDirectRendererOccluders(origin, direction, distance, camera ?? Camera.main);
        }

        private static void HideContainingGeometry(Vector3 origin)
        {
            Collider[] colliders = DirectOverlapColliders;
            int colliderCount = Physics.OverlapSphereNonAlloc(
                origin,
                DirectInsideProbeRadius,
                colliders,
                ~0,
                QueryTriggerInteraction.Collide);
            if (colliderCount >= colliders.Length)
            {
                colliders = Physics.OverlapSphere(
                    origin,
                    DirectInsideProbeRadius,
                    ~0,
                    QueryTriggerInteraction.Collide);
                colliderCount = colliders.Length;
            }
            int hidden = 0;
            for (int i = 0; i < colliderCount && hidden < MaxDirectPhysicalOccluders; i++)
            {
                var collider = colliders[i];
                if (collider == null || IsNonOccludingVisualName(collider.name) ||
                    collider.GetComponentInParent<AIChara.ChaControl>() != null)
                {
                    continue;
                }
                HideDirectOccluder(collider);
                hidden++;
            }
        }

        private static void HideDirectRendererOccluders(
            Vector3 origin,
            Vector3 direction,
            float distance,
            Camera? camera)
        {
            EnsureDirectMapRendererCache();
            if (_directMapRenderers.Length == 0)
                return;

            var ray = new Ray(origin, direction);
            DirectRendererCandidates.Clear();
            for (int i = 0; i < _directMapRenderers.Length; i++)
            {
                var renderer = _directMapRenderers[i];
                if (!IsDirectRendererEligible(renderer))
                    continue;
                Bounds bounds = renderer.bounds;
                if (!bounds.IntersectRay(ray, out float hitDistance) ||
                    hitDistance <= DirectRayPadding ||
                    hitDistance >= distance - DirectRayPadding ||
                    !HasMeaningfulCoverage(bounds, ray, camera))
                {
                    continue;
                }
                DirectRendererCandidates.Add(new DirectRendererHit
                {
                    Renderer = renderer,
                    Distance = hitDistance
                });
            }

            DirectRendererCandidates.Sort(DirectRendererDistanceComparer.Instance);
            int count = Mathf.Min(DirectRendererCandidates.Count, MaxDirectRendererOccluders);
            for (int i = 0; i < count; i++)
                HideDirectRenderer(DirectRendererCandidates[i].Renderer);
        }

        private static void EnsureDirectMapRendererCache()
        {
            int rootId = _injectedMapRoot != null ? _injectedMapRoot.GetInstanceID() : -1;
            if (rootId == _directMapRendererRootId)
                return;
            _directMapRendererRootId = rootId;
            _directMapRenderers = _injectedMapRoot != null
                ? _injectedMapRoot.GetComponentsInChildren<Renderer>(true)
                : Array.Empty<Renderer>();
        }

        private static bool IsDirectRendererEligible(Renderer? renderer)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                renderer is ParticleSystemRenderer || renderer is TrailRenderer ||
                renderer is LineRenderer || IsNonOccludingVisualName(renderer.name))
            {
                return false;
            }
            return renderer.GetComponentInParent<AIChara.ChaControl>() == null;
        }

        private static bool IsNonOccludingVisualName(string? name) =>
            !string.IsNullOrEmpty(name) &&
            name!.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) >= 0;

        private static void HideDirectRenderer(Renderer renderer)
        {
            if (!_directOriginalRendererEnabled.ContainsKey(renderer))
                _directOriginalRendererEnabled[renderer] = renderer.enabled;
            _directOccludersThisFrame.Add(renderer);
            renderer.enabled = false;
        }

        private static bool HasMeaningfulCoverage(Bounds bounds, Ray ray, Camera? camera)
        {
            if (camera == null)
                return true;
            Vector3 toCenter = bounds.center - ray.origin;
            float depth = Mathf.Max(Vector3.Dot(toCenter, ray.direction), 0.05f);
            Vector3 right = Vector3.Cross(ray.direction, Vector3.up);
            if (right.sqrMagnitude < 1e-5f)
                right = Vector3.Cross(ray.direction, Vector3.forward);
            right.Normalize();
            Vector3 up = Vector3.Cross(right, ray.direction).normalized;
            Vector3 extents = bounds.extents;
            float halfWidth = Mathf.Abs(right.x) * extents.x
                + Mathf.Abs(right.y) * extents.y
                + Mathf.Abs(right.z) * extents.z;
            float halfHeight = Mathf.Abs(up.x) * extents.x
                + Mathf.Abs(up.y) * extents.y
                + Mathf.Abs(up.z) * extents.z;
            float verticalTan = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            if (verticalTan <= 1e-5f)
                return true;
            float heightSpan = halfHeight / (depth * verticalTan);
            float widthSpan = halfWidth / (depth * verticalTan * Mathf.Max(camera.aspect, 0.1f));
            return Mathf.Min(widthSpan, heightSpan) >= MinimumViewportSpan;
        }

        /// <summary>地圖就緒後：有快取則只套用；無則掃描一次並落盤（含 0 筆）。</summary>
        internal static void EnsureInjected(HScene? hScene)
        {
            if (hScene == null)
                return;

            var map = OrbitHelpers.GetMapObject(hScene);
            if (map == null)
                return;

            var ctrl = hScene.ctrlFlag?.cameraCtrl as CameraControl_Ver2;
            if (ctrl == null)
                return;

            int mapGoId = map.GetInstanceID();
            int camId = ctrl.GetInstanceID();
            _injectedMapRoot = map;

            EnsureResolved();
            if (_lstMapVanishField == null)
                return;

            if (!(_lstMapVanishField.GetValue(ctrl) is List<CameraControl_Ver2.VisibleObject> list))
                return;

            bool sameCamera = mapGoId == _injectedMapInstanceId && camId == _injectedCameraInstanceId;
            if (sameCamera && IsInjectionStillPresent(list))
                return;
            ResetInjectedState();

            int gameMapId = -1;
            try
            {
                if (Singleton<HSceneManager>.IsInstance())
                    gameMapId = Singleton<HSceneManager>.Instance.mapID;
            }
            catch { /* ignore */ }

            var seen = new HashSet<string>();
            var voByName = new Dictionary<string, CameraControl_Ver2.VisibleObject>();
            for (int i = 0; i < list.Count; i++)
            {
                var existing = list[i];
                if (existing == null || string.IsNullOrEmpty(existing.nameCollider))
                    continue;
                // Existing vanilla map entries must also use the orbit-safe
                // renderer-only path when direct line-of-sight finds them.
                RegisterInjectedEntry(existing);
                seen.Add(existing.nameCollider);
                if (!voByName.ContainsKey(existing.nameCollider))
                    voByName[existing.nameCollider] = existing;
            }

            var excludeRoots = CollectCharacterRoots(hScene);
            int added;
            string source;
            CacheFile? cache = gameMapId >= 0 ? TryLoadCache(gameMapId) : null;
            if (cache != null)
            {
                added = ApplyCache(map, list, seen, voByName, cache);
                // 快取可能偏薄：實況再掃一次合併（人物／衣服除外）
                int live = ScanApply(map, excludeRoots, seen, voByName, list, out _);
                added += live;
                if (gameMapId >= 0)
                    SaveCache(gameMapId, SnapshotEntries(list));
                source = live > 0 ? "快取+實況補齊" : "快取";
            }
            else
            {
                added = ScanApply(map, excludeRoots, seen, voByName, list, out var built);
                if (gameMapId >= 0)
                    SaveCache(gameMapId, built.Count > 0 ? built : SnapshotEntries(list));
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
            Dictionary<string, CameraControl_Ver2.VisibleObject> voByName,
            List<CameraControl_Ver2.VisibleObject> list,
            out List<CacheEntry> built)
        {
            built = new List<CacheEntry>(256);
            var builtByName = new Dictionary<string, CacheEntry>();

            var colliders = CollectMapColliders(map, excludeRoots, out int rawCount, out int excludedCha);
            int skippedDupMerge = 0;
            int added = 0;

            for (int i = 0; i < colliders.Count; i++)
            {
                var col = colliders[i];
                if (col == null || string.IsNullOrEmpty(col.name))
                    continue;

                var hide = new List<GameObject>(16);
                CollectHideTargets(col, map, excludeRoots, hide);
                if (hide.Count == 0)
                    hide.Add(col.gameObject);

                if (voByName.TryGetValue(col.name, out var existingVo))
                {
                    bool merged = false;
                    for (int h = 0; h < hide.Count; h++)
                    {
                        var go = hide[h];
                        if (go == null || existingVo.listObj.Contains(go))
                            continue;
                        existingVo.listObj.Add(go);
                        merged = true;
                    }

                    if (builtByName.TryGetValue(col.name, out var be))
                    {
                        for (int h = 0; h < hide.Count; h++)
                        {
                            string n = hide[h] != null ? hide[h].name : col.name;
                            if (!be.objects.Contains(n))
                                be.objects.Add(n);
                        }
                    }

                    if (merged)
                        skippedDupMerge++;
                    continue;
                }

                if (!seen.Add(col.name))
                    continue;

                var vo = new CameraControl_Ver2.VisibleObject
                {
                    nameCollider = col.name,
                    isVisible = true,
                    delay = 0f
                };
                for (int h = 0; h < hide.Count; h++)
                {
                    if (hide[h] != null && !vo.listObj.Contains(hide[h]))
                        vo.listObj.Add(hide[h]);
                }

                var entry = new CacheEntry { collider = col.name };
                for (int j = 0; j < vo.listObj.Count; j++)
                    entry.objects.Add(vo.listObj[j] != null ? vo.listObj[j].name : col.name);

                built.Add(entry);
                builtByName[col.name] = entry;
                voByName[col.name] = vo;
                RegisterInjectedEntry(vo);
                list.Add(vo);
                added++;
            }

            HS2OrbitAndExciter.Log?.LogInfo(
                $"Orbit: vanish 掃描 map colliders={rawCount} 排除角色={excludedCha} 新建={added} 同名合併={skippedDupMerge}");

            return added;
        }

        /// <summary>
        /// 掃 objMap 全部 Collider，並一律補同 scene 內非人物／非衣服 Collider
        /// （自訂圖常把傢俱掛在 mapRoot 外）。
        /// </summary>
        private static List<Collider> CollectMapColliders(
            GameObject map,
            List<Transform> excludeRoots,
            out int rawCount,
            out int excludedCha)
        {
            var result = new List<Collider>(512);
            var uniq = new HashSet<int>();
            excludedCha = 0;

            var underMap = map.GetComponentsInChildren<Collider>(true);
            rawCount = underMap != null ? underMap.Length : 0;
            AppendColliders(underMap, excludeRoots, result, uniq, ref excludedCha);

            Collider[]? all = null;
            try
            {
                all = UnityEngine.Resources.FindObjectsOfTypeAll<Collider>();
            }
            catch { /* ignore */ }

            if (all != null)
            {
                var scene = map.scene;
                int before = result.Count;
                for (int i = 0; i < all.Length; i++)
                {
                    var col = all[i];
                    if (col == null)
                        continue;
                    try
                    {
                        if (!col.gameObject.scene.IsValid() || !col.gameObject.scene.isLoaded)
                            continue;
                        if (scene.IsValid() && col.gameObject.scene != scene)
                            continue;
                    }
                    catch
                    {
                        continue;
                    }

                    if (IsCharacterOrClothes(col.transform, excludeRoots))
                    {
                        excludedCha++;
                        continue;
                    }

                    int id = col.GetInstanceID();
                    if (!uniq.Add(id))
                        continue;
                    result.Add(col);
                }

                if (result.Count > before)
                {
                    HS2OrbitAndExciter.Log?.LogInfo(
                        $"Orbit: vanish 擴掃 scene colliders +{result.Count - before}（objMap 已有 {before}）");
                }
            }

            return result;
        }

        private static void AppendColliders(
            Collider[]? colliders,
            List<Transform> excludeRoots,
            List<Collider> dst,
            HashSet<int> uniq,
            ref int excludedCha)
        {
            if (colliders == null)
                return;
            for (int i = 0; i < colliders.Length; i++)
            {
                var col = colliders[i];
                if (col == null)
                    continue;
                if (IsCharacterOrClothes(col.transform, excludeRoots))
                {
                    excludedCha++;
                    continue;
                }

                int id = col.GetInstanceID();
                if (!uniq.Add(id))
                    continue;
                dst.Add(col);
            }
        }

        private static List<CacheEntry> SnapshotEntries(List<CameraControl_Ver2.VisibleObject> list)
        {
            var built = new List<CacheEntry>(list.Count);
            var seen = new HashSet<string>();
            for (int i = 0; i < list.Count; i++)
            {
                var vo = list[i];
                if (vo == null || string.IsNullOrEmpty(vo.nameCollider))
                    continue;
                if (!seen.Add(vo.nameCollider))
                    continue;
                var entry = new CacheEntry { collider = vo.nameCollider };
                if (vo.listObj != null)
                {
                    for (int j = 0; j < vo.listObj.Count; j++)
                    {
                        var go = vo.listObj[j];
                        string n = go != null ? go.name : vo.nameCollider;
                        if (!string.IsNullOrEmpty(n) && !entry.objects.Contains(n))
                            entry.objects.Add(n);
                    }
                }

                if (entry.objects.Count == 0)
                    entry.objects.Add(vo.nameCollider);
                built.Add(entry);
            }

            return built;
        }

        private static int ApplyCache(
            GameObject map,
            List<CameraControl_Ver2.VisibleObject> list,
            HashSet<string> seen,
            Dictionary<string, CameraControl_Ver2.VisibleObject> voByName,
            CacheFile cache)
        {
            int added = 0;
            if (cache.entries == null)
                return 0;

            for (int i = 0; i < cache.entries.Count; i++)
            {
                var e = cache.entries[i];
                if (e == null || string.IsNullOrEmpty(e.collider))
                    continue;

                if (voByName.TryGetValue(e.collider, out var existing))
                {
                    RegisterInjectedEntry(existing);
                    MergeNamedObjects(map, existing.listObj, e.objects);
                    continue;
                }

                if (!seen.Add(e.collider))
                    continue;

                var vo = new CameraControl_Ver2.VisibleObject
                {
                    nameCollider = e.collider,
                    isVisible = true,
                    delay = 0f
                };
                MergeNamedObjects(map, vo.listObj, e.objects);

                if (vo.listObj.Count == 0)
                {
                    var tCol = FindNamedTransform(map, e.collider);
                    if (tCol != null)
                        vo.listObj.Add(tCol.gameObject);
                }

                if (vo.listObj.Count == 0)
                    continue;

                list.Add(vo);
                RegisterInjectedEntry(vo);
                voByName[e.collider] = vo;
                added++;
            }

            return added;
        }

        private static void MergeNamedObjects(GameObject map, List<GameObject> dst, List<string>? names)
        {
            if (names == null)
                return;
            for (int j = 0; j < names.Count; j++)
            {
                string n = names[j];
                if (string.IsNullOrEmpty(n))
                    continue;
                var t = FindNamedTransform(map, n);
                if (t != null && !dst.Contains(t.gameObject))
                    dst.Add(t.gameObject);
            }
        }

        /// <summary>優先 map.FindLoop；找不到再掃已載入 scene（自訂圖擴掃用）。</summary>
        private static Transform? FindNamedTransform(GameObject map, string name)
        {
            if (map != null)
            {
                Transform? underMap = map.transform.FindLoop(name);
                if (underMap != null)
                    return underMap;
            }

            try
            {
                var all = UnityEngine.Resources.FindObjectsOfTypeAll<Transform>();
                if (all == null)
                    return null;
                var scene = map != null ? map.scene : default;
                for (int i = 0; i < all.Length; i++)
                {
                    Transform? t = all[i];
                    if (t == null || t.name != name)
                        continue;
                    try
                    {
                        if (!t.gameObject.scene.IsValid() || !t.gameObject.scene.isLoaded)
                            continue;
                        if (scene.IsValid() && t.gameObject.scene != scene)
                            continue;
                    }
                    catch
                    {
                        continue;
                    }

                    return t;
                }
            }
            catch { /* ignore */ }

            return null;
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

                var file = ParseCacheJson(json);
                if (file == null || file.version != CacheVersion || file.mapId != mapId)
                    return null;
                // 允許 entries 為空：代表「已掃過、無需新建」，之後不再掃
                if (file.entries == null)
                    file.entries = new List<CacheEntry>();
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
                    entries = built ?? new List<CacheEntry>()
                };
                string path = CachePath(mapId);
                File.WriteAllText(path, FormatCacheJson(file), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                HS2OrbitAndExciter.Log?.LogInfo(
                    $"Orbit: vanish 快取已寫入 {path}（{file.entries.Count} 筆）");
            }
            catch (Exception ex)
            {
                HS2OrbitAndExciter.Log?.LogWarning($"Orbit: 寫 vanish 快取失敗: {ex.Message}");
            }
        }

        private static string FormatCacheJson(CacheFile file)
        {
            var sb = new StringBuilder(256 + file.entries.Count * 64);
            sb.Append("{\n");
            sb.Append("  \"mapId\": ").Append(file.mapId).Append(",\n");
            sb.Append("  \"version\": ").Append(file.version).Append(",\n");
            sb.Append("  \"entries\": [\n");
            for (int i = 0; i < file.entries.Count; i++)
            {
                var e = file.entries[i];
                sb.Append("    {\"collider\": ").Append(JsonStr(e.collider)).Append(", \"objects\": [");
                for (int j = 0; j < e.objects.Count; j++)
                {
                    if (j > 0) sb.Append(", ");
                    sb.Append(JsonStr(e.objects[j]));
                }

                sb.Append("]}");
                if (i + 1 < file.entries.Count)
                    sb.Append(',');
                sb.Append('\n');
            }

            sb.Append("  ]\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        private static string JsonStr(string? s)
        {
            if (s == null) s = "";
            var sb = new StringBuilder(s.Length + 8);
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }

            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>極簡 JSON 解析（僅本快取格式）。</summary>
        private static CacheFile? ParseCacheJson(string json)
        {
            // 去掉 BOM
            if (json.Length > 0 && json[0] == '\uFEFF')
                json = json.Substring(1);

            var file = new CacheFile();
            if (!TryReadIntField(json, "mapId", out file.mapId))
                return null;
            if (!TryReadIntField(json, "version", out file.version))
                return null;

            int entriesKey = json.IndexOf("\"entries\"", StringComparison.Ordinal);
            if (entriesKey < 0)
                return file;

            int arrStart = json.IndexOf('[', entriesKey);
            if (arrStart < 0)
                return file;
            int arrEnd = FindMatchingBracket(json, arrStart, '[', ']');
            if (arrEnd < 0)
                return file;

            string arr = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
            int pos = 0;
            while (pos < arr.Length)
            {
                int objStart = arr.IndexOf('{', pos);
                if (objStart < 0)
                    break;
                int objEnd = FindMatchingBracket(arr, objStart, '{', '}');
                if (objEnd < 0)
                    break;
                string obj = arr.Substring(objStart, objEnd - objStart + 1);
                pos = objEnd + 1;

                var entry = new CacheEntry();
                if (!TryReadStringField(obj, "collider", out entry.collider))
                    continue;
                entry.objects = ReadStringArrayField(obj, "objects");
                file.entries.Add(entry);
            }

            return file;
        }

        private static bool TryReadIntField(string json, string name, out int value)
        {
            value = 0;
            string key = "\"" + name + "\"";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return false;
            i = json.IndexOf(':', i + key.Length);
            if (i < 0) return false;
            i++;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            int start = i;
            if (i < json.Length && (json[i] == '-' || json[i] == '+')) i++;
            while (i < json.Length && char.IsDigit(json[i])) i++;
            return int.TryParse(json.Substring(start, i - start), out value);
        }

        private static bool TryReadStringField(string json, string name, out string value)
        {
            value = "";
            string key = "\"" + name + "\"";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return false;
            i = json.IndexOf(':', i + key.Length);
            if (i < 0) return false;
            i++;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length || json[i] != '"') return false;
            return TryParseJsonString(json, i, out value, out _);
        }

        private static List<string> ReadStringArrayField(string json, string name)
        {
            var list = new List<string>();
            string key = "\"" + name + "\"";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return list;
            i = json.IndexOf(':', i + key.Length);
            if (i < 0) return list;
            i++;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length || json[i] != '[') return list;
            int end = FindMatchingBracket(json, i, '[', ']');
            if (end < 0) return list;
            string arr = json.Substring(i + 1, end - i - 1);
            int p = 0;
            while (p < arr.Length)
            {
                while (p < arr.Length && (char.IsWhiteSpace(arr[p]) || arr[p] == ',')) p++;
                if (p >= arr.Length) break;
                if (arr[p] != '"') break;
                if (!TryParseJsonString(arr, p, out string s, out int next))
                    break;
                list.Add(s);
                p = next;
            }

            return list;
        }

        private static bool TryParseJsonString(string s, int startQuote, out string value, out int after)
        {
            value = "";
            after = startQuote;
            if (startQuote >= s.Length || s[startQuote] != '"')
                return false;
            var sb = new StringBuilder();
            int i = startQuote + 1;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"')
                {
                    value = sb.ToString();
                    after = i;
                    return true;
                }

                if (c == '\\' && i < s.Length)
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case '\\': sb.Append('\\'); break;
                        case '"': sb.Append('"'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 <= s.Length &&
                                int.TryParse(s.Substring(i, 4), System.Globalization.NumberStyles.HexNumber, null, out int code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }

            return false;
        }

        private static int FindMatchingBracket(string s, int openIdx, char open, char close)
        {
            int depth = 0;
            bool inStr = false;
            for (int i = openIdx; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr)
                {
                    if (c == '\\' && i + 1 < s.Length) { i++; continue; }
                    if (c == '"') inStr = false;
                    continue;
                }

                if (c == '"') { inStr = true; continue; }
                if (c == open) depth++;
                else if (c == close)
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }

            return -1;
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

        private static bool IsInjectionStillPresent(List<CameraControl_Ver2.VisibleObject> list)
        {
            if (_injectedEntries.Count == 0)
                return true;

            foreach (var entry in _injectedEntries)
            {
                if (entry == null || !list.Contains(entry))
                    return false;
            }

            return true;
        }

        private static void RegisterInjectedEntry(CameraControl_Ver2.VisibleObject entry)
        {
            if (entry != null)
                _injectedEntries.Add(entry);
        }

        /// <summary>
        /// Orbit-only vanish state. Keep map colliders alive while hiding only their
        /// renderers; otherwise SetActive(false) causes the camera trigger to exit
        /// and the vanilla camera immediately shows the object again.
        /// </summary>
        internal static bool IsInjectedEntry(CameraControl_Ver2.VisibleObject? entry) =>
            entry != null && _injectedEntries.Contains(entry);

        internal static void SetInjectedEntryVisibility(CameraControl_Ver2.VisibleObject entry, bool visible)
        {
            if (entry == null)
                return;

            if (visible)
            {
                foreach (var pair in _hiddenByRenderer)
                    pair.Value.Remove(entry);
            }

            if (!visible)
            {
                foreach (var go in entry.listObj)
                {
                    if (go == null)
                        continue;

                    VisibilityRenderers.Clear();
                    go.GetComponentsInChildren(true, VisibilityRenderers);
                    for (int i = 0; i < VisibilityRenderers.Count; i++)
                    {
                        var renderer = VisibilityRenderers[i];
                        if (renderer == null)
                            continue;

                        if (!_originalRendererEnabled.ContainsKey(renderer))
                            _originalRendererEnabled[renderer] = renderer.enabled;
                        if (!_hiddenByRenderer.TryGetValue(renderer, out var owners))
                        {
                            owners = new HashSet<CameraControl_Ver2.VisibleObject>();
                            _hiddenByRenderer[renderer] = owners;
                        }

                        owners.Add(entry);
                    }
                }
            }

            EmptyRenderers.Clear();
            foreach (var pair in _hiddenByRenderer)
            {
                var renderer = pair.Key;
                pair.Value.RemoveWhere(owner => owner == null || !_injectedEntries.Contains(owner));
                if (pair.Value.Count > 0)
                {
                    if (renderer != null)
                        renderer.enabled = false;
                    continue;
                }

                if (renderer != null && _originalRendererEnabled.TryGetValue(renderer, out bool wasEnabled))
                    renderer.enabled = wasEnabled;
                EmptyRenderers.Add(renderer);
            }

            for (int i = 0; i < EmptyRenderers.Count; i++)
            {
                var renderer = EmptyRenderers[i];
                if (renderer == null)
                    continue;
                _hiddenByRenderer.Remove(renderer);
                _originalRendererEnabled.Remove(renderer);
            }

            entry.delay = visible ? 0.3f : 0f;
            entry.isVisible = visible;
        }

        internal static void ResetInjectedState()
        {
            foreach (var pair in _originalRendererEnabled)
            {
                if (pair.Key != null)
                    pair.Key.enabled = pair.Value;
            }

            _hiddenByRenderer.Clear();
            _originalRendererEnabled.Clear();
            foreach (var pair in _directOriginalRendererEnabled)
            {
                if (pair.Key != null)
                    pair.Key.enabled = pair.Value;
            }
            _directOccludersThisFrame.Clear();
            _directOriginalRendererEnabled.Clear();
            _directEntries.Clear();
            _directColliderNames.Clear();
            foreach (var pair in _directInactiveObjects)
                if (pair.Key != null) pair.Key.SetActive(pair.Value);
            _directInactiveObjects.Clear();
            _injectedEntries.Clear();
            _injectedMapInstanceId = -1;
            _injectedCameraInstanceId = -1;
            _injectedMapRoot = null;
            _directMapRendererRootId = -1;
            _directMapRenderers = Array.Empty<Renderer>();
            DirectSeenColliderIds.Clear();
            DirectRendererCandidates.Clear();
            DirectHierarchyRenderers.Clear();
            DirectUsableRenderers.Clear();
            VisibilityRenderers.Clear();
            EmptyRenderers.Clear();
            Array.Clear(DirectRaycastHits, 0, DirectRaycastHits.Length);
            Array.Clear(DirectOverlapColliders, 0, DirectOverlapColliders.Length);
        }

        private static List<Transform> CollectCharacterRoots(HScene hScene)
        {
            var roots = new List<Transform>(8);
            AddChaRoots(OrbitHelpers.GetChaFemales(hScene), roots);
            AddChaRoots(OrbitHelpers.GetChaMales(hScene), roots);
            return roots;
        }

        private static void AddChaRoots(AIChara.ChaControl[]? chas, List<Transform> roots)
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

        private static bool IsCharacterOrClothes(Transform t, List<Transform> excludeRoots)
        {
            if (t == null)
                return true;
            if (IsUnderAny(t, excludeRoots))
                return true;
            try
            {
                if (t.GetComponentInParent<AIChara.ChaControl>() != null)
                    return true;
            }
            catch { /* ChaControl 未就緒 */ }

            // 散落衣物／角色相關命名（不在 Cha 根下時）
            string n = t.name;
            if (string.IsNullOrEmpty(n))
                return false;
            if (n.StartsWith("ct_", StringComparison.OrdinalIgnoreCase))
                return true;
            if (n.IndexOf("clothes", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("Cloth", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        /// <summary>單一 vanish 條目最多牽連的 Renderer 數。牆／門／沙發等單一道具通常在此範圍內；
        /// 一旦超過，代表往上爬已經爬進了整棟建築或整張地圖背景的容器節點——寧可少藏，也不要炸背景。</summary>
        private const int MaxHideRendererCount = 40;

        /// <summary>
        /// 藏「同道具」下的 Mesh／SkinnedMesh（人物／衣服除外）。
        /// 從 collider 自己逐層往上爬（最多 8 層，不跨出 mapRoot）；每爬一層都重新統計
        /// 這一層會牽連的 Renderer 數，一旦超過 <see cref="MaxHideRendererCount"/> 就停在上一層。
        /// 這樣可避免（v3 的 bug）把整棟建築／整張地圖背景當成同一個「道具根」，
        /// 導致視線膠囊體一碰到牆／門就把大片背景一起 SetActive(false)。
        /// </summary>
        private static void CollectHideTargets(
            Collider col,
            GameObject map,
            List<Transform> excludeRoots,
            List<GameObject> dst)
        {
            if (col == null)
                return;

            Transform? mapRoot = map != null ? map.transform : null;
            Transform best = col.transform;
            List<GameObject> bestHide = CollectFilteredRenderObjects(best, excludeRoots);

            Transform cur = col.transform;
            for (int hop = 0; hop < 8 && cur.parent != null && cur.parent != mapRoot; hop++)
            {
                Transform candidate = cur.parent;
                var candidateHide = CollectFilteredRenderObjects(candidate, excludeRoots);
                if (candidateHide.Count > MaxHideRendererCount)
                    break; // 再往上爬牽連過多物件（很可能已是建築／背景容器），停在上一層

                best = candidate;
                bestHide = candidateHide;
                cur = candidate;

                // 兄弟很多的容器節點通常已經不是單一道具，別再往上爬
                if (candidate.childCount > 24)
                    break;
            }

            for (int i = 0; i < bestHide.Count; i++)
            {
                if (!dst.Contains(bestHide[i]))
                    dst.Add(bestHide[i]);
            }

            if (dst.Count == 0)
            {
                Transform p = col.transform.parent;
                for (int up = 0; up < 4 && p != null; up++, p = p.parent)
                {
                    if (IsCharacterOrClothes(p, excludeRoots))
                        continue;
                    var r = p.GetComponent<Renderer>();
                    if (r is MeshRenderer || r is SkinnedMeshRenderer)
                    {
                        dst.Add(p.gameObject);
                        break;
                    }
                }
            }
        }

        private static List<GameObject> CollectFilteredRenderObjects(Transform? root, List<Transform> excludeRoots)
        {
            var result = new List<GameObject>(16);
            if (root == null)
                return result;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null)
                    continue;
                if (IsCharacterOrClothes(r.transform, excludeRoots))
                    continue;
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
                    continue;
                if (r is MeshRenderer || r is SkinnedMeshRenderer)
                {
                    if (!result.Contains(r.gameObject))
                        result.Add(r.gameObject);
                }
            }

            return result;
        }
    }
}
