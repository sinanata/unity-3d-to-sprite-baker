using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpriteBakerDemo
{
    /// <summary>
    /// Spawns the whole demo (ground, lights, camera, four cards, UI) from
    /// code — the .unity scene stays empty so refactors don't rot scene
    /// YAML.
    /// </summary>
    public class DemoBootstrap : MonoBehaviour
    {
        // Auto-spawn on scene load so .unity stays empty.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
#if UNITY_2023_1_OR_NEWER
            var existing = Object.FindFirstObjectByType<DemoBootstrap>();
#else
            var existing = Object.FindObjectOfType<DemoBootstrap>();
#endif
            if (existing != null) return;
            var go = new GameObject("DemoBootstrap");
            go.AddComponent<DemoBootstrap>();
        }

        const float CARD_SPACING_X    = 4.4f;
        const int   CARD_COUNT        = 4;
        const float CAMERA_DISTANCE   = 22f;
        const float CAMERA_HEIGHT     = 5.5f;
        const float CAMERA_FOV        = 45f;

        // Wheel + pinch write to a target distance; SmoothDamp eases live.
        const float WHEEL_SENSITIVITY     = 12f;
        const float WHEEL_NORMALIZE_DIV   = 100f;
        const float WHEEL_NORMALIZE_CLAMP = 3f;
        const float PINCH_SENSITIVITY     = 1f / 8f;
        const float ZOOM_SMOOTH_TIME      = 0.12f;
        const float MIN_ZOOM              = 6f;
        const float MAX_ZOOM              = 32f;

        Camera _camera;
        readonly List<DemoCharacterCard> _cards = new();
        DemoUI _ui;

        // Yaw 0° puts the orbit camera on +Z, matching the bake's
        // yaw=0 (character's "front"). Single-angle atlases ONLY capture
        // this view, so starting elsewhere would mismatch the live mesh.
        float _orbitYaw   = 0f;
        float _orbitPitch = 18f;
        float _orbitDistance;
        float _targetOrbitDistance;
        float _orbitDistanceVelocity;

        bool       _primaryActive;
        Vector2    _primaryDownPos;
        Vector2    _primaryLastPos;
        bool       _primaryDragging;
        const float DRAG_THRESHOLD_CSSPX = 11f;

        bool    _altDragging;
        Vector2 _altLastMouse;

        bool  _pinching;
        float _lastPinchDist;

        // ── Settings the UI mutates ────────────────────────────────────────
        public int FramePixelSize = DemoCharacterCatalog.DefaultFramePixelSize;
        public int FrameRate      = DemoCharacterCatalog.DefaultFrameRate;
        public int YawCount       = DemoCharacterCatalog.YawCount;
        public int CurrentRow     = DemoCharacterCatalog.RowIdle;

        public IReadOnlyList<DemoCharacterCard> Cards => _cards;

        void Start()
        {
            BuildEnvironment();
            BuildCamera();
            BuildCards();
            BuildUI();
        }

        // =================================================================
        // Scene construction
        // =================================================================

        void BuildEnvironment()
        {
            // ── Sun ─────────────────────────────────────────────────────────
            var sunGO = new GameObject("Sun");
            sunGO.transform.SetParent(transform, false);
            sunGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var sun = sunGO.AddComponent<Light>();
            sun.type      = LightType.Directional;
            sun.color     = new Color(1f, 0.96f, 0.88f);
            sun.intensity = 1.6f;
            sun.shadows   = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sunGO.AddComponent<UniversalAdditionalLightData>();

            // ── Ground ──────────────────────────────────────────────────────
            var groundGO = new GameObject("Ground");
            groundGO.transform.SetParent(transform, false);
            groundGO.transform.position = new Vector3(0f, -0.401f, 0f);
            groundGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            groundGO.transform.localScale = new Vector3(60f, 60f, 1f);
            var mf = groundGO.AddComponent<MeshFilter>();
            mf.sharedMesh = BuildGroundMesh();
            var mr = groundGO.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MaterialFactory.GroundMaterial();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows    = true;

            // ── Global volume ──────────────────────────────────────────────
            var volumeGO = new GameObject("Global Volume");
            volumeGO.transform.SetParent(transform, false);
            var vol = volumeGO.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 0;
        }

        void BuildCamera()
        {
            var camGO = new GameObject("Main Camera");
            camGO.tag = "MainCamera";
            camGO.transform.SetParent(transform, false);
            _camera = camGO.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.043f, 0.058f, 0.090f, 1f);
            _camera.fieldOfView = CAMERA_FOV;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane  = 100f;
            _camera.allowHDR = true;
            _camera.allowMSAA = true;
            camGO.AddComponent<AudioListener>();

            var urpData = camGO.AddComponent<UniversalAdditionalCameraData>();
            urpData.renderPostProcessing = true;

            _orbitDistance       = CAMERA_DISTANCE;
            _targetOrbitDistance = CAMERA_DISTANCE;
            ApplyCameraOrbit();
        }

        void BuildCards()
        {
            var defs = DemoCharacterCatalog.GetDefinitions();
            int count = Mathf.Min(defs.Count, CARD_COUNT);

            float totalWidth = (count - 1) * CARD_SPACING_X;
            float startX = -totalWidth * 0.5f;

            for (int i = 0; i < count; i++)
            {
                var def = defs[i];
                var go = new GameObject($"Card_{def.DisplayName}");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(startX + i * CARD_SPACING_X, 0f, 0f);

                var card = go.AddComponent<DemoCharacterCard>();
                card.Initialize(def, this);
                card.SetRow(CurrentRow);
                _cards.Add(card);
            }
        }

        void BuildUI()
        {
            var uiGO = new GameObject("DemoUI");
            uiGO.transform.SetParent(transform, false);
            _ui = uiGO.AddComponent<DemoUI>();
            _ui.AttachTo(this);
        }

        // =================================================================
        // Update loop — input & camera
        // =================================================================

        void Update()
        {
            HandlePinchZoom();
            HandlePrimaryPointer();
            HandleAltMouseDrag();
            HandleWheelZoom();
            HandleHotkeys();

            _orbitDistance = Mathf.SmoothDamp(_orbitDistance, _targetOrbitDistance,
                ref _orbitDistanceVelocity, ZOOM_SMOOTH_TIME);
            ApplyCameraOrbit();

            if (_camera != null)
            {
                Vector3 camPos = _camera.transform.position;
                foreach (var c in _cards) c.UpdateFromCamera(camPos);
            }
        }

        void HandlePrimaryPointer()
        {
            if (_pinching) { _primaryActive = false; return; }
            var pointer = Pointer.current;
            if (pointer == null) return;

            bool down = pointer.press.wasPressedThisFrame;
            bool held = pointer.press.isPressed;
            bool up   = pointer.press.wasReleasedThisFrame;

            if (down)
            {
                if (_ui != null && _ui.PointerOverUI) { _primaryActive = false; return; }
                _primaryActive   = true;
                _primaryDragging = false;
                _primaryDownPos  = pointer.position.ReadValue();
                _primaryLastPos  = _primaryDownPos;
            }

            if (_primaryActive && held)
            {
                Vector2 cur = pointer.position.ReadValue();
                if (!_primaryDragging)
                {
                    float thresholdPx = DRAG_THRESHOLD_CSSPX * GetInputDprScale();
                    if ((cur - _primaryDownPos).sqrMagnitude > thresholdPx * thresholdPx)
                        _primaryDragging = true;
                }
                if (_primaryDragging)
                {
                    Vector2 delta = cur - _primaryLastPos;
                    _orbitYaw   += delta.x * 0.25f;
                    _orbitPitch  = Mathf.Clamp(_orbitPitch - delta.y * 0.2f, 5f, 60f);
                }
                _primaryLastPos = cur;
            }

            if (_primaryActive && up)
            {
                // Up without a drag = click → open atlas modal.
                if (!_primaryDragging)
                    TryOpenAtlasOnClick(pointer.position.ReadValue());
                _primaryActive   = false;
                _primaryDragging = false;
            }
        }

        void TryOpenAtlasOnClick(Vector2 screenPos)
        {
            if (_camera == null || _ui == null) return;

            // Sprite quad colliders are triggers, so QueryTriggerInteraction
            // must be Collide for the raycast to see them.
            var ray = _camera.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out var hit, 100f, ~0, QueryTriggerInteraction.Collide))
                return;
            foreach (var c in _cards)
            {
                if (c.SpriteClickCollider == hit.collider)
                {
                    _ui.OpenAtlasModal(c.BakeKey, c.Definition.DisplayName);
                    return;
                }
            }
        }

        void HandleAltMouseDrag()
        {
            var mouse = Mouse.current;
            if (mouse == null) { _altDragging = false; return; }

            bool downAlt = mouse.rightButton.wasPressedThisFrame  || mouse.middleButton.wasPressedThisFrame;
            bool upAlt   = mouse.rightButton.wasReleasedThisFrame || mouse.middleButton.wasReleasedThisFrame;
            Vector2 mp   = mouse.position.ReadValue();

            if (downAlt) { _altDragging = true; _altLastMouse = mp; }
            if (upAlt)     _altDragging = false;

            if (_altDragging)
            {
                Vector2 delta = mp - _altLastMouse;
                _altLastMouse = mp;
                _orbitYaw   += delta.x * 0.25f;
                _orbitPitch  = Mathf.Clamp(_orbitPitch - delta.y * 0.2f, 5f, 60f);
            }
        }

        void HandlePinchZoom()
        {
            var ts = Touchscreen.current;
            if (ts == null) { _pinching = false; return; }

            TouchControl t0 = null, t1 = null;
            int active = 0;
            foreach (var t in ts.touches)
            {
                if (!t.press.isPressed) continue;
                if      (active == 0) t0 = t;
                else if (active == 1) t1 = t;
                active++;
                if (active >= 2) break;
            }

            if (active >= 2)
            {
                Vector2 p0 = t0.position.ReadValue();
                Vector2 p1 = t1.position.ReadValue();
                float dist = Vector2.Distance(p0, p1);
                if (!_pinching) { _pinching = true; _lastPinchDist = dist; return; }
                float deltaPx = dist - _lastPinchDist;
                _targetOrbitDistance = Mathf.Clamp(
                    _targetOrbitDistance - deltaPx * PINCH_SENSITIVITY,
                    MIN_ZOOM, MAX_ZOOM);
                _lastPinchDist = dist;
            }
            else
            {
                _pinching = false;
            }
        }

        void HandleWheelZoom()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            float raw = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(raw) < 0.001f) return;
            float scroll = Mathf.Clamp(raw / WHEEL_NORMALIZE_DIV,
                -WHEEL_NORMALIZE_CLAMP, WHEEL_NORMALIZE_CLAMP);
            _targetOrbitDistance = Mathf.Clamp(
                _targetOrbitDistance - scroll * WHEEL_SENSITIVITY,
                MIN_ZOOM, MAX_ZOOM);
        }

        void HandleHotkeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.digit1Key.wasPressedThisFrame) SetRowAll(DemoCharacterCatalog.RowIdle);
            if (kb.digit2Key.wasPressedThisFrame) SetRowAll(DemoCharacterCatalog.RowRun);
            if (kb.digit3Key.wasPressedThisFrame) SetRowAll(DemoCharacterCatalog.RowJump);

            const int RowCount = 3; // Idle, Run, Jump
            if (kb.leftArrowKey.wasPressedThisFrame)
                SetRowAll((CurrentRow - 1 + RowCount) % RowCount);
            if (kb.rightArrowKey.wasPressedThisFrame)
                SetRowAll((CurrentRow + 1) % RowCount);
        }

        void ApplyCameraOrbit()
        {
            if (_camera == null) return;
            float yawRad   = _orbitYaw   * Mathf.Deg2Rad;
            float pitchRad = _orbitPitch * Mathf.Deg2Rad;
            float horiz = Mathf.Cos(pitchRad) * _orbitDistance;
            Vector3 pos = new Vector3(
                Mathf.Sin(yawRad) * horiz,
                Mathf.Sin(pitchRad) * _orbitDistance,
                Mathf.Cos(yawRad) * horiz);
            _camera.transform.position = new Vector3(pos.x, Mathf.Max(pos.y, 1.5f), pos.z);
            _camera.transform.LookAt(new Vector3(0f, CAMERA_HEIGHT * 0.4f, 0f));
        }

        // =================================================================
        // UI hooks
        // =================================================================

        public void SetFramePixelSize(int size)
        {
            FramePixelSize = Mathf.Clamp(size, 32, 256);
            foreach (var c in _cards) c.OnQualitySettingsChanged();
        }

        public void SetFrameRate(int rate)
        {
            FrameRate = Mathf.Clamp(rate, 6, 30);
            foreach (var c in _cards) c.OnQualitySettingsChanged();
        }

        public void SetYawCount(int count)
        {
            YawCount = Mathf.Clamp(count, 1, 16);
            foreach (var c in _cards) c.OnQualitySettingsChanged();
        }

        public void SetRowAll(int row)
        {
            CurrentRow = row;
            foreach (var c in _cards) c.SetRow(row);
            // Single notify path — hotkeys, UI clicks, etc. all flow here.
            if (_ui != null) _ui.OnRowChangedExternal();
        }

        public bool IsBaking
        {
            get
            {
                foreach (var c in _cards) if (!c.IsBakeReady) return true;
                return false;
            }
        }

        // =================================================================
        // DPR helper (CSS pixels)
        // =================================================================
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern float SpriteBakerDemo_GetDevicePixelRatio();
#endif
        public static float GetInputDprScale()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                float dpr = SpriteBakerDemo_GetDevicePixelRatio();
                if (dpr > 0f) return dpr;
            }
            catch { /* fall through */ }
#endif
            float dpi = Screen.dpi;
            if (dpi <= 0f) return 1f;
            return Mathf.Max(1f, dpi / 96f);
        }

        // =================================================================
        // Helpers
        // =================================================================

        static Mesh BuildGroundMesh()
        {
            var mesh = new Mesh { name = "DemoGround" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f),
            };
            mesh.normals = new[] { -Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward };
            mesh.uv = new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
