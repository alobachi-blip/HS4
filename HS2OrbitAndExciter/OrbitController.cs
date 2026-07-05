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
    /// Drives orbit camera in H scene: Ctrl+Shift+O. Each <b>rotation</b> is one 360° leg (outbound then inbound); yaw uses
    /// <c>_startOrbitY + _orbitAccumulatedDegrees</c> so inbound is a true reverse. Each full out+in is one <b>round-trip</b>.
    /// Cycle side effects (N rotations / M round-trips) are delegated to <see cref="OrbitCycleCoordinator"/>.
    /// Runs LateUpdate before <see cref="CameraControl_Ver2"/> (default 0) so yaw is written to CamDat.Rot and CameraUpdate() applies transBase correctly.
    /// H-scene flag assist runs in <see cref="OrbitHSceneLateAssist"/> after game proc.
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

        private float _lastHotkeyTime = -999f;
        private int _manualDirectorHSceneId = -1;

        private float _startOrbitY;
        private int _orbitPhase;
        private float _orbitAccumulatedDegrees;
        /// <summary>Completed single-direction 360° legs (outbound end + inbound end each +1).</summary>
        private int _rotationCount;
        /// <summary>Completed full out+in cycles.</summary>
        private int _roundTripCount;
        /// <summary>Current view option: 0..maxFocus-1 = body focus index, maxFocus = pose default camera. Total options = maxFocus + 1.</summary>
        private int _currentViewOption;
        /// <summary>Index into sequence [0,1,2,3,2,1]; next step is (_currentClothesSequenceIndex + 1) % 6.</summary>
        private int _currentClothesSequenceIndex;
        /// <summary>Track last nowAnimationInfo for pose-change detection; reapply view when it changes.</summary>
        private object? _lastNowAnimationInfoRef;
        /// <summary>When true, next LateUpdate will call ApplyCurrentViewOption once (e.g. after faintness toggle).</summary>
        private static bool _requestViewReapplyNextFrame;
        private static OrbitController? _activeInstance;

        private static FieldInfo? _feelFField;
        /// <summary>When orbit started in preparation (Idle + speed 0): wait this many seconds then set speed=1 to start; excitement only accumulates after start.</summary>
        private bool _waitingForPrepStart;
        private float _prepCountdownStart;
        /// <summary>Elapsed prep seconds frozen while pose transition is active.</summary>
        private float _prepFrozenElapsed = -1f;
        private const float PrepWaitSeconds = 3f;

        private bool _hudSnapshotValid;
        private OrbitHudSnapshot _hudSnapshot;

        private static float GetOrbitFeelAddPerSecond()
        {
            var v = HS2OrbitAndExciter.FeelAddPerSecondWhenOrbit?.Value ?? 0.1f;
            return v <= 0f ? 0f : v;
        }

        private const float OrbitSpeedAddPerSecond = 0.35f;

        /// <summary>Whether orbit is currently active (for Harmony patches). Authoritative state is <see cref="OrbitBehaviorHub.IsOrbitAssistActive"/>.</summary>
        public static bool IsOrbitActive() => OrbitBehaviorHub.IsOrbitAssistActive();

        private void OnEnable() => _activeInstance = this;

        private void OnDisable()
        {
            if (_activeInstance == this)
                _activeInstance = null;
        }

        /// <summary>After G/H/J manual hotkey completes: restart motion and auto-advance assist.</summary>
        internal static void NotifyManualHotkeyCompleted(HScene hScene)
        {
            if (!OrbitBehaviorHub.IsOrbitAssistActive() || hScene == null || _activeInstance == null)
                return;
            OrbitBehaviorHub.NotifyManualHotkeyCompleted(hScene);
            _activeInstance.ResumeMotionAfterManualHotkey(hScene);
        }

        private void ResumeMotionAfterManualHotkey(HScene hScene)
        {
            var ctrlFlag = hScene.ctrlFlag;
            if (ctrlFlag == null)
                return;

            var speedField = Traverse.Create(ctrlFlag).Field("speed");
            if (!speedField.FieldExists())
                return;

            float speed = (float)(speedField.GetValue() ?? 0f);
            if (IsInPreparationState(hScene) || (speed <= 0.01f && !OrbitHelpers.IsFirstFemaleInActionLoop(hScene)))
            {
                _waitingForPrepStart = false;
                _prepFrozenElapsed = -1f;
                speedField.SetValue(1f);
            }
        }

        /// <summary>True when in preparation state: Idle/D_Idle and speed &lt;= 0.</summary>
        private static bool IsInPreparationState(HScene hScene)
        {
            if (hScene == null) return false;
            var ctrlFlag = hScene.ctrlFlag;
            if (ctrlFlag == null) return false;
            float speed = (float)(Traverse.Create(ctrlFlag).Field("speed").GetValue() ?? 0f);
            if (speed > 0.01f) return false;
            var chaFemales = OrbitHelpers.GetChaFemales(hScene);
            if (chaFemales == null || chaFemales.Length == 0) return false;
            var cha = chaFemales[0];
            if (cha == null) return false;
            var animBody = OrbitHelpers.TryGetFemaleAnimBody(cha);
            if (animBody == null) return false;
            var stateInfo = animBody.GetCurrentAnimatorStateInfo(0);
            return stateInfo.IsName("Idle") || stateInfo.IsName("D_Idle");
        }

        /// <summary>When orbit is active and in action loop only: add to excitement gauge and to speed so W/S/O segments advance without wheel.</summary>
        private void AccumulateFeelWhenOrbit(HScene hScene)
        {
            if (!OrbitBehaviorHub.CanAccumulateFeelDuringOrbit(hScene, _waitingForPrepStart)) return;
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

        private void Update()
        {
            var hProbe = TryGetHScene();
            if (hProbe != null)
            {
                int hId = hProbe.GetInstanceID();
                if (hId != _manualDirectorHSceneId)
                {
                    _manualDirectorHSceneId = hId;
                    OrbitManualDirector.OnHSceneEntered();
                }
            }
            else
            {
                _manualDirectorHSceneId = -1;
            }

            if (hProbe != null && Input.GetMouseButtonDown(0))
            {
                bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                if (overUi)
                    OrbitBehaviorHub.NotifyManualUiClick();
            }
            else if (OrbitBehaviorHub.IsOrbitAssistActive() && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
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
                bool active = !OrbitBehaviorHub.IsOrbitAssistActive();
                OrbitBehaviorHub.NotifyOrbitToggled(active);
                OnOrbitToggled(active);
            }

            TryManualHotkeys(hProbe);
        }

        private void TryManualHotkeys(HScene? hScene)
        {
            if (hScene == null)
                return;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                return;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                return;
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
                return;
            if (Time.unscaledTime - _lastHotkeyTime < HotkeyCooldownSeconds)
                return;

            if (Input.GetKeyDown(OrbitManualHotkeys.CharaKey))
            {
                if (OrbitManualDirector.TrySwapFemale0(hScene, this))
                    _lastHotkeyTime = Time.unscaledTime;
                return;
            }
            if (Input.GetKeyDown(OrbitManualHotkeys.CoordinateKey))
            {
                if (OrbitManualDirector.TrySwapCoordinate(hScene, this))
                    _lastHotkeyTime = Time.unscaledTime;
                return;
            }
            if (Input.GetKeyDown(OrbitManualHotkeys.WearKey))
            {
                if (OrbitManualDirector.TryRandomWear(hScene))
                    _lastHotkeyTime = Time.unscaledTime;
                return;
            }
            if (Input.GetKeyDown(OrbitManualHotkeys.PoseCameraKey))
            {
                if (OrbitManualDirector.TryCyclePoseCamera(hScene, this))
                    _lastHotkeyTime = Time.unscaledTime;
                return;
            }
            if (Input.GetKeyDown(OrbitManualHotkeys.PoseKey))
            {
                if (OrbitManualDirector.TryChangePose(hScene))
                    _lastHotkeyTime = Time.unscaledTime;
            }
        }

        private void LateUpdate()
        {
            if (!OrbitBehaviorHub.IsOrbitAssistActive())
            {
                _hudSnapshotValid = false;
                _requestViewReapplyNextFrame = false;
                return;
            }

            var hScene = GetHScene();
            if (hScene == null)
            {
                _hudSnapshotValid = false;
                return;
            }

            var ctrl = hScene.ctrlFlag?.cameraCtrl as CameraControl_Ver2;
            if (ctrl == null)
            {
                _hudSnapshotValid = false;
                return;
            }

            ApplyOrbitFocusHotkeys(hScene, ctrl);

            OrbitPoseDirector.Tick(hScene, this, ctrl);

            // If we started in preparation (Idle + speed 0): after 3 s set speed=1 to start motion; excitement only rises after that
            if (_waitingForPrepStart)
            {
                if (OrbitPoseDirector.IsTransitionActive || OrbitManualDirector.IsCameraPaused)
                {
                    if (_prepFrozenElapsed < 0f)
                        _prepFrozenElapsed = Time.unscaledTime - _prepCountdownStart;
                }
                else
                {
                    if (_prepFrozenElapsed >= 0f)
                    {
                        _prepCountdownStart = Time.unscaledTime - _prepFrozenElapsed;
                        _prepFrozenElapsed = -1f;
                    }
                    float elapsed = Time.unscaledTime - _prepCountdownStart;
                    if (elapsed >= PrepWaitSeconds)
                    {
                        _waitingForPrepStart = false;
                        _prepFrozenElapsed = -1f;
                        var ctrlFlag = hScene.ctrlFlag;
                        if (ctrlFlag != null)
                            Traverse.Create(ctrlFlag).Field("speed").SetValue(1f);
                    }
                }
            }

            // Excitement gauge auto-accumulates while orbit is active (skipped during prep countdown)
            AccumulateFeelWhenOrbit(hScene);

            // ApplyOrbitAutoAction / TryAutoAdvancePastCheckpoint: see OrbitHSceneLateAssist (runs after H proc)

            // Re-apply camera takeover every frame (e.g. after ChangeAnimation sets camera flag)
            ctrl.NoCtrlCondition = NoCtrlOrbit;

            // When pose changes (UI manual / game), defer rebind to Director instead of immediate ApplyCurrentViewOption
            var nowInfo = hScene.ctrlFlag?.nowAnimationInfo;
            if (nowInfo != null && !ReferenceEquals(nowInfo, _lastNowAnimationInfoRef))
                OrbitPoseDirector.NotifyExternalPoseChange(hScene);

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

            if (!OrbitPoseDirector.IsCameraPaused && !OrbitManualDirector.IsCameraPaused)
            {
                if (_orbitPhase == 0)
                {
                    _orbitAccumulatedDegrees += speedDegPerSec * dt;
                    if (_orbitAccumulatedDegrees >= 360f)
                    {
                        _orbitAccumulatedDegrees = 360f;
                        _orbitPhase = 1;
                        _rotationCount++;
                        OrbitCycleCoordinator.ApplyRotationEffects(this, hScene, ctrl, _rotationCount, allowStartYRandom: false, roundTripJustCompleted: false);
                    }
                }
                else
                {
                    _orbitAccumulatedDegrees -= speedDegPerSec * dt;
                    if (_orbitAccumulatedDegrees <= 0f)
                    {
                        _orbitAccumulatedDegrees = 0f;
                        _orbitPhase = 0;
                        _rotationCount++;
                        _roundTripCount++;
                        OrbitCycleCoordinator.ApplyRotationEffects(this, hScene, ctrl, _rotationCount, allowStartYRandom: true, roundTripJustCompleted: true);
                        OrbitCycleCoordinator.ApplyPoseIfNeeded(hScene, _roundTripCount);
                    }
                }
            }

            float rotY = _startOrbitY + _orbitAccumulatedDegrees;
            rotY = ((rotY % 360f) + 360f) % 360f;
            // Do NOT assign CameraAngle: its setter does transform.rotation = Euler(v) without transBase, breaking orbit vs CameraUpdate().
            // Match game autoCamera: only CamDat.Rot; CameraControl_Ver2.LateUpdate -> CameraUpdate applies transBase * Euler(CamDat.Rot).
            var rot = ctrl.CameraAngle;
            rot.y = rotY;
            ctrl.Rot = rot;

            RefreshHudSnapshot(hScene, orbitTime, speedDegPerSec);
        }

        internal bool TryGetCachedHudSnapshot(out OrbitHudSnapshot s)
        {
            s = _hudSnapshot;
            return _hudSnapshotValid;
        }

        private static int CountsUntilNextMultiple(int completedCount, int period)
        {
            if (period <= 0) return -1;
            int k = (period - (completedCount % period)) % period;
            if (k == 0) k = period;
            return k;
        }

        private void RefreshHudSnapshot(HScene hScene, float orbitTimePer360, float speedDegPerSec)
        {
            float tLeg = _orbitPhase == 0
                ? (360f - _orbitAccumulatedDegrees) / speedDegPerSec
                : _orbitAccumulatedDegrees / speedDegPerSec;
            float tCompleteRoundTrip = _orbitPhase == 0 ? tLeg + orbitTimePer360 : tLeg;
            float roundTripSec = 2f * orbitTimePer360;

            float prepRemain = 0f;
            if (_waitingForPrepStart)
            {
                float prepElapsed = _prepFrozenElapsed >= 0f
                    ? _prepFrozenElapsed
                    : Time.unscaledTime - _prepCountdownStart;
                prepRemain = Mathf.Max(0f, PrepWaitSeconds - prepElapsed);
            }

            int nRandom = HS2OrbitAndExciter.OrbitCountBeforeRandom?.Value ?? 0;
            int mPose = HS2OrbitAndExciter.OrbitCountBeforePoseChange?.Value ?? 2;
            bool changePose = HS2OrbitAndExciter.ChangePoseOnCycle?.Value ?? false;
            bool clothesEnabled = HS2OrbitAndExciter.ClothesChangeEnabled?.Value ?? false;

            int rotationsUntilRandom = nRandom > 0 ? CountsUntilNextMultiple(_rotationCount, nRandom) : -1;
            int rotationsUntilClothes;
            if (!clothesEnabled)
                rotationsUntilClothes = -1;
            else if (nRandom > 0)
                rotationsUntilClothes = CountsUntilNextMultiple(_rotationCount, nRandom);
            else
                rotationsUntilClothes = OrbitHudSnapshot.ClothesHintNextRoundTrip;

            int roundTripsUntilPose = changePose && mPose > 0 ? CountsUntilNextMultiple(_roundTripCount, mPose) : -1;

            OrbitBehaviorHub.ShouldSuppressAssist(hScene.ctrlFlag, out string reason);
            bool faint = hScene.ctrlFlag?.isFaintness ?? false;

            _hudSnapshot = new OrbitHudSnapshot(
                _waitingForPrepStart,
                prepRemain,
                _orbitPhase,
                tLeg,
                tCompleteRoundTrip,
                rotationsUntilRandom,
                rotationsUntilClothes,
                roundTripsUntilPose,
                reason,
                faint,
                orbitTimePer360,
                roundTripSec,
                OrbitPoseDirector.IsCameraPaused || OrbitManualDirector.IsCameraPaused);
            _hudSnapshotValid = true;
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
            if (OrbitBehaviorHub.ShouldDeferOrbitFocusHotkeysToGame(hScene.ctrlFlag))
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

        internal void InternalAdvanceClothesStage(HScene hScene)
        {
            var chaFemales = OrbitHelpers.GetChaFemales(hScene);
            _currentClothesSequenceIndex = (_currentClothesSequenceIndex + 1) % 6;
            int stage = OrbitHelpers.ClothesSequenceStage(_currentClothesSequenceIndex);
            var chaMales = OrbitHelpers.GetChaMales(hScene);
            OrbitHelpers.SetClothesStage(chaFemales, chaMales, stage);
        }

        internal void InternalRandomizeViewOption(HScene hScene, CameraControl_Ver2 ctrl)
        {
            var chaFemales = OrbitHelpers.GetChaFemales(hScene);
            int maxFocus = OrbitHelpers.GetMaxFocusIndex(chaFemales);
            int totalOptions = maxFocus + 1;
            if (totalOptions <= 0)
                return;
            int current = _currentViewOption;
            if (current < 0 || current > maxFocus) current = 0;
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

        internal void InternalRandomizeStartOrbitY()
        {
            _startOrbitY = AnglePresets[Random.Range(0, AnglePresets.Length)];
        }

        /// <summary>Load another pose's default camera preset without changing the active pose.</summary>
        internal void ApplyBorrowedPoseCamera(HScene hScene, HScene.AnimationListInfo info)
        {
            var ctrl = hScene.ctrlFlag?.cameraCtrl as CameraControl_Ver2;
            if (ctrl == null)
                return;

            hScene.setCameraLoad(info, true);

            if (!OrbitBehaviorHub.IsOrbitAssistActive())
                return;

            var chaFemales = OrbitHelpers.GetChaFemales(hScene);
            int maxFocus = OrbitHelpers.GetMaxFocusIndex(chaFemales);
            _currentViewOption = maxFocus;
            _startOrbitY = ((ctrl.CameraAngle.y % 360f) + 360f) % 360f;
            _orbitAccumulatedDegrees = 0f;
        }

        /// <summary>Rebind camera after pose transition completes (called by <see cref="OrbitPoseDirector"/>).</summary>
        internal void InternalRebindAfterPoseChange(HScene hScene, CameraControl_Ver2 ctrl)
        {
            var chaFemales = OrbitHelpers.GetChaFemales(hScene);
            int maxFocus = OrbitHelpers.GetMaxFocusIndex(chaFemales);
            if (_currentViewOption > maxFocus)
                _currentViewOption = maxFocus;
            ApplyCurrentViewOption(hScene, ctrl);
            _startOrbitY = ((ctrl.CameraAngle.y % 360f) + 360f) % 360f;
            _lastNowAnimationInfoRef = hScene.ctrlFlag?.nowAnimationInfo;
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
                ctrl.NoCtrlCondition = NoCtrlOrbit;
                _startOrbitY = ((ctrl.CameraAngle.y % 360f) + 360f) % 360f;
                _orbitPhase = 0;
                _orbitAccumulatedDegrees = 0f;
                _rotationCount = 0;
                _roundTripCount = 0;
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
                _waitingForPrepStart = false;
                _prepFrozenElapsed = -1f;
                OrbitPoseDirector.Reset();
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
            if (!OrbitBehaviorHub.IsOrbitAssistActive() || hScene == null) return;
            OrbitBehaviorHub.TickStaleSelectionRecovery(hScene);
            OrbitBehaviorHub.TryPushOrbitAutoActionAssist(hScene.ctrlFlag);
            OrbitBehaviorHub.TickOrbitCheckpointAssist(hScene, Time.deltaTime);
        }

        /// <summary>Request that the next LateUpdate reapplies the current orbit view (e.g. after faintness toggle).</summary>
        public static void RequestViewReapply()
        {
            _requestViewReapplyNextFrame = true;
        }

    }
}
