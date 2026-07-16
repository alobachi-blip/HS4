using AIChara;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// Each female orgasm multiplies BustSize (shape index 1) by (1 + percent/100), clamped to 0–1.
    /// Baseline is captured on H enter / G swap; restore with B (or settings button).
    /// </summary>
    internal static class OrbitOrgasmBustGrowth
    {
        private const int BustSizeIndex = (int)ChaFileDefine.BodyShapeIdx.BustSize;

        private static float _baseline = -1f;
        private static bool _hasBaseline;
        private static float _lastBefore = -1f;
        private static float _lastAfter = -1f;
        private static bool _justRestored;

        internal static bool Enabled => HS2OrbitAndExciter.OrgasmBustGrowEnabled?.Value ?? true;

        internal static float GrowPercent
        {
            get
            {
                float v = HS2OrbitAndExciter.OrgasmBustGrowPercent?.Value ?? 15f;
                return Mathf.Clamp(v, 0f, 100f);
            }
        }

        /// <summary>HUD fragment, e.g. 胸55→63% or 胸回復55%.</summary>
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

        /// <summary>Remember current bust as restore target (H enter / after G swap).</summary>
        internal static void CaptureBaseline(ChaControl? cha)
        {
            if (cha == null || cha.fileBody?.shapeValueBody == null)
                return;
            if (BustSizeIndex >= cha.fileBody.shapeValueBody.Length)
                return;

            _baseline = cha.GetShapeBodyValue(BustSizeIndex);
            _hasBaseline = true;
            _justRestored = false;
            _lastBefore = -1f;
            _lastAfter = -1f;
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

            float before = cha.GetShapeBodyValue(BustSizeIndex);
            float after = Mathf.Clamp01(before * (1f + GrowPercent / 100f));
            // Near-zero bust: multiplicative stays tiny; nudge by absolute percent of full scale once.
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

            cha.SetShapeBodyValue(BustSizeIndex, after);
            _lastBefore = before;
            _lastAfter = after;
            HS2OrbitAndExciter.Log?.LogInfo(
                $"Orbit: 高潮胸部 +{GrowPercent:F0}%  {before * 100f:F0}%→{after * 100f:F0}%（基{_baseline * 100f:F0}%）");
        }

        /// <summary>Restore bust to baseline captured at H enter / G swap. Hotkey B.</summary>
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

            float current = cha.GetShapeBodyValue(BustSizeIndex);
            cha.SetShapeBodyValue(BustSizeIndex, _baseline);
            _lastBefore = current;
            _lastAfter = _baseline;
            _justRestored = true;
            HS2OrbitAndExciter.Log?.LogInfo(
                $"Orbit: B 胸部回復 {current * 100f:F0}%→{_baseline * 100f:F0}%");
            return true;
        }

        internal static void ResetHud()
        {
            _baseline = -1f;
            _hasBaseline = false;
            _lastBefore = -1f;
            _lastAfter = -1f;
            _justRestored = false;
        }
    }
}
