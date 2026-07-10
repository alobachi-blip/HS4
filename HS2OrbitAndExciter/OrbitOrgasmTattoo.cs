using System.Collections.Generic;
using AIChara;
using IllusionUtility.GetUtility;
using Manager;
using UnityEngine;
using UnityEngine.Rendering;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// Orgasm tattoos from maker <c>st_paint</c>:
    /// 1) visible quad decal on body attach points (thigh → face);
    /// 2) also writes body-paint slots so skin shows a tattoo.
    /// Toggle with T (turning ON immediately places one sample).
    /// </summary>
    internal static class OrbitOrgasmTattoo
    {
        private const float BaseSize = 0.22f;

        private static readonly string[] BodyParents =
        {
            "N_Leg_L", "N_Leg_R", "N_Knee_L", "N_Knee_R",
            "N_Kokan", "N_Waist", "N_Waist_f", "N_Waist_b", "N_Waist_L", "N_Waist_R",
            "N_Back", "N_Back_L", "N_Back_R",
            "N_Chest_f", "N_Chest", "N_Tikubi_L", "N_Tikubi_R",
            "N_Elbo_L", "N_Elbo_R", "N_Arm_L", "N_Arm_R", "N_Shoulder_L", "N_Shoulder_R",
            "N_Wrist_L", "N_Wrist_R", "N_Hand_L", "N_Hand_R",
            "N_Neck", "N_Face",
        };

        /// <summary>Fallback bones if accessory parents missing.</summary>
        private static readonly string[] BoneFallbacks =
        {
            "cf_J_LegUp01_L", "cf_J_LegUp01_R", "cf_J_Knee_L", "cf_J_Knee_R",
            "cf_J_Kokan", "cf_J_Kosi01", "cf_J_Kosi02",
            "cf_J_Spine01", "cf_J_Spine02", "cf_J_Spine03",
            "cf_J_Mune00", "cf_J_Mune01_L", "cf_J_Mune01_R",
            "cf_J_ArmUp00_L", "cf_J_ArmUp00_R", "cf_J_Shoulder_L", "cf_J_Shoulder_R",
            "cf_J_Hand_L", "cf_J_Hand_R",
            "cf_J_Neck", "cf_J_Head",
        };

        private static readonly List<GameObject> Decals = new List<GameObject>(32);
        private static readonly List<Material> DecalMats = new List<Material>(32);
        private static readonly List<string> SiteLabels = new List<string>(32);
        private static int[]? _paintIds;
        private static int[]? _layoutIds;
        private static bool _catalogTried;
        private static int _parentCursor;
        private static int _paintSlot;
        private static int _lastHSceneId = -1;
        private static Shader? _decalShader;
        private static string _lastSiteLabel = "";

        internal static int Count => Decals.Count;
        /// <summary>Chinese label of the most recently placed tattoo site (for HUD).</summary>
        internal static string LastSiteLabel => _lastSiteLabel;
        /// <summary>Compact HUD: count + last site, e.g. 刺3·左大腿.</summary>
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
                HS2OrbitAndExciter.Log?.LogInfo($"Orbit: T 高潮刺青 ON（上限 {GetMaxStamps()}）");
                // Immediate sample so user can see it without waiting for orgasm.
                TryAddOne(forceLog: true);
            }
            else
            {
                HS2OrbitAndExciter.Log?.LogInfo("Orbit: T 高潮刺青 OFF");
            }
            return Enabled;
        }

        internal static void ClearStamps()
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
            SiteLabels.Clear();
            _parentCursor = 0;
            _paintSlot = 0;
            _lastSiteLabel = "";
        }

        internal static void OnHSceneEntered()
        {
            ClearStamps();
            _lastHSceneId = -1;
            _catalogTried = false;
            _paintIds = null;
            _layoutIds = null;
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
            if (tex == null)
            {
                // Retry a few ids.
                for (int i = 0; i < 8 && tex == null; i++)
                {
                    paintId = _paintIds[Random.Range(0, _paintIds.Length)];
                    tex = LoadPaintTexture(cha, paintId);
                }
            }
            if (tex == null)
            {
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: 刺青貼圖全部載入失敗");
                return;
            }

            Color color = Color.HSVToRGB(Random.value, Random.Range(0.55f, 1f), Random.Range(0.5f, 1f));
            color.a = 1f;
            float size = BaseSize * Random.Range(GetScaleMin(), GetScaleMax());

            // Skin paint (always try) — visible on body texture.
            ApplyBodyPaint(cha, paintId, color);

            // 3D decal on attach point.
            if (!TryResolveParent(cha, out string parentKey, out Transform parent))
            {
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: 找不到身體掛點（貼花略過，已寫 body paint）");
                return;
            }

            int max = GetMaxStamps();
            while (Decals.Count >= max)
                DestroyOldest();

            if (!TrySpawnDecal(cha, parent, parentKey, tex, color, size, paintId))
                return;
        }

        private static void ApplyBodyPaint(ChaControl cha, int paintId, Color color)
        {
            var paints = cha.fileBody?.paintInfo;
            if (paints == null || paints.Length < 2)
                return;

            int slot = _paintSlot % 2;
            _paintSlot = slot + 1;

            int layoutId = 0;
            if (_layoutIds != null && _layoutIds.Length > 0)
                layoutId = _layoutIds[Random.Range(0, _layoutIds.Length)];

            var info = paints[slot];
            info.id = paintId;
            info.layoutId = layoutId;
            info.color = color;
            info.glossPower = 0.4f;
            info.metallicPower = 0.2f;
            // Large stamp: low layout.x/y → larger scale in CreateBodyTexture lerp.
            info.layout = new Vector4(
                Random.Range(0.05f, 0.35f),
                Random.Range(0.05f, 0.35f),
                Random.Range(0.2f, 0.8f),
                Random.Range(0.2f, 0.8f));
            info.rotation = Random.value;

            bool s0 = slot == 0;
            bool s1 = slot == 1;
            cha.AddUpdateCMBodyTexFlags(inpBase: false, inpPaint01: s0, inpPaint02: s1, inpSunburn: false);
            cha.AddUpdateCMBodyColorFlags(inpBase: false, inpPaint01: s0, inpPaint02: s1, inpSunburn: false);
            cha.AddUpdateCMBodyGlossFlags(inpPaint01: s0, inpPaint02: s1);
            cha.AddUpdateCMBodyLayoutFlags(inpPaint01: s0, inpPaint02: s1);
            cha.CreateBodyTexture();
        }

        private static bool TryResolveParent(ChaControl cha, out string key, out Transform parent)
        {
            key = "";
            parent = null!;

            for (int n = 0; n < BodyParents.Length; n++)
            {
                key = PickParent();
                parent = cha.GetAccessoryParentTransform(key);
                if (parent != null)
                    return true;
            }

            // Bone name fallbacks.
            var root = cha.objBodyBone != null ? cha.objBodyBone.transform : null;
            if (root == null)
                return false;

            int boneIdx = (_parentCursor - 1 + BoneFallbacks.Length) % BoneFallbacks.Length;
            for (int n = 0; n < BoneFallbacks.Length; n++)
            {
                int i = (boneIdx + n) % BoneFallbacks.Length;
                key = BoneFallbacks[i];
                var t = root.FindLoop(key);
                if (t != null)
                {
                    parent = t;
                    return true;
                }
            }
            return false;
        }

        private static bool TrySpawnDecal(
            ChaControl cha, Transform parent, string parentKey, Texture2D tex, Color color, float size, int paintId)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = $"OrbitTattoo_{Decals.Count}_{parentKey}";
            var col = go.GetComponent("Collider") as Component;
            if (col != null)
                Object.Destroy(col);

            go.layer = cha.gameObject.layer;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.025f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(size, size, size);

            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null)
            {
                Object.Destroy(go);
                return false;
            }

            var mat = CreateDecalMaterial(cha, tex, color);
            if (mat == null)
            {
                Object.Destroy(go);
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: 無法建立刺青材質");
                return false;
            }

            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            Decals.Add(go);
            DecalMats.Add(mat);
            string site = FormatSiteLabel(parentKey);
            SiteLabels.Add(site);
            _lastSiteLabel = site;

            HS2OrbitAndExciter.Log?.LogInfo(
                $"Orbit: 高潮刺青 +1 位置={site}({parentKey}) paintId={paintId} size={size:F2} 共{Decals.Count}");
            return true;
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
                case "cf_J_Kokan": return "下腹";
                case "N_Waist":
                case "cf_J_Kosi01": return "腰";
                case "N_Waist_f": return "腰前";
                case "N_Waist_b":
                case "cf_J_Kosi02": return "腰後";
                case "N_Waist_L": return "左腰";
                case "N_Waist_R": return "右腰";
                case "N_Back":
                case "cf_J_Spine01": return "背中";
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

        private static Material? CreateDecalMaterial(ChaControl cha, Texture2D tex, Color color)
        {
            var shader = GetDecalShader(cha);
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
            if (mat.HasProperty("_Cull"))
                mat.SetInt("_Cull", (int)CullMode.Off);

            // Prefer cutout/alpha so paint edges show.
            if (mat.HasProperty("_Mode"))
                mat.SetFloat("_Mode", 1f);
            mat.renderQueue = (int)RenderQueue.Transparent;
            return mat;
        }

        private static Shader? GetDecalShader(ChaControl cha)
        {
            if (_decalShader != null)
                return _decalShader;

            // Prefer a shader already loaded in the character (HS2 often strips built-ins).
            try
            {
                if (cha.customTexCtrlBody?.matDraw != null && cha.customTexCtrlBody.matDraw.shader != null)
                    _decalShader = cha.customTexCtrlBody.matDraw.shader;
            }
            catch { /* ignore */ }

            if (_decalShader == null && cha.objBody != null)
            {
                var r = cha.objBody.GetComponentInChildren<Renderer>();
                if (r != null && r.sharedMaterial != null && r.sharedMaterial.shader != null)
                    _decalShader = r.sharedMaterial.shader;
            }

            if (_decalShader == null)
            {
                _decalShader = Shader.Find("Standard")
                               ?? Shader.Find("Unlit/Transparent")
                               ?? Shader.Find("Legacy Shaders/Transparent/Diffuse")
                               ?? Shader.Find("Sprites/Default")
                               ?? Shader.Find("Diffuse");
            }
            return _decalShader;
        }

        private static void DestroyOldest()
        {
            if (Decals.Count == 0)
                return;
            if (Decals[0] != null)
                Object.Destroy(Decals[0]);
            Decals.RemoveAt(0);
            if (SiteLabels.Count > 0)
                SiteLabels.RemoveAt(0);
            if (DecalMats.Count > 0)
            {
                if (DecalMats[0] != null)
                    Object.Destroy(DecalMats[0]);
                DecalMats.RemoveAt(0);
            }
            _lastSiteLabel = SiteLabels.Count > 0 ? SiteLabels[SiteLabels.Count - 1] : "";
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
