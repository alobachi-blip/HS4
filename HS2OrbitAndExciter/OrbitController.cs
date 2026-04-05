using System.Collections.Generic;
using System.Reflection;
using AIChara;
using HarmonyLib;
using Manager;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// Drives orbit camera in H scene: hotkey Ctrl+Shift+O, 360° then reverse; optional random focus/angle, pose change, clothes.
    /// Runs LateUpdate before <see cref="CameraControl_Ver2"/> (default 0) so yaw is written to CamDat.Rot and CameraUpdate() applies transBase correctly.
    /// H-scene flag assist (auto action / checkpoint) runs in <see cref="OrbitHSceneLateAssist"/> after game proc.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class OrbitController : MonoBehaviour
    {
        private const KeyCode OrbitHotkey = KeyCode.O;
        private const KeyCode Modifier = KeyCode.LeftShift;
        private const KeyCode Modifier2 = KeyCode.LeftControl;

        private static readonly float[] AnglePresets = { 0f, 45f, 90f, 135f, 180f };

        /// <summary>While orbit is on, return true so vanilla treats the player as able to interact (H UI action lists receive LMB). Yaw is still written in this LateUpdate before <see cref="CameraControl_Ver2.LateUpdate"/>.</summary>
        private static readonly BaseCameraControl_Ver2.NoCtrlFunc NoCtrlOrbit = () => true;

        private const float HotkeyCooldownSeconds = 0.25f;
        /// <summary>When choosing orbit focus option, prefer the game's default camera (option==maxFocus) to reduce distance surprises.</summary>
        private const float PreferGameDefaultCameraChance = 0.8f;

        private bool _orbitActive;
        /// <summary>Mirror of _orbitActive for Harmony patches (static read).</summary>
        private static bool _orbitActiveForPatches;
        private float _lastHotkeyTime = -999f;

        private float _startOrbitY;
        private int _orbitPhase;
        private float _orbitAccumulatedDegrees;
        private int _orbitCycleCount;
        /// <summary>Current view option: 0..maxFocus-1 = body focus index, maxFocus = pose default camera. Total options = maxFocus + 1.</summary>
        private int _currentViewOption;
        /// <summary>Index into sequence [0,1,2,3,2,1]; next step is (_currentClothesSequenceIndex + 1) % 6.</summary>
        private int _currentClothesSequenceIndex;
        /// <summary>Track last nowAnimationInfo for pose-change detection; reapply view when it changes.</summary>
        private object? _lastNowAnimationInfoRef;
        /// <summary>When true, next LateUpdate will call ApplyCurrentViewOption once (e.g. after faintness toggle).</summary>
        private static bool _requestViewReapplyNextFrame;

        private static FieldInfo _feelFField;
        /// <summary>Seconds spent at checkpoint (Idle, no selection) while orbit is on; reset when we advance or leave checkpoint.</summary>
        private float _checkpointIdleTime;
        private static MethodInfo? _getAutoAnimationMethod;
        private static FieldInfo? _isAutoActionChangeField;
        private static PropertyInfo? _isAutoActionChangeProp;
        /// <summary>When orbit started in preparation (Idle + speed 0): wait this many seconds then set speed=1 to start; excitement only accumulates after start.</summary>
        private bool _waitingForPrepStart;
        private float _prepCountdownStart;
        private const float PrepWaitSeconds = 3f;

        private static float GetOrbitFeelAddPerSecond()
        {
            var v = HS2OrbitAndExciter.FeelAddPerSecondWhenOrbit?.Value ?? 0.1f;
            return v <= 0f ? 0f : v;
        }

        /// <summary>Animator state names where excitement is accumulated (action loop). Aibu/Houshi/Sonyu/Les/MultiPlay: W/S/O; Masturbation: W/M/S/O; Spnking: WIdle/SIdle/WAction/SAction.</summary>
        private static readonly HashSet<string> ActionLoopStateNames = new HashSet<string>
        {
            "WLoop", "SLoop", "OLoop", "D_WLoop", "D_SLoop", "D_OLoop",
            "MLoop",
            "WIdle", "SIdle", "WAction", "SAction", "D_Action"
        };

        private const float OrbitSpeedAddPerSecond = 0.35f;

        /// <summary>True when first female's animator is in an action loop state (only then we add feel_f / speed during orbit).</summary>
        private static bool IsInActionLoopState(HScene hScene)
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
            foreach (string name in ActionLoopStateNames)
            {
                if ((bool)isName.Invoke(state, new object[] { name }))
                    return true;
            }
            return false;
        }

        /// <summary>Whether orbit is currently active (for Harmony patches).</summary>
        public static bool IsOrbitActive() => _orbitActiveForPatches;

        /// <summary>True when in preparation state: Idle/D_Idle and speed &lt;= 0.</summary>
        private static bool IsInPreparationState(HScene hScene)
        {
            var ctrlFlag = hScene?.ctrlFlag;
            if (ctrlFlag == null) return false;
            float speed = (float)(Traverse.Create(ctrlFlag).Field("speed").GetValue() ?? 0f);
            if (speed > 0.01f) return false;
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
            return (bool)isName.Invoke(state, new object[] { "Idle" }) || (bool)isName.Invoke(state, new object[] { "D_Idle" });
        }

        /// <summary>When orbit is active and in action loop only: add to excitement gauge and to speed so W/S/O segments advance without wheel.</summary>
        private void AccumulateFeelWhenOrbit(HScene hScene)
        {
            if (_waitingForPrepStart) return;
            if (!IsInActionLoopState(hScene)) return;
            var ctrlFlag = hScene.ctrlFlag;
            if (ctrlFlag == null) return;
            float addPerSec = GetOrbitFeelAddPerSecond();
            if (addPerSec > 0f)
            {
                if (_feelFField == null)
                {
                    _feelFField = ctrlFlag.GetType().GetField("feel_f", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (_feelFField == null) return;
                }
                float current = (float)(_feelFField.GetValue(ctrlFlag) ?? 0f);
                float next = Mathf.Clamp01(current + addPerSec * Time.deltaTime);
                if (next > current)
                    _feelFField.SetValue(ctrlFlag, next);
            }
            var speedField = Traverse.Create(ctrlFlag).Field("speed");
            if (speedField.FieldExists())
            {
                float speed = (float)(speedField.GetValue() ?? 0f);
                speed = Mathf.Clamp(speed + OrbitSpeedAddPerSecond * Time.deltaTime, 0f, 2f);
                speedField.SetValue(speed);
            }
        }

        /// <summary>When orbit is on and OrbitAutoActionEnabled: set isAutoActionChange and initiative so game auto-picks next action.</summary>
        private void ApplyOrbitAutoAction(HScene hScene)
        {
            if (HS2OrbitAndExciter.OrbitAutoActionEnabled?.Value != true)
                return;
            var ctrlFlag = hScene.ctrlFlag;
            if (ctrlFlag == null)
                return;
            if (OrbitBehaviorHub.ShouldSuppressAssist(ctrlFlag, out _))
            {
                ctrlFlag.isAutoActionChange = false;
                ctrlFlag.initiative = 0;
                OrbitBehaviorHub.ResetNullSelectionTracking();
                return;
            }
            bool hasSelectionNow = ctrlFlag.selectAnimationListInfo != null;
            if (hasSelectionNow)
            {
                OrbitBehaviorHub.ResetNullSelectionTracking();
                return;
            }
            if (!OrbitBehaviorHub.IsNullSelectionReadyForAssist())
                return;
            if (!OrbitBehaviorHub.TryConsumeAssistFlagPush(out _))
                return;

            var flagType = ctrlFlag.GetType();
            if (_isAutoActionChangeField == null && _isAutoActionChangeProp == null)
            {
                _isAutoActionChangeField = flagType.GetField("isAutoActionChange", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (_isAutoActionChangeField == null)
                    _isAutoActionChangeProp = flagType.GetProperty("isAutoActionChange", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }
            try
            {
                if (_isAutoActionChangeField != null)
                    _isAutoActionChangeField.SetValue(ctrlFlag, true);
                else
                    _isAutoActionChangeProp?.SetValue(ctrlFlag, true, null);
            }
            catch { }
            Traverse.Create(ctrlFlag).Field("initiative").SetValue(1);
        }

        /// <summary>When orbit is on, not yet in action loop, and (no pose list or similar gate) for OrbitCheckpointTimeoutSeconds, call HScene.GetAutoAnimation. In-loop means checkpoint passed — do not use list-null alone.</summary>
        private void TryAutoAdvancePastCheckpoint(HScene hScene)
        {
            float timeout = HS2OrbitAndExciter.OrbitCheckpointTimeoutSeconds?.Value ?? 2f;
            if (timeout <= 0f) return;
            var ctrlFlag = hScene.ctrlFlag;
            if (ctrlFlag == null) return;
            if (IsInActionLoopState(hScene))
            {
                _checkpointIdleTime = 0f;
                return;
            }
            if (OrbitBehaviorHub.ShouldSuppressAssist(ctrlFlag, out _))
            {
                _checkpointIdleTime = 0f;
                return;
            }
            var sel = Traverse.Create(ctrlFlag).Property("selectAnimationListInfo").GetValue();
            bool hasSelection = sel != null;
            if (hasSelection)
            {
                _checkpointIdleTime = 0f;
                OrbitBehaviorHub.ResetCheckpointInvokeCooldown();
                return;
            }
            if (Input.GetMouseButton(0))
            {
                _checkpointIdleTime = 0f;
                return;
            }
            if (OrbitBehaviorHub.IsCheckpointInvokeOnLegacyCooldown())
                return;
            // Not in action loop (see IsInActionLoopState gate above); list-null still used as gate hint for pre-loop UI stalls.
            _checkpointIdleTime += Time.deltaTime;
            if (_checkpointIdleTime < timeout) return;
            _checkpointIdleTime = 0f;
            if (!OrbitBehaviorHub.TryConsumeCheckpointInvoke(out _))
                return;
            if (_getAutoAnimationMethod == null)
            {
                _getAutoAnimationMethod = typeof(HScene).GetMethod("GetAutoAnimation", BindingFlags.NonPublic | BindingFlags.Instance);
                if (_getAutoAnimationMethod == null)
                    return;
            }
            try
            {
                _getAutoAnimationMethod.Invoke(hScene, new object[] { false });
                if (Traverse.Create(ctrlFlag).Property("selectAnimationListInfo").GetValue() == null)
                    _getAutoAnimationMethod.Invoke(hScene, new object[] { true });
            }
            catch { }
            OrbitBehaviorHub.MarkCheckpointInvokeLegacyCooldown(timeout);

            // Fallback: if we still can't pick next action, try to bump speed once (no feel_f change).
            bool hasSelAfter = Traverse.Create(ctrlFlag).Property("selectAnimationListInfo").GetValue() != null;
            if (!hasSelAfter)
            {
                try
                {
                    const float FallbackSpeedBump = 1.2f;
                    Traverse.Create(ctrlFlag).Field("speed").SetValue(FallbackSpeedBump);
                }
                catch { }
            }
        }

        private void Update()
        {
            var hProbe = TryGetHScene();
            if (hProbe != null && Input.GetMouseButtonDown(0))
            {
                bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                if (overUi)
                    OrbitBehaviorHub.NotifyManualUiClick();
            }
            else if (_orbitActive && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                // Keep suppress window alive while cursor stays on UI, so auto-action can't race hover->click.
                OrbitBehaviorHub.NotifyUiHoverWhileOrbit();
            }
            bool mod2 = Input.GetKey(Modifier2);
            bool mod = Input.GetKey(Modifier);
            bool oDown = Input.GetKeyDown(OrbitHotkey);
            if (mod2 && mod && oDown)
            {
                if (Time.unscaledTime - _lastHotkeyTime < HotkeyCooldownSeconds)
                    return;
                _lastHotkeyTime = Time.unscaledTime;
                _orbitActive = !_orbitActive;
                _orbitActiveForPatches = _orbitActive;
                OnOrbitToggled(_orbitActive);
            }
        }

        private void LateUpdate()
        {
            if (!_orbitActive)
            {
                _requestViewReapplyNextFrame = false;
                return;
            }

            var hScene = GetHScene();
            if (hScene == null) return;

            var ctrl = hScene.ctrlFlag?.cameraCtrl as CameraControl_Ver2;
            if (ctrl == null) return;
            ApplyOrbitFocusHotkeys(hScene, ctrl);

            // If we started in preparation (Idle + speed 0): after 3 s set speed=1 to start motion; excitement only rises after that
            if (_waitingForPrepStart)
            {
                float elapsed = Time.unscaledTime - _prepCountdownStart;
                if (elapsed >= PrepWaitSeconds)
                {
                    _waitingForPrepStart = false;
                    var ctrlFlag = hScene.ctrlFlag;
                    if (ctrlFlag != null)
                        Traverse.Create(ctrlFlag).Field("speed").SetValue(1f);
                }
            }

            // Excitement gauge auto-accumulates while orbit is active (skipped during prep countdown)
            AccumulateFeelWhenOrbit(hScene);

            // ApplyOrbitAutoAction / TryAutoAdvancePastCheckpoint: see OrbitHSceneLateAssist (runs after H proc)

            // Re-apply camera takeover every frame (e.g. after ChangeAnimation sets camera flag)
            ctrl.NoCtrlCondition = NoCtrlOrbit;

            // When pose changes (plugin or game), reapply current view so character stays in frame
            var nowInfo = hScene.ctrlFlag?.nowAnimationInfo;
            if (nowInfo != null && !ReferenceEquals(nowInfo, _lastNowAnimationInfoRef))
            {
                _lastNowAnimationInfoRef = nowInfo;
                ApplyCurrentViewOption(hScene, ctrl);
            }

            // After faintness toggle or other state change: reapply view once
            if (_requestViewReapplyNextFrame)
            {
                _requestViewReapplyNextFrame = false;
                ApplyCurrentViewOption(hScene, ctrl);
                _lastNowAnimationInfoRef = hScene.ctrlFlag?.nowAnimationInfo;
            }

            float orbitTime = HS2OrbitAndExciter.OrbitTimePer360?.Value ?? 10f;
            if (orbitTime <= 0f) orbitTime = 10f;
            float speedDegPerSec = 360f / orbitTime;
            float dt = Time.deltaTime;

            if (_orbitPhase == 0)
            {
                _orbitAccumulatedDegrees += speedDegPerSec * dt;
                if (_orbitAccumulatedDegrees >= 360f)
                {
                    _orbitAccumulatedDegrees = 360f;
                    _orbitPhase = 1;
                }
            }
            else
            {
                _orbitAccumulatedDegrees -= speedDegPerSec * dt;
                if (_orbitAccumulatedDegrees <= 0f)
                {
                    _orbitAccumulatedDegrees = 0f;
                    _orbitPhase = 0;
                    OnOrbitCycleComplete(hScene, ctrl);
                }
            }

            float rotY = _startOrbitY + (_orbitPhase == 0 ? _orbitAccumulatedDegrees : 360f - _orbitAccumulatedDegrees);
            rotY = ((rotY % 360f) + 360f) % 360f;
            // Do NOT assign CameraAngle: its setter does transform.rotation = Euler(v) without transBase, breaking orbit vs CameraUpdate().
            // Match game autoCamera: only CamDat.Rot; CameraControl_Ver2.LateUpdate -> CameraUpdate applies transBase * Euler(CamDat.Rot).
            var rot = ctrl.CameraAngle;
            rot.y = rotY;
            ctrl.Rot = rot;
        }

        /// <summary>Minimum camera distance in body-height units (legacy configs often had 0.3; too tight for full-body framing).</summary>
        private const float OrbitDistanceMultMin = 1.35f;
        private const float OrbitDistanceMultMax = 3f;

        /// <summary>Set camera distance = body height × config multiplier for this focus. Call after setting TargetPos.</summary>
        private static void SetDistanceForFocus(CameraControl_Ver2 ctrl, ChaControl[]? chaFemales, int focusIndex)
        {
            if (chaFemales == null || chaFemales.Length == 0) return;
            int femaleIdx = focusIndex < 3 ? 0 : 1;
            float bodyHeight = OrbitHelpers.GetBodyHeight(chaFemales, femaleIdx);
            float mult = 1f;
            if (focusIndex == 0 || focusIndex == 3) mult = HS2OrbitAndExciter.OrbitDistanceHead?.Value ?? 1.4f;
            else if (focusIndex == 1 || focusIndex == 4) mult = HS2OrbitAndExciter.OrbitDistanceChest?.Value ?? 1.4f;
            else mult = HS2OrbitAndExciter.OrbitDistancePelvis?.Value ?? 1.4f;
            // Old cfg defaults (e.g. 0.3) mapped to 1× height and felt "inside" the model; pull back.
            if (mult < 1f)
                mult = OrbitDistanceMultMin;
            mult = Mathf.Clamp(mult, OrbitDistanceMultMin, OrbitDistanceMultMax);
            float d = bodyHeight * mult;
            d = Mathf.Clamp(d, OrbitDistanceMultMin * bodyHeight, OrbitDistanceMultMax * bodyHeight);
            ctrl.CameraDir = new Vector3(0f, 0f, -d);
        }

        /// <summary>
        /// Mirror vanilla <c>GlobalMethod.CameraKeyCtrl</c> in LateUpdate before orbit writes yaw so focus keys apply in the same frame as <see cref="CameraControl_Ver2.LateUpdate"/>.
        /// Vanilla only sets <see cref="CameraControl_Ver2.TargetPos"/>; we then set distance from body height so rapid Q/W/E reads clearly (matches plugin focus options 0..5).
        /// Skips when <c>HSceneFlagCtrl.inputForcus</c> (same as <c>HScene.ShortcutKey</c>).
        /// </summary>
        private void ApplyOrbitFocusHotkeys(HScene hScene, CameraControl_Ver2 ctrl)
        {
            var ctrlFlag = hScene.ctrlFlag;
            if (ctrlFlag != null && ctrlFlag.inputForcus)
                return;

            var chaFemales = OrbitHelpers.GetChaFemales(hScene);
            if (chaFemales == null || chaFemales.Length == 0)
                return;

            GlobalMethod.CameraKeyCtrl(ctrl, chaFemales);

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            int maxFocus = OrbitHelpers.GetMaxFocusIndex(chaFemales);
            int newOpt = -1;
            if (!shift)
            {
                if (Input.GetKeyDown(KeyCode.Q)) newOpt = 0;
                else if (Input.GetKeyDown(KeyCode.W)) newOpt = 1;
                else if (Input.GetKeyDown(KeyCode.E)) newOpt = 2;
            }
            else
            {
                if (chaFemales.Length > 1 && chaFemales[1] != null && chaFemales[1].objBodyBone != null)
                {
                    if (Input.GetKeyDown(KeyCode.Q)) newOpt = 3;
                    else if (Input.GetKeyDown(KeyCode.W)) newOpt = 4;
                    else if (Input.GetKeyDown(KeyCode.E)) newOpt = 5;
                }
            }

            if (newOpt < 0 || newOpt > maxFocus)
                return;

            SetDistanceForFocus(ctrl, chaFemales, newOpt);
            _currentViewOption = newOpt;
        }

        /// <summary>Apply current view option: body focus (GetFocusPosition + SetDistanceForFocus) or pose default camera (setCameraLoad).</summary>
        private void ApplyCurrentViewOption(HScene hScene, CameraControl_Ver2 ctrl)
        {
            var chaFemales = OrbitHelpers.GetChaFemales(hScene);
            if (chaFemales == null || ctrl == null) return;
            int maxFocus = OrbitHelpers.GetMaxFocusIndex(chaFemales);
            int totalOptions = maxFocus + 1;
            if (totalOptions <= 0) return;
            int option = _currentViewOption;
            if (option < 0 || option > maxFocus)
                option = 0;
            if (option < maxFocus)
            {
                var pos = OrbitHelpers.GetFocusPosition(chaFemales, option, ctrl.transBase);
                if (pos.HasValue)
                {
                    ctrl.TargetPos = pos.Value;
                    SetDistanceForFocus(ctrl, chaFemales, option);
                }
            }
            else
            {
                var ctrlFlag = hScene.ctrlFlag;
                if (ctrlFlag != null && ctrlFlag.nowAnimationInfo != null)
                    hScene.setCameraLoad(ctrlFlag.nowAnimationInfo, true);
            }
        }

        private void OnOrbitCycleComplete(HScene hScene, CameraControl_Ver2 ctrl)
        {
            _orbitCycleCount++;
            var chaFemales = OrbitHelpers.GetChaFemales(hScene);
            int maxFocus = OrbitHelpers.GetMaxFocusIndex(chaFemales);
            int nRandom = HS2OrbitAndExciter.OrbitCountBeforeRandom?.Value ?? 1;
            int nPose = HS2OrbitAndExciter.OrbitCountBeforePoseChange?.Value ?? 2;
            bool changePose = HS2OrbitAndExciter.ChangePoseOnCycle?.Value ?? false;
            bool clothesEnabled = HS2OrbitAndExciter.ClothesChangeEnabled?.Value ?? false;
            bool suppressCycleActions = OrbitBehaviorHub.ShouldSuppressAssist(hScene.ctrlFlag, out _);

            if (nRandom > 0 && _orbitCycleCount % nRandom == 0)
            {
                int totalOptions = maxFocus + 1;
                if (totalOptions > 0)
                {
                    int current = _currentViewOption;
                    if (current < 0 || current > maxFocus) current = 0;
                    // Exclude current so we don't get the same option twice in a row
                    var candidates = new List<int>();
                    for (int i = 0; i <= maxFocus; i++)
                        if (i != current)
                            candidates.Add(i);
                    if (Random.value < PreferGameDefaultCameraChance)
                        _currentViewOption = maxFocus;
                    else if (candidates.Count > 0)
                        _currentViewOption = candidates[Random.Range(0, candidates.Count)];
                    else
                        _currentViewOption = current;
                    ApplyCurrentViewOption(hScene, ctrl);
                }
                _startOrbitY = AnglePresets[Random.Range(0, AnglePresets.Length)];
            }

            if (clothesEnabled && !suppressCycleActions)
            {
                _currentClothesSequenceIndex = (_currentClothesSequenceIndex + 1) % 6;
                int stage = OrbitHelpers.ClothesSequenceStage(_currentClothesSequenceIndex);
                var chaMales = OrbitHelpers.GetChaMales(hScene);
                OrbitHelpers.SetClothesStage(chaFemales, chaMales, stage);
            }

            if (changePose && !suppressCycleActions && nPose > 0 && _orbitCycleCount % nPose == 0)
            {
                var all = OrbitHelpers.GetAllPoseList();
                if (all.Count > 0)
                {
                    var current = hScene.ctrlFlag?.nowAnimationInfo;
                    var next = OrbitHelpers.PickNextPose(current, all);
                    if (next != null)
                    {
                        hScene.StartCoroutine(hScene.ChangeAnimation(next, _isForceResetCamera: false, _isForceLoopAction: false, _UseFade: true));
                    }
                }
            }
        }

        private void OnOrbitToggled(bool active)
        {
            var hScene = GetHScene();
            if (hScene == null)
            {
                HS2OrbitAndExciter.Log?.LogInfo("Orbit: No H scene; orbit will start when entering H.");
                return;
            }

            var ctrl = hScene.ctrlFlag?.cameraCtrl;
            if (ctrl == null) return;

            if (active)
            {
                _orbitActiveForPatches = true;
                OrbitBehaviorHub.NotifyOrbitToggled(true);
                ctrl.NoCtrlCondition = NoCtrlOrbit;
                _startOrbitY = ((ctrl.CameraAngle.y % 360f) + 360f) % 360f;
                _orbitPhase = 0;
                _orbitAccumulatedDegrees = 0f;
                _orbitCycleCount = 0;
                var chaFemales = OrbitHelpers.GetChaFemales(hScene);
                _currentClothesSequenceIndex = OrbitHelpers.GetClothesSequenceIndexFromCurrent(chaFemales);
                int maxFocus = OrbitHelpers.GetMaxFocusIndex(chaFemales);
                int totalOptions = maxFocus + 1;
                if (totalOptions > 0 && Random.value < PreferGameDefaultCameraChance)
                    _currentViewOption = maxFocus;
                else
                    _currentViewOption = totalOptions > 0 ? Random.Range(0, totalOptions) : 0;
                ApplyCurrentViewOption(hScene, (CameraControl_Ver2)ctrl);
                _lastNowAnimationInfoRef = hScene.ctrlFlag?.nowAnimationInfo;
                if (IsInPreparationState(hScene))
                {
                    _waitingForPrepStart = true;
                    _prepCountdownStart = Time.unscaledTime;
                }
                else
                    _waitingForPrepStart = false;
            }
            else
            {
                _orbitActiveForPatches = false;
                OrbitBehaviorHub.NotifyOrbitToggled(false);
                _waitingForPrepStart = false;
                // Restoring saved delegate can evaluate false after orbit stop and keep UI unclickable; keep permissive after orbit off.
                ctrl.NoCtrlCondition = () => true;
            }
        }

        private static HScene? GetHScene() => TryGetHScene();

        /// <summary>For <see cref="OrbitHSceneLateAssist"/> and patches.</summary>
        internal static HScene? TryGetHScene()
        {
            if (!Singleton<HSceneManager>.IsInstance())
                return null;
            return Singleton<HSceneManager>.Instance.Hscene;
        }

        /// <summary>Runs after H-scene proc (see OrbitHSceneLateAssist). Do not call from early LateUpdate.</summary>
        internal void RunLateHSceneAssist(HScene hScene)
        {
            if (!_orbitActive || hScene == null) return;
            ApplyOrbitAutoAction(hScene);
            TryAutoAdvancePastCheckpoint(hScene);
        }

        /// <summary>Request that the next LateUpdate reapplies the current orbit view (e.g. after faintness toggle).</summary>
        public static void RequestViewReapply()
        {
            _requestViewReapplyNextFrame = true;
        }

    }
}
