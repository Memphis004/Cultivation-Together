using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using UnityEngine;
using VContainer.Unity;
using Xianxia.Sect.Messages;

namespace Xianxia.Sect.Visual
{
    /// <summary>
    /// World-space isometric grid overlay for build mode.
    /// White diamond = free cell, green = cell under the cursor (placeable),
    /// occupied cells (2D collider overlap) are not drawn.
    ///
    /// Does NOT depend on the scene Grid component: cell size comes from the
    /// measured gridblock sprite (CameraFramingConfig) and isometric diamond
    /// positions are computed directly -
    ///   center(cell) = origin + ((x - y) * w/2, (x + y) * h/2)
    /// so the overlay cannot be dropped by scene-load component issues.
    /// Lives entirely outside the HUD: world-space SpriteRenderers on the
    /// "GridOverlay" sorting layer, so the HUD canvas is never dirtied.
    /// </summary>
    public sealed class GridOverlayRenderer : IStartable, ITickable, IDisposable
    {
        private const string OverlayLayerName = "GridOverlay";
        private const string GameplaySceneName = "TestGameplayScene";
        private const float CursorPollHz = 30f;
        private const int OccupancyProbeCount = 3;

        private readonly ISubscriber<BuildModeStartedMessage> _buildStartedSubscriber;
        private readonly ISubscriber<BuildModeEndedMessage> _buildEndedSubscriber;
        private readonly CameraFramingConfig _framing;

        private IDisposable _buildStartedSubscription;
        private IDisposable _buildEndedSubscription;
        private CancellationTokenSource _cursorLoopCancellation;

        private Camera _camera;
        private Transform _overlayRoot;
        private readonly List<SpriteRenderer> _cellRenderers = new List<SpriteRenderer>(512);
        private readonly List<Vector2Int> _cellCoords = new List<Vector2Int>(512);
        private readonly Collider2D[] _occupancyHits = new Collider2D[OccupancyProbeCount];

        private Sprite _whiteSprite;
        private Sprite _greenSprite;
        private Color _whiteTint = new Color(1f, 1f, 1f, 0.55f);
        private Color _greenTint = new Color(0.35f, 1f, 0.35f, 0.85f);

        private Vector2 _overlayOrigin; // world center of cell (0,0)
        private bool _overlayVisible;
        private Vector2Int _lastCursorCell;
        private bool _hasCursorCell;
        private const int GridExtent = 24; // 18x16 courtyard plus margin
        private bool _disposed;

        public GridOverlayRenderer(
            ISubscriber<BuildModeStartedMessage> buildStartedSubscriber,
            ISubscriber<BuildModeEndedMessage> buildEndedSubscriber,
            CameraFramingConfig framing)
        {
            _buildStartedSubscriber = buildStartedSubscriber;
            _buildEndedSubscriber = buildEndedSubscriber;
            _framing = framing;
        }

        public void Start()
        {
            _buildStartedSubscription = _buildStartedSubscriber.Subscribe(OnBuildModeStarted);
            _buildEndedSubscription = _buildEndedSubscriber.Subscribe(OnBuildModeEnded);

            // The persistent scope can start after a gameplay scene was opened
            // directly in the Editor rather than through SceneLoader.
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (scene.isLoaded && scene.name == GameplaySceneName)
                {
                    HandleSceneReadyAsync().Forget();
                    break;
                }
            }
        }

        public void Tick()
        {
            // Heavy work is in the async cursor loop; Tick only validates the
            // camera reference (scene unload nulls it).
            if (_overlayVisible && _camera == null)
                HideOverlay();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _buildStartedSubscription?.Dispose();
            _buildEndedSubscription?.Dispose();
            _cursorLoopCancellation?.Cancel();
            _cursorLoopCancellation?.Dispose();
            _cursorLoopCancellation = null;

            if (_overlayRoot != null)
                UnityEngine.Object.Destroy(_overlayRoot.gameObject);
            _cellRenderers.Clear();
            _cellCoords.Clear();
            _camera = null;
        }

        private void OnBuildModeStarted(BuildModeStartedMessage message)
        {
            HandleBuildModeStartedAsync().Forget();
        }

        private async UniTask HandleBuildModeStartedAsync()
        {
            // BuildModeStarted can arrive from the interprocess/MCP side.
            await UniTask.SwitchToMainThread();
            if (_disposed) return;

            EnsureSceneRefs();
            if (_camera == null)
            {
                Debug.LogWarning("[GridOverlayRenderer] Build mode requested but gameplay Camera not found.");
                return;
            }
            if (!_framing.IsMeasured)
            {
                Debug.LogWarning("[GridOverlayRenderer] Framing not measured yet; cannot size overlay cells.");
                return;
            }

            EnsureBuilt();
            ShowOverlay();
        }

        private void OnBuildModeEnded(BuildModeEndedMessage message)
        {
            HandleBuildModeEndedAsync().Forget();
        }

        private async UniTask HandleBuildModeEndedAsync()
        {
            await UniTask.SwitchToMainThread();
            if (_disposed) return;

            HideOverlay();
        }

        private async UniTask HandleSceneReadyAsync()
        {
            await UniTask.SwitchToMainThread();
            if (_disposed) return;
            EnsureSceneRefs();
        }

        private void EnsureSceneRefs()
        {
            if (_camera != null) return;

            _camera = null;
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || scene.name != GameplaySceneName) continue;

                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length && _camera == null; r++)
                {
                    Camera cam = roots[r].GetComponentInChildren<Camera>(true);
                    if (cam != null && cam.enabled) _camera = cam;
                }
            }

            Debug.Log("[GridOverlayRenderer] EnsureSceneRefs: camera=" +
                      (_camera != null ? _camera.name : "NULL"));
        }

        private void EnsureBuilt()
        {
            if (_overlayRoot != null) return;

            _whiteSprite = LoadCellSprite("gridblock_0");
            _greenSprite = LoadCellSprite("gridblock_1") ?? _whiteSprite;
            if (_whiteSprite == null)
            {
                Debug.LogWarning("[GridOverlayRenderer] gridblock sprites missing from Resources.");
                return;
            }

            var rootGo = new GameObject("GridOverlayRoot");
            rootGo.SetActive(false);
            _overlayRoot = rootGo.transform;

            float cellW = _framing.TileWidthWorld;
            float cellH = _framing.TileHeightWorld;

            _cellRenderers.Clear();
            _cellCoords.Clear();

            for (int y = -GridExtent / 2; y < GridExtent / 2; y++)
            {
                for (int x = -GridExtent / 2; x < GridExtent / 2; x++)
                {
                    var cellGo = new GameObject("Cell_" + x + "_" + y);
                    cellGo.transform.SetParent(_overlayRoot, false);

                    var sr = cellGo.AddComponent<SpriteRenderer>();
                    sr.sprite = _whiteSprite;
                    sr.color = _whiteTint;
                    sr.sortingLayerName = ResolveOverlayLayerName();
                    sr.sortingOrder = ResolveOverlayOrder();

                    // Sprite is authored as one grid cell (128x66 @ PPU100);
                    // rescale only if the measured cell diverges from it.
                    var bounds = _whiteSprite.bounds;
                    sr.transform.localScale = new Vector3(
                        bounds.size.x > 0.001f ? cellW / bounds.size.x : 1f,
                        bounds.size.y > 0.001f ? cellH / bounds.size.y : 1f,
                        1f);

                    _cellRenderers.Add(sr);
                    _cellCoords.Add(new Vector2Int(x, y));
                }
            }

            Debug.Log("[GridOverlayRenderer] overlay built: cells=" + _cellRenderers.Count +
                      " cellSize=" + cellW.ToString("0.000") + "x" + cellH.ToString("0.000"));
        }

        private Sprite LoadCellSprite(string spriteName)
        {
            Sprite[] sprites = Resources.LoadAll<Sprite>(CameraFramingConfig.GridSpritePath);
            if (sprites == null) return null;
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] != null && sprites[i].name == spriteName)
                    return sprites[i];
            }
            return sprites.Length > 0 ? sprites[0] : null;
        }

        // Isometric diamond mapping (2:1): cell (x,y) center relative to origin.
        // Delegates to IsometricCellMath so EditMode tests exercise the exact
        // same code path as the live renderer.
        private Vector3 CellCenterToWorld(Vector2Int cell)
        {
            return IsometricCellMath.CellCenterToWorld(cell, _overlayOrigin, _framing.TileWidthWorld, _framing.TileHeightWorld);
        }

        // Inverse pick: standard isometric floor division.
        private Vector2Int WorldToCell(Vector3 world)
        {
            return IsometricCellMath.WorldToCell(world, _overlayOrigin, _framing.TileWidthWorld, _framing.TileHeightWorld);
        }

        private void ShowOverlay()
        {
            if (_overlayRoot == null) return;

            // Center the courtyard in the view: use the camera's current XY as
            // the world position of cell (0,0).
            _overlayOrigin = new Vector2(_camera.transform.position.x, _camera.transform.position.y);

            for (int i = 0; i < _cellCoords.Count; i++)
            {
                Vector3 p = CellCenterToWorld(_cellCoords[i]);
                _cellRenderers[i].transform.position = p;
            }

            _overlayRoot.gameObject.SetActive(true);
            _overlayVisible = true;
            _hasCursorCell = false;
            UpdateCursorCell(new Vector2Int(int.MinValue, int.MinValue)); // paint pass, no cursor yet
            StartCursorLoop();
        }

        private void HideOverlay()
        {
            _overlayVisible = false;
            _cursorLoopCancellation?.Cancel();
            _cursorLoopCancellation?.Dispose();
            _cursorLoopCancellation = null;
            if (_overlayRoot != null)
                _overlayRoot.gameObject.SetActive(false);
        }

        private void StartCursorLoop()
        {
            _cursorLoopCancellation?.Cancel();
            _cursorLoopCancellation?.Dispose();
            var cts = new CancellationTokenSource();
            _cursorLoopCancellation = cts;
            CursorLoopAsync(cts.Token).Forget();
        }

        // Polls the pointer at CursorPollHz instead of every Tick - overlay is
        // a low-frequency visual aid, not gameplay input.
        private async UniTaskVoid CursorLoopAsync(CancellationToken token)
        {
            int intervalMs = (int)(1000f / CursorPollHz);
            while (!token.IsCancellationRequested && _overlayVisible)
            {
                await UniTask.Delay(intervalMs, cancellationToken: token);
                if (_disposed || _camera == null || _overlayRoot == null) continue;
                if (_overlayRoot.gameObject == null) continue;

                Vector3 world = _camera.ScreenToWorldPoint(Input.mousePosition);
                world.z = 0f;

                Vector2Int cell = WorldToCell(world);
                if (_hasCursorCell && cell == _lastCursorCell) continue;

                UpdateCursorCell(cell);
                _lastCursorCell = cell;
                _hasCursorCell = true;
            }
        }

        private void UpdateCursorCell(Vector2Int cursorCell)
        {
            for (int i = 0; i < _cellRenderers.Count; i++)
            {
                SpriteRenderer sr = _cellRenderers[i];
                if (sr == null) continue;

                if (IsOccupied(_cellCoords[i]))
                {
                    sr.enabled = false; // hidden under an existing building
                    continue;
                }

                bool isCursor = _cellCoords[i] == cursorCell;
                sr.enabled = true;
                sr.sprite = isCursor ? _greenSprite : _whiteSprite;
                sr.color = isCursor ? _greenTint : _whiteTint;
            }
        }

        // Occupied = any 2D collider overlapping the cell center. Cheap probe;
        // BuildingSystem will later own real occupancy data and can replace this.
        private bool IsOccupied(Vector2Int cell)
        {
            Vector3 world = CellCenterToWorld(cell);
            Vector2 point = new Vector2(world.x, world.y);
            int hits = Physics2D.OverlapPointNonAlloc(point, _occupancyHits);
            for (int h = 0; h < hits; h++)
            {
                var col = _occupancyHits[h];
                if (col == null) continue;
                if (col.attachedRigidbody != null || !col.isTrigger) return true;
            }
            return false;
        }

        // The 'GridOverlay' sorting layer is added to TagManager.asset (project
        // settings) at edit time; here we only resolve it and fall back to the
        // default layer with a high order if it is missing.
        private static string ResolveOverlayLayerName()
        {
            int id = SortingLayer.NameToID(OverlayLayerName);
            return id != 0 ? OverlayLayerName : "Default";
        }

        private static int ResolveOverlayOrder()
        {
            int id = SortingLayer.NameToID(OverlayLayerName);
            return id != 0 ? 0 : 32000; // stay above world sprites on Default
        }
    }
}
