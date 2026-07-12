using Manager;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// 地圖／房間地板法線（朝天空）。以射線打到地圖 Collider 取 normal；失敗則世界鉛垂。
    /// </summary>
    internal static class OrbitFloorNormal
    {
        private static Vector3 _cached = Vector3.up;
        private static int _cachedMapId = int.MinValue;
        private static float _nextSampleUnscaled;
        private static readonly RaycastHit[] Hits = new RaycastHit[24];

        /// <summary>朝天空的地板法線（單位向量）。</summary>
        internal static Vector3 GetSkyward(HScene? hScene, Vector3 nearWorld)
        {
            int mapId = -1;
            try
            {
                if (Singleton<HSceneManager>.IsInstance())
                    mapId = Singleton<HSceneManager>.Instance.mapID;
            }
            catch { /* ignore */ }

            float now = Time.unscaledTime;
            bool mapChanged = mapId != _cachedMapId;
            if (!mapChanged && now < _nextSampleUnscaled)
                return _cached;

            _cachedMapId = mapId;
            _nextSampleUnscaled = now + 0.35f;

            GameObject? mapGo = null;
            if (hScene != null)
            {
                try
                {
                    mapGo = HarmonyLib.Traverse.Create(hScene).Field("objMap").GetValue<GameObject>();
                }
                catch { /* ignore */ }
            }

            if (TrySample(nearWorld, mapGo, out Vector3 n))
            {
                _cached = n;
                return _cached;
            }

            // 仍保留上一筆；完全沒有才用世界 up
            if (_cached.sqrMagnitude < 1e-6f)
                _cached = Vector3.up;
            return _cached;
        }

        private static bool TrySample(Vector3 nearWorld, GameObject? mapRoot, out Vector3 skyward)
        {
            skyward = Vector3.up;
            Vector3 best = Vector3.zero;
            float bestScore = -1f;

            // 多點向下打，找最像「地板」（法線朝上、距離合理）
            Vector3[] origins =
            {
                nearWorld + Vector3.up * 2.5f,
                nearWorld + Vector3.up * 6f,
                nearWorld + new Vector3(0.8f, 3f, 0f),
                nearWorld + new Vector3(-0.8f, 3f, 0f),
                nearWorld + new Vector3(0f, 3f, 0.8f),
                nearWorld + new Vector3(0f, 3f, -0.8f),
            };

            for (int o = 0; o < origins.Length; o++)
            {
                int count = Physics.RaycastNonAlloc(
                    origins[o], Vector3.down, Hits, 40f, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    var hit = Hits[i];
                    if (hit.collider == null)
                        continue;
                    if (mapRoot != null && !hit.collider.transform.IsChildOf(mapRoot.transform)
                        && hit.collider.transform != mapRoot.transform)
                    {
                        // 沒有 map 根時仍接受；有 map 根則優先地圖命中
                        // 非地圖碰撞略過（角色身體等）
                        continue;
                    }

                    Vector3 n = hit.normal.normalized;
                    if (Vector3.Dot(n, Vector3.up) < 0f)
                        n = -n;

                    // 越接近鉛垂、越接近採樣點下方越好
                    float upright = Vector3.Dot(n, Vector3.up);
                    if (upright < 0.25f)
                        continue;
                    float score = upright * 2f + Mathf.Clamp01(1f - hit.distance / 40f);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = n;
                    }
                }
            }

            // map 過濾太嚴時放寬：再打一次不限 map
            if (bestScore < 0f)
            {
                int count = Physics.RaycastNonAlloc(
                    nearWorld + Vector3.up * 4f, Vector3.down, Hits, 40f, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    var hit = Hits[i];
                    if (hit.collider == null)
                        continue;
                    Vector3 n = hit.normal.normalized;
                    if (Vector3.Dot(n, Vector3.up) < 0f)
                        n = -n;
                    float upright = Vector3.Dot(n, Vector3.up);
                    if (upright < 0.35f)
                        continue;
                    float score = upright;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = n;
                    }
                }
            }

            if (bestScore < 0f)
                return false;
            skyward = best.normalized;
            return true;
        }

        internal static void ResetCache()
        {
            _cached = Vector3.up;
            _cachedMapId = int.MinValue;
            _nextSampleUnscaled = 0f;
        }
    }
}
