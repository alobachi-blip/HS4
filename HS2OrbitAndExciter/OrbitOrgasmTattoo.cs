using System.Collections;
using System.Collections.Generic;
using AIChara;
using IllusionUtility.GetUtility;
using Manager;
using UnityEngine;
using UnityEngine.Rendering;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// Orgasm tattoos from maker <c>st_paint</c> (NOT accessory slots — will not appear in 飾品清單):
    /// visible quad decals on body bones (thigh → face) + body-paint slots.
    /// Survives H coordinate swap via <see cref="ReapplyAfterReloadRoutine"/>.
    /// </summary>
    internal static class OrbitOrgasmTattoo
    {
        private const float BaseSize = 0.22f;
        private const float SurfaceOffset = 0.035f;
        private const int ReapplySettleFrames = 6;

        private struct Stamp
        {
            public int PaintId;
            public string ParentKey;
            public Color Color;
            public float Size;
            public int LayoutId;
            public Vector4 Layout;
            public float Rotation;
        }

        /// <summary>Bone-first attach order (stable across clothes). Thigh → up → face last.</summary>
        private static readonly string[] BodyParents =
        {
            "cf_J_LegUp01_L", "cf_J_LegUp01_R", "cf_J_Knee_L", "cf_J_Knee_R",
            "cf_J_Kokan", "cf_J_Kosi01", "cf_J_Kosi02",
            "cf_J_Spine01", "cf_J_Spine02", "cf_J_Spine03",
            "cf_J_Mune00", "cf_J_Mune01_L", "cf_J_Mune01_R",
            "cf_J_ArmUp00_L", "cf_J_ArmUp00_R", "cf_J_Shoulder_L", "cf_J_Shoulder_R",
            "cf_J_Hand_L", "cf_J_Hand_R",
            "cf_J_Neck", "cf_J_Head",
            // Accessory parents as extras if bones missing on some cards.
            "N_Leg_L", "N_Leg_R", "N_Waist", "N_Chest_f", "N_Back", "N_Face",
        };

        private static readonly List<Stamp> Stamps = new List<Stamp>(32);
        private static readonly List<GameObject> Decals = new List<GameObject>(32);
        private static readonly List<Material> DecalMats = new List<Material>(32);
        private static int[]? _paintIds;
        private static int[]? _layoutIds;
        private static bool _catalogTried;
        private static int _parentCursor;
        private static int _lastHSceneId = -1;
        private static Shader? _decalShader;
        private static string _lastSiteLabel = "";
        private static int _reapplyGen;

        internal static int Count => Stamps.Count;
        internal static string LastSiteLabel => _lastSiteLabel;

        internal static string HudStatus
        {
            get
            {
                if (!Enabled)
                    return "刺關";
                if (Count <= 0)
                    return "刺0";
                if (string.IsNullOrEmpty(_lastSiteLabel))
                    return $"刺{Count}";
                return $"刺{Count}·{_lastSiteLabel}";
            }
        }

        internal static bool Enabled
        {
            get => HS2OrbitAndExciter.OrgasmTattooEnabled?.Value ?? false;
            set
            {
                if (HS2OrbitAndExciter.OrgasmTattooEnabled != null)
                    HS2OrbitAndExciter.OrgasmTattooEnabled.Value = value;
            }
        }

        internal static bool Toggle()
        {
            Enabled = !Enabled;
            if (Enabled)
            {
                HS2OrbitAndExciter.Log?.LogInfo($"Orbit: T 高潮刺青 ON（上限 {GetMaxStamps()}；非飾品欄）");
                TryAddOne(forceLog: true);
            }
            else
            {
                HS2OrbitAndExciter.Log?.LogInfo("Orbit: T 高潮刺青 OFF");
            }
            return Enabled;
        }

        /// <summary>Drop all tattoos (new H scene / G chara swap).</summary>
        internal static void ClearStamps()
        {
            _reapplyGen++;
            DestroyVisualsOnly();
            Stamps.Clear();
            _parentCursor = 0;
            _lastSiteLabel = "";
        }

        internal static void OnHSceneEntered()
        {
            ClearStamps();
            _lastHSceneId = -1;
            _catalogTried = false;
            _paintIds = null;
            _layoutIds = null;
            _decalShader = null;
        }

        /// <summary>After H coordinate reload: wait for bones, then rebuild from stamps.</summary>
        internal static IEnumerator ReapplyAfterReloadRoutine(ChaControl cha)
        {
            int gen = ++_reapplyGen;
            for (int i = 0; i < ReapplySettleFrames; i++)
                yield return null;

            if (gen != _reapplyGen || cha == null || !cha.loadEnd || Stamps.Count == 0)
                yield break;

            ReapplyAfterReload(cha);

            // Second pass: some parents appear a frame later after clothes settle.
            yield return null;
            yield return null;
            if (gen != _reapplyGen || cha == null || !cha.loadEnd || Stamps.Count == 0)
                yield break;
            if (Decals.Count < Stamps.Count)
                ReapplyAfterReload(cha);
        }

        /// <summary>After H coordinate reload: rebuild decals + body paint from remembered stamps.</summary>
        internal static void ReapplyAfterReload(ChaControl? cha)
        {
            if (cha == null || Stamps.Count == 0)
                return;

            DestroyVisualsOnly();
            EnsureCatalog(cha);

            int hung = 0;
            for (int i = 0; i < Stamps.Count; i++)
            {
                var s = Stamps[i];
                ApplyBodyPaintSlot(cha, s, i);
                var tex = LoadPaintTexture(cha, s.PaintId);
                if (tex == null)
                    continue;
                if (!TryFindParent(cha, s.ParentKey, out Transform parent))
                {
                    HS2OrbitAndExciter.Log?.LogWarning($"Orbit: 刺青重掛找不到 {s.ParentKey}");
                    continue;
                }
                SpawnDecalVisual(cha, parent, s.ParentKey, tex, s.Color, s.Size, recordStamp: false);
                hung++;
            }

            if (Stamps.Count > 0)
                _lastSiteLabel = FormatSiteLabel(Stamps[Stamps.Count - 1].ParentKey);

            HS2OrbitAndExciter.Log?.LogInfo($"Orbit: 刺青換衣後重掛 {hung}/{Stamps.Count}");
        }

        internal static void OnOrgasm(HSceneFlagCtrl? ctrlFlag)
        {
            if (!Enabled || ctrlFlag == null)
                return;

            var hScene = OrbitController.TryGetHScene();
            if (hScene == null || !ReferenceEquals(hScene.ctrlFlag, ctrlFlag))
            {
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: 高潮刺青略過（HScene/ctrlFlag 不符）");
                return;
            }

            SyncHScene(hScene);
            TryAddOne(forceLog: false);
        }

        private static void SyncHScene(HScene hScene)
        {
            int hId = hScene.GetInstanceID();
            if (hId == _lastHSceneId)
                return;
            _lastHSceneId = hId;
            ClearStamps();
        }

        private static void TryAddOne(bool forceLog)
        {
            var hScene = OrbitController.TryGetHScene();
            if (hScene == null)
            {
                if (forceLog)
                    HS2OrbitAndExciter.Log?.LogWarning("Orbit: 高潮刺青需要在 H 場景");
                return;
            }

            SyncHScene(hScene);
            var cha = OrbitHelpers.GetChaFemales(hScene)?[0];
            if (cha == null)
            {
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: 高潮刺青無女主");
                return;
            }

            EnsureCatalog(cha);
            if (_paintIds == null || _paintIds.Length == 0)
            {
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: 無 st_paint 刺青貼圖（目錄為空）");
                return;
            }

            int paintId = _paintIds[Random.Range(0, _paintIds.Length)];
            var tex = LoadPaintTexture(cha, paintId);
            for (int i = 0; i < 8 && tex == null; i++)
            {
                paintId = _paintIds[Random.Range(0, _paintIds.Length)];
                tex = LoadPaintTexture(cha, paintId);
            }
            if (tex == null)
            {
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: 刺青貼圖全部載入失敗");
                return;
            }

            if (!TryResolveNextParent(cha, out string parentKey, out Transform parent))
            {
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: 找不到身體掛點");
                return;
            }

            Color color = Color.HSVToRGB(Random.value, Random.Range(0.55f, 1f), Random.Range(0.5f, 1f));
            color.a = 1f;
            float size = BaseSize * Random.Range(GetScaleMin(), GetScaleMax());
            int layoutId = (_layoutIds != null && _layoutIds.Length > 0)
                ? _layoutIds[Random.Range(0, _layoutIds.Length)]
                : 0;
            var layout = new Vector4(
                Random.Range(0.05f, 0.35f),
                Random.Range(0.05f, 0.35f),
                Random.Range(0.2f, 0.8f),
                Random.Range(0.2f, 0.8f));
            float rotation = Random.value;

            int max = GetMaxStamps();
            while (Stamps.Count >= max)
                DestroyOldest();

            var stamp = new Stamp
            {
                PaintId = paintId,
                ParentKey = parentKey,
                Color = color,
                Size = size,
                LayoutId = layoutId,
                Layout = layout,
                Rotation = rotation,
            };
            Stamps.Add(stamp);
            ApplyBodyPaintSlot(cha, stamp, Stamps.Count - 1);
            SpawnDecalVisual(cha, parent, parentKey, tex, color, size, recordStamp: true);
        }

        private static void ApplyBodyPaintSlot(ChaControl cha, Stamp stamp, int stampIndex)
        {
            var paints = cha.fileBody?.paintInfo;
            if (paints == null || paints.Length < 2)
                return;

            // Only the last two stamps fit in native paint slots.
            int fromEnd = Stamps.Count - 1 - stampIndex;
            if (fromEnd > 1)
                return;

            int slot = fromEnd == 0 ? (Stamps.Count - 1) % 2 : (Stamps.Count - 2) % 2;

            var info = paints[slot];
            info.id = stamp.PaintId;
            info.layoutId = stamp.LayoutId;
            info.color = stamp.Color;
            info.glossPower = 0.4f;
            info.metallicPower = 0.2f;
            info.layout = stamp.Layout;
            info.rotation = stamp.Rotation;

            bool s0 = slot == 0;
            bool s1 = slot == 1;
            cha.AddUpdateCMBodyTexFlags(inpBase: false, inpPaint01: s0, inpPaint02: s1, inpSunburn: false);
            cha.AddUpdateCMBodyColorFlags(inpBase: false, inpPaint01: s0, inpPaint02: s1, inpSunburn: false);
            cha.AddUpdateCMBodyGlossFlags(inpPaint01: s0, inpPaint02: s1);
            cha.AddUpdateCMBodyLayoutFlags(inpPaint01: s0, inpPaint02: s1);
            cha.CreateBodyTexture();
        }

        private static bool TryResolveNextParent(ChaControl cha, out string key, out Transform parent)
        {
            key = "";
            parent = null!;
            for (int n = 0; n < BodyParents.Length; n++)
            {
                key = PickParent();
                if (TryFindParent(cha, key, out parent))
                    return true;
            }
            return false;
        }

        private static bool TryFindParent(ChaControl cha, string key, out Transform parent)
        {
            parent = null!;
            var root = cha.objBodyBone != null ? cha.objBodyBone.transform : null;
            if (root != null)
            {
                var bone = root.FindLoop(key);
                if (bone != null)
                {
                    parent = bone;
                    return true;
                }
            }

            try
            {
                parent = cha.GetAccessoryParentTransform(key);
                if (parent != null)
                    return true;
            }
            catch { /* ignore */ }

            return false;
        }

        private static void SpawnDecalVisual(
            ChaControl cha, Transform parent, string parentKey, Texture2D tex, Color color, float size, bool recordStamp)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = $"OrbitTattoo_{Decals.Count}_{parentKey}";
            var col = go.GetComponent("Collider") as Component;
            if (col != null)
                Object.Destroy(col);

            go.layer = cha.gameObject.layer;

            Vector3 bodyCenter = cha.objBody != null
                ? cha.objBody.transform.position
                : cha.transform.position;
            Vector3 outward = parent.position - bodyCenter;
            if (outward.sqrMagnitude < 1e-6f)
                outward = parent.TransformDirection(Vector3.forward);
            outward.Normalize();

            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = parent.position + outward * SurfaceOffset;
            go.transform.rotation = Quaternion.LookRotation(outward, Vector3.up);
            go.transform.localScale = new Vector3(size, size, size);

            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null)
            {
                Object.Destroy(go);
                return;
            }

            var mat = CreateDecalMaterial(tex, color);
            if (mat == null)
            {
                Object.Destroy(go);
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: 無法建立刺青材質");
                return;
            }

            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.enabled = true;

            Decals.Add(go);
            DecalMats.Add(mat);
            string site = FormatSiteLabel(parentKey);
            _lastSiteLabel = site;

            if (recordStamp)
            {
                HS2OrbitAndExciter.Log?.LogInfo(
                    $"Orbit: 高潮刺青 +1 位置={site}({parentKey}) 材質={mat.shader?.name} 共{Stamps.Count}");
            }
        }

        private static string FormatSiteLabel(string key)
        {
            switch (key)
            {
                case "N_Leg_L":
                case "cf_J_LegUp01_L": return "左大腿";
                case "N_Leg_R":
                case "cf_J_LegUp01_R": return "右大腿";
                case "N_Knee_L":
                case "cf_J_Knee_L": return "左膝";
                case "N_Knee_R":
                case "cf_J_Knee_R": return "右膝";
                case "N_Kokan":
                case "cf_J_Kokan": return "股間";
                case "N_Waist":
                case "N_Waist_f":
                case "cf_J_Kosi01": return "腰";
                case "N_Waist_b":
                case "cf_J_Kosi02": return "後腰";
                case "N_Waist_L": return "左腰";
                case "N_Waist_R": return "右腰";
                case "N_Back":
                case "cf_J_Spine01": return "背";
                case "N_Back_L": return "左背";
                case "N_Back_R": return "右背";
                case "cf_J_Spine02":
                case "cf_J_Spine03": return "上背";
                case "N_Chest_f":
                case "N_Chest":
                case "cf_J_Mune00": return "胸";
                case "N_Tikubi_L":
                case "cf_J_Mune01_L": return "左胸";
                case "N_Tikubi_R":
                case "cf_J_Mune01_R": return "右胸";
                case "N_Elbo_L": return "左肘";
                case "N_Elbo_R": return "右肘";
                case "N_Arm_L":
                case "cf_J_ArmUp00_L": return "左臂";
                case "N_Arm_R":
                case "cf_J_ArmUp00_R": return "右臂";
                case "N_Shoulder_L":
                case "cf_J_Shoulder_L": return "左肩";
                case "N_Shoulder_R":
                case "cf_J_Shoulder_R": return "右肩";
                case "N_Wrist_L": return "左腕";
                case "N_Wrist_R": return "右腕";
                case "N_Hand_L":
                case "cf_J_Hand_L": return "左手";
                case "N_Hand_R":
                case "cf_J_Hand_R": return "右手";
                case "N_Neck":
                case "cf_J_Neck": return "頸";
                case "N_Face":
                case "cf_J_Head": return "臉";
                default: return key;
            }
        }

        private static Material? CreateDecalMaterial(Texture2D tex, Color color)
        {
            var shader = GetDecalShader();
            if (shader == null)
                return null;

            var mat = new Material(shader);
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
            if (mat.HasProperty("_TintColor"))
                mat.SetColor("_TintColor", color);
            if (mat.HasProperty("_Color1"))
                mat.SetColor("_Color1", color);

            // Standard fade / transparent setup when available.
            if (mat.HasProperty("_Mode"))
                mat.SetFloat("_Mode", 2f);
            if (mat.HasProperty("_SrcBlend"))
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))
                mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite"))
                mat.SetInt("_ZWrite", 0);
            if (mat.HasProperty("_Cull"))
                mat.SetInt("_Cull", (int)CullMode.Off);

            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)RenderQueue.Transparent;
            return mat;
        }

        /// <summary>Prefer simple transparent shaders — body skin shaders often make quads invisible.</summary>
        private static Shader? GetDecalShader()
        {
            if (_decalShader != null)
                return _decalShader;

            string[] candidates =
            {
                "Unlit/Transparent",
                "Legacy Shaders/Transparent/Diffuse",
                "Sprites/Default",
                "UI/Default",
                "Standard",
                "Diffuse",
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                var s = Shader.Find(candidates[i]);
                if (s != null)
                {
                    _decalShader = s;
                    HS2OrbitAndExciter.Log?.LogInfo($"Orbit: 刺青 shader={candidates[i]}");
                    return _decalShader;
                }
            }
            return null;
        }

        private static void DestroyVisualsOnly()
        {
            for (int i = 0; i < Decals.Count; i++)
            {
                if (Decals[i] != null)
                    Object.Destroy(Decals[i]);
            }
            Decals.Clear();
            for (int i = 0; i < DecalMats.Count; i++)
            {
                if (DecalMats[i] != null)
                    Object.Destroy(DecalMats[i]);
            }
            DecalMats.Clear();
        }

        private static void DestroyOldest()
        {
            if (Stamps.Count > 0)
                Stamps.RemoveAt(0);
            if (Decals.Count > 0)
            {
                if (Decals[0] != null)
                    Object.Destroy(Decals[0]);
                Decals.RemoveAt(0);
            }
            if (DecalMats.Count > 0)
            {
                if (DecalMats[0] != null)
                    Object.Destroy(DecalMats[0]);
                DecalMats.RemoveAt(0);
            }
            _lastSiteLabel = Stamps.Count > 0
                ? FormatSiteLabel(Stamps[Stamps.Count - 1].ParentKey)
                : "";
        }

        private static string PickParent()
        {
            if (_parentCursor >= BodyParents.Length)
                _parentCursor = 0;
            return BodyParents[_parentCursor++];
        }

        private static int GetMaxStamps()
        {
            int v = HS2OrbitAndExciter.OrgasmTattooMaxCount?.Value ?? 24;
            return Mathf.Clamp(v, 1, 64);
        }

        private static float GetScaleMin()
        {
            float v = HS2OrbitAndExciter.OrgasmTattooScaleMin?.Value ?? 2.5f;
            return Mathf.Clamp(v, 1f, 20f);
        }

        private static float GetScaleMax()
        {
            float min = GetScaleMin();
            float v = HS2OrbitAndExciter.OrgasmTattooScaleMax?.Value ?? 4.5f;
            return Mathf.Max(min, Mathf.Clamp(v, 1f, 20f));
        }

        private static Texture2D? LoadPaintTexture(ChaControl cha, int paintId)
        {
            var listInfo = cha.lstCtrl?.GetListInfo(ChaListDefine.CategoryNo.st_paint, paintId);
            if (listInfo == null)
                return null;

            string manifest = listInfo.GetInfo(ChaListDefine.KeyType.MainManifest);
            if (manifest == "0")
                manifest = "";
            string ab = listInfo.GetInfo(ChaListDefine.KeyType.MainAB);
            string asset = listInfo.GetInfo(ChaListDefine.KeyType.AddTex);
            if (ab == "0" || asset == "0" || string.IsNullOrEmpty(ab) || string.IsNullOrEmpty(asset))
                return null;

            var tex = CommonLib.LoadAsset<Texture2D>(ab, asset, clone: false, manifest);
            if (tex != null && Singleton<Character>.IsInstance())
                Singleton<Character>.Instance.AddLoadAssetBundle(ab, manifest);
            return tex;
        }

        private static void EnsureCatalog(ChaControl cha)
        {
            if (_catalogTried)
                return;
            _catalogTried = true;

            var list = new List<int>(64);
            try
            {
                var paintCat = cha.lstCtrl?.GetCategoryInfo(ChaListDefine.CategoryNo.st_paint);
                if (paintCat != null)
                {
                    foreach (var kv in paintCat)
                    {
                        if (kv.Key > 0)
                            list.Add(kv.Key);
                    }
                }
                _paintIds = list.Count > 0 ? list.ToArray() : null;

                list.Clear();
                var layoutCat = cha.lstCtrl?.GetCategoryInfo(ChaListDefine.CategoryNo.bodypaint_layout);
                if (layoutCat != null)
                {
                    foreach (var kv in layoutCat)
                    {
                        if (kv.Key >= 0)
                            list.Add(kv.Key);
                    }
                }
                _layoutIds = list.Count > 0 ? list.ToArray() : null;
            }
            catch (System.Exception ex)
            {
                HS2OrbitAndExciter.Log?.LogWarning($"Orbit: 刺青目錄讀取失敗: {ex.Message}");
                _paintIds = null;
                _layoutIds = null;
            }

            HS2OrbitAndExciter.Log?.LogInfo(
                $"Orbit: st_paint={_paintIds?.Length ?? 0} layout={_layoutIds?.Length ?? 0}");
        }
    }
}
