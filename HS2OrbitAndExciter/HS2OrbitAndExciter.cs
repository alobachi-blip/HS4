using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class HS2OrbitAndExciter : BaseUnityPlugin
    {
        internal static ManualLogSource? Log;

        internal static ConfigEntry<float>? OrbitTimePer360;
        internal static ConfigEntry<float>? ExcitementTriggerDelaySeconds;
        /// <summary>When orbit is active, add this much to feel_f per second (0 = no auto accumulation).</summary>
        internal static ConfigEntry<float>? FeelAddPerSecondWhenOrbit;
        internal static ConfigEntry<int>? OrbitCountBeforeRandom;
        internal static ConfigEntry<int>? OrbitCountBeforePoseChange;
        internal static ConfigEntry<bool>? ChangePoseOnCycle;
        internal static ConfigEntry<bool>? ClothesChangeEnabled;
        /// <summary>When orbit is on: enable game auto action (isAutoActionChange + initiative) so user rarely needs to operate.</summary>
        internal static ConfigEntry<bool>? OrbitAutoActionEnabled;
        /// <summary>Use IsFinishVisible + ctrlFlag.click for B/C finish paths during Orbit assist.</summary>
        internal static ConfigEntry<bool>? EnableOrbitFinishDirector;
        /// <summary>Drive per-pose H-loop session script: W/S pacing and IN_A Pull.</summary>
        internal static ConfigEntry<bool>? EnableOrbitSessionDirector;
        /// <summary>When orbit is on and stuck at checkpoint (Idle, no selection): auto-advance after this many seconds (0 = use game auto only).</summary>
        internal static ConfigEntry<float>? OrbitCheckpointTimeoutSeconds;
        /// <summary>Minimum unscaled seconds between orbit assist pushes (flag write and checkpoint invoke).</summary>
        internal static ConfigEntry<float>? AutoAssistMinIntervalSeconds;
        /// <summary>Fallback switch for per-Proc AfterProc assist. Default off: rely on late-assist path only.</summary>
        internal static ConfigEntry<bool>? EnableAfterProcAssistPostfixFallback;
        /// <summary>Orbit camera distance = body height × this (head focus).</summary>
        internal static ConfigEntry<float>? OrbitDistanceHead;
        /// <summary>Orbit camera distance = body height × this (chest focus).</summary>
        internal static ConfigEntry<float>? OrbitDistanceChest;
        /// <summary>Orbit camera distance = body height × this (pelvis focus).</summary>
        internal static ConfigEntry<float>? OrbitDistancePelvis;
        /// <summary>每圈是否亂數拉近／拉遠（相對焦點距離）。</summary>
        internal static ConfigEntry<bool>? OrbitCircleZoomEnabled;
        /// <summary>每圈拉近倍率下限（相對焦點距離；越小越近）。</summary>
        internal static ConfigEntry<float>? OrbitZoomNearMult;
        /// <summary>每圈拉遠倍率上限（相對焦點距離；越大越遠）。</summary>
        internal static ConfigEntry<float>? OrbitZoomFarMult;
        /// <summary>Override faintness state in H scene (HScene.ctrlFlag.isFaintness). When toggled, pose list and camera view update.</summary>
        internal static ConfigEntry<bool>? OverrideFaintness;
        /// <summary>Relax safe pose gates while preserving actor/event/place/AppendEV hard limits.</summary>
        internal static ConfigEntry<bool>? EnableSafePoseUnlock;
        /// <summary>When false, the compact orbit status HUD is disabled entirely.</summary>
        internal static ConfigEntry<bool>? OrbitStatusHudEnabled;
        /// <summary>When true, each female orgasm spawns a st_paint tattoo decal on a body attach point (thigh→face). Toggle with T in H.</summary>
        internal static ConfigEntry<bool>? OrgasmTattooEnabled;
        /// <summary>Max accumulated orgasm tattoo decals (1–64).</summary>
        internal static ConfigEntry<int>? OrgasmTattooMaxCount;
        /// <summary>Minimum size multiplier for orgasm tattoo decals.</summary>
        internal static ConfigEntry<float>? OrgasmTattooScaleMin;
        /// <summary>Maximum size multiplier for orgasm tattoo decals.</summary>
        internal static ConfigEntry<float>? OrgasmTattooScaleMax;
        /// <summary>When true, each female orgasm grows BustSize by OrgasmBustGrowPercent.</summary>
        internal static ConfigEntry<bool>? OrgasmBustGrowEnabled;
        /// <summary>Relative bust size increase per orgasm (percent), e.g. 15 = ×1.15.</summary>
        internal static ConfigEntry<float>? OrgasmBustGrowPercent;
        /// <summary>When true, each female orgasm sprays urine/潮吹 from both nipples.</summary>
        internal static ConfigEntry<bool>? OrgasmNippleSprayEnabled;
        /// <summary>True = Play(-1,height) urine splitInfos; false = custom Bursts/Speed/Amount.</summary>
        internal static ConfigEntry<bool>? OrgasmNippleSprayUseNativeUrineRhythm;
        internal static ConfigEntry<float>? OrgasmNippleSprayOffsetX;
        internal static ConfigEntry<float>? OrgasmNippleSprayOffsetY;
        internal static ConfigEntry<float>? OrgasmNippleSprayOffsetZ;
        internal static ConfigEntry<float>? OrgasmNippleSprayRotX;
        internal static ConfigEntry<float>? OrgasmNippleSprayRotY;
        internal static ConfigEntry<float>? OrgasmNippleSprayRotZ;
        /// <summary>Custom-rhythm pulse count (when UseNativeUrineRhythm is false).</summary>
        internal static ConfigEntry<int>? OrgasmNippleSprayBursts;
        /// <summary>Seconds between custom pulses.</summary>
        internal static ConfigEntry<float>? OrgasmNippleSprayBurstInterval;
        /// <summary>First custom pulse speed vs base.</summary>
        internal static ConfigEntry<float>? OrgasmNippleSpraySpeedStart;
        /// <summary>Last custom pulse speed vs base.</summary>
        internal static ConfigEntry<float>? OrgasmNippleSpraySpeedEnd;
        /// <summary>Overall custom-rhythm volume (1 = default).</summary>
        internal static ConfigEntry<float>? OrgasmNippleSprayAmount;
        /// <summary>First custom pulse volume weight.</summary>
        internal static ConfigEntry<float>? OrgasmNippleSprayAmountStart;
        /// <summary>Last custom pulse volume weight.</summary>
        internal static ConfigEntry<float>? OrgasmNippleSprayAmountEnd;
        /// <summary>When true, inside finish grows PregnancyPlus H-scene belly (cumflation).</summary>
        internal static ConfigEntry<bool>? CumflationEnabled;
        /// <summary>Advance H voice banks by orgasm / houshi male finish (session overlay; card stats unchanged).</summary>
        internal static ConfigEntry<bool>? VoiceTourEnabled;
        /// <summary>Hits (female orgasm or houshi male finish) required per voice stage.</summary>
        internal static ConfigEntry<int>? VoiceTourHitsPerStage;
        /// <summary>After last stage, wrap to Blank shy again.</summary>
        internal static ConfigEntry<bool>? VoiceTourLoop;
        /// <summary>Remember stage per character across H / game restart.</summary>
        internal static ConfigEntry<bool>? VoiceTourPersistProgress;
        /// <summary>If true, every new H starts at stage 0 (ignores saved progress for that enter).</summary>
        internal static ConfigEntry<bool>? VoiceTourResetOnNewH;
        /// <summary>Count houshi outside/drink male finish as a voice-tour hit.</summary>
        internal static ConfigEntry<bool>? VoiceTourCountHoushiMaleFinish;
        /// <summary>Write detailed NDJSON state-machine traces for smoke/regression runs.</summary>
        internal static ConfigEntry<bool>? EnableStateMachineTrace;
        internal static ConfigEntry<bool>? EnableOcclusion20CircleTest;
        /// <summary>Smoke-only shortcut: jump directly into HScene after startup.</summary>
        internal static ConfigEntry<bool>? EnableDirectHSmokeDriver;
        internal static ConfigEntry<float>? DirectHSmokeDelaySeconds;
        internal static ConfigEntry<int>? DirectHSmokeMapId;
        internal static ConfigEntry<int>? DirectHSmokeEventNo;
        internal static ConfigEntry<string>? DirectHSmokeFemaleCardPath;
        internal static ConfigEntry<string>? DirectHSmokeSecondFemaleCardPath;
        internal static ConfigEntry<string>? DirectHSmokeMaleCardPath;
        internal static ConfigEntry<bool>? EnableDirectHSmokeOrbitAssist;
        internal static ConfigEntry<bool>? EnableSmokeKeyframeScreenshots;
        internal static ConfigEntry<string>? SmokeKeyframeDirectory;
        internal static ConfigEntry<bool>? EnableSmokeFamilyCoverage;
        internal static ConfigEntry<string>? SmokeFamilyCoverageSequence;
        /// <summary>When true, orbit records a local Storyboard Package v1 under the configured HS4 output root.</summary>
        internal static ConfigEntry<bool>? StoryboardPackageEnabled;
        internal static ConfigEntry<string>? StoryboardPackageOutputRoot;
        internal static ConfigEntry<float>? StoryboardShotDurationSeconds;
        internal static ConfigEntry<bool>? StoryboardCaptureEndFrame;
        internal static ConfigEntry<int>? StoryboardFps;
        internal static ConfigEntry<string>? StoryboardModelTarget;
        internal static ConfigEntry<bool>? StoryboardSafeCameraEnabled;
        internal static ConfigEntry<float>? StoryboardMaxOrbitDegreesPerShot;
        internal static ConfigEntry<bool>? StoryboardRawSequenceEnabled;
        internal static ConfigEntry<float>? StoryboardRawSequenceSeconds;

        private static void PatchSafe(Harmony harmony, System.Type patchType)
        {
            try { harmony.PatchAll(patchType); }
            catch (System.Exception ex) { Log?.LogWarning($"[{PluginInfo.PLUGIN_NAME}] Patch skipped {patchType?.Name}: {ex.Message}"); }
        }

        private void Awake()
        {
            Log = Logger;

            OrbitTimePer360 = Config.Bind("Orbit", "OrbitTimePer360", 10f,
                "Seconds for one 360° camera leg in one direction (one rotation). One full round-trip (out+back) ≈ 2× this value.");
            OrbitCountBeforeRandom = Config.Bind("Orbit", "OrbitCountBeforeRandom", 2,
                "After this many rotations (each 360° leg), randomize body focus and horizontal angle preset. 0 = no random (clothes then advance once per round-trip only). 1 round-trip = 2 rotations.");
            OrbitCountBeforePoseChange = Config.Bind("Orbit", "OrbitCountBeforePoseChange", 2,
                "After this many full round-trips (out+back), change pose when ChangePoseOnCycle is true.");
            ChangePoseOnCycle = Config.Bind("Orbit", "ChangePoseOnCycle", false,
                "When true, change pose every OrbitCountBeforePoseChange round-trips.");
            ClothesChangeEnabled = Config.Bind("Orbit", "ClothesChangeEnabled", false,
                "Advance clothes stage on the same schedule as OrbitCountBeforeRandom rotations; if that count is 0, once per round-trip instead.");
            ExcitementTriggerDelaySeconds = Config.Bind("Exciter", "ExcitementTriggerDelaySeconds", 0f,
                "Seconds at full gauge before auto trigger (0 = immediate, range 0–10). Mouse click still triggers immediately.");
            FeelAddPerSecondWhenOrbit = Config.Bind("Exciter", "FeelAddPerSecondWhenOrbit", 0.1f,
                "When orbit (Ctrl+Shift+O) is active, add this much to excitement gauge per second (0 = only game default / mouse). 0.1 = fill in 10 s.");
            OrbitAutoActionEnabled = Config.Bind("Orbit", "OrbitAutoActionEnabled", true,
                "When orbit is on: enable game auto action so next pose/action is chosen automatically (user rarely needs to operate).");
            EnableOrbitFinishDirector = Config.Bind("HLoop", "EnableOrbitFinishDirector", true,
                "Slice 3: during Orbit assist, pick B/C finish paths with Orbit ledger and set ctrlFlag.click instead of setPlay.");
            EnableOrbitSessionDirector = Config.Bind("HLoop", "EnableOrbitSessionDirector", true,
                "Slice 4: during Orbit assist, drive W/S pacing and force Orgasm_IN_A down-wheel Pull -> Drop.");
            OrbitCheckpointTimeoutSeconds = Config.Bind("Orbit", "OrbitCheckpointTimeoutSeconds", 5f,
                "When orbit is on and stuck at checkpoint (Idle, no selection): auto-advance after this many seconds. 0 = only use game auto, no forced advance.");
            AutoAssistMinIntervalSeconds = Config.Bind("Orbit", "AutoAssistMinIntervalSeconds", 2.5f,
                "Minimum unscaled seconds between auto-assist pushes (isAutoActionChange/initiative and checkpoint invoke). 0 = legacy aggressive behavior.");
            EnableAfterProcAssistPostfixFallback = Config.Bind("Orbit", "EnableAfterProcAssistPostfixFallback", false,
                "Fallback: run per-Proc AfterProc assist patch. Default false (late-assist only). Enable only if auto-advance stalls in your setup.");
            OrbitDistanceHead = Config.Bind("Orbit", "OrbitDistanceHead", 1.4f,
                new ConfigDescription("Body-focus orbit: distance multiplier vs character height (0 = extreme close-up; runtime floor ~0.02).", new AcceptableValueRange<float>(0f, 3f)));
            OrbitDistanceChest = Config.Bind("Orbit", "OrbitDistanceChest", 1.4f,
                new ConfigDescription("Body-focus orbit: distance multiplier vs character height (0 = extreme close-up; runtime floor ~0.02).", new AcceptableValueRange<float>(0f, 3f)));
            OrbitDistancePelvis = Config.Bind("Orbit", "OrbitDistancePelvis", 1.4f,
                new ConfigDescription("Body-focus orbit: distance multiplier vs character height (0 = extreme close-up; runtime floor ~0.02).", new AcceptableValueRange<float>(0f, 3f)));
            OrbitCircleZoomEnabled = Config.Bind("Orbit", "OrbitCircleZoomEnabled", true,
                "When true, each rotation picks a random zoom between Near and Far multipliers (clear push-in / pull-back).");
            OrbitZoomNearMult = Config.Bind("Orbit", "OrbitZoomNearMult", 0.65f,
                new ConfigDescription("Per-circle zoom-in multiplier vs focus distance (0 = extreme close-up; if Near>Far they are swapped).", new AcceptableValueRange<float>(0f, 3f)));
            OrbitZoomFarMult = Config.Bind("Orbit", "OrbitZoomFarMult", 1.75f,
                new ConfigDescription("Per-circle zoom-out multiplier vs focus distance (larger = farther; if Near>Far they are swapped).", new AcceptableValueRange<float>(0f, 3f)));
            OverrideFaintness = Config.Bind("State", "OverrideFaintness", false,
                "In H scene: force faintness state on/off (ctrlFlag.isFaintness). Affects pose list and triggers camera reapply when orbit is on.");
            EnableSafePoseUnlock = Config.Bind("State", "EnableSafePoseUnlock", true,
                "Slice 0: relax safe pose gates (state/achievement/pain/faintness) while preserving actor/event/place/AppendEV limits.");
            OrbitStatusHudEnabled = Config.Bind("Orbit", "OrbitStatusHudEnabled", true,
                "Enable compact orbit status HUD (bottom-left, Traditional Chinese). Toggle with P or Ctrl+Shift+I while orbit is on.");
            OrgasmTattooEnabled = Config.Bind("Orbit", "OrgasmTattooEnabled", true,
                "When true, each female orgasm adds a st_paint tattoo. In H: T enables + places next stamp; Shift+T disables.");
            OrgasmTattooMaxCount = Config.Bind("Orbit", "OrgasmTattooMaxCount", 24,
                new ConfigDescription("Max orgasm tattoo decals before oldest is removed.", new AcceptableValueRange<int>(1, 64)));
            OrgasmTattooScaleMin = Config.Bind("Orbit", "OrgasmTattooScaleMin", 2.5f,
                new ConfigDescription("Minimum size multiplier for tattoo decals.", new AcceptableValueRange<float>(1f, 20f)));
            OrgasmTattooScaleMax = Config.Bind("Orbit", "OrgasmTattooScaleMax", 4.5f,
                new ConfigDescription("Maximum size multiplier for tattoo decals.", new AcceptableValueRange<float>(1f, 20f)));
            OrgasmBustGrowEnabled = Config.Bind("Orbit", "OrgasmBustGrowEnabled", true,
                "When true, each female orgasm multiplies BustSize (胸サイズ) by (1 + percent/100), clamped to 0–1.");
            OrgasmBustGrowPercent = Config.Bind("Orbit", "OrgasmBustGrowPercent", 15f,
                new ConfigDescription("Relative bust growth per orgasm (percent). 15 = +15% of current size.", new AcceptableValueRange<float>(0f, 100f)));
            OrgasmNippleSprayEnabled = Config.Bind("Orbit", "OrgasmNippleSprayEnabled", true,
                "When true, each female orgasm sprays from both nipples using female urine/潮吹 Obi (or urine particles).");
            OrgasmNippleSprayUseNativeUrineRhythm = Config.Bind("Orbit", "OrgasmNippleSprayUseNativeUrineRhythm", false,
                "When true, use urine EmitterPtn splitInfos via Play(-1,height). Default false: custom Bursts with strong-first fade (Speed/Amount start→end).");
            OrgasmNippleSprayOffsetX = Config.Bind("Orbit", "OrgasmNippleSprayOffsetX", 0f,
                new ConfigDescription("Nipple spray local position X.", new AcceptableValueRange<float>(-0.2f, 0.2f)));
            OrgasmNippleSprayOffsetY = Config.Bind("Orbit", "OrgasmNippleSprayOffsetY", 0f,
                new ConfigDescription("Nipple spray local position Y.", new AcceptableValueRange<float>(-0.2f, 0.2f)));
            OrgasmNippleSprayOffsetZ = Config.Bind("Orbit", "OrgasmNippleSprayOffsetZ", 0.02f,
                new ConfigDescription("Nipple spray local position Z.", new AcceptableValueRange<float>(-0.2f, 0.2f)));
            OrgasmNippleSprayRotX = Config.Bind("Orbit", "OrgasmNippleSprayRotX", 90f,
                new ConfigDescription("Nipple spray local euler X (degrees).", new AcceptableValueRange<float>(-180f, 180f)));
            OrgasmNippleSprayRotY = Config.Bind("Orbit", "OrgasmNippleSprayRotY", 0f,
                new ConfigDescription("Nipple spray local euler Y (degrees).", new AcceptableValueRange<float>(-180f, 180f)));
            OrgasmNippleSprayRotZ = Config.Bind("Orbit", "OrgasmNippleSprayRotZ", 0f,
                new ConfigDescription("Nipple spray local euler Z (degrees).", new AcceptableValueRange<float>(-180f, 180f)));
            OrgasmNippleSprayBursts = Config.Bind("Orbit", "OrgasmNippleSprayBursts", 5,
                new ConfigDescription("Custom-rhythm pulse count (when UseNativeUrineRhythm is false).", new AcceptableValueRange<int>(2, 20)));
            OrgasmNippleSprayBurstInterval = Config.Bind("Orbit", "OrgasmNippleSprayBurstInterval", 0.35f,
                new ConfigDescription("Seconds between custom pulses.", new AcceptableValueRange<float>(0.1f, 1.5f)));
            OrgasmNippleSpraySpeedStart = Config.Bind("Orbit", "OrgasmNippleSpraySpeedStart", 1.8f,
                new ConfigDescription("First custom pulse speed vs base.", new AcceptableValueRange<float>(0.5f, 4f)));
            OrgasmNippleSpraySpeedEnd = Config.Bind("Orbit", "OrgasmNippleSpraySpeedEnd", 0.4f,
                new ConfigDescription("Last custom pulse speed vs base.", new AcceptableValueRange<float>(0.05f, 2f)));
            OrgasmNippleSprayAmount = Config.Bind("Orbit", "OrgasmNippleSprayAmount", 1f,
                new ConfigDescription("Overall custom-rhythm volume (1 = default).", new AcceptableValueRange<float>(0.2f, 24f)));
            OrgasmNippleSprayAmountStart = Config.Bind("Orbit", "OrgasmNippleSprayAmountStart", 1.5f,
                new ConfigDescription("First custom pulse volume weight.", new AcceptableValueRange<float>(0.2f, 24f)));
            OrgasmNippleSprayAmountEnd = Config.Bind("Orbit", "OrgasmNippleSprayAmountEnd", 0.5f,
                new ConfigDescription("Last custom pulse volume weight.", new AcceptableValueRange<float>(0.1f, 15f)));
            CumflationEnabled = Config.Bind("Orbit", "CumflationEnabled", true,
                "When true, each inside finish grows PregnancyPlus H-scene belly one level (HS2Inflation). I clears.");
            VoiceTourEnabled = Config.Bind("VoiceTour", "VoiceTourEnabled", true,
                "H voice tour: cycle Blank→Favor→…→Dependence→Broken by orgasm/houshi finish. Does not write card Favor/etc.");
            VoiceTourHitsPerStage = Config.Bind("VoiceTour", "VoiceTourHitsPerStage", 1,
                new ConfigDescription("Hits per stage (female orgasm or houshi male finish).", new AcceptableValueRange<int>(1, 10)));
            VoiceTourLoop = Config.Bind("VoiceTour", "VoiceTourLoop", true,
                "After Broken stage, loop back to Blank shy.");
            VoiceTourPersistProgress = Config.Bind("VoiceTour", "VoiceTourPersistProgress", true,
                "Remember stage per character (JSON under BepInEx/config). Switch away and back resumes.");
            VoiceTourResetOnNewH = Config.Bind("VoiceTour", "VoiceTourResetOnNewH", false,
                "If true, each H enter starts at stage 0 for that character.");
            VoiceTourCountHoushiMaleFinish = Config.Bind("VoiceTour", "VoiceTourCountHoushiMaleFinish", true,
                "Count houshi outside/drink male finish as one hit. Insertion inside (numInside) always counts.");
            PregnancyPlusAssist.TryRaiseMaxInflationLevel();
            EnableStateMachineTrace = Config.Bind("Diagnostics", "EnableStateMachineTrace", false,
                "Write detailed NDJSON state-machine traces. Default false; enable only for automated diagnosis runs.");
            EnableOcclusion20CircleTest = Config.Bind("Diagnostics", "EnableOcclusion20CircleTest", false,
                "Run exactly 20 orbit rotations, log visual occlusion evidence, then stop the camera. Requires state trace logging.");
            EnableDirectHSmokeDriver = Config.Bind("Smoke", "EnableDirectHSmokeDriver", false,
                "Smoke test only: jump directly into HScene after startup. Default false.");
            DirectHSmokeDelaySeconds = Config.Bind("Smoke", "DirectHSmokeDelaySeconds", 8f,
                "Seconds to wait after plugin load before the direct-H smoke jump starts trying.");
            DirectHSmokeMapId = Config.Bind("Smoke", "DirectHSmokeMapId", 3,
                "Map id for the direct-H smoke jump. 3 is a room-style default.");
            DirectHSmokeEventNo = Config.Bind("Smoke", "DirectHSmokeEventNo", -1,
                "EventNo for the direct-H smoke jump. -1 uses ordinary/free H flow.");
            DirectHSmokeFemaleCardPath = Config.Bind("Smoke", "DirectHSmokeFemaleCardPath", "",
                "Optional female card path for direct-H smoke. Empty lets HS2 create its default female.");
            DirectHSmokeSecondFemaleCardPath = Config.Bind("Smoke", "DirectHSmokeSecondFemaleCardPath", "",
                "Optional second female card path for direct-H smoke.");
            DirectHSmokeMaleCardPath = Config.Bind("Smoke", "DirectHSmokeMaleCardPath", "",
                "Optional male card path for direct-H smoke. Empty uses the saved player card when available.");
            EnableDirectHSmokeOrbitAssist = Config.Bind("Smoke", "EnableDirectHSmokeOrbitAssist", false,
                "Smoke test only: turn on Orbit assist after DirectH reaches an active H scene.");
            EnableSmokeKeyframeScreenshots = Config.Bind("Smoke", "EnableSmokeKeyframeScreenshots", false,
                "Smoke test only: capture keyframe screenshots during DirectH smoke runs.");
            SmokeKeyframeDirectory = Config.Bind("Smoke", "SmokeKeyframeDirectory", "",
                "Directory for smoke keyframe screenshots. Empty uses BepInEx/LogOutput/OrbitSmokeKeyframes.");
            EnableSmokeFamilyCoverage = Config.Bind("Smoke", "EnableSmokeFamilyCoverage", false,
                "Smoke test only: make Orbit pose selection cycle through configured H-loop families for coverage.");
            SmokeFamilyCoverageSequence = Config.Bind("Smoke", "SmokeFamilyCoverageSequence",
                "A_Aibu,B_Houshi,C_Sonyu,D_Masturbation,E_Spnking,A_Les",
                "Comma-separated family sequence used when EnableSmokeFamilyCoverage is true.");
            StoryboardPackageEnabled = Config.Bind("StoryboardPackage", "Enabled", false,
                "When true, orbit records Storyboard Package v1 assets for local Wan2GP / ComfyUI / FramePack use.");
            StoryboardPackageOutputRoot = Config.Bind("StoryboardPackage", "OutputRoot", "D:\\HS4\\Output\\OrbitSourcePackages",
                "Output root for Storyboard Package v1. Must stay outside the HS2 game/BepInEx folders.");
            StoryboardShotDurationSeconds = Config.Bind("StoryboardPackage", "ShotDurationSeconds", 4f,
                new ConfigDescription("Seconds per generated shot. Runtime clamps to 3..6 seconds.", new AcceptableValueRange<float>(3f, 6f)));
            StoryboardCaptureEndFrame = Config.Bind("StoryboardPackage", "CaptureEndFrame", true,
                "When true, also capture an end frame for each shot.");
            StoryboardFps = Config.Bind("StoryboardPackage", "Fps", 24,
                new ConfigDescription("Target FPS written to metadata/job files.", new AcceptableValueRange<int>(12, 60)));
            StoryboardModelTarget = Config.Bind("StoryboardPackage", "ModelTarget", "Wan2GP/ComfyUI/FramePack",
                "Metadata label for the intended local video generation target.");
            StoryboardSafeCameraEnabled = Config.Bind("StoryboardPackage", "SafeCameraEnabled", true,
                "When recording storyboard packages, use a horizon-locked world-vertical camera path instead of rolling around body axes.");
            StoryboardMaxOrbitDegreesPerShot = Config.Bind("StoryboardPackage", "MaxOrbitDegreesPerShot", 12f,
                new ConfigDescription("Storyboard safe camera cap: maximum camera orbit degrees per shot.", new AcceptableValueRange<float>(0f, 45f)));
            StoryboardRawSequenceEnabled = Config.Bind("StoryboardPackage", "RawSequenceEnabled", true,
                "When true, also captures a short in-game PNG sequence to frames_raw/source_%04d.png for EbSynth/deflicker tests.");
            StoryboardRawSequenceSeconds = Config.Bind("StoryboardPackage", "RawSequenceSeconds", 2f,
                new ConfigDescription("Seconds of raw PNG sequence capture per storyboard package session.", new AcceptableValueRange<float>(1f, 6f)));
            OrbitStateMachineLog.Boot();

            Patches.ExciterState.DelaySecondsAtFull = ExcitementTriggerDelaySeconds.Value;
            ExcitementTriggerDelaySeconds.SettingChanged += (_, __) => Patches.ExciterState.DelaySecondsAtFull = ExcitementTriggerDelaySeconds.Value;

            var harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            PatchSafe(harmony, typeof(Patches.PregnancyPlusInflationStepPatch));
            PatchSafe(harmony, typeof(Patches.OrgasmEffectsPatch));
            PatchSafe(harmony, typeof(Patches.BustGrowthLifecyclePatch));
            PatchSafe(harmony, typeof(Patches.BustGrowthSavePatch));
            PatchSafe(harmony, typeof(Patches.VoiceTourPhasePatch));
            PatchSafe(harmony, typeof(Patches.VoiceTourBreathFallbackPatch));
            PatchSafe(harmony, typeof(Patches.FeelHitPatches));
            PatchSafe(harmony, typeof(Patches.ExciterTranspiler_F2M1_OLoopAibuProc));
            PatchSafe(harmony, typeof(Patches.ExciterTranspiler_F2M1_OLoopSonyuProc));
            PatchSafe(harmony, typeof(Patches.ExciterTranspiler_F1M2_OLoopAibuProc));
            PatchSafe(harmony, typeof(Patches.ExciterTranspiler_F1M2_OLoopSonyuProc));
            PatchSafe(harmony, typeof(Patches.ExciterTranspiler_Spnking_ActionProc));
            PatchSafe(harmony, typeof(Patches.OrbitSpankingWheelPatch));
            PatchSafe(harmony, typeof(Patches.OrbitBypass_StartProcTrigger));
            PatchSafe(harmony, typeof(Patches.OrbitBypass_StartAibuProc));
            PatchSafe(harmony, typeof(Patches.OrbitBypass_StartHoushiProc));
            PatchSafe(harmony, typeof(Patches.OrbitBypass_FaintnessStartProcTrigger));
            PatchSafe(harmony, typeof(Patches.OrbitBypass_FaintnessStartAibuProc));
            PatchSafe(harmony, typeof(Patches.OrbitBypass_AfterTheInsideWaitingProc));
            PatchSafe(harmony, typeof(Patches.OrbitBypass_Masturbation_StartProcTrriger));
            PatchSafe(harmony, typeof(Patches.OrbitBypass_AutoStartProcTrigger));
            PatchSafe(harmony, typeof(Patches.OrbitBypass_AutoStartAibuProc));
            PatchSafe(harmony, typeof(Patches.OrbitBypass_AutoStartHoushiProc));
            PatchSafe(harmony, typeof(Patches.OrbitBypass_AutoStartSonyuProc));
            PatchSafe(harmony, typeof(Patches.OrbitBypass_StartSonyuProc));
            PatchSafe(harmony, typeof(Patches.OrbitBypass_AutoAfterTheInsideWaitingProc));
            PatchSafe(harmony, typeof(Patches.OrbitBypass1v1_Sonyu_StartProcTrigger));
            PatchSafe(harmony, typeof(Patches.OrbitBypass1v1_Sonyu_StartProc));
            PatchSafe(harmony, typeof(Patches.OrbitBypass1v1_Sonyu_AutoStartProcTrigger));
            PatchSafe(harmony, typeof(Patches.OrbitBypass1v1_Sonyu_AutoStartProc));
            PatchSafe(harmony, typeof(Patches.OrbitBypass1v1_Sonyu_AfterTheInsideWaitingProc));
            PatchSafe(harmony, typeof(Patches.OrbitBypass1v1_Sonyu_AutoAfterTheInsideWaitingProc));
            PatchSafe(harmony, typeof(Patches.OrbitAutoAfterIdleRestartPatch));
            PatchSafe(harmony, typeof(Patches.OrbitAutoIdleStartPatch));
            PatchSafe(harmony, typeof(Patches.OrbitForceSonyuAutoAfterIdlePatch));
            PatchSafe(harmony, typeof(Patches.OrbitForceSonyuManualAfterIdlePatch));
            PatchSafe(harmony, typeof(Patches.OrbitForceF2M1AutoAfterIdlePatch));
            PatchSafe(harmony, typeof(Patches.OrbitForceF1M2AutoAfterIdlePatch));
            PatchSafe(harmony, typeof(Patches.OrbitAutoPullPatch));
            PatchSafe(harmony, typeof(Patches.OrbitBypass1v1_Aibu_StartProcTrigger));
            PatchSafe(harmony, typeof(Patches.OrbitBypass1v1_Aibu_StartProc));
            PatchSafe(harmony, typeof(Patches.OrbitBypass1v1_Aibu_FaintnessStartProcTrigger));
            PatchSafe(harmony, typeof(Patches.OrbitBypass1v1_Aibu_FaintnessStartProc));
            PatchSafe(harmony, typeof(Patches.OrbitBypass1v1_Aibu_AutoStartProc));
            PatchSafe(harmony, typeof(Patches.OrbitBypass1v1_Houshi_StartProcTrigger));
            PatchSafe(harmony, typeof(Patches.OrbitBypass1v1_Houshi_StartProc));
            PatchSafe(harmony, typeof(Patches.OrbitBypass1v1_Houshi_AutoStartProcTrigger));
            PatchSafe(harmony, typeof(Patches.OrbitBypass1v1_Houshi_AutoStartProc));
            PatchSafe(harmony, typeof(Patches.OrbitPoseUnlockPatches));
            PatchSafe(harmony, typeof(Patches.OrbitAutoActionAfterProcPatches));
            PatchSafe(harmony, typeof(Patches.OrbitCameraVanishOnTriggerEnterPatch));
            PatchSafe(harmony, typeof(Patches.OrbitCameraVanishOnTriggerStayPatch));
            PatchSafe(harmony, typeof(Patches.OrbitCameraVanishOnTriggerExitPatch));
            PatchSafe(harmony, typeof(Patches.OrbitCameraRendererVanishPatch));
            // Masturbation/Les/Sonyu/Aibu 不載入（此遊戲 build 無對應方法，避免警告）
            var go = new GameObject("HS2OrbitAndExciterController");
            DontDestroyOnLoad(go);
            go.AddComponent<OrbitController>();
            go.AddComponent<OrbitHSceneLateAssist>();
            go.AddComponent<OrbitSettingsGUI>();
            go.AddComponent<OrbitStatusHud>();
            go.AddComponent<OrbitSmokeDriver>();
            Log.LogInfo($"{PluginInfo.PLUGIN_NAME} v{PluginInfo.PLUGIN_VERSION} loaded. Settings: Ctrl+Shift+P; status HUD: P / Ctrl+Shift+I; stop orbit: O; clear belly: I.");
        }
    }
}
