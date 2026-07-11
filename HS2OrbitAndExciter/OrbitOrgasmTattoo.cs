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
    /// visible quad decals on body attach points (thigh → face) + body-paint slots.
    /// Survives H coordinate swap via <see cref="ReapplyAfterReloadRoutine"/>.
    /// Avoids joint parents (knee/elbow/wrist/hand/shoulder) — those float off the limb.
    /// </summary>
    internal static class OrbitOrgasmTattoo
    {
        private const float BaseSize = 0.22f;
        private const float AccessoryLocalZ = 0.025f;
        private const float BoneSurfaceOffset = 0.028f;
        private const int ReapplySettleFrames = 6;
        private const int PaintRefreshExtraFrames = 3;

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

        /// <summary>
        /// Accessory parents first (correct local Z), then mid-limb/torso bones.
        /// No knee / elbow / wrist / hand / shoulder — joint pivots sit inside the mesh.
        /// </summary>
        private static readonly string[] BodyParents =
        {
            "N_Leg_L", "N_Leg_R",
            "N_Kokan", "N_Waist", "N_Waist_f", "N_Waist_b", "N_Waist_L", "N_Waist_R",
            "N_Back", "N_Back_L", "N_Back_R",
            "N_Chest_f", "N_Chest", "N_Tikubi_L", "N_Tikubi_R",
            "N_Arm_L", "N_Arm_R",
            "N_Neck", "N_Face",
            "cf_J_LegUp01_L", "cf_J_LegUp01_R",
            "cf_J_Kokan", "cf_J_Kosi01", "cf_J_Kosi02",
            "cf_J_Spine01", "cf_J_Spine02", "cf_J_Spine03",
            "cf_J_Mune00", "cf_J_Mune01_L", "cf_J_Mune01_R",
            "cf_J_ArmUp00_L", "cf_J_ArmUp00_R",
            "cf_J_Neck", "cf_J_Head",
        };

        private static readonly List<Stamp> Stamps = new List<Stamp>(32);
        private static readonly List<GameObject> Decals = new List<GameObject>(32);
        private static readonly List<Material> DecalMats = new List<Material>(32);
        private static int[]? _paintIds;
        private static int[]? _layoutIds;
        /// <summary>bodypaint_layout id → Japanese Name (for matching decal site).</summary>
        private static Dictionary<int, string>? _layoutNames;
        /// <summary>Maker 色見本（排除過淺膚色 index &lt; 17），刺青染色用。</summary>
        private static Color[]? _tattooPalette;
        private static bool _catalogTried;
        private const int ColorSampleSkipSkin = 17;
        private static int _parentCursor;
        private static int _lastHSceneId = -1;
        private static Shader? _decalShader;
        private static string _lastSiteLabel = "";
        private static int _reapplyGen;
        private static int _paintRefreshGen;

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

        /// <summary>T：開啟（若尚未開）並依序貼一張，方便連按檢查掛點。</summary>
        internal static bool EnableAndStamp()
        {
            if (!Enabled)
            {
                Enabled = true;
                HS2OrbitAndExciter.Log?.LogInfo($"Orbit: T 高潮刺青 ON（上限 {GetMaxStamps()}；連按 T 依序貼；Shift+T 關）");
            }
            TryAddOne(forceLog: true);
            return Enabled;
        }

        /// <summary>Shift+T：關閉高潮自動貼圖（已貼的保留）。</summary>
        internal static bool Disable()
        {
            if (!Enabled)
            {
                HS2OrbitAndExciter.Log?.LogInfo("Orbit: Shift+T 高潮刺青已是 OFF");
                return false;
            }
            Enabled = false;
            HS2OrbitAndExciter.Log?.LogInfo($"Orbit: Shift+T 高潮刺青 OFF（已貼 {Count} 張保留）");
            return false;
        }

        /// <summary>Drop all tattoos (new H scene / G chara swap).</summary>
        internal static void ClearStamps()
        {
            _reapplyGen++;
            _paintRefreshGen++;
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
            _layoutNames = null;
            _tattooPalette = null;
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
                ApplyBodyPaintSlot(cha, s, i, createTexture: false);
                var tex = LoadPaintTexture(cha, s.PaintId);
                if (tex == null)
                    continue;
                if (!TryFindParent(cha, s.ParentKey, out Transform parent))
                {
                    HS2OrbitAndExciter.Log?.LogWarning($"Orbit: 刺青重掛找不到 {s.ParentKey}");
                    continue;
                }
                if (TrySpawnDecalVisual(cha, parent, s.ParentKey, tex, s.Color, s.Size, out _))
                    hung++;
            }

            // One skin rebuild after all paint slots are written (avoid N× 4096 blit).
            RebuildBodyPaintTexture(cha);

            if (Stamps.Count > 0)
                _lastSiteLabel = FormatSiteLabel(Stamps[Stamps.Count - 1].ParentKey);

            HS2OrbitAndExciter.Log?.LogInfo($"Orbit: 刺青換衣後重掛 {hung}/{Stamps.Count}");
            ScheduleBodyPaintRefresh(cha);
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

            Color color = PickTattooColor();
            float size = BaseSize * Random.Range(GetScaleMin(), GetScaleMax());
            // Match body-paint layout region to the same site as the decal / HUD — never random UV.
            int layoutId = ResolveLayoutIdForParent(cha, parentKey);
            // Centered in the matched layout region; low x/y ⇒ larger stamp in CreateBodyTexture lerp.
            var layout = new Vector4(
                Random.Range(0.08f, 0.28f),
                Random.Range(0.08f, 0.28f),
                0.5f,
                0.5f);
            float rotation = Random.Range(0.35f, 0.65f);

            // Spawn first — only record stamp if the visible decal is created.
            // Trim after add so a failed spawn does not drop an existing stamp.
            if (!TrySpawnDecalVisual(cha, parent, parentKey, tex, color, size, out _))
                return;

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
            int max = GetMaxStamps();
            while (Stamps.Count > max)
                DestroyOldest();

            ApplyBodyPaintSlot(cha, stamp, Stamps.Count - 1);
            string layoutNote = layoutId >= 0 && _layoutNames != null && _layoutNames.TryGetValue(layoutId, out var ln)
                ? $" paint={ln}#{layoutId}"
                : " paint=略";
            HS2OrbitAndExciter.Log?.LogInfo(
                $"Orbit: 高潮刺青 +1 位置={FormatSiteLabel(parentKey)}({parentKey}){layoutNote} 共{Stamps.Count}");
            // Orgasm / H systems often rebuild body tex after AddOrgasm; refresh again EoF + N frames.
            ScheduleBodyPaintRefresh(cha);
        }

        /// <summary>
        /// Maker 色見本隨機色：跳過 index 0–16（膚色／過淺，刺青看不出），從 17 起抽。
        /// </summary>
        private static Color PickTattooColor()
        {
            EnsureTattooPalette();
            if (_tattooPalette != null && _tattooPalette.Length > 0)
            {
                var c = _tattooPalette[Random.Range(0, _tattooPalette.Length)];
                c.a = 1f;
                return c;
            }

            // Fallback if presets missing.
            var fallback = Color.HSVToRGB(Random.value, Random.Range(0.55f, 1f), Random.Range(0.35f, 0.85f));
            fallback.a = 1f;
            return fallback;
        }

        private static void EnsureTattooPalette()
        {
            if (_tattooPalette != null)
                return;

            string? json = null;
            try
            {
                string path = UserData.Path + "Custom/ColorPresets.json";
                if (System.IO.File.Exists(path))
                    json = System.IO.File.ReadAllText(path);
            }
            catch { /* ignore */ }

            if (string.IsNullOrEmpty(json))
            {
                try
                {
                    var ta = CommonLib.LoadAsset<TextAsset>("custom/colorsample.unity3d", "ColorPresets");
                    if (ta != null)
                    {
                        json = ta.text;
                        AssetBundleManager.UnloadAssetBundle("custom/colorsample.unity3d", isUnloadForceRefCount: true);
                    }
                }
                catch { /* ignore */ }
            }

            if (string.IsNullOrEmpty(json))
            {
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: 無法載入 ColorPresets，刺青改用 HSV 備援");
                _tattooPalette = System.Array.Empty<Color>();
                return;
            }

            try
            {
                var sample = ParseColorSampleList(json!);
                if (sample == null || sample.Count <= ColorSampleSkipSkin)
                {
                    HS2OrbitAndExciter.Log?.LogWarning(
                        $"Orbit: ColorPresets 色見本過短 ({sample?.Count ?? 0})，刺青改用 HSV 備援");
                    _tattooPalette = System.Array.Empty<Color>();
                    return;
                }

                int n = sample.Count - ColorSampleSkipSkin;
                var pal = new Color[n];
                for (int i = 0; i < n; i++)
                    pal[i] = sample[ColorSampleSkipSkin + i];
                _tattooPalette = pal;
                HS2OrbitAndExciter.Log?.LogInfo(
                    $"Orbit: 刺青色票=色見本[{ColorSampleSkipSkin}..{sample.Count - 1}] 共{n}色（已排除膚色）");
            }
            catch (System.Exception ex)
            {
                HS2OrbitAndExciter.Log?.LogWarning($"Orbit: ColorPresets 解析失敗: {ex.Message}");
                _tattooPalette = System.Array.Empty<Color>();
            }
        }

        /// <summary>Parse lstColorSample from ColorPresets.json without JsonUtility (stub UnityEngine).</summary>
        private static List<Color>? ParseColorSampleList(string json)
        {
            const string key = "\"lstColorSample\"";
            int keyAt = json.IndexOf(key, System.StringComparison.Ordinal);
            if (keyAt < 0)
                return null;
            int arrStart = json.IndexOf('[', keyAt);
            if (arrStart < 0)
                return null;

            var list = new List<Color>(80);
            int i = arrStart + 1;
            while (i < json.Length)
            {
                // skip whitespace / commas
                while (i < json.Length && (json[i] == ' ' || json[i] == '\n' || json[i] == '\r' || json[i] == '\t' || json[i] == ','))
                    i++;
                if (i >= json.Length || json[i] == ']')
                    break;
                if (json[i] != '{')
                    break;

                int objEnd = json.IndexOf('}', i);
                if (objEnd < 0)
                    break;
                string obj = json.Substring(i, objEnd - i + 1);
                float r = ReadJsonFloat(obj, "r");
                float g = ReadJsonFloat(obj, "g");
                float b = ReadJsonFloat(obj, "b");
                list.Add(new Color(r, g, b, 1f));
                i = objEnd + 1;
            }
            return list;
        }

        private static float ReadJsonFloat(string obj, string field)
        {
            string needle = "\"" + field + "\":";
            int at = obj.IndexOf(needle, System.StringComparison.Ordinal);
            if (at < 0)
                return 0f;
            int start = at + needle.Length;
            int end = start;
            while (end < obj.Length && (char.IsDigit(obj[end]) || obj[end] == '.' || obj[end] == '-' || obj[end] == 'e' || obj[end] == 'E' || obj[end] == '+'))
                end++;
            if (float.TryParse(obj.Substring(start, end - start), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float v))
                return v;
            return 0f;
        }

        /// <summary>
        /// Pick a bodypaint_layout whose Name matches the decal site (e.g. 左太もも).
        /// Returns -1 when no match — skip body paint so a random layout cannot contradict the HUD.
        /// </summary>
        private static int ResolveLayoutIdForParent(ChaControl cha, string parentKey)
        {
            EnsureCatalog(cha);
            if (_layoutNames == null || _layoutNames.Count == 0)
                return -1;

            string[] hints = GetLayoutNameHints(parentKey);
            for (int h = 0; h < hints.Length; h++)
            {
                string hint = hints[h];
                foreach (var kv in _layoutNames)
                {
                    if (kv.Key <= 0)
                        continue; // 0 = なし
                    if (kv.Value.IndexOf(hint, System.StringComparison.Ordinal) >= 0)
                        return kv.Key;
                }
            }
            return -1;
        }

        private static string[] GetLayoutNameHints(string parentKey)
        {
            switch (parentKey)
            {
                case "N_Leg_L":
                case "cf_J_LegUp01_L":
                    return new[] { "左太もも", "左腿", "Left Thigh", "太もも" };
                case "N_Leg_R":
                case "cf_J_LegUp01_R":
                    return new[] { "右太もも", "右腿", "Right Thigh", "太もも" };
                case "N_Kokan":
                case "cf_J_Kokan":
                    return new[] { "股間", "股", "秘部", "局部" };
                case "N_Waist":
                case "N_Waist_f":
                case "cf_J_Kosi01":
                    return new[] { "腰前", "お腹", "腹", "腰" };
                case "N_Waist_b":
                case "cf_J_Kosi02":
                    return new[] { "腰後", "後腰", "尻", "腰" };
                case "N_Waist_L":
                    return new[] { "左腰", "腰" };
                case "N_Waist_R":
                    return new[] { "右腰", "腰" };
                case "N_Back":
                case "cf_J_Spine01":
                case "cf_J_Spine02":
                case "cf_J_Spine03":
                    return new[] { "背中", "背" };
                case "N_Back_L":
                    return new[] { "左背", "背中", "背" };
                case "N_Back_R":
                    return new[] { "右背", "背中", "背" };
                case "N_Chest_f":
                case "N_Chest":
                case "cf_J_Mune00":
                    return new[] { "胸", "バスト" };
                case "N_Tikubi_L":
                case "cf_J_Mune01_L":
                    return new[] { "左胸", "左乳", "胸" };
                case "N_Tikubi_R":
                case "cf_J_Mune01_R":
                    return new[] { "右胸", "右乳", "胸" };
                case "N_Arm_L":
                case "cf_J_ArmUp00_L":
                    return new[] { "左上腕", "左腕", "左腕部", "腕" };
                case "N_Arm_R":
                case "cf_J_ArmUp00_R":
                    return new[] { "右上腕", "右腕", "右腕部", "腕" };
                case "N_Neck":
                case "cf_J_Neck":
                    return new[] { "首", "ネック", "Neck" };
                case "N_Face":
                case "cf_J_Head":
                    // Body paint rarely has a face slot; prefer neck over a wrong random region.
                    return new[] { "顔", "首", "ネック" };
                default:
                    return System.Array.Empty<string>();
            }
        }

        /// <summary>
        /// Re-push the last ≤2 stamps into paint slots after H-scene systems finish overwriting skin tex.
        /// First CreateBodyTexture often wins only on the initial stamp; later orgasms get clobbered mid-frame.
        /// </summary>
        private static void ScheduleBodyPaintRefresh(ChaControl cha)
        {
            if (cha == null)
                return;
            int gen = ++_paintRefreshGen;
            cha.StartCoroutine(BodyPaintRefreshRoutine(cha, gen));
        }

        private static IEnumerator BodyPaintRefreshRoutine(ChaControl cha, int gen)
        {
            yield return new WaitForEndOfFrame();
            if (gen != _paintRefreshGen || cha == null || !cha.loadEnd || Stamps.Count == 0)
                yield break;
            ReapplyLatestBodyPaint(cha);

            for (int i = 0; i < PaintRefreshExtraFrames; i++)
                yield return null;

            if (gen != _paintRefreshGen || cha == null || !cha.loadEnd || Stamps.Count == 0)
                yield break;
            ReapplyLatestBodyPaint(cha);
        }

        private static void ReapplyLatestBodyPaint(ChaControl cha)
        {
            int n = Stamps.Count;
            if (n <= 0)
                return;

            int start = Mathf.Max(0, n - 2);
            for (int i = start; i < n; i++)
                ApplyBodyPaintSlot(cha, Stamps[i], i, createTexture: false);

            RebuildBodyPaintTexture(cha);
        }

        private static void RebuildBodyPaintTexture(ChaControl cha)
        {
            var paints = cha.fileBody?.paintInfo;
            if (paints == null || paints.Length < 2)
                return;

            cha.AddUpdateCMBodyTexFlags(inpBase: false, inpPaint01: true, inpPaint02: true, inpSunburn: false);
            cha.AddUpdateCMBodyColorFlags(inpBase: false, inpPaint01: true, inpPaint02: true, inpSunburn: false);
            cha.AddUpdateCMBodyGlossFlags(inpPaint01: true, inpPaint02: true);
            cha.AddUpdateCMBodyLayoutFlags(inpPaint01: true, inpPaint02: true);
            cha.CreateBodyTexture();
        }

        private static void ApplyBodyPaintSlot(ChaControl cha, Stamp stamp, int stampIndex, bool createTexture = true)
        {
            // LayoutId < 0: no matching body region — keep decal only so paint cannot contradict HUD.
            if (stamp.LayoutId < 0)
                return;

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

            if (!createTexture)
                return;

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

            // Accessory attach points (N_*) live on the accessory tree, not bone FindLoop.
            if (key.StartsWith("N_"))
            {
                try
                {
                    parent = cha.GetAccessoryParentTransform(key);
                    if (parent != null)
                        return true;
                }
                catch { /* ignore */ }
            }

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

            if (!key.StartsWith("N_"))
            {
                try
                {
                    parent = cha.GetAccessoryParentTransform(key);
                    if (parent != null)
                        return true;
                }
                catch { /* ignore */ }
            }

            return false;
        }

        private static bool TrySpawnDecalVisual(
            ChaControl cha, Transform parent, string parentKey, Texture2D tex, Color color, float size, out GameObject? go)
        {
            go = null;
            var created = GameObject.CreatePrimitive(PrimitiveType.Quad);
            created.name = $"OrbitTattoo_{Decals.Count}_{parentKey}";
            var col = created.GetComponent("Collider") as Component;
            if (col != null)
                Object.Destroy(col);

            created.layer = cha.gameObject.layer;
            created.transform.SetParent(parent, worldPositionStays: false);

            // Accessory parents (N_*) have a correct outward local Z; bone pivots need body-center offset.
            if (parentKey.StartsWith("N_"))
            {
                created.transform.localPosition = new Vector3(0f, 0f, AccessoryLocalZ);
                created.transform.localRotation = Quaternion.identity;
            }
            else
            {
                Vector3 bodyCenter = cha.objBody != null
                    ? cha.objBody.transform.position
                    : cha.transform.position;
                Vector3 outward = parent.position - bodyCenter;
                if (outward.sqrMagnitude < 1e-6f)
                    outward = parent.TransformDirection(Vector3.forward);
                outward.Normalize();
                created.transform.position = parent.position + outward * BoneSurfaceOffset;
                created.transform.rotation = Quaternion.LookRotation(outward, Vector3.up);
            }

            created.transform.localScale = new Vector3(size, size, size);

            var mr = created.GetComponent<MeshRenderer>();
            if (mr == null)
            {
                Object.Destroy(created);
                return false;
            }

            var mat = CreateDecalMaterial(tex, color);
            if (mat == null)
            {
                Object.Destroy(created);
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: 無法建立刺青材質");
                return false;
            }

            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.enabled = true;

            Decals.Add(created);
            DecalMats.Add(mat);
            _lastSiteLabel = FormatSiteLabel(parentKey);
            go = created;
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
                var names = new Dictionary<int, string>(64);
                var layoutCat = cha.lstCtrl?.GetCategoryInfo(ChaListDefine.CategoryNo.bodypaint_layout);
                if (layoutCat != null)
                {
                    foreach (var kv in layoutCat)
                    {
                        if (kv.Key < 0)
                            continue;
                        list.Add(kv.Key);
                        // Prefer authored JP Name for matching; append display Name if different.
                        string jp = kv.Value != null ? kv.Value.GetInfo(ChaListDefine.KeyType.Name) : "";
                        string display = kv.Value?.Name ?? "";
                        string combined = jp;
                        if (!string.IsNullOrEmpty(display) && display != jp)
                            combined = string.IsNullOrEmpty(jp) ? display : jp + "|" + display;
                        if (!string.IsNullOrEmpty(combined))
                            names[kv.Key] = combined;
                    }
                }
                _layoutIds = list.Count > 0 ? list.ToArray() : null;
                _layoutNames = names.Count > 0 ? names : null;
            }
            catch (System.Exception ex)
            {
                HS2OrbitAndExciter.Log?.LogWarning($"Orbit: 刺青目錄讀取失敗: {ex.Message}");
                _paintIds = null;
                _layoutIds = null;
                _layoutNames = null;
            }

            HS2OrbitAndExciter.Log?.LogInfo(
                $"Orbit: st_paint={_paintIds?.Length ?? 0} layout={_layoutIds?.Length ?? 0}");
        }
    }
}
