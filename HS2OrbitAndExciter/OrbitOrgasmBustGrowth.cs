using AIChara;
using HarmonyLib;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// Session-only bust growth. <see cref="ChaControl.SetShapeBodyValue"/> writes card buffer
    /// (<c>fileBody</c>), so orgasm growth is applied visually via shape bones only and never
    /// mutates <c>fileBody</c>. EndProc / SaveFile still force-restore as a safety net.
    /// </summary>
    internal static class OrbitOrgasmBustGrowth
    {
        private const int BustSizeIndex = (int)ChaFileDefine.BodyShapeIdx.BustSize;

        private static float _baseline = -1f;
        private static bool _hasBaseline;
        private static float _lastBefore = -1f;
        private static float _lastAfter = -1f;
        private static bool _justRestored;
        private static ChaControl? _sessionCha;
        private static string _sessionFileName = "";
        /// <summary>Display bust for this H session (may differ from fileBody).</summary>
        private static float _displayBust = -1f;
        private static float _reapplyAfterSave = -1f;

        internal static bool Enabled => HS2OrbitAndExciter.OrgasmBustGrowEnabled?.Value ?? true;

        internal static float GrowPercent
        {
            get
            {
                float v = HS2OrbitAndExciter.OrgasmBustGrowPercent?.Value ?? 15f;
                return Mathf.Clamp(v, 0f, 100f);
            }
        }

        internal static string HudStatus
        {
            get
            {
                if (!Enabled)
                    return "胸關";
                if (_justRestored && _hasBaseline)
                    return $"胸回復{_baseline * 100f:F0}%";
                if (_lastAfter < 0f)
                    return _hasBaseline ? $"胸基{_baseline * 100f:F0}%" : "胸待命";
                if (_hasBaseline && _lastAfter > _baseline + 0.005f)
                    return $"胸{_lastAfter * 100f:F0}%·基{_baseline * 100f:F0}%·B回";
                return $"胸{_lastBefore * 100f:F0}→{_lastAfter * 100f:F0}%";
            }
        }

        internal static void CaptureBaseline(ChaControl? cha)
        {
            if (cha == null || cha.fileBody?.shapeValueBody == null)
                return;
            if (BustSizeIndex >= cha.fileBody.shapeValueBody.Length)
                return;

            _sessionCha = cha;
            try { _sessionFileName = cha.chaFile?.charaFileName ?? ""; }
            catch { _sessionFileName = ""; }
            _baseline = cha.GetShapeBodyValue(BustSizeIndex);
            _displayBust = _baseline;
            _hasBaseline = true;
            _justRestored = false;
            _lastBefore = -1f;
            _lastAfter = -1f;
            _reapplyAfterSave = -1f;
            HS2OrbitAndExciter.Log?.LogInfo($"Orbit: 胸部基準 {_baseline * 100f:F0}%");
        }

        internal static void OnOrgasm(HSceneFlagCtrl? ctrlFlag)
        {
            if (!Enabled || ctrlFlag == null || GrowPercent <= 0f)
                return;

            var hScene = OrbitController.TryGetHScene();
            if (hScene == null || !ReferenceEquals(hScene.ctrlFlag, ctrlFlag))
                return;

            var cha = OrbitHelpers.GetChaFemales(hScene)?[0];
            if (cha == null || cha.fileBody?.shapeValueBody == null)
                return;
            if (BustSizeIndex >= cha.fileBody.shapeValueBody.Length)
                return;

            if (!_hasBaseline)
                CaptureBaseline(cha);
            else
            {
                _sessionCha = cha;
                try { _sessionFileName = cha.chaFile?.charaFileName ?? _sessionFileName; }
                catch { /* ignore */ }
            }

            // Grow from session display value, NOT fileBody (fileBody stays at card baseline).
            float before = _displayBust >= 0f ? _displayBust : cha.GetShapeBodyValue(BustSizeIndex);
            float after = Mathf.Clamp01(before * (1f + GrowPercent / 100f));
            if (before < 0.05f && after <= before + 0.001f)
                after = Mathf.Clamp01(GrowPercent / 100f);

            _justRestored = false;
            if (Mathf.Approximately(before, after))
            {
                _lastBefore = before;
                _lastAfter = after;
                HS2OrbitAndExciter.Log?.LogInfo($"Orbit: 高潮胸部已達上限 {after * 100f:F0}%");
                return;
            }

            // Visual only — never SetShapeBodyValue (that writes card buffer).
            ApplyVisualBust(cha, after);
            _displayBust = after;

            _lastBefore = before;
            _lastAfter = after;
            HS2OrbitAndExciter.Log?.LogInfo(
                $"Orbit: 高潮胸部 +{GrowPercent:F0}%  {before * 100f:F0}%→{after * 100f:F0}%（視覺；卡身基{_baseline * 100f:F0}%）");
        }

        /// <summary>H exit: ensure fileBody is baseline, reset visual, clear session.</summary>
        internal static void OnHSceneExiting(HScene? hScene)
        {
            ChaControl? cha = null;
            try
            {
                cha = hScene != null
                    ? OrbitHelpers.GetChaFemales(hScene)?[0]
                    : _sessionCha;
            }
            catch { /* ignore */ }

            if (_hasBaseline && cha != null)
                ForceFileAndVisualToBaseline(cha, "h_exit");

            ResetHud();
        }

        /// <summary>Before any card serialize: force baseline into this file if it looks like our session growth.</summary>
        internal static bool EnsureBaselineOnFileForSave(ChaFile? file)
        {
            _reapplyAfterSave = -1f;
            if (!_hasBaseline || file?.custom?.body?.shapeValueBody == null)
                return false;
            if (BustSizeIndex >= file.custom.body.shapeValueBody.Length)
                return false;
            if (!IsSessionSaveTarget(file))
                return false;

            float current = file.custom.body.shapeValueBody[BustSizeIndex];
            if (current <= _baseline + 0.005f)
                return false;

            _reapplyAfterSave = _displayBust >= 0f ? _displayBust : current;
            file.custom.body.shapeValueBody[BustSizeIndex] = _baseline;
            if (_sessionCha != null)
            {
                try { ApplyVisualBust(_sessionCha, _baseline); }
                catch { /* ignore */ }
            }

            return true;
        }

        internal static void ReapplyAfterSave()
        {
            if (_reapplyAfterSave < 0f || !_hasBaseline)
                return;
            if (OrbitController.TryGetHScene() == null)
            {
                _reapplyAfterSave = -1f;
                return;
            }
            var cha = _sessionCha;
            if (cha == null)
            {
                _reapplyAfterSave = -1f;
                return;
            }
            float v = _reapplyAfterSave;
            _reapplyAfterSave = -1f;
            // Visual only — keep fileBody at baseline after save.
            ApplyVisualBust(cha, v);
            _displayBust = v;
            _lastAfter = v;
            _justRestored = false;
        }

        internal static bool TryRestore(HScene? hScene)
        {
            if (hScene == null)
            {
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: 胸部回復需要在 H 場景");
                return false;
            }

            var cha = OrbitHelpers.GetChaFemales(hScene)?[0];
            if (cha == null || cha.fileBody?.shapeValueBody == null)
                return false;
            if (BustSizeIndex >= cha.fileBody.shapeValueBody.Length)
                return false;

            if (!_hasBaseline)
                CaptureBaseline(cha);

            return ForceFileAndVisualToBaseline(cha, "B");
        }

        private static bool IsSessionSaveTarget(ChaFile file)
        {
            if (_sessionCha?.chaFile != null && ReferenceEquals(_sessionCha.chaFile, file))
                return true;
            try
            {
                string name = file.charaFileName ?? "";
                if (!string.IsNullOrEmpty(_sessionFileName)
                    && !string.IsNullOrEmpty(name)
                    && string.Equals(_sessionFileName, name, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { /* ignore */ }

            // Fallback: same grown value as our session display / last after.
            float bust = file.custom.body.shapeValueBody[BustSizeIndex];
            if (_lastAfter >= 0f && Mathf.Abs(bust - _lastAfter) < 0.01f && bust > _baseline + 0.005f)
                return true;
            return false;
        }

        private static bool ForceFileAndVisualToBaseline(ChaControl cha, string reason)
        {
            float fileBefore = ReadFileBust(cha);
            float displayBefore = _displayBust;
            // Safety: if anything wrote fileBody, put card buffer back.
            if (fileBefore >= 0f && Mathf.Abs(fileBefore - _baseline) > 0.001f)
                cha.SetShapeBodyValue(BustSizeIndex, _baseline);
            ApplyVisualBust(cha, _baseline);
            _displayBust = _baseline;
            _lastBefore = displayBefore >= 0f ? displayBefore : fileBefore;
            _lastAfter = _baseline;
            _justRestored = true;
            HS2OrbitAndExciter.Log?.LogInfo(
                $"Orbit: 胸部回復（{reason}） 顯示{_lastBefore * 100f:F0}%→基{_baseline * 100f:F0}%");
            return true;
        }

        /// <summary>
        /// Change visible bust without writing <c>fileBody.shapeValueBody</c> (card buffer).
        /// Mirrors the visual half of <see cref="ChaControl.SetShapeBodyValue"/>.
        /// </summary>
        private static void ApplyVisualBust(ChaControl cha, float value)
        {
            float value2 = Mathf.Clamp01(value);
            try
            {
                var t = Traverse.Create(cha);
                object? sib = null;
                if (t.Field("sibBody").FieldExists())
                    sib = t.Field("sibBody").GetValue();
                if (sib != null)
                {
                    var st = Traverse.Create(sib);
                    bool initEnd = true;
                    try
                    {
                        if (st.Property("InitEnd").PropertyExists())
                            initEnd = st.Property("InitEnd").GetValue<bool>();
                        else if (st.Field("InitEnd").FieldExists())
                            initEnd = st.Field("InitEnd").GetValue<bool>();
                    }
                    catch { /* assume ready */ }
                    if (initEnd)
                        st.Method("ChangeValue", BustSizeIndex, value2).GetValue();
                }
                TrySetBool(t, "updateShapeBody", true);
                TrySetBool(t, "updateBustSize", true);
                TrySetBool(t, "reSetupDynamicBoneBust", true);
            }
            catch (System.Exception ex)
            {
                HS2OrbitAndExciter.Log?.LogWarning($"Orbit: 胸部視覺套用失敗，回退 SetShapeBodyValue: {ex.Message}");
                // Last resort — prefer visible growth over silent no-op; save hooks must scrub fileBody.
                cha.SetShapeBodyValue(BustSizeIndex, value2);
            }
        }

        private static void TrySetBool(Traverse t, string name, bool value)
        {
            try
            {
                if (t.Field(name).FieldExists())
                    t.Field(name).SetValue(value);
            }
            catch { /* ignore */ }
        }

        private static float ReadFileBust(ChaControl? cha)
        {
            if (cha?.fileBody?.shapeValueBody == null)
                return -1f;
            if (BustSizeIndex >= cha.fileBody.shapeValueBody.Length)
                return -1f;
            return cha.fileBody.shapeValueBody[BustSizeIndex];
        }

        internal static void ResetHud()
        {
            _baseline = -1f;
            _hasBaseline = false;
            _lastBefore = -1f;
            _lastAfter = -1f;
            _justRestored = false;
            _sessionCha = null;
            _sessionFileName = "";
            _displayBust = -1f;
            _reapplyAfterSave = -1f;
        }
    }
}
