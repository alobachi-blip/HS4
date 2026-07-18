using AIChara;
using Manager;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// Drives orbit camera in H scene: Ctrl+Shift+O. Three spin axes (torso / world-up / body-lateral)
    /// alternate each rotation; no zoom/pitch shake. O pauses spin; Ctrl+Shift+O off restores user camera.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class OrbitController : MonoBehaviour
    {
        private const KeyCode OrbitHotkey = KeyCode.O;
        private const KeyCode Modifier = KeyCode.LeftShift;
        private const KeyCode Modifier2 = KeyCode.LeftControl;

        /// <summary>
        /// Unambiguous vanilla camera keys that hand camera ownership back to the
        /// player. Mouse buttons, wheel, and Q/W/E are deliberately excluded
        /// because HScene also uses them for UI, pacing, and orbit focus.
        /// </summary>
        private static readonly KeyCode[] NativeCameraTakeoverKeys =
        {
            KeyCode.R,
            KeyCode.Keypad5,
            KeyCode.Slash,
            KeyCode.Semicolon,
            KeyCode.Home,
            KeyCode.End,
            KeyCode.RightArrow,
            KeyCode.LeftArrow,
            KeyCode.UpArrow,
            KeyCode.DownArrow,
            KeyCode.PageUp,
            KeyCode.PageDown,
            KeyCode.Period,
            KeyCode.Backslash,
            KeyCode.Keypad2,
            KeyCode.Keypad8,
            KeyCode.Keypad4,
            KeyCode.Keypad6,
            KeyCode.Equals,
            KeyCode.RightBracket,
        };

        /// <summary>While assist is on: true = block vanilla mouse/keyboard camera (NoCtrlCondition).</summary>
        private static readonly BaseCameraControl_Ver2.NoCtrlFunc NoCtrlOrbit = () => true;
        /// <summary>Assist off: false = allow vanilla mouse/keyboard camera.</summary>
        private static readonly BaseCameraControl_Ver2.NoCtrlFunc NoCtrlUser = () => false;

        private const float HotkeyCooldownSeconds = 0.25f;
        /// <summary>§11：環視期間幾乎只用骨焦點（舊 0.8 姿預設相機退役）。</summary>
        private const float PreferGameDefaultCameraChance = 0.05f;

        private float _lastHotkeyTime = -999f;
        private int _manualDirectorHSceneId = -1;

        /// <summary>本圈起始：相機相對當前繞軸的方位角（度）。</summary>
        private float _startRelativeAzimuth;
        private OrbitBodyAxis.OrbitAxisMode _orbitAxisMode = OrbitBodyAxis.OrbitAxisMode.Torso;
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

        private bool _waitingForPrepStart;
        private float _prepCountdownStart;
        private float _prepFrozenElapsed = -1f;
        private const float PrepWaitSeconds = 3f;

        private bool _hudSnapshotValid;
        private OrbitHudSnapshot _hudSnapshot;
        private bool _wasOrbitCameraSpinning;

        /// <summary>§11 1A：上一有效骨焦點（世界座標），失敗時回退。</summary>
        private Vector3? _lastValidFocusWorld;
        /// <summary>提前算好的下一圈：軸模式＋起始方位角。</summary>
        private OrbitBodyAxis.OrbitAxisMode? _plannedAxisMode;
        private float? _plannedStartAzimuth;
        private Vector3 _plannedAxisSnapshot;
        /// <summary>本圈距離倍率（相對焦點距離設定；1＝不 zoom）。</summary>
        private float _circleZoomMult = 1f;
        private float? _plannedZoomMult;

        /// <summary>
        /// 圈／焦點鎖定基準（相對 objBodyBone 本地）。換焦／換軸時採樣一次；
        /// 圈內不跟頭／胸等活骨頭，避免手部／姿勢動畫晃鏡頭（不靠平滑）。
        /// </summary>
        private bool _lockedBasisValid;
        private int _lockedFemaleIdx;
        private Vector3 _lockedFocusLocal;
        private Vector3 _lockedTorsoUpLocal;
        private Vector3 _lockedFacingLocal;
        private Vector3 _lockedRightLocal;
        /// <summary>上一幀相機 up（世界），用於視線接近鉛垂時短暫鎖定。</summary>
        private Vector3? _previousCameraUpWorld;

        private static float GetOrbitFeelAddPerSecond()
        {
            // 尊重使用者設定（含 0.001）；0＝不加（只靠遊戲／滑鼠）
            var v = HS2OrbitAndExciter.FeelAddPerSecondWhenOrbit?.Value ?? 0.1f;
            if (v < 0f)
                return 0f;
            return v;
        }

        private static bool IsStoryboardSafeCameraEnabled() =>
            HS2OrbitAndExciter.StoryboardPackageEnabled?.Value == true
            && (HS2OrbitAndExciter.StoryboardSafeCameraEnabled?.Value ?? true);

        private static float GetStoryboardSafeSpeedDegPerSec()
        {
            float shotSeconds = Mathf.Clamp(HS2OrbitAndExciter.StoryboardShotDurationSeconds?.Value ?? 4f, 3f, 6f);
            float maxDegrees = Mathf.Clamp(HS2OrbitAndExciter.StoryboardMaxOrbitDegreesPerShot?.Value ?? 12f, 0f, 45f);
            return maxDegrees <= 0f ? 0f : maxDegrees / shotSeconds;
        }

        private const float OrbitSpeedAddPerSecond = 0.35f;

        /// <summary>Whether orbit is currently active (for Harmony patches). Authoritative state is <see cref="OrbitBehaviorHub.IsOrbitAssistActive"/>.</summary>
        public static bool IsOrbitActive() => OrbitBehaviorHub.IsOrbitAssistActive();

        internal static bool SetOrbitAssistActive(bool active, string reason)
        {
            if (OrbitBehaviorHub.IsOrbitAssistActive() == active)
            {
                OrbitStateMachineLog.Event("orbit", active ? "enable_already" : "disable_already",
                    "{\"reason\":\"" + EscForJson(reason) + "\"}");
                return true;
            }

            OrbitBehaviorHub.NotifyOrbitToggled(active);
            if (_activeInstance == null)
            {
                OrbitStateMachineLog.Event("orbit", active ? "enable_no_controller" : "disable_no_controller",
                    "{\"reason\":\"" + EscForJson(reason) + "\"}");
                return false;
            }

            _activeInstance.OnOrbitToggled(active);
            OrbitStateMachineLog.Event("orbit", active ? "enable" : "disable",
                "{\"reason\":\"" + EscForJson(reason) + "\"}");
            return true;
        }

        private void OnEnable() => _activeInstance = this;

        private void OnDisable()
        {
            if (_activeInstance == this)
                _activeInstance = null;
        }

        private void OnApplicationQuit()
        {
            // Do not leave temporary bust/belly changes in character data when
            // Alt+F4 or another quit path closes the game directly from H.
            OrbitOrgasmBustGrowth.TryRestoreForLifecycle("application_quit");
            PregnancyPlusAssist.TryRestoreForLifecycle("application_quit");
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

            float speed = ctrlFlag.speed;
            if (IsInPreparationState(hScene) || (speed <= 0.01f && !OrbitHelpers.IsFirstFemaleInActionLoop(hScene)))
            {
                _waitingForPrepStart = false;
                _prepFrozenElapsed = -1f;
                ctrlFlag.speed = 1f;
            }
        }

        /// <summary>True when in preparation state: Idle/D_Idle and speed &lt;= 0.</summary>
        private static bool IsInPreparationState(HScene hScene)
        {
            if (hScene == null) return false;
            var ctrlFlag = hScene.ctrlFlag;
            if (ctrlFlag == null) return false;
            float speed = ctrlFlag.speed;
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
                float current = ctrlFlag.feel_f;
                float next = Mathf.Clamp01(current + addPerSec * Time.deltaTime);
                if (next > current)
                    ctrlFlag.feel_f = next;
            }
            ctrlFlag.speed = Mathf.Clamp(
                ctrlFlag.speed + OrbitSpeedAddPerSecond * Time.deltaTime,
                0f,
                2f);
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
                PregnancyPlusAssist.TickInsideFinish(hProbe);
                OrbitVoiceTour.Tick(hProbe);
                OrbitFsmFlow.Tick(hProbe);
                OrbitFinishDirector.Tick(hProbe);
                OrbitStateMachineLog.Tick(hProbe);
            }
            else
            {
                PregnancyPlusAssist.ResetInsideTracking();
                if (_manualDirectorHSceneId != -1)
                {
                    OrbitHelpers.ResetSceneCaches();
                    OrbitOrgasmBustGrowth.TryRestoreForLifecycle("h_scene_exit");
                    PregnancyPlusAssist.TryRestoreForLifecycle("h_scene_exit");
                    OrbitVoiceTour.OnHSceneExited();
                }
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
                SetOrbitAssistActive(active, "hotkey");
            }

            TryManualHotkeys(hProbe);
            TryYieldOrbitCameraToNativeInput(hProbe);
        }

        private static void TryYieldOrbitCameraToNativeInput(HScene? hScene)
        {
            if (hScene?.ctrlFlag == null
                || !OrbitBehaviorHub.IsOrbitCameraSpinning()
                || OrbitSettingsGUI.IsVisible
                || hScene.ctrlFlag.inputForcus)
            {
                return;
            }

            foreach (KeyCode key in NativeCameraTakeoverKeys)
            {
                if (!Input.GetKey(key))
                    continue;

                OrbitBehaviorHub.SetOrbitCameraSpinning(false);
                OrbitStateMachineLog.Event(
                    "orbit",
                    "native_camera_takeover",
                    "{\"key\":\"" + key + "\"}");
                return;
            }
        }

        private void TryManualHotkeys(HScene? hScene)
        {
            if (hScene == null || !OrbitBehaviorHub.IsOrbitAssistActive())
                return;
            // Block Ctrl/Alt chords (orbit uses Ctrl+Shift+O). Shift alone is reserved for Shift+T tattoo off.
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                return;
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
                return;
            if (Time.unscaledTime - _lastHotkeyTime < HotkeyCooldownSeconds)
                return;

            // T / Shift+T and G / Shift+G before the bare-Shift gate used by H/J/…
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

            if (Input.GetKeyDown(OrbitManualHotkeys.CharaKey))
            {
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                bool ok = OrbitManualDirector.TrySwapFemale0(hScene, this, lowerCurrentWeight: shift);
                OrbitStateMachineLog.Hotkey(shift ? "Shift+G" : "G", ok, ok ? (shift ? "lower_swap" : "swap") : "reject");
                if (ok)
                    _lastHotkeyTime = Time.unscaledTime;
                return;
            }

            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                return;
            if (Input.GetKeyDown(OrbitManualHotkeys.SceneKey))
            {
                bool ok = OrbitManualDirector.TrySwapScene(hScene, this);
                OrbitStateMachineLog.Hotkey("F", ok, ok ? "scene" : OrbitManualDirector.DescribeHotkeyBlockReason(hScene));
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
                StoryboardPackageRecorder.NotifyOrbitToggled(false, this, null);
                _hudSnapshotValid = false;
                _requestViewReapplyNextFrame = false;
                return;
            }

            var hScene = GetHScene();
            if (hScene == null)
            {
                StoryboardPackageRecorder.NotifyOrbitToggled(false, this, null);
                _hudSnapshotValid = false;
                return;
            }

            var ctrl = hScene.ctrlFlag?.cameraCtrl as CameraControl_Ver2;
            if (ctrl == null)
            {
                StoryboardPackageRecorder.NotifyOrbitToggled(false, this, hScene);
                _hudSnapshotValid = false;
                return;
            }

            bool orbitCameraRequested = OrbitBehaviorHub.IsOrbitCameraSpinning();
            bool cameraPausedForUi = OrbitBehaviorHub.ShouldPauseOrbitCameraForUi();
            if (orbitCameraRequested && !cameraPausedForUi)
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
                            ctrlFlag.speed = 1f;
                    }
                }
            }

            // Excitement gauge auto-accumulates while orbit is active (skipped during prep countdown)
            AccumulateFeelWhenOrbit(hScene);
            OrbitSessionDirector.Tick(hScene);

            // ApplyOrbitAutoAction / TryAutoAdvancePastCheckpoint: see OrbitHSceneLateAssist (runs after H proc)

            bool cameraPausedForDirector = OrbitManualDirector.IsCameraPaused;
            bool userOwnsCamera = !orbitCameraRequested
                                  && !cameraPausedForDirector
                                  && !cameraPausedForUi;

            // O 停轉後完整交還原版相機；設定窗／換姿過場仍保持輸入隔離。
            ctrl.NoCtrlCondition = userOwnsCamera ? NoCtrlUser : NoCtrlOrbit;

            if (orbitCameraRequested && !_wasOrbitCameraSpinning)
                RebaseOrbitFromCurrentCamera(hScene, ctrl);
            _wasOrbitCameraSpinning = orbitCameraRequested;

            // When pose changes (UI manual / game), defer rebind to Director instead of immediate ApplyCurrentViewOption
            var nowInfo = hScene.ctrlFlag?.nowAnimationInfo;
            if (nowInfo != null && !ReferenceEquals(nowInfo, _lastNowAnimationInfoRef))
                OrbitPoseDirector.NotifyExternalPoseChange(hScene);

            // After faintness toggle or other state change: reapply view once
            if (_requestViewReapplyNextFrame && !userOwnsCamera)
            {
                _requestViewReapplyNextFrame = false;
                ApplyCurrentViewOption(hScene, ctrl);
                _lastNowAnimationInfoRef = hScene.ctrlFlag?.nowAnimationInfo;
            }

            // 環視協助中強制穿牆 hide（Shield 關了也開）；地圖 Collider 補進 vanish
            try { ctrl.ConfigVanish = true; } catch { /* ignore */ }
            OrbitMapVanishAssist.EnsureInjected(hScene);

            float orbitTime = HS2OrbitAndExciter.OrbitTimePer360?.Value ?? 10f;
            if (orbitTime <= 0f) orbitTime = 10f;
            float speedDegPerSec = 360f / orbitTime;
            bool storyboardSafeCamera = IsStoryboardSafeCameraEnabled();
            if (storyboardSafeCamera)
                speedDegPerSec = Mathf.Min(speedDegPerSec, GetStoryboardSafeSpeedDegPerSec());
            float dt = Time.deltaTime;

            bool spinning = orbitCameraRequested
                            && !cameraPausedForDirector
                            && !cameraPausedForUi;

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
                            OrbitOcclusion20Test.OnRotationBoundary(_rotationCount);
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
                            OrbitOcclusion20Test.OnRotationBoundary(_rotationCount);
                            OnRotationBoundary(hScene, ctrl, allowNewRelativeAngle: true, roundTripJustCompleted: true);
                        }
                    }
                }

                // 空檔預算下一圈軸＋起始角（軸變則作廢）
                if (OrbitPoseDirector.IsPoseChangeInFlight)
                {
                    ApplyLiveBoneFocusOnly(hScene, ctrl, "pose_transition");
                }
                else
                {
                    if (!storyboardSafeCamera)
                        MaybePrecomputeNextCircle(hScene);
                    ApplyBodyAxisCamera(hScene, ctrl);
                }
                OrbitOcclusion20Test.Sample(hScene, ctrl, BoneFocusIndex());
                MaybeDetectFocusJump(hScene, ctrl);
                MaybeLogFramingDiag(hScene, ctrl);
            }
            else if (!userOwnsCamera)
            {
                // 設定窗／換姿過場暫停：保留焦點，但不改 Rot。
                if (OrbitPoseDirector.IsPoseChangeInFlight)
                    ApplyLiveBoneFocusOnly(hScene, ctrl, "pose_transition");
                else
                    ApplyBoneFocusOnly(hScene, ctrl);
                OrbitOcclusion20Test.Sample(hScene, ctrl, BoneFocusIndex());
                MaybeDetectFocusJump(hScene, ctrl);
                MaybeLogFramingDiag(hScene, ctrl);
            }

            RefreshHudSnapshot(hScene, orbitTime, speedDegPerSec);
            StoryboardPackageRecorder.Tick(
                this,
                hScene,
                ctrl,
                spinning,
                _orbitPhase,
                _orbitAccumulatedDegrees,
                _rotationCount,
                _roundTripCount,
                _currentViewOption,
                storyboardSafeCamera ? OrbitBodyAxis.OrbitAxisMode.WorldVertical : _orbitAxisMode,
                0f,
                storyboardSafeCamera ? 1f : _circleZoomMult);
        }

        private void OnRotationBoundary(
            HScene hScene,
            CameraControl_Ver2 ctrl,
            bool allowNewRelativeAngle,
            bool roundTripJustCompleted)
        {
            OrbitCycleCoordinator.ApplyRotationEffects(
                this, hScene, ctrl, _rotationCount, allowNewRelativeAngle, roundTripJustCompleted);

            // 每圈 zoom（可關；幅度由設定 Near／Far 決定）
            if (IsStoryboardSafeCameraEnabled())
            {
                _orbitAxisMode = OrbitBodyAxis.OrbitAxisMode.WorldVertical;
                _plannedAxisMode = null;
                _plannedZoomMult = null;
                _circleZoomMult = 1f;
                return;
            }

            if (HS2OrbitAndExciter.OrbitCircleZoomEnabled?.Value == true)
            {
                if (_plannedZoomMult.HasValue)
                {
                    _circleZoomMult = _plannedZoomMult.Value;
                    _plannedZoomMult = null;
                }
                else
                    _circleZoomMult = RollCircleZoomMult();
            }
            else
            {
                _circleZoomMult = 1f;
                _plannedZoomMult = null;
            }

            if (allowNewRelativeAngle)
            {
                if (_plannedAxisMode.HasValue && _plannedStartAzimuth.HasValue && IsPlannedAxisStillValid(hScene))
                {
                    _orbitAxisMode = _plannedAxisMode.Value;
                    _startRelativeAzimuth = NormalizeDeg(_plannedStartAzimuth.Value);
                }
                else
                {
                    _orbitAxisMode = OrbitBodyAxis.PickNextMode(_orbitAxisMode);
                    _startRelativeAzimuth = OrbitBodyAxis.RollAnyAzimuthDegrees();
                }
                _plannedAxisMode = null;
                _plannedStartAzimuth = null;
                InvalidateLockedBasis("換軸");
                OrbitStateMachineLog.Event("環視", "換軸", OrbitBodyAxis.ModeLabel(_orbitAxisMode));
            }
        }

        private void MaybePrecomputeNextCircle(HScene hScene)
        {
            bool needAxis = !_plannedAxisMode.HasValue || !_plannedStartAzimuth.HasValue;
            bool needZoom = HS2OrbitAndExciter.OrbitCircleZoomEnabled?.Value == true && !_plannedZoomMult.HasValue;
            if (!needAxis && !needZoom)
                return;
            if (_orbitAccumulatedDegrees < 180f && _orbitPhase == 0)
                return;
            if (_orbitAccumulatedDegrees > 180f && _orbitPhase == 1)
                return;

            var cha = OrbitHelpers.GetChaFemales(hScene);
            var basis = OrbitBodyAxis.TryBuild(cha, BoneFocusIndex(), _lastValidFocusWorld);
            if (!basis.Valid)
                return;

            if (needAxis)
            {
                var next = OrbitBodyAxis.PickNextMode(_orbitAxisMode);
                _plannedAxisMode = next;
                _plannedStartAzimuth = OrbitBodyAxis.RollAnyAzimuthDegrees();
                _plannedAxisSnapshot = OrbitBodyAxis.SpinAxis(basis, next);
            }
            if (needZoom)
                _plannedZoomMult = RollCircleZoomMult();
        }

        private static float RollCircleZoomMult()
        {
            float near = HS2OrbitAndExciter.OrbitZoomNearMult?.Value ?? 0.65f;
            float far = HS2OrbitAndExciter.OrbitZoomFarMult?.Value ?? 1.75f;
            if (near > far)
            {
                float t = near;
                near = far;
                far = t;
            }
            near = Mathf.Max(0f, near);
            far = Mathf.Max(near, far);
            // 明顯拉近或拉遠（各半）；允許 <1 特寫與 >1 拉開
            float mid = Mathf.Lerp(near, far, 0.5f);
            return UnityEngine.Random.value < 0.5f
                ? UnityEngine.Random.Range(near, Mathf.Lerp(near, mid, 0.7f))
                : UnityEngine.Random.Range(Mathf.Lerp(mid, far, 0.3f), far);
        }

        private bool IsPlannedAxisStillValid(HScene hScene)
        {
            if (!_plannedAxisMode.HasValue)
                return false;
            var cha = OrbitHelpers.GetChaFemales(hScene);
            var basis = OrbitBodyAxis.TryBuild(cha, BoneFocusIndex(), _lastValidFocusWorld);
            if (!basis.Valid)
                return false;
            Vector3 axis = OrbitBodyAxis.SpinAxis(basis, _plannedAxisMode.Value);
            return Vector3.Dot(axis.normalized, _plannedAxisSnapshot.normalized) > 0.85f;
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
            if (!TryGetOrbitBasis(chaFemales, focusIdx, out var basis))
                return;

            bool storyboardSafeCamera = IsStoryboardSafeCameraEnabled();
            if (storyboardSafeCamera)
                _orbitAxisMode = OrbitBodyAxis.OrbitAxisMode.WorldVertical;
            _lastValidFocusWorld = basis.FocusWorld;
            ctrl.TargetPos = ctrl.transBase.InverseTransformPoint(basis.FocusWorld);

            float azimuth = NormalizeDeg(_startRelativeAzimuth + _orbitAccumulatedDegrees);
            Vector3 floorSky = OrbitFloorNormal.GetSkyward(hScene, basis.FocusWorld);
            Vector3 spinAxis = storyboardSafeCamera
                ? floorSky
                : OrbitBodyAxis.SpinAxis(basis, _orbitAxisMode);
            Vector3 dirWorld = OrbitBodyAxis.DirectionFromAxisAzimuth(basis, spinAxis, azimuth);
            Vector3 upWorld = OrbitBodyAxis.CameraUp(basis, dirWorld, floorSky, ref _previousCameraUpWorld);

            Vector3 dirLocal = ctrl.transBase.InverseTransformDirection(dirWorld);
            Vector3 upLocal = ctrl.transBase.InverseTransformDirection(upWorld);
            if (dirLocal.sqrMagnitude < 1e-6f)
                return;
            if (upLocal.sqrMagnitude < 1e-6f)
                upLocal = Vector3.up;
            Quaternion look = Quaternion.LookRotation(dirLocal.normalized, upLocal.normalized);
            ctrl.Rot = look.eulerAngles;

            SetDistanceForFocus(ctrl, chaFemales, focusIdx, _circleZoomMult);
        }

        /// <summary>O 恢復轉動時沿用使用者目前視角，避免跳回停轉前方位。</summary>
        private void RebaseOrbitFromCurrentCamera(HScene hScene, CameraControl_Ver2 ctrl)
        {
            var chaFemales = OrbitHelpers.GetChaFemales(hScene);
            if (chaFemales == null || !TryGetOrbitBasis(chaFemales, BoneFocusIndex(), out var basis))
                return;

            bool storyboardSafeCamera = IsStoryboardSafeCameraEnabled();
            if (storyboardSafeCamera)
                _orbitAxisMode = OrbitBodyAxis.OrbitAxisMode.WorldVertical;

            Vector3 camForward = ctrl.thisCamera != null
                ? ctrl.thisCamera.transform.forward
                : Quaternion.Euler(ctrl.CameraAngle) * Vector3.forward;
            Vector3 axis = storyboardSafeCamera
                ? OrbitFloorNormal.GetSkyward(hScene, basis.FocusWorld)
                : OrbitBodyAxis.SpinAxis(basis, _orbitAxisMode);

            _startRelativeAzimuth = OrbitBodyAxis.AzimuthMatchingDirection(basis, axis, camForward);
            _orbitPhase = 0;
            _orbitAccumulatedDegrees = 0f;
            _plannedAxisMode = null;
            _plannedStartAzimuth = null;
            _plannedZoomMult = null;
            _previousCameraUpWorld = null;
            InvalidateLockedBasis("user_camera_resume");
            OrbitStateMachineLog.Event("環視", "從使用者視角恢復轉動");
        }

        private void ApplyBoneFocusOnly(HScene hScene, CameraControl_Ver2 ctrl)
        {
            var chaFemales = OrbitHelpers.GetChaFemales(hScene);
            if (chaFemales == null || ctrl.transBase == null)
                return;
            int focusIdx = BoneFocusIndex();
            if (!TryGetOrbitBasis(chaFemales, focusIdx, out var basis))
                return;
            _lastValidFocusWorld = basis.FocusWorld;
            ctrl.TargetPos = ctrl.transBase.InverseTransformPoint(basis.FocusWorld);
            SetDistanceForFocus(ctrl, chaFemales, focusIdx, _circleZoomMult);
        }

        /// <summary>During pose transitions, follow the live bone focus until the new pose is stable enough to lock.</summary>
        private void ApplyLiveBoneFocusOnly(HScene hScene, CameraControl_Ver2 ctrl, string lockReason)
        {
            var chaFemales = OrbitHelpers.GetChaFemales(hScene);
            if (chaFemales == null || ctrl.transBase == null)
                return;
            int focusIdx = BoneFocusIndex();
            var basis = OrbitBodyAxis.TryBuild(chaFemales, focusIdx, _lastValidFocusWorld);
            if (!basis.Valid)
                return;

            InvalidateLockedBasis(lockReason);
            _lastValidFocusWorld = basis.FocusWorld;
            ctrl.TargetPos = ctrl.transBase.InverseTransformPoint(basis.FocusWorld);
            SetDistanceForFocus(ctrl, chaFemales, focusIdx, _circleZoomMult);
        }

        /// <summary>鎖定基準：採樣時用骨焦點；之後只跟身體根節點剛體位移，不跟部位動畫。</summary>
        private bool TryGetOrbitBasis(ChaControl[] chaFemales, int focusIdx, out OrbitBodyAxis.Basis basis)
        {
            basis = default;
            // The body root is stable across a pose change, but the selected head/chest/pelvis
            // bone is not: animations can move it several metres after NowChangeAnim clears.
            // Keep the locked axes for a smooth orbit, while always aiming at the live selected
            // bone.  Using _lockedFocusLocal here leaves TargetPos at the outgoing pose.
            var live = OrbitBodyAxis.TryBuild(chaFemales, focusIdx, _lastValidFocusWorld);
            if (!_lockedBasisValid)
            {
                if (!live.Valid)
                    return false;
                if (!TryLockBasis(chaFemales, focusIdx, live, _pendingLockReason ?? "fresh"))
                {
                    basis = live;
                    return true;
                }
                _pendingLockReason = null;
            }

            if (!TryResolveLockedBasis(chaFemales, out basis))
            {
                InvalidateLockedBasis("resolve_fail");
                if (!live.Valid)
                    return false;
                if (TryLockBasis(chaFemales, focusIdx, live, "resolve_fail_relock")
                    && TryResolveLockedBasis(chaFemales, out var relocked))
                {
                    basis = new OrbitBodyAxis.Basis(
                        live.FocusWorld,
                        relocked.TorsoUp,
                        relocked.Facing,
                        relocked.Right,
                        true);
                    return true;
                }
                basis = live;
                return true;
            }

            // Keep the selected bone's position relative to the body root for the
            // whole orbit leg.  The old behaviour used live.FocusWorld here, which
            // passed every animation jiggle directly into TargetPos and made large
            // actions uncomfortable to watch.  Pose transitions still use
            // ApplyLiveBoneFocusOnly until their new basis is locked.
            if (!live.Valid)
                return true;
            return true;
        }

        private static int FemaleIndexFromFocus(int focusIndex) => focusIndex < 3 ? 0 : 1;

        private static Transform? GetBodyRoot(ChaControl[]? chaFemales, int femaleIdx)
        {
            if (chaFemales == null || femaleIdx < 0 || femaleIdx >= chaFemales.Length)
                return null;
            var cha = chaFemales[femaleIdx];
            if (cha == null)
                return null;
            return cha.objBodyBone != null ? cha.objBodyBone.transform : cha.transform;
        }

        private bool TryLockBasis(
            ChaControl[] chaFemales,
            int focusIdx,
            OrbitBodyAxis.Basis live,
            string reason)
        {
            int femaleIdx = FemaleIndexFromFocus(focusIdx);
            var body = GetBodyRoot(chaFemales, femaleIdx);
            if (body == null)
                return false;

            _lockedFemaleIdx = femaleIdx;
            _lockedFocusLocal = body.InverseTransformPoint(live.FocusWorld);
            _lockedTorsoUpLocal = body.InverseTransformDirection(live.TorsoUp).normalized;
            _lockedFacingLocal = body.InverseTransformDirection(live.Facing).normalized;
            _lockedRightLocal = body.InverseTransformDirection(live.Right).normalized;
            _lockedBasisValid = true;
            _lockGen++;
            _lastLockReason = reason ?? "lock";
            _lastLockUnscaled = Time.unscaledTime;
            OrbitStateMachineLog.Event(
                "focus_lock",
                _lastLockReason,
                "{"
                + "\"gen\":" + _lockGen
                + ",\"focusIdx\":" + focusIdx
                + ",\"female\":" + femaleIdx
                + ",\"focusW\":[" + F3(live.FocusWorld.x) + "," + F3(live.FocusWorld.y) + "," + F3(live.FocusWorld.z) + "]"
                + ",\"rootW\":[" + F3(body.position.x) + "," + F3(body.position.y) + "," + F3(body.position.z) + "]"
                + ",\"local\":[" + F3(_lockedFocusLocal.x) + "," + F3(_lockedFocusLocal.y) + "," + F3(_lockedFocusLocal.z) + "]"
                + "}");
            return true;
        }

        private bool TryResolveLockedBasis(ChaControl[] chaFemales, out OrbitBodyAxis.Basis basis)
        {
            basis = default;
            if (!_lockedBasisValid)
                return false;
            var body = GetBodyRoot(chaFemales, _lockedFemaleIdx);
            if (body == null)
                return false;

            Vector3 focus = body.TransformPoint(_lockedFocusLocal);
            Vector3 torsoUp = body.TransformDirection(_lockedTorsoUpLocal).normalized;
            Vector3 facing = body.TransformDirection(_lockedFacingLocal).normalized;
            Vector3 right = body.TransformDirection(_lockedRightLocal).normalized;
            if (torsoUp.sqrMagnitude < 1e-6f || facing.sqrMagnitude < 1e-6f)
                return false;
            // 重建正交，避免長時間旋轉累積誤差
            right = Vector3.Cross(torsoUp, facing).normalized;
            if (right.sqrMagnitude < 1e-6f)
                right = body.TransformDirection(_lockedRightLocal).normalized;
            facing = Vector3.Cross(right, torsoUp).normalized;
            basis = new OrbitBodyAxis.Basis(focus, torsoUp, facing, right, true);
            return true;
        }

        private void InvalidateLockedBasis(string reason = "invalidate")
        {
            if (_lockedBasisValid)
            {
                OrbitStateMachineLog.Event(
                    "focus_lock",
                    "invalidate",
                    "{\"reason\":\"" + EscJson(reason) + "\",\"gen\":" + _lockGen + "}");
            }
            _lockedBasisValid = false;
            _pendingLockReason = reason;
        }

        private static string EscJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
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
            float tLeg = speedDegPerSec > 0.001f
                ? (_orbitPhase == 0
                    ? (360f - _orbitAccumulatedDegrees) / speedDegPerSec
                    : _orbitAccumulatedDegrees / speedDegPerSec)
                : 0f;
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

        private const float OrbitDistanceMultMin = 0f;
        private const float OrbitDistanceMultMax = 3f;
        private const float OrbitZoomMultFloor = 0.02f;
        /// <summary>鎖定焦點／身體根單幀位移超過此值 → 立刻打 focus_jump。</summary>
        private const float FocusJumpWarnMeters = 2f;

        private float _nextFramingLogUnscaled;
        private int _lockGen;
        private string _lastLockReason = "";
        private string? _pendingLockReason;
        private float _lastLockUnscaled;
        private bool _framingHavePrev;
        private Vector3 _prevFocusW;
        private Vector3 _prevRootW;
        private Vector3 _prevLiveChestW;
        private Vector3 _prevLiveHeadW;
        private Vector3 _prevLivePelvisW;

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
            mult = Mathf.Clamp(mult, OrbitDistanceMultMin, OrbitDistanceMultMax);
            float zoomLo = HS2OrbitAndExciter.OrbitZoomNearMult?.Value ?? 0.65f;
            float zoomHi = HS2OrbitAndExciter.OrbitZoomFarMult?.Value ?? 1.75f;
            if (zoomLo > zoomHi)
            {
                float t = zoomLo;
                zoomLo = zoomHi;
                zoomHi = t;
            }
            zoomLo = Mathf.Max(0f, zoomLo);
            zoomHi = Mathf.Max(zoomLo, zoomHi);
            float zoom = Mathf.Clamp(circleZoomMult, zoomLo, zoomHi);
            // 實際倍率勿為 0，否則相機落在目標點
            zoom = Mathf.Max(OrbitZoomMultFloor, zoom);
            mult = Mathf.Max(OrbitZoomMultFloor, mult) * zoom;
            float d = bodyHeight * mult;
            float minD = bodyHeight * OrbitZoomMultFloor;
            float maxD = OrbitDistanceMultMax * bodyHeight * Mathf.Max(1f, zoomHi);
            d = Mathf.Clamp(d, minD, maxD);
            ctrl.CameraDir = new Vector3(0f, 0f, -d);
        }

        private struct FramingBones
        {
            public Vector3 RootW;
            public Vector3 RootEuler;
            public bool HasRoot;
            public Vector3? Head;
            public Vector3? Chest;
            public Vector3? Pelvis;
            public string Clip;
            public string NowAnim;
            public string Director;
        }

        private FramingBones SampleFramingBones(HScene hScene, int focusIdx)
        {
            var r = new FramingBones
            {
                Clip = "?",
                NowAnim = "",
                Director = ""
            };

            int femaleIdx = focusIdx < 3 ? 0 : 1;
            var cha = OrbitHelpers.GetChaFemales(hScene);
            var body = GetBodyRoot(cha, femaleIdx);
            if (body != null)
            {
                r.HasRoot = true;
                r.RootW = body.position;
                r.RootEuler = body.eulerAngles;
            }
            if (cha != null)
            {
                r.Head = OrbitHelpers.GetBonePosition(cha, femaleIdx, OrbitHelpers.BoneHead);
                r.Chest = OrbitHelpers.GetBonePosition(cha, femaleIdx, OrbitHelpers.BoneChest);
                r.Pelvis = OrbitHelpers.GetBonePosition(cha, femaleIdx, OrbitHelpers.BonePelvis);
            }

            return r;
        }

        private static void PopulateFramingLabels(HScene hScene, int femaleIdx, ref FramingBones r)
        {
            r.Director = OrbitPoseDirector.DebugStateName ?? "";
            try
            {
                var flag = hScene.ctrlFlag;
                if (flag?.nowAnimationInfo != null)
                    r.NowAnim = flag.nowAnimationInfo.nameAnimation + "#id" + flag.nowAnimationInfo.id
                                + ";down" + flag.nowAnimationInfo.nDownPtn;
                var cha0 = OrbitHelpers.GetChaFemales(hScene);
                var c0 = cha0 != null && cha0.Length > 0 ? cha0[0] : null;
                if (c0?.animBody != null)
                    r.Clip = "h=" + c0.animBody.GetCurrentAnimatorStateInfo(0).fullPathHash;
            }
            catch { /* ignore */ }
            try
            {
                var cArr = OrbitHelpers.GetChaFemales(hScene);
                var c = cArr != null && femaleIdx < cArr.Length ? cArr[femaleIdx] : null;
                if (c?.animBody != null)
                {
                    var st = c.animBody.GetCurrentAnimatorStateInfo(0);
                    if (c.animBody.layerCount > 0)
                    {
                        // 嘗試讀取當前 state 名
                        var clips = c.animBody.GetCurrentAnimatorClipInfo(0);
                        if (clips != null && clips.Length > 0 && clips[0].clip != null)
                            r.Clip = clips[0].clip.name;
                        else
                            r.Clip = "h=" + st.fullPathHash + ";n=" + st.normalizedTime.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
            }
            catch { /* ignore */ }
        }

        private static string V3(Vector3 v) =>
            "[" + F3(v.x) + "," + F3(v.y) + "," + F3(v.z) + "]";

        private static string V3Opt(Vector3? v) =>
            v.HasValue ? V3(v.Value) : "null";

        /// <summary>每幀：鎖定焦點或身體根跳超過門檻立刻記 focus_jump。</summary>
        private void MaybeDetectFocusJump(HScene hScene, CameraControl_Ver2 ctrl)
        {
            if (!_lastValidFocusWorld.HasValue)
                return;

            int focusIdx = BoneFocusIndex();
            var bones = SampleFramingBones(hScene, focusIdx);
            Vector3 focusW = _lastValidFocusWorld.Value;
            Vector3 liveChest = bones.Chest ?? focusW;
            Vector3 liveHead = bones.Head ?? focusW;
            Vector3 livePelvis = bones.Pelvis ?? focusW;
            Vector3 rootW = bones.HasRoot ? bones.RootW : focusW;

            if (!_framingHavePrev)
            {
                _framingHavePrev = true;
                _prevFocusW = focusW;
                _prevRootW = rootW;
                _prevLiveChestW = liveChest;
                _prevLiveHeadW = liveHead;
                _prevLivePelvisW = livePelvis;
                return;
            }

            float dFocus = Vector3.Distance(focusW, _prevFocusW);
            float dRoot = Vector3.Distance(rootW, _prevRootW);
            float dChest = Vector3.Distance(liveChest, _prevLiveChestW);
            float dHead = Vector3.Distance(liveHead, _prevLiveHeadW);
            float dPelvis = Vector3.Distance(livePelvis, _prevLivePelvisW);
            float errLive = bones.Chest.HasValue
                ? Vector3.Distance(focusW, bones.Chest.Value)
                : -1f;

            bool jumped = dFocus >= FocusJumpWarnMeters || dRoot >= FocusJumpWarnMeters;
            if (jumped)
            {
                PopulateFramingLabels(hScene, focusIdx < 3 ? 0 : 1, ref bones);
                string json =
                    "{"
                    + "\"t\":" + Time.unscaledTime.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"dFocus\":" + F3(dFocus)
                    + ",\"dRoot\":" + F3(dRoot)
                    + ",\"dChest\":" + F3(dChest)
                    + ",\"dHead\":" + F3(dHead)
                    + ",\"dPelvis\":" + F3(dPelvis)
                    + ",\"errLiveChest\":" + F3(errLive)
                    + ",\"locked\":" + (_lockedBasisValid ? "true" : "false")
                    + ",\"lockGen\":" + _lockGen
                    + ",\"lockReason\":\"" + EscJson(_lastLockReason) + "\""
                    + ",\"lockAge\":" + F3(Time.unscaledTime - _lastLockUnscaled)
                    + ",\"focusIdx\":" + focusIdx
                    + ",\"clip\":\"" + EscJson(bones.Clip) + "\""
                    + ",\"nowAnim\":\"" + EscJson(bones.NowAnim) + "\""
                    + ",\"director\":\"" + EscJson(bones.Director) + "\""
                    + ",\"focusW0\":" + V3(_prevFocusW)
                    + ",\"focusW1\":" + V3(focusW)
                    + ",\"rootW0\":" + V3(_prevRootW)
                    + ",\"rootW1\":" + V3(rootW)
                    + ",\"rootEul\":" + V3(bones.RootEuler)
                    + ",\"chest0\":" + V3(_prevLiveChestW)
                    + ",\"chest1\":" + V3(liveChest)
                    + ",\"head1\":" + V3(liveHead)
                    + ",\"pelvis1\":" + V3(livePelvis)
                    + ",\"targetL\":" + V3(ctrl.TargetPos)
                    + ",\"local\":" + V3(_lockedFocusLocal)
                    + ",\"spin\":" + (OrbitBehaviorHub.IsOrbitCameraSpinning() ? "true" : "false")
                    + "}";
                OrbitStateMachineLog.Event("focus_jump", "warn", json);
                HS2OrbitAndExciter.Log?.LogWarning("[OrbitFocusJump] " + json);
            }

            _prevFocusW = focusW;
            _prevRootW = rootW;
            _prevLiveChestW = liveChest;
            _prevLiveHeadW = liveHead;
            _prevLivePelvisW = livePelvis;
        }

        /// <summary>
        /// 診斷「主角出畫面」：相對空間 + 根／live 骨。0.5s 一次。
        /// </summary>
        private void MaybeLogFramingDiag(HScene hScene, CameraControl_Ver2 ctrl)
        {
            float now = Time.unscaledTime;
            if (now < _nextFramingLogUnscaled)
                return;
            _nextFramingLogUnscaled = now + 0.5f;

            try
            {
                var cam = ctrl.thisCamera != null ? ctrl.thisCamera : Camera.main;
                Vector3 camWorld = cam != null ? cam.transform.position : Vector3.zero;
                Vector3 camFwd = cam != null ? cam.transform.forward : Vector3.forward;
                Vector3 focusWorld = _lastValidFocusWorld ?? Vector3.zero;
                Vector3 toFocus = focusWorld - camWorld;
                float dist = toFocus.magnitude;
                float along = dist > 1e-4f ? Vector3.Dot(toFocus.normalized, camFwd) : 0f;
                Vector3 targetLocal = ctrl.TargetPos;
                Vector3 camDir = ctrl.CameraDir;
                Vector3 rot = cam != null ? cam.transform.eulerAngles : ctrl.CameraAngle;
                int focusIdx = BoneFocusIndex();
                var cha = OrbitHelpers.GetChaFemales(hScene);
                float bodyH = cha != null ? OrbitHelpers.GetBodyHeight(cha, focusIdx < 3 ? 0 : 1) : 0f;
                bool inFront = along > 0.05f;
                bool approxInView = false;
                if (cam != null && dist > 1e-3f)
                {
                    Vector3 vp = cam.WorldToViewportPoint(focusWorld);
                    approxInView = vp.z > 0f && vp.x > -0.05f && vp.x < 1.05f && vp.y > -0.05f && vp.y < 1.05f;
                }

                var bones = SampleFramingBones(hScene, focusIdx);
                float errChest = bones.Chest.HasValue
                    ? Vector3.Distance(focusWorld, bones.Chest.Value)
                    : -1f;
                float errHead = bones.Head.HasValue
                    ? Vector3.Distance(focusWorld, bones.Head.Value)
                    : -1f;

                bool warning = !approxInView || !inFront || errChest > FocusJumpWarnMeters;
                bool trace = OrbitStateMachineLog.Enabled;
                if (!trace && !warning)
                    return;
                PopulateFramingLabels(hScene, focusIdx < 3 ? 0 : 1, ref bones);

                string json =
                    "{"
                    + "\"t\":" + now.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"axis\":\"" + OrbitBodyAxis.ModeLabel(_orbitAxisMode) + "\""
                    + ",\"phase\":" + _orbitPhase
                    + ",\"az\":" + (_startRelativeAzimuth + _orbitAccumulatedDegrees).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"focusIdx\":" + focusIdx
                    + ",\"zoom\":" + _circleZoomMult.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"bodyH\":" + bodyH.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"camW\":" + V3(camWorld)
                    + ",\"focusW\":" + V3(focusWorld)
                    + ",\"toFocus\":" + V3(toFocus)
                    + ",\"dist\":" + F3(dist)
                    + ",\"dotFwd\":" + F3(along)
                    + ",\"inFront\":" + (inFront ? "true" : "false")
                    + ",\"inView\":" + (approxInView ? "true" : "false")
                    + ",\"targetL\":" + V3(targetLocal)
                    + ",\"camDir\":" + V3(camDir)
                    + ",\"rot\":" + V3(rot)
                    + ",\"spin\":" + (OrbitBehaviorHub.IsOrbitCameraSpinning() ? "true" : "false")
                    + ",\"locked\":" + (_lockedBasisValid ? "true" : "false")
                    + ",\"lockGen\":" + _lockGen
                    + ",\"lockReason\":\"" + EscJson(_lastLockReason) + "\""
                    + ",\"lockAge\":" + F3(now - _lastLockUnscaled)
                    + ",\"local\":" + V3(_lockedFocusLocal)
                    + ",\"rootW\":" + (bones.HasRoot ? V3(bones.RootW) : "null")
                    + ",\"rootEul\":" + (bones.HasRoot ? V3(bones.RootEuler) : "null")
                    + ",\"liveHead\":" + V3Opt(bones.Head)
                    + ",\"liveChest\":" + V3Opt(bones.Chest)
                    + ",\"livePelvis\":" + V3Opt(bones.Pelvis)
                    + ",\"errChest\":" + F3(errChest)
                    + ",\"errHead\":" + F3(errHead)
                    + ",\"clip\":\"" + EscJson(bones.Clip) + "\""
                    + ",\"nowAnim\":\"" + EscJson(bones.NowAnim) + "\""
                    + ",\"director\":\"" + EscJson(bones.Director) + "\""
                    + "}";

                if (trace)
                    OrbitStateMachineLog.Event("framing", approxInView ? "ok" : "out", json);
                if (warning)
                    HS2OrbitAndExciter.Log?.LogWarning("[OrbitFraming] " + json);
            }
            catch (System.Exception ex)
            {
                HS2OrbitAndExciter.Log?.LogWarning($"[OrbitFraming] log failed: {ex.Message}");
            }
        }

        private static string F3(float v) =>
            v.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

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
            InvalidateLockedBasis("hotkey_focus");
            // 1A：立刻用骨焦點
            ApplyBoneFocusOnly(hScene, ctrl);
        }

        /// <summary>§11 1A：骨焦點優先；失敗回退上一有效焦點。環視中不用姿預設相機。</summary>
        private void ApplyCurrentViewOption(HScene hScene, CameraControl_Ver2 ctrl, string lockReason = "apply_view")
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
                string effectiveLockReason = _pendingLockReason ?? lockReason;
                InvalidateLockedBasis(effectiveLockReason);
                _lastValidFocusWorld = basis.FocusWorld;
                ctrl.TargetPos = ctrl.transBase.InverseTransformPoint(basis.FocusWorld);
                SetDistanceForFocus(ctrl, chaFemales, option, _circleZoomMult);
                if (TryLockBasis(chaFemales, option, basis, _pendingLockReason ?? effectiveLockReason))
                    _pendingLockReason = null;
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
            // §11：幾乎只用骨焦點；極低機率才碰姿預設（且立即被 Apply 改回骨）
            if (maxFocus > 1)
            {
                int next = UnityEngine.Random.Range(0, maxFocus - 1);
                _currentViewOption = next >= current ? next + 1 : next;
            }
            else
                _currentViewOption = current;
            InvalidateLockedBasis("randomize_view");
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
            ApplyCurrentViewOption(hScene, ctrl, "borrowed_camera");
            _startRelativeAzimuth = NormalizeDeg(ctrl.CameraAngle.y);
            _orbitAccumulatedDegrees = 0f;
            _framingHavePrev = false;
            _previousCameraUpWorld = null;
        }

        /// <summary>Rebind camera after pose transition completes (called by <see cref="OrbitPoseDirector"/>).</summary>
        internal void InternalRebindAfterPoseChange(HScene hScene, CameraControl_Ver2 ctrl)
        {
            var chaFemales = OrbitHelpers.GetChaFemales(hScene);
            int maxFocus = OrbitHelpers.GetMaxFocusIndex(chaFemales);
            if (_currentViewOption >= maxFocus)
                _currentViewOption = maxFocus > 1 ? 1 : 0;
            InvalidateLockedBasis("pose_rebind");
            ApplyCurrentViewOption(hScene, ctrl, "pose_rebind");
            _startRelativeAzimuth = OrbitBodyAxis.RollAnyAzimuthDegrees();
            _plannedAxisMode = null;
            _plannedStartAzimuth = null;
            _framingHavePrev = false;
            _previousCameraUpWorld = null;
            if (chaFemales != null && TryGetOrbitBasis(chaFemales, BoneFocusIndex(), out var basis))
            {
                bool storyboardSafeCamera = IsStoryboardSafeCameraEnabled();
                if (storyboardSafeCamera)
                    _orbitAxisMode = OrbitBodyAxis.OrbitAxisMode.WorldVertical;
                Vector3 camFwd = ctrl.thisCamera != null
                    ? ctrl.thisCamera.transform.forward
                    : Quaternion.Euler(ctrl.CameraAngle) * Vector3.forward;
                Vector3 axis = storyboardSafeCamera
                    ? OrbitFloorNormal.GetSkyward(hScene, basis.FocusWorld)
                    : OrbitBodyAxis.SpinAxis(basis, _orbitAxisMode);
                _startRelativeAzimuth = OrbitBodyAxis.AzimuthMatchingDirection(
                    basis, axis, camFwd);
                _orbitAccumulatedDegrees = 0f;
            }
            _lastNowAnimationInfoRef = hScene.ctrlFlag?.nowAnimationInfo;
        }

        private void OnOrbitToggled(bool active)
        {
            if (!active)
            {
                OrbitMapVanishAssist.ResetInjectedState();
                OrbitOcclusion20Test.Reset();
            }

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
                _wasOrbitCameraSpinning = true;
                // true = 擋原版滑鼠／鍵盤相機；環視寫入 Rot
                ctrl.NoCtrlCondition = NoCtrlOrbit;
                try { ctrl.ConfigVanish = true; } catch { /* ignore */ }
                OrbitMapVanishAssist.EnsureInjected(hScene);
                bool storyboardSafeCamera = IsStoryboardSafeCameraEnabled();
                _orbitAxisMode = storyboardSafeCamera
                    ? OrbitBodyAxis.OrbitAxisMode.WorldVertical
                    : OrbitBodyAxis.OrbitAxisMode.Torso;
                _orbitPhase = 0;
                _orbitAccumulatedDegrees = 0f;
                _rotationCount = 0;
                _roundTripCount = 0;
                _plannedAxisMode = null;
                _plannedStartAzimuth = null;
                _circleZoomMult = 1f;
                _plannedZoomMult = null;
                _lastValidFocusWorld = null;
                InvalidateLockedBasis("orbit_enable");
                _framingHavePrev = false;
                _previousCameraUpWorld = null;
                OrbitFloorNormal.ResetCache();
                var chaFemales = OrbitHelpers.GetChaFemales(hScene);
                _currentClothesSequenceIndex = OrbitHelpers.GetClothesSequenceIndexFromCurrent(chaFemales);
                int maxFocus = OrbitHelpers.GetMaxFocusIndex(chaFemales);
                _currentViewOption = maxFocus > 1 ? 1 : 0;
                ApplyCurrentViewOption(hScene, (CameraControl_Ver2)ctrl);
                OrbitOcclusion20Test.Arm(hScene, (CameraControl_Ver2)ctrl, BoneFocusIndex());
                // 初始軸＋傾斜；方位角對齊目前相機朝向
                if (chaFemales != null && TryGetOrbitBasis(chaFemales, BoneFocusIndex(), out var basis))
                {
                    Vector3 camFwd = ctrl.thisCamera != null
                        ? ctrl.thisCamera.transform.forward
                        : Quaternion.Euler(ctrl.CameraAngle) * Vector3.forward;
                    Vector3 axis = storyboardSafeCamera
                        ? OrbitFloorNormal.GetSkyward(hScene, basis.FocusWorld)
                        : OrbitBodyAxis.SpinAxis(basis, _orbitAxisMode);
                    _startRelativeAzimuth = OrbitBodyAxis.AzimuthMatchingDirection(basis, axis, camFwd);
                }
                else
                    _startRelativeAzimuth = 0f;
                _lastNowAnimationInfoRef = hScene.ctrlFlag?.nowAnimationInfo;
                if (IsInPreparationState(hScene))
                {
                    _waitingForPrepStart = true;
                    _prepCountdownStart = Time.unscaledTime;
                }
                else
                    _waitingForPrepStart = false;
                HS2OrbitAndExciter.Log?.LogInfo("Orbit: 協助開啟（擋手控相機；繞軸=" + OrbitBodyAxis.ModeLabel(_orbitAxisMode) + "）");
            }
            else
            {
                _wasOrbitCameraSpinning = false;
                _waitingForPrepStart = false;
                _prepFrozenElapsed = -1f;
                OrbitPoseDirector.Reset();
                // false = 還原原版滑鼠／鍵盤調視角
                ctrl.NoCtrlCondition = NoCtrlUser;
                HS2OrbitAndExciter.Log?.LogInfo("Orbit: 協助關閉（已還原滑鼠／鍵盤相機）");
            }

            StoryboardPackageRecorder.NotifyOrbitToggled(active, this, hScene);
        }

        private static HScene? GetHScene() => TryGetHScene();

        private static string EscForJson(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value!.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

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
