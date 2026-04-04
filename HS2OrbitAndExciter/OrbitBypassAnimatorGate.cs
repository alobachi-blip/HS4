using AIChara;
using HarmonyLib;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// Gates <see cref="Patches.OrbitBypassWheelState"/>: only inject fake wheel when the first female's animator layer 0
    /// matches H-scene "idle / insert / spanking idle" states — not WLoop/OLoop etc., so scroll wheel speed/Finish control stays intact.
    /// Aligns with vanilla <c>HScene.IsIdle</c> state names. See <c>HScene_Idle_States.md</c>.
    /// </summary>
    internal static class OrbitBypassAnimatorGate
    {
        private static readonly string[] AllowedStateNames =
        {
            "Idle", "D_Idle", "WIdle", "SIdle", "Insert", "D_Insert"
        };

        /// <summary>True if current H scene exists and first female animator is in an allowed state for wheel bypass.</summary>
        public static bool IsBypassAllowedForCurrentHScene()
        {
            var hScene = OrbitController.TryGetHScene();
            return hScene != null && IsFirstFemaleInAllowedState(hScene);
        }

        private static bool IsFirstFemaleInAllowedState(HScene hScene)
        {
            var chaFemales = OrbitHelpers.GetChaFemales(hScene);
            if (chaFemales == null || chaFemales.Length == 0) return false;
            var cha = chaFemales[0];
            if (cha == null) return false;
            var animBody = Traverse.Create(cha).Field("animBody").GetValue();
            if (animBody == null) return false;
            var animType = animBody.GetType();
            var getState = animType.GetMethod("GetCurrentAnimatorStateInfo", new[] { typeof(int) });
            if (getState == null) return false;
            var state = getState.Invoke(animBody, new object[] { 0 });
            if (state == null) return false;
            var isName = state.GetType().GetMethod("IsName", new[] { typeof(string) });
            if (isName == null) return false;
            foreach (var name in AllowedStateNames)
            {
                try
                {
                    if ((bool)isName.Invoke(state, new object[] { name }))
                        return true;
                }
                catch { }
            }
            return false;
        }
    }
}
