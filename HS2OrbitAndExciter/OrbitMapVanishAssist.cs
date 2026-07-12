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
    /// 同一地圖＋同一相機只做一次；關／開協助不重做。換地圖或相機重建才再補。
    /// </summary>
    internal static class OrbitMapVanishAssist
    {
        private static FieldInfo? _lstMapVanishField;
        private static bool _resolved;
        private static int _injectedMapInstanceId = -1;
        private static int _injectedCameraInstanceId = -1;

        /// <summary>地圖就緒後對該相機只注入一次。</summary>
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

            int mapId = map.GetInstanceID();
            int camId = ctrl.GetInstanceID();
            if (mapId == _injectedMapInstanceId && camId == _injectedCameraInstanceId)
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
            _injectedCameraInstanceId = camId;

            try { ctrl.ConfigVanish = true; } catch { /* ignore */ }

            HS2OrbitAndExciter.Log?.LogInfo(
                $"Orbit: 地圖 vanish 補齊 +{added}（清單總計 {list.Count}；同地圖／相機只做一次）");
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
