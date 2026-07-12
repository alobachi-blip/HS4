using System.Collections.Generic;
using System.Reflection;
using AIChara;
using HarmonyLib;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// §11 穿牆：原版 vanish 只藏 Excel 清單內物件；H／環視時把地圖下所有 Collider 補進
    /// <see cref="CameraControl_Ver2"/> 的 <c>lstMapVanish</c>（地板可藏；角色排除）。
    /// 機制仍是撞到碰撞體 → <c>SetActive(false)</c>，不是材質半透明。
    /// </summary>
    internal static class OrbitMapVanishAssist
    {
        private static FieldInfo? _lstMapVanishField;
        private static bool _resolved;
        private static int _injectedMapInstanceId = -1;
        private static int _injectedCount;

        internal static void Reset()
        {
            _injectedMapInstanceId = -1;
            _injectedCount = 0;
        }

        /// <summary>協助開啟或每幀備援：地圖就緒後只注入一次（同地圖實例）。</summary>
        internal static void EnsureInjected(HScene? hScene)
        {
            if (hScene == null)
                return;

            var map = Traverse.Create(hScene).Field("objMap").GetValue<GameObject>();
            if (map == null)
                return;

            int mapId = map.GetInstanceID();
            if (mapId == _injectedMapInstanceId)
                return;

            var ctrl = hScene.ctrlFlag?.cameraCtrl as CameraControl_Ver2;
            if (ctrl == null)
                return;

            EnsureResolved();
            if (_lstMapVanishField == null)
                return;

            if (!(_lstMapVanishField.GetValue(ctrl) is List<CameraControl_Ver2.VisibleObject> list))
                return;

            var excludeRoots = CollectCharacterRoots(hScene);
            var colliders = map.GetComponentsInChildren<Collider>(true);
            int added = 0;
            var seenColliderNames = new HashSet<string>();

            // 保留原版 Excel 已有名稱，避免重複刷同一碰撞名
            for (int i = 0; i < list.Count; i++)
            {
                var existing = list[i];
                if (existing != null && !string.IsNullOrEmpty(existing.nameCollider))
                    seenColliderNames.Add(existing.nameCollider);
            }

            for (int i = 0; i < colliders.Length; i++)
            {
                var col = colliders[i];
                if (col == null)
                    continue;
                if (IsUnderAny(col.transform, excludeRoots))
                    continue;
                if (string.IsNullOrEmpty(col.name))
                    continue;
                // 同名碰撞：原版 VanishProc 依名字對；已有一筆即可（Excel 或我們剛加的）
                if (!seenColliderNames.Add(col.name))
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

                list.Add(vo);
                added++;
            }

            _injectedMapInstanceId = mapId;
            _injectedCount = added;

            try { ctrl.ConfigVanish = true; } catch { /* ignore */ }

            HS2OrbitAndExciter.Log?.LogInfo(
                $"Orbit: 地圖 vanish 補齊 +{added}（清單總計 {list.Count}；地板可藏；已排除角色）");
            OrbitStateMachineLog.Event("環視", "vanish補齊", $"added={added};total={list.Count}");
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

        /// <summary>藏該碰撞體底下／自身的 Mesh／Skinned 顯示物件。</summary>
        private static void CollectHideTargets(Collider col, List<GameObject> dst)
        {
            var renderers = col.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null)
                    continue;
                if (r is ParticleSystemRenderer)
                    continue;
                if (r is TrailRenderer || r is LineRenderer)
                    continue;
                if (r is MeshRenderer || r is SkinnedMeshRenderer)
                {
                    if (!dst.Contains(r.gameObject))
                        dst.Add(r.gameObject);
                }
            }

            // 碰撞在子、mesh 在父：往上找一層有 Renderer 的節點（不越過太遠）
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
