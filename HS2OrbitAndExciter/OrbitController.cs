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
    /// Drives orbit camera in H scene: Ctrl+Shift+O. Each <b>rotation</b> is one 360° leg around the body torso axis
    /// (head−pelvis); inbound reverses the same relative azimuth. Each full out+in is one <b>round-trip</b>.
    /// Assist ≠ spinning: O toggles camera spin only. Cycle side effects go to <see cref="OrbitCycleCoordinator"/>.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class OrbitController : MonoBehaviour
    {
        private const KeyCode OrbitHotkey = KeyCode.O;
        private const KeyCode Modifier = KeyCode.LeftShift;
        private const KeyCode Modifier2 = KeyCode.LeftControl;

        /// <summary>While assist is on, return true so vanilla treats the player as able to interact. Yaw still written in LateUpdate.</summary>
        private static readonly BaseCameraControl_Ver2.NoCtrlFunc NoCtrlOrbit = () => true;

        private const float HotkeyCooldownSeconds = 0.25f;
        /// <summary>§11：環視期間幾乎只用骨焦點（舊 0.8 姿預設相機退役）。</summary>
        private const float PreferGameDefaultCameraChance = 0.05f;

        private float _lastHotkeyTime = -999f;
        private int _manualDirectorHSceneId = -1;

        /// <summary>本圈起始：相機相對身體軸向的方位角（度）。</summary>
        private float _startRelativeAzimuth;
        private int _orbitPhase;
        private float _orbitAccumulatedDegrees;
        /// <summary>Completed single-direction 360° legs.</summary>
        private int _rotationCount;
        private int _roundTripCount;
        private int _currentViewOption;
        private int _currentClothesSequenceIndex;
        private object? _lastNowAnimationInfoRef;
        private static bool _requestViewReapplyNextFrame;
        private static OrbitController? _activeInstance;

        private static FieldInfo? _feelFField;
        private bool _waitingForPrepStart;
        private float _prepCountdownStart;
        private float _prepFrozenElapsed = -1f;
        private const float PrepWaitSeconds = 3f;

        private bool _hudSnapshotValid;
        private OrbitHudSnapshot _hudSnapshot;

        /// <summary>§11 1A：上一有效骨焦點（世界座標），失敗時回退。</summary>
        private Vector3? _lastValidFocusWorld;
        /// <summary>提前算好的下一圈相對角 Δ（空檔算；軸變則作廢）。</summary>
        private float? _plannedRelativeDelta;
        private Vector3 _plannedAxisSnapshot;
        /// <summary>每圈 zoom 倍率（相對設定距離）。</summary>
        private float _circleZoomMult = 1f;
        private float? _plannedZoomMult;

        private static float GetOrbitFeelAddPerSecond()
        {
            // §3／§21：感度有最小值，約 10 秒推進一循環階段（0.1／秒）
            const float MinPerStageBudget = 0.1f;
            var v = HS2OrbitAndExciter.FeelAddPerSecondWhenOrbit?.Value ?? MinPerStageBudget;
            if (v <= 0f)
                return MinPerStageBudget; // 協助開時不允許完全不加（避免卡死）
            return Mathf.Max(v, MinPerStageBudget);
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
                    OrbitManualDirector.OnHSceneEntered(hProbe);
                }
                OrbitPoseDirector.TickStuckRecovery(hProbe);
                OrbitVoiceTour.Tick(hProbe);
                OrbitFsmFlow.Tick(hProbe);
                OrbitStateMachineLog.Tick(hProbe);
            }
            else
            {
                if (_manualDirectorHSceneId != -1)
                    OrbitVoiceTour.OnHSceneExited();
                _manualDirectorHSceneId = -1;
                OrbitPoseDirector.Reset();
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
            // Block Ctrl/Alt chords (orbit uses Ctrl+Shift+O). Shift alone is reserved for Shift+T tattoo off.
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                return;
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
                return;
            if (Time.unscaledTime - _lastHotkeyTime < HotkeyCooldownSeconds)
                return;

            // T / Shift+T before the bare-Shift gate used by G/H/J/…
            if (Input.GetKeyDown(OrbitManualHotkeys.TattooKey))
            {
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (shift)
                    OrbitOrgasmTattoo.Disable();
                else
                    OrbitOrgasmTattoo.EnableAndStamp();
                _lastHotkeyTime = Time.unscaledTime;
                return;
            }

            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                return;

            if (Input.GetKeyDown(OrbitManualHotkeys.CharaKey))
            {
                bool ok = OrbitManualDirector.TrySwapFemale0(hScene, this);
                OrbitStateMachineLog.Hotkey("G", ok, ok ? "swap" : "reject");
                if (ok)
                    _lastHotkeyTime = Time.unscaledTime;
                return;
            }
            if (Input.GetKeyDown(OrbitManualHotkeys.CoordinateKey))
            {
                bool ok = OrbitManualDirector.TrySwapCoordinate(hScene, this);
                OrbitStateMachineLog.Hotkey("H", ok, ok ? "coord" : "reject");
                if (ok)
                    _lastHotkeyTime = Time.unscaledTime;
                return;
            }
            if (Input.GetKeyDown(OrbitManualHotkeys.WearKey))
            {
                bool ok = OrbitManualDirector.TryRandomWear(hScene);
                OrbitStateMachineLog.Hotkey("J", ok, ok ? "wear" : "reject");
                if (ok)
                    _lastHotkeyTime = Time.unscaledTime;
                return;
            }
            if (Input.GetKeyDown(OrbitManualHotkeys.PoseCameraKey))
            {
                bool ok = OrbitManualDirector.TryCyclePoseCamera(hScene, this);
                OrbitStateMachineLog.Hotkey("K", ok, ok ? "cam" : "reject");
                if (ok)
                    _lastHotkeyTime = Time.unscaledTime;
                return;
            }
            if (Input.GetKeyDown(OrbitManualHotkeys.PoseKey))
            {
                bool ok = OrbitManualDirector.TryChangePose(hScene);
                string detail = ok ? "pose" : OrbitManualDirector.DescribeHotkeyBlockReason(hScene);
                if (!ok && (detail == OrbitAssistReasons.None || string.IsNullOrEmpty(detail)))
                    detail = OrbitPoseDirector.LastHotkeyFailReason;
                OrbitStateMachineLog.Hotkey("L", ok, detail);
                if (ok)
                    _lastHotkeyTime = Time.unscaledTime;
                return;
            }
            if (Input.GetKeyDown(OrbitManualHotkeys.StartSexKey))
            {
                bool ok = OrbitFsmFlow.HandleN(hScene);
                OrbitStateMachineLog.Hotkey("N", ok, ok ? "往前推" : "reject");
                if (ok)
                    _lastHotkeyTime = Time.unscaledTime;
                return;
            }
            if (Input.GetKeyDown(OrbitManualHotkeys.BustRestoreKey))
            {
                if (OrbitOrgasmBustGrowth.TryRestore(hScene))
                    _lastHotkeyTime = Time.unscaledTime;
                return;
            }
            // YUIOP：I＝清腹；P＝狀態面板；O＝只停／恢復環視轉動（協助照常）
            if (Input.GetKeyDown(OrbitManualHotkeys.BellyResetKey))
            {
                if (PregnancyPlusAssist.TryResetBelly(hScene))
                    _lastHotkeyTime = Time.unscaledTime;
                return;
            }
            if (Input.GetKeyDown(OrbitManualHotkeys.StatusHudKey))
            {
                if (HS2OrbitAndExciter.OrbitStatusHudEnabled?.Value != false
                    && (OrbitBehaviorHub.IsOrbitAssistActive()
                        || OrbitVoiceTour.IsActive
                        || (OrbitVoiceTour.Enabled && hScene != null)))
                {
                    OrbitStatusHud.SetPanelVisible(!OrbitStatusHud.GetPanelVisible());
                    _lastHotkeyTime = Time.unscaledTime;
                }
                return;
            }
            if (Input.GetKeyDown(OrbitManualHotkeys.StopOrbitCameraKey)
                && OrbitBehaviorHub.IsOrbitAssistActive())
            {
                OrbitBehaviorHub.ToggleOrbitCameraSpinning();
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
                if (OrbitPoseDirector.ShouldFreezeCycleCounters)
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

            // 貫徹原版穿牆透明（圖像 Shield）
            try { ctrl.ConfigVanish = Manager.Config.GraphicData.Shield; } catch { /* ignore */ }

            float orbitTime = HS2OrbitAndExciter.OrbitTimePer360?.Value ?? 10f;
            if (orbitTime <= 0f) orbitTime = 10f;
            float speedDegPerSec = 360f / orbitTime;
            float dt = Time.deltaTime;

            bool spinning = OrbitBehaviorHub.IsOrbitCameraSpinning()
                            && !OrbitManualDirector.IsCameraPaused;

            if (spinning)
            {
                bool freezeCycle = OrbitPoseDirector.ShouldFreezeCycleCounters;
                if (_orbitPhase == 0)
                {
                    _orbitAccumulatedDegrees += speedDegPerSec * dt;
                    if (_orbitAccumulatedDegrees >= 360f)
                    {
                        _orbitAccumulatedDegrees = 360f;
                        _orbitPhase = 1;
                        if (!freezeCycle)
                        {
                            _rotationCount++;
                            OnRotationBoundary(hScene, ctrl, allowNewRelativeAngle: true, roundTripJustCompleted: false);
                        }
                    }
                }
                else
                {
                    _orbitAccumulatedDegrees -= speedDegPerSec * dt;
                    if (_orbitAccumulatedDegrees <= 0f)
                    {
                        _orbitAccumulatedDegrees = 0f;
                        _orbitPhase = 0;
                        if (!freezeCycle)
                        {
                            _rotationCount++;
                            _roundTripCount++;
                            OnRotationBoundary(hScene, ctrl, allowNewRelativeAngle: true, roundTripJustCompleted: true);
                        }
                    }
                }

                // 空檔預算下一圈相對角＋zoom（軸變則作廢）
                MaybePrecomputeNextCircle(hScene);

                ApplyBodyAxisCamera(hScene, ctrl);
            }
            else
            {
                // 停轉：仍每幀重綁骨焦點，保留看部位與距離
                ApplyBoneFocusOnly(hScene, ctrl);
            }

            RefreshHudSnapshot(hScene, orbitTime, speedDegPerSec);
        }

        private void OnRotationBoundary(
            HScene hScene,
            CameraControl_Ver2 ctrl,
            bool allowNewRelativeAngle,
            bool roundTripJustCompleted)
        {
            OrbitCycleCoordinator.ApplyRotationEffects(
                this, hScene, ctrl, _rotationCount, allowNewRelativeAngle, roundTripJustCompleted);

            // 每圈 zoom in 或 out
            if (_plannedZoomMult.HasValue)
            {
                _circleZoomMult = _plannedZoomMult.Value;
                _plannedZoomMult = null;
            }
            else
            {
                _circleZoomMult = RollCircleZoomMult();
            }

            if (allowNewRelativeAngle)
            {
                float delta;
                if (_plannedRelativeDelta.HasValue && IsPlannedAxisStillValid(hScene))
                {
                    delta = _plannedRelativeDelta.Value;
                    _plannedRelativeDelta = null;
                }
                else
                {
                    delta = OrbitBodyAxis.RollRelativeDeltaDegrees();
                    _plannedRelativeDelta = null;
                }
                _startRelativeAzimuth = NormalizeDeg(_startRelativeAzimuth + delta);
            }
        }

        private void MaybePrecomputeNextCircle(HScene hScene)
        {
            // 在一圈過半後預算，不卡遊戲
            if (_plannedRelativeDelta.HasValue && _plannedZoomMult.HasValue)
                return;
            if (_orbitAccumulatedDegrees < 180f && _orbitPhase == 0)
                return;
            if (_orbitAccumulatedDegrees > 180f && _orbitPhase == 1)
                return;

            var cha = OrbitHelpers.GetChaFemales(hScene);
            var basis = OrbitBodyAxis.TryBuild(cha, BoneFocusIndex(), _lastValidFocusWorld);
            if (!basis.Valid)
                return;

            if (!_plannedRelativeDelta.HasValue)
            {
                _plannedRelativeDelta = OrbitBodyAxis.RollRelativeDeltaDegrees();
                _plannedAxisSnapshot = basis.TorsoUp;
            }
            if (!_plannedZoomMult.HasValue)
                _plannedZoomMult = RollCircleZoomMult();
        }

        private bool IsPlannedAxisStillValid(HScene hScene)
        {
            var cha = OrbitHelpers.GetChaFemales(hScene);
            var basis = OrbitBodyAxis.TryBuild(cha, BoneFocusIndex(), _lastValidFocusWorld);
            if (!basis.Valid)
                return false;
            return Vector3.Dot(basis.TorsoUp, _plannedAxisSnapshot) > 0.85f;
        }

        private static float RollCircleZoomMult()
        {
            // 近≥下限、遠端拉大：約 0.92～1.35 相對設定距離
            bool zoomOut = UnityEngine.Random.value < 0.5f;
            return zoomOut
                ? UnityEngine.Random.Range(1.12f, 1.38f)
                : UnityEngine.Random.Range(0.92f, 1.05f);
        }

        private int BoneFocusIndex()
        {
            var h = OrbitController.TryGetHScene();
            var cha = h != null ? OrbitHelpers.GetChaFemales(h) : null;
            int maxFocus = OrbitHelpers.GetMaxFocusIndex(cha);
            int opt = _currentViewOption;
            if (opt < 0 || opt >= maxFocus)
                opt = 1; // 預設胸
            return opt;
        }

        private void ApplyBodyAxisCamera(HScene hScene, CameraControl_Ver2 ctrl)
        {
            var chaFemales = OrbitHelpers.GetChaFemales(hScene);
            if (chaFemales == null || ctrl.transBase == null)
                return;

            int focusIdx = BoneFocusIndex();
            var basis = OrbitBodyAxis.TryBuild(chaFemales, focusIdx, _lastValidFocusWorld);
            if (!basis.Valid)
                return;

            _lastValidFocusWorld = basis.FocusWorld;
            Vector3 localFocus = ctrl.transBase.InverseTransformPoint(basis.FocusWorld);
            ctrl.TargetPos = localFocus;

            float azimuth = NormalizeDeg(_startRelativeAzimuth + _orbitAccumulatedDegrees);
            float pitch = OrbitBodyAxis.PitchForFocus(focusIdx);
            Vector3 dirWorld = OrbitBodyAxis.DirectionFromBodyAzimuth(basis, azimuth, pitch);

            // 轉成相對 transBase 的歐拉：只寫 Rot，讓 CameraUpdate 套用
            Vector3 dirLocal = ctrl.transBase.InverseTransformDirection(dirWorld);
            if (dirLocal.sqrMagnitude < 1e-6f)
                return;
            Quaternion look = Quaternion.LookRotation(dirLocal, ctrl.transBase.InverseTransformDirection(basis.TorsoUp));
            Vector3 euler = look.eulerAngles;
            ctrl.Rot = euler;

            SetDistanceForFocus(ctrl, chaFemales, focusIdx, _circleZoomMult);
        }

        private void ApplyBoneFocusOnly(HScene hScene, CameraControl_Ver2 ctrl)
        {
            var chaFemales = OrbitHelpers.GetChaFemales(hScene);
            if (chaFemales == null || ctrl.transBase == null)
                return;
            int focusIdx = BoneFocusIndex();
            var basis = OrbitBodyAxis.TryBuild(chaFemales, focusIdx, _lastValidFocusWorld);
            if (!basis.Valid)
                return;
            _lastValidFocusWorld = basis.FocusWorld;
            ctrl.TargetPos = ctrl.transBase.InverseTransformPoint(basis.FocusWorld);
            SetDistanceForFocus(ctrl, chaFemales, focusIdx, _circleZoomMult);
        }

        private static float NormalizeDeg(float deg)
        {
            deg %= 360f;
            if (deg < 0f) deg += 360f;
            return deg;
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

            OrbitBehaviorHub.CanAutoAdvance(hScene.ctrlFlag, out string reason);
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
                OrbitManualDirector.IsCameraPaused);
            _hudSnapshotValid = true;
        }

        /// <summary>Minimum camera distance in body-height units (legacy configs often had 0.3; too tight for full-body framing).</summary>
        private const float OrbitDistanceMultMin = 1.35f;
        private const float OrbitDistanceMultMax = 3f;

        /// <summary>Set camera distance = body height × config × circle zoom. Call after setting TargetPos.</summary>
        private static void SetDistanceForFocus(
            CameraControl_Ver2 ctrl,
            ChaControl[]? chaFemales,
            int focusIndex,
            float circleZoomMult = 1f)
        {
            if (chaFemales == null || chaFemales.Length == 0) return;
            int femaleIdx = focusIndex < 3 ? 0 : 1;
            float bodyHeight = OrbitHelpers.GetBodyHeight(chaFemales, femaleIdx);
            float mult = 1f;
            if (focusIndex == 0 || focusIndex == 3) mult = HS2OrbitAndExciter.OrbitDistanceHead?.Value ?? 1.4f;
            else if (focusIndex == 1 || focusIndex == 4) mult = HS2OrbitAndExciter.OrbitDistanceChest?.Value ?? 1.4f;
            else mult = HS2OrbitAndExciter.OrbitDistancePelvis?.Value ?? 1.4f;
            if (mult < 1f)
                mult = OrbitDistanceMultMin;
            mult = Mathf.Clamp(mult, OrbitDistanceMultMin, OrbitDistanceMultMax);
            mult *= Mathf.Clamp(circleZoomMult, 0.85f, 1.45f);
            float d = bodyHeight * mult;
            float minD = OrbitDistanceMultMin * bodyHeight;
            float maxD = OrbitDistanceMultMax * 1.25f * bodyHeight; // 遠端行程拉大
            d = Mathf.Clamp(d, minD, maxD);
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

            SetDistanceForFocus(ctrl, chaFemales, newOpt, _circleZoomMult);
            _currentViewOption = newOpt;
            // 1A：立刻用骨焦點
            ApplyBoneFocusOnly(hScene, ctrl);
        }

        /// <summary>§11 1A：骨焦點優先；失敗回退上一有效焦點。環視中不用姿預設相機。</summary>
        private void ApplyCurrentViewOption(HScene hScene, CameraControl_Ver2 ctrl)
        {
            var chaFemales = OrbitHelpers.GetChaFemales(hScene);
            if (chaFemales == null || ctrl == null) return;
            int maxFocus = OrbitHelpers.GetMaxFocusIndex(chaFemales);
            if (maxFocus <= 0) return;

            int option = _currentViewOption;
            // 環視轉動中：強制骨焦點（頭／胸／骨盆），不用姿預設
            if (option < 0 || option >= maxFocus)
                option = 1;
            _currentViewOption = option;

            var basis = OrbitBodyAxis.TryBuild(chaFemales, option, _lastValidFocusWorld);
            if (basis.Valid && ctrl.transBase != null)
            {
                _lastValidFocusWorld = basis.FocusWorld;
                ctrl.TargetPos = ctrl.transBase.InverseTransformPoint(basis.FocusWorld);
                SetDistanceForFocus(ctrl, chaFemales, option, _circleZoomMult);
                return;
            }

            // 回退：若有上一有效點仍用；否則胸
            if (_lastValidFocusWorld.HasValue && ctrl.transBase != null)
            {
                ctrl.TargetPos = ctrl.transBase.InverseTransformPoint(_lastValidFocusWorld.Value);
                SetDistanceForFocus(ctrl, chaFemales, 1, _circleZoomMult);
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
            if (maxFocus <= 0)
                return;
            int current = _currentViewOption;
            if (current < 0 || current >= maxFocus) current = 0;
            var candidates = new List<int>();
            for (int i = 0; i < maxFocus; i++)
                if (i != current)
                    candidates.Add(i);
            // §11：幾乎只用骨焦點；極低機率才碰姿預設（且立即被 Apply 改回骨）
            if (candidates.Count > 0)
                _currentViewOption = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            else
                _currentViewOption = current;
            ApplyCurrentViewOption(hScene, ctrl);
        }

        /// <summary>§11：相對角改由每圈邊界套用；此處保留給 Coordinator 呼叫相容。</summary>
        internal void InternalRandomizeStartOrbitY()
        {
            // no-op：避免與 OnRotationBoundary 重複加 Δ
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
            _currentViewOption = maxFocus > 1 ? 1 : 0;
            _startRelativeAzimuth = NormalizeDeg(ctrl.CameraAngle.y);
            _orbitAccumulatedDegrees = 0f;
        }

        /// <summary>Rebind camera after pose transition completes (called by <see cref="OrbitPoseDirector"/>).</summary>
        internal void InternalRebindAfterPoseChange(HScene hScene, CameraControl_Ver2 ctrl)
        {
            var chaFemales = OrbitHelpers.GetChaFemales(hScene);
            int maxFocus = OrbitHelpers.GetMaxFocusIndex(chaFemales);
            if (_currentViewOption >= maxFocus)
                _currentViewOption = maxFocus > 1 ? 1 : 0;
            ApplyCurrentViewOption(hScene, ctrl);
            _startRelativeAzimuth = NormalizeDeg(ctrl.CameraAngle.y);
            _plannedRelativeDelta = null;
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
                try { ctrl.ConfigVanish = Manager.Config.GraphicData.Shield; } catch { /* ignore */ }
                _startRelativeAzimuth = NormalizeDeg(ctrl.CameraAngle.y);
                _orbitPhase = 0;
                _orbitAccumulatedDegrees = 0f;
                _rotationCount = 0;
                _roundTripCount = 0;
                _circleZoomMult = 1f;
                _plannedRelativeDelta = null;
                _plannedZoomMult = null;
                _lastValidFocusWorld = null;
                var chaFemales = OrbitHelpers.GetChaFemales(hScene);
                _currentClothesSequenceIndex = OrbitHelpers.GetClothesSequenceIndexFromCurrent(chaFemales);
                int maxFocus = OrbitHelpers.GetMaxFocusIndex(chaFemales);
                // §11：開環視一律骨焦點（胸）
                _currentViewOption = maxFocus > 1 ? 1 : 0;
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

        /// <summary>
        /// Runs after H-scene proc (see OrbitHSceneLateAssist). Do not call from early LateUpdate.
        /// Order: pose invariants (Sanitize→Resolve→Kick→Landed) → wheel latch → escape ticks → assist.
        /// </summary>
        internal void RunLateHSceneAssist(HScene hScene)
        {
            if (!OrbitBehaviorHub.IsOrbitAssistActive() || hScene == null) return;
            OrbitBehaviorHub.TickPoseFlagRecovery(hScene);
            OrbitBehaviorHub.TickUserWheelEscape();
            OrbitBehaviorHub.TickMotionEscapeLatch(hScene);
            OrbitBehaviorHub.TickAfterIdleEscape(hScene);
            OrbitBehaviorHub.TickIdleEscape(hScene);
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
