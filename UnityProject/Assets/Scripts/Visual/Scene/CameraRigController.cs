using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using VContainer.Unity;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// Owns the orthographic camera placed in the additive gameplay scene.
    /// This is a plain C# VContainer entry point; it does not add a camera,
    /// AudioListener, or per-scene LifetimeScope.
    ///
    /// All Unity and bus access goes through the ports in CameraRigPorts.cs
    /// (IRigMessageBus, ICameraRigEnvironment, ICellSpriteMetrics,
    /// IMainThreadQueue, IRigClock, ICameraRigCameraView) so EditMode tests
    /// can drive this controller with fakes — no scene, Camera, Resources or
    /// PlayerLoop required. Production adapters are registered in
    /// GameLifetimeScope.
    ///
    /// Preset ortho sizes are computed at runtime by CameraFramingConfig
    /// (formula: tilesVisible * tileWidthWorld / (2 * camera.aspect)) - never
    /// hardcoded - so ultrawide/16:10 window shapes frame the same tile count.
    /// </summary>
    public sealed class CameraRigController : IStartable, ITickable, IDisposable
    {
        private const string GameplaySceneName = "TestGameplayScene";
        private const float TransitionDurationSeconds = 0.35f;
        private const float FollowSharpness = 12f;

        // ── mouse-wheel zoom (continuous, ไม่ snap) ──
        // ความเร็ว: orthoSize หด/ขยายต่อ scroll tick 1 หน่วย (exponential feel แบบ
        // editor — ใกล้ซูมละเอียด ไกลซูมหยาบ); smoothing ผ่าน exponential blend
        // เดียวกับ follow (ไม่กระตุกเมื่อ scroll เร็ว ๆ)
        // 0.15 = หนึ่ง notch เปลี่ยนขนาด ~14% (range เต็ม 2.5→8 ใช้ ~8-9 notch);
        // ลด/เพิ่มที่ค่าค่านี้ค่าเดียว (เดิม 0.6 → 0.25 ตามผลวัด live แล้วผู้ใช้เลือก 0.15)
        internal const float ZoomSpeedOrthoPerTick = 0.15f;
        internal const float ZoomSharpness = 10f;
        // ช่วง zoom: แคบสุด = placement preset (เห็น 10 ไทล์), กว้างสุด =
        // ขนาดที่ ClampToBackdrop ยังพอมีที่ pan (backdrop หัก margin 3 ไทล์)
        internal const int ZoomMaxVisibleTiles = 25;

        private readonly IRigMessageBus _bus;
        private readonly ICameraRigEnvironment _environment;
        private readonly ICellSpriteMetrics _spriteMetrics;
        private readonly IMainThreadQueue _mainThread;
        private readonly IRigClock _clock;
        private readonly CameraFramingConfig _framing;

        private IDisposable _sceneLoadedSubscription;
        private IDisposable _sceneUnloadedSubscription;
        private IDisposable _buildStartedSubscription;
        private IDisposable _buildEndedSubscription;
        private CancellationTokenSource _transitionCancellation;

        private ICameraRigCameraView _cameraView;
        private Quaternion _fixedRotation;
        private Vector3 _overviewPosition;
        private RigGridSnapshot _grid = RigGridSnapshot.None;
        private IRigGhost _placementGhost;
        private bool _placementRequested;
        private bool _isTransitioning;
        private bool _disposed;

        // ── pan ด้วยคลิกขวาค้าง+ลาก (ผู้เล่นขอ: เห็นพื้นที่รอบ ๆ) ──
        // Input ผ่าน RigPanInput (static seam — composition root wire เฉพาะ play mode);
        // ใช้ได้เฉพาะ overview mode — placement mode กล้องผูกกับ ghost และ grid overlay
        // origin ถูก set ตอนเข้า build mode จึงห้าม pan ทับ (กัน overlay เหลื่อม)
        private bool _panning;
        private Vector3 _lastPanMouseScreen;

        // ── zoom state: target ถูกปรับต่อ scroll, ค่าจริง lerp ตาม (เนียน) ──
        private float _zoomTargetSize;
        private bool _zoomTargetInitialized;

        public CameraRigController(
            IRigMessageBus bus,
            ICameraRigEnvironment environment,
            ICellSpriteMetrics spriteMetrics,
            IMainThreadQueue mainThread,
            IRigClock clock,
            CameraFramingConfig framing)
        {
            _bus = bus;
            _environment = environment;
            _spriteMetrics = spriteMetrics;
            _mainThread = mainThread;
            _clock = clock;
            _framing = framing;
        }

        public void Start()
        {
            _sceneLoadedSubscription = _bus.SubscribeSceneLoaded(OnSceneLoaded);
            _sceneUnloadedSubscription = _bus.SubscribeSceneUnloaded(OnSceneUnloaded);
            _buildStartedSubscription = _bus.SubscribeBuildModeStarted(OnBuildModeStarted);
            _buildEndedSubscription = _bus.SubscribeBuildModeEnded(OnBuildModeEnded);

            // The persistent scope can start after a gameplay scene was opened
            // directly in the Editor rather than through SceneLoader.
            if (_environment.IsSceneLoaded(GameplaySceneName))
                HandleSceneLoadedAsync(GameplaySceneName).Forget();
        }

        public void Tick()
        {
            if (_cameraView == null || !_cameraView.IsValid) return;

            // Lock the rotation to the scene-authored isometric framing.
            _cameraView.Rotation = _fixedRotation;

            HandlePanInput();
            HandleZoomInput();

            // placement follow ชนะ pan เสมอ (กล้องอยู่กับ ghost ขณะ build)
            if (_placementRequested && !_isTransitioning && _placementGhost != null && _placementGhost.IsValid)
            {
                Vector3 target = GetPlacementPosition();
                float blend = 1f - Mathf.Exp(-FollowSharpness * _clock.UnscaledDeltaTime);
                _cameraView.Position = Vector3.Lerp(_cameraView.Position, target, blend);
            }
        }

        // ── pan: คลิกขวาค้าง+ลาก — clamp ในกรอบแบ็คกราวภูเขาเสมอ (ไม่หลุดเห็นขอบดำ) ──
        // ใช้ได้ทั้ง overview และ placement mode (เมื่อไม่มี ghost ให้ follow —
        // UI ของเรา publish GhostId=null; ถ้ามี ghost จริง follow ชนะ pan ตาม if ด้านล่าง)
        private void HandlePanInput()
        {
            if (!RigPanInput.IsWired || _cameraView == null || !_cameraView.IsValid) return;
            // pointer บน HUD (ปุ่ม/เมนู) ไม่เริ่ม pan — กันลากปุ่มแล้วกล้องหลุด
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            bool held = RigPanInput.GetMouseButton(1);
            if (held && !_panning)
            {
                _panning = true;
                _lastPanMouseScreen = RigPanInput.MousePosition();
            }
            else if (!held && _panning)
            {
                _panning = false;
            }

            if (!_panning || !held) return;

            var mouseScreen = RigPanInput.MousePosition();
            var deltaScreen = mouseScreen - _lastPanMouseScreen;
            _lastPanMouseScreen = mouseScreen;
            if (deltaScreen.sqrMagnitude < 0.01f) return;

            // ortho: screen px → world ด้วย orthoSize (สูงครึ่งจอ) + aspect; ทิศกลับ (ลากขวา = กล้องไปซ้าย)
            float worldPerPixelY = 2f * _cameraView.OrthographicSize / Mathf.Max(1f, Screen.height);
            float worldPerPixelX = worldPerPixelY / Mathf.Max(0.01f, _cameraView.Aspect);
            var delta = new Vector3(-deltaScreen.x * worldPerPixelX, -deltaScreen.y * worldPerPixelY, 0f);

            _cameraView.Position = ClampToBackdrop(_cameraView.Position + delta);
            _overviewPosition = _cameraView.Position; // pan = overview ใหม่ (transition กลับมาที่นี่)
        }

        /// <summary>Mouse-wheel zoom — continuous (scroll ปรับ target ทีละน้อย ค่าจริง
        /// lerp ตามด้วย ZoomSharpness จึงลื่นไม่กระตุก) ใช้ได้เฉพาะ overview mode:
        /// ระหว่าง placement ล็อกที่ preset เดียว (ตัดสินใจตาม task — กันผู้เล่นซูม
        /// จนเห็น ghost ผิดสัดส่วนกับกริด) guard เดียวกับ pan: เมาส์บน UI = ไม่ zoom
        /// และ transition ของ preset ยังคงชนะ zoom เสมอ</summary>
        private void HandleZoomInput()
        {
            if (!RigZoomInput.IsWired || _cameraView == null || !_cameraView.IsValid) return;

            // preset owns the size ระหว่าง transition/placement — ทิ้ง target เก่า
            // ให้ sync ใหม่กับ preset ล่าสุดเมื่อกลับมา zoom ได้
            if (_isTransitioning || _placementRequested)
            {
                _zoomTargetInitialized = false;
                return;
            }

            // ตำแหน่งจริงเดินตาม target ทุกเฟรม (แม้เฟรมนี้ไม่มี scroll — กลืนที่ค้าง)
            if (!_zoomTargetInitialized)
            {
                _zoomTargetSize = _cameraView.OrthographicSize;
                _zoomTargetInitialized = true;
            }

            float scroll = RigZoomInput.GetScrollDelta();
            if (Mathf.Abs(scroll) >= 0.01f)
            {
                // exponential: orthoSize *= (1 + speed)^(−ticks) — scroll บวก = เข้า (หด)
                float minSize = GetPlacementSize();       // ซูมเข้าสุด = PlacementVisibleTiles
                float maxSize = ComputeMaxZoomOutSize();  // ซูมออกสุด = พอดี backdrop
                float factor = Mathf.Pow(1f + ZoomSpeedOrthoPerTick, -Mathf.Clamp(scroll, -3f, 3f));
                _zoomTargetSize = Mathf.Clamp(_zoomTargetSize * factor, minSize, maxSize);
            }

            float current = _cameraView.OrthographicSize;
            if (Mathf.Abs(current - _zoomTargetSize) <= 0.0005f) return;

            float blend = 1f - Mathf.Exp(-ZoomSharpness * _clock.UnscaledDeltaTime);
            _cameraView.OrthographicSize = Mathf.Lerp(current, _zoomTargetSize, blend);

            // re-clamp position หลัง size เปลี่ยน (กันขอบดำพร้อมกันทั้งสองแกน)
            _cameraView.Position = ClampToBackdrop(_cameraView.Position);
            _overviewPosition = _cameraView.Position;
        }

        /// <summary>ซูมออกสุดที่ยังไม่เห็นขอบดำ: ใช้ขอบเขต backdrop ที่ ClampToBackdrop
        /// ใช้อยู่เป็นฐาน — หา orthoSize ที่กรอบมองพอดี backdrop (กว้างนั้น pan ล็อกกลาง
        /// พอดี ไม่เหลือขอบดำ)</summary>
        private float ComputeMaxZoomOutSize()
        {
            if (_framing.HasBackdropBounds)
            {
                float fitH = _framing.BackdropHeightWorld * 0.5f;
                float fitW = _framing.BackdropWidthWorld * 0.5f / Mathf.Max(0.01f, _cameraView.Aspect);
                return Mathf.Max(GetPlacementSize(), Mathf.Min(fitH, fitW));
            }
            // ไม่มีขอบเขต (ยังวัดไม่ได้) — fallback เป็น preset กว้างสุดที่รู้จัก
            return _framing.ComputeOrthoSize(ZoomMaxVisibleTiles,
                _cameraView != null ? _cameraView.Aspect : 16f / 9f);
        }

        /// <summary>จำกัดกล้องให้เห็นแต่พื้นที่ในแบ็คกราวภูเขา (กึ่งกลาง origin) —
        /// ถ้ากรอบมองใหญ่กว่าแบ็คกราว กล้องติดกลาง (ไม่ pan ได้) ตาม axis นั้น</summary>
        private Vector3 ClampToBackdrop(Vector3 position)
        {
            if (!_framing.HasBackdropBounds) return position;

            float halfW = _framing.BackdropWidthWorld * 0.5f;
            float halfH = _framing.BackdropHeightWorld * 0.5f;
            float halfViewH = Mathf.Max(0.01f, _cameraView.OrthographicSize);
            float halfViewW = halfViewH * Mathf.Max(0.01f, _cameraView.Aspect);

            float minX = -(halfW - halfViewW);
            float maxX = +(halfW - halfViewW);
            float minY = -(halfH - halfViewH);
            float maxY = +(halfH - halfViewH);

            // มุมมองใหญ่กว่าแบ็คกราว = ล็อกกึ่งกลาง (ป้องกันเห็นขอบดำ)
            if (minX > maxX) { minX = maxX = 0f; }
            if (minY > maxY) { minY = maxY = 0f; }

            position.x = Mathf.Clamp(position.x, minX, maxX);
            position.y = Mathf.Clamp(position.y, minY, maxY);
            return position;
        }

        /// <summary>
        /// Enter placement mode and focus the camera on the live building ghost.
        /// Publishes BuildModeStartedMessage; the camera reacts through the bus
        /// handler below so there is exactly one code path (direct calls and
        /// interprocess publishers produce identical behavior).
        /// </summary>
        public async UniTask EnterPlacementAsync(string ghostId = null)
        {
            await _mainThread.SwitchToMainThread();
            if (_disposed) return;

            PublishBuildModeStarted(ghostId);
        }

        /// <summary>
        /// Return to overview after placement exits, confirms, or cancels.
        /// Publishes BuildModeEndedMessage (bus-driven, same as enter).
        /// </summary>
        public async UniTask ExitPlacementAsync()
        {
            await _mainThread.SwitchToMainThread();
            if (_disposed) return;

            PublishBuildModeEnded(confirmed: true);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _sceneLoadedSubscription?.Dispose();
            _sceneUnloadedSubscription?.Dispose();
            _buildStartedSubscription?.Dispose();
            _buildEndedSubscription?.Dispose();
            _transitionCancellation?.Cancel();
            _transitionCancellation?.Dispose();
            _transitionCancellation = null;
            _cameraView = null;
            _grid = RigGridSnapshot.None;
            _placementGhost = null;
        }

        private void OnBuildModeStarted(BuildModeStartedMessage message)
        {
            HandleBuildModeStartedAsync(message).Forget();
        }

        private async UniTask HandleBuildModeStartedAsync(BuildModeStartedMessage message)
        {
            // BuildModeStarted can be published from the interprocess/MCP side.
            await _mainThread.SwitchToMainThread();
            if (_disposed) return;

            if (!string.IsNullOrEmpty(message.GhostId) &&
                _environment.TryFindGhost(message.GhostId, out IRigGhost ghost))
            {
                _placementGhost = ghost;
            }

            _placementRequested = true;
            if (_cameraView == null) return;
            await TransitionToAsync(GetPlacementSize());
        }

        private void OnBuildModeEnded(BuildModeEndedMessage message)
        {
            HandleBuildModeEndedAsync(message).Forget();
        }

        private async UniTask HandleBuildModeEndedAsync(BuildModeEndedMessage message)
        {
            await _mainThread.SwitchToMainThread();
            if (_disposed) return;

            _placementRequested = false;
            _placementGhost = null;
            if (_cameraView == null) return;
            await TransitionToAsync(GetOverviewSize());
        }

        private void PublishBuildModeStarted(string ghostId)
        {
            _bus?.PublishBuildModeStarted("CameraRigController", ghostId);
        }

        private void PublishBuildModeEnded(bool confirmed)
        {
            _bus?.PublishBuildModeEnded("CameraRigController", confirmed);
        }

        private void OnSceneLoaded(SceneLoadedMessage message)
        {
            HandleSceneLoadedAsync(message?.SceneName).Forget();
        }

        private void OnSceneUnloaded(SceneUnloadedMessage message)
        {
            HandleSceneUnloadedAsync(message?.SceneName).Forget();
        }

        private async UniTask HandleSceneUnloadedAsync(string sceneName)
        {
            await _mainThread.SwitchToMainThread();
            if (_cameraView == null || !string.Equals(_cameraView.SceneName, sceneName, StringComparison.Ordinal)) return;

            _transitionCancellation?.Cancel();
            _transitionCancellation?.Dispose();
            _transitionCancellation = null;
            _isTransitioning = false;
            _cameraView = null;
            _grid = RigGridSnapshot.None;
            _placementGhost = null;
            _placementRequested = false;
        }

        private async UniTask HandleSceneLoadedAsync(string sceneName)
        {
            // SceneLoaded can be forwarded from an interprocess-driven flow.
            await _mainThread.SwitchToMainThread();
            if (_disposed || string.IsNullOrEmpty(sceneName) || sceneName != GameplaySceneName) return;

            if (!_environment.TryGetSceneCamera(sceneName, out ICameraRigCameraView cameraView, out RigGridSnapshot grid))
            {
                CameraRigLogger.Warn("[CameraRigController] No enabled Camera found in gameplay scene '" +
                                     sceneName + "'. The camera must live in the gameplay scene.");
                return;
            }

            _cameraView = cameraView;
            _cameraView.Orthographic = true;
            _fixedRotation = _cameraView.Rotation;
            _overviewPosition = _cameraView.Position;
            _grid = grid; // may be none - framing uses sprite-derived sizes only

            MeasureFraming();
            _cameraView.OrthographicSize = GetOverviewSize();

            if (_placementRequested && _placementGhost != null)
                await TransitionToAsync(GetPlacementSize());
        }

        // Reads the real sprite/PPU/Grid values and logs them (task step 1:
        // verify before computing - no guessing).
        private void MeasureFraming()
        {
            int spriteWidthPx = 0, spriteHeightPx = 0, ppu = 100;
            _spriteMetrics.TryGetCellSprite("gridblock_0", out spriteWidthPx, out spriteHeightPx, out ppu);

            Vector3 cellSize = Vector3.one;
            bool isIsometric = false;
            if (_grid.HasGrid)
            {
                cellSize = _grid.CellSize;
                isIsometric = _grid.IsIsometric;
            }

            _framing.SetMeasured(
                tileWidthWorld: spriteWidthPx / (float)ppu,
                tileHeightWorld: spriteHeightPx / (float)ppu,
                spriteWidthPx: spriteWidthPx,
                spriteHeightPx: spriteHeightPx,
                ppu: ppu,
                cellSize: cellSize,
                isIsometric: isIsometric);

            CameraRigLogger.Info("[CameraRigController] Framing source values: gridblock sprite " +
                      spriteWidthPx + "x" + spriteHeightPx + " px @ " + ppu + " PPU" +
                      " (tileWorld=" + _framing.TileWidthWorld + "x" + _framing.TileHeightWorld + ")" +
                      "; Grid cellSize=" + cellSize + "; layout=" +
                      (isIsometric ? "Isometric" : "Rectangle") +
                      "; Overview orthoSize=" + GetOverviewSize().ToString("0.000") +
                      " (" + CameraFramingConfig.OverviewVisibleTiles + " tiles wide)" +
                      "; Placement orthoSize=" + GetPlacementSize().ToString("0.000") +
                      " (" + CameraFramingConfig.PlacementVisibleTiles + " tiles wide).");
        }

        // orthoSize is a half-height in Unity, so full visible height is
        // 2 * orthoSize; solving tilesWide * tileWidth = 2 * size * aspect
        // gives the formula used in CameraFramingConfig.ComputeOrthoSize.
        private float GetOverviewSize()
        {
            return _framing.ComputeOrthoSize(
                CameraFramingConfig.OverviewVisibleTiles,
                _cameraView != null ? _cameraView.Aspect : 16f / 9f);
        }

        private float GetPlacementSize()
        {
            return _framing.ComputeOrthoSize(
                CameraFramingConfig.PlacementVisibleTiles,
                _cameraView != null ? _cameraView.Aspect : 16f / 9f);
        }

        private Vector3 GetPlacementPosition()
        {
            Vector3 target = _placementGhost.Position;
            target.z = _cameraView.Position.z;
            return target;
        }

        private async UniTask TransitionToAsync(float targetSize)
        {
            _transitionCancellation?.Cancel();
            _transitionCancellation?.Dispose();
            var cancellation = new CancellationTokenSource();
            _transitionCancellation = cancellation;
            _isTransitioning = true;

            Vector3 startPosition = _cameraView.Position;
            float startSize = _cameraView.OrthographicSize;
            float elapsed = 0f;

            try
            {
                while (elapsed < TransitionDurationSeconds)
                {
                    await _clock.NextFrame(cancellation.Token);
                    if (_disposed || _cameraView == null || !_cameraView.IsValid) return;

                    elapsed += _clock.UnscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / TransitionDurationSeconds);
                    float eased = t * t * (3f - 2f * t);
                    Vector3 targetPosition = _placementRequested && _placementGhost != null
                        ? GetPlacementPosition()
                        : _overviewPosition;

                    _cameraView.Position = Vector3.Lerp(startPosition, targetPosition, eased);
                    _cameraView.OrthographicSize = Mathf.Lerp(startSize, targetSize, eased);
                    _cameraView.Rotation = _fixedRotation;
                }

                _cameraView.OrthographicSize = targetSize;
                _cameraView.Position = _placementRequested && _placementGhost != null
                    ? GetPlacementPosition()
                    : _overviewPosition;
                _cameraView.Rotation = _fixedRotation;
            }
            catch (OperationCanceledException)
            {
                // A newer preset request or scene unload superseded this transition.
            }
            finally
            {
                if (_transitionCancellation == cancellation)
                {
                    _transitionCancellation.Dispose();
                    _transitionCancellation = null;
                    _isTransitioning = false;
                }
            }
        }
    }
}
