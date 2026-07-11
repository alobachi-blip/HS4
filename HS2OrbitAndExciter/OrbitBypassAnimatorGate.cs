using AIChara;
using UnityEngine;

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
            // Idle / AfterIdle only after RequestMotionEscape (see TryInjectOrbitWheelBypass).
            "Idle", "D_Idle", "WIdle", "SIdle", "Insert", "D_Insert",
            "Orgasm_A", "Orgasm_IN_A", "Orgasm_OUT_A", "Drink_A", "Vomit_A", "OrgasmM_OUT_A",
            "D_Orgasm_A", "D_Orgasm_OUT_A", "D_Orgasm_IN_A", "D_OrgasmM_OUT_A"
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
            var animBody = OrbitHelpers.TryGetFemaleAnimBody(cha);
            if (animBody == null) return false;
            var stateInfo = animBody.GetCurrentAnimatorStateInfo(0);
            foreach (var name in AllowedStateNames)
            {
                if (stateInfo.IsName(name))
                    return true;
            }
            return false;
        }
    }
}
