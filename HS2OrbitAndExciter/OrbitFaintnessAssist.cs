using System.Reflection;
using HarmonyLib;
using Manager;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// §22：脫力次數門檻＝6；協助開啟時忽略遊戲「弱體化停止」(WeakStop)。
    /// </summary>
    internal static class OrbitFaintnessAssist
    {
        internal const int TargetGotoFaintnessCount = 6;

        private static bool _weakStopSaved;
        private static bool _weakStopWasOn;

        internal static void ApplyOnAssistStart()
        {
            ApplyGotoFaintnessCount();
            IgnoreWeakStopWhileAssist();
        }

        internal static void RestoreOnAssistStop()
        {
            RestoreWeakStop();
        }

        internal static void ApplyGotoFaintnessCount()
        {
            var hScene = OrbitController.TryGetHScene();
            var ctrl = hScene?.ctrlFlag;
            if (ctrl == null)
                return;

            try
            {
                var fi = typeof(HSceneFlagCtrl).GetField(
                    "gotoFaintnessCount",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fi == null)
                    return;
                // readonly 實例欄位仍可用反射寫入
                fi.SetValue(ctrl, TargetGotoFaintnessCount);
                HS2OrbitAndExciter.Log?.LogInfo(
                    $"Orbit: 脫力次數門檻改為 {TargetGotoFaintnessCount}（契約 §22）");
            }
            catch (System.Exception ex)
            {
                HS2OrbitAndExciter.Log?.LogWarning($"Orbit: 寫入脫力次數失敗: {ex.Message}");
            }
        }

        private static void IgnoreWeakStopWhileAssist()
        {
            try
            {
                var hData = Manager.Config.HData;
                if (hData == null)
                    return;
                if (!_weakStopSaved)
                {
                    _weakStopWasOn = hData.WeakStop;
                    _weakStopSaved = true;
                }
                if (hData.WeakStop)
                {
                    hData.WeakStop = false;
                    HS2OrbitAndExciter.Log?.LogInfo(
                        "Orbit: 協助開啟期間暫時關閉「弱體化停止」（契約 22-甲；結束協助後還原）");
                }
            }
            catch (System.Exception ex)
            {
                HS2OrbitAndExciter.Log?.LogWarning($"Orbit: 處理弱體化停止失敗: {ex.Message}");
            }
        }

        private static void RestoreWeakStop()
        {
            if (!_weakStopSaved)
                return;
            try
            {
                if (Manager.Config.HData != null)
                    Manager.Config.HData.WeakStop = _weakStopWasOn;
            }
            catch { /* ignore */ }
            _weakStopSaved = false;
        }
    }
}
