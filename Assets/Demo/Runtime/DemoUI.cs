using System.Runtime.InteropServices;
using SpriteBaker;
using UIDocumentDesignSystem;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;

namespace SpriteBakerDemo
{
    /// <summary>
    /// UI Toolkit overlay. Layout in <c>Resources/UI/DemoUI.uxml</c>,
    /// styled by the ds-* design system at <c>Assets/DesignSystem</c>
    /// (junctioned from the unity-ui-document-design-system submodule).
    /// This file only binds data + callbacks; no inline styling.
    /// </summary>
    public class DemoUI : MonoBehaviour
    {
        const float MOBILE_BREAKPOINT = 768f;
        const string UXML_RESOURCE_PATH = "UI/DemoUI";
        const string MOBILE_CLASS = "mobile";
        const string STATUS_VISIBLE_CLASS = "is-visible";

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern float SpriteBakerDemo_GetDevicePixelRatio();
#endif

        DemoBootstrap _bootstrap;
        UIDocument _doc;
        VisualElement _root;
        VisualElement _statusToast;
        Label _statusLabel;
        VisualElement _statusIcon;
        IVisualElementScheduledItem _statusFade;

        VisualElement _panelTitle;
        VisualElement _panelPromo;
        VisualElement _panelControls;
        VisualElement _panelInstructions;

        Button _btnIdle;
        Button _btnRun;
        Button _btnJump;
        Label  _frameSizeValue;
        Label  _frameRateValue;
        Label  _yawCountValue;
        SliderInt _frameSizeSlider;
        SliderInt _frameRateSlider;
        SliderInt _yawCountSlider;

        VisualElement _atlasModal;
        VisualElement _atlasModalImage;
        Label         _atlasModalTitle;
        Label         _atlasModalMeta;
        Button        _atlasModalClose;
        VisualElement _atlasModalBackdrop;
        int           _atlasModalKey;
        bool          _atlasModalIsOpen;

        bool _lastBakingState;

        public bool PointerOverUI { get; private set; }

        public void AttachTo(DemoBootstrap bootstrap)
        {
            _bootstrap = bootstrap;
        }

        void Start()
        {
            EnsureInputSystem();
            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = MakePanelSettings();
            _doc.visualTreeAsset = Resources.Load<VisualTreeAsset>(UXML_RESOURCE_PATH);
            if (_doc.visualTreeAsset == null)
            {
                Debug.LogError($"[SpriteBakerDemo] Could not load {UXML_RESOURCE_PATH}.uxml. " +
                               "Did the design-system junction (Assets/DesignSystem) get created? " +
                               "See README 'Cloning' section.");
            }

            gameObject.AddComponent<DesignSystemRuntime>();

            StartCoroutine(WaitForRootThenBuild());
        }

        System.Collections.IEnumerator WaitForRootThenBuild()
        {
            int safety = 60;
            while (safety-- > 0 && (_doc == null || _doc.rootVisualElement == null))
                yield return null;

            if (_doc == null || _doc.rootVisualElement == null)
            {
                Debug.LogWarning("[SpriteBakerDemo] DemoUI rootVisualElement never appeared — UI not built.");
                yield break;
            }
            BindLayout();
        }

        void BindLayout()
        {
            _root = _doc.rootVisualElement;
            if (_root == null) return;

            _panelTitle        = _root.Q<VisualElement>("panel-title");
            _panelPromo        = _root.Q<VisualElement>("panel-promo");
            _panelControls     = _root.Q<VisualElement>("panel-controls");
            _panelInstructions = _root.Q<VisualElement>("panel-instructions");
            _statusToast       = _root.Q<VisualElement>("status");
            _statusLabel       = _root.Q<Label>("status-text");
            _statusIcon        = _root.Q<VisualElement>("status-icon");
            _frameSizeValue    = _root.Q<Label>("frame-size-value");
            _frameRateValue    = _root.Q<Label>("frame-rate-value");
            _yawCountValue     = _root.Q<Label>("yaw-count-value");
            _btnIdle           = _root.Q<Button>("btn-idle");
            _btnRun            = _root.Q<Button>("btn-run");
            _btnJump           = _root.Q<Button>("btn-jump");

            _atlasModal         = _root.Q<VisualElement>("atlas-modal");
            _atlasModalImage    = _root.Q<VisualElement>("atlas-modal-image");
            _atlasModalTitle    = _root.Q<Label>("atlas-modal-title");
            _atlasModalMeta     = _root.Q<Label>("atlas-modal-meta");
            _atlasModalClose    = _root.Q<Button>("atlas-modal-close");
            _atlasModalBackdrop = _root.Q<VisualElement>("atlas-modal-backdrop");

            BindPromoLinks();
            BindControls();
            BindAtlasModal();

            _root.RegisterCallback<GeometryChangedEvent>(_ => ApplyResponsive());
            ApplyResponsive();

            // Show "Baking..." for the initial pass. _lastBakingState
            // mirrors so the next Update tick only fires on a transition.
            bool baking = _bootstrap != null && _bootstrap.IsBaking;
            UpdateBakingState(baking);
            _lastBakingState = baking;

            foreach (var panel in new[] { _panelTitle, _panelPromo, _panelControls, _panelInstructions })
            {
                if (panel == null) continue;
                panel.RegisterCallback<PointerEnterEvent>(_ => PointerOverUI = true);
                panel.RegisterCallback<PointerLeaveEvent>(_ => PointerOverUI = false);
            }

            UpdateRowButtonHighlight();
        }

        void BindPromoLinks()
        {
            BindButtonLink("promo-github", "https://github.com/sinanata/unity-3d-to-sprite-baker");
            BindLabelLink("credit-steam",  "https://store.steampowered.com/app/2269500/");
            BindLabelLink("credit-kenney", "https://kenney.nl/");
        }

        void BindButtonLink(string elementName, string url)
        {
            var btn = _root.Q<Button>(elementName);
            if (btn != null) btn.clicked += () => Application.OpenURL(url);
        }

        void BindLabelLink(string elementName, string url)
        {
            var lbl = _root.Q<Label>(elementName);
            if (lbl != null)
                lbl.RegisterCallback<ClickEvent>(_ => Application.OpenURL(url));
        }

        void BindControls()
        {
            if (_btnIdle != null) _btnIdle.clicked += () => OnRowClicked(DemoCharacterCatalog.RowIdle, "Idle");
            if (_btnRun  != null) _btnRun.clicked  += () => OnRowClicked(DemoCharacterCatalog.RowRun,  "Run");
            if (_btnJump != null) _btnJump.clicked += () => OnRowClicked(DemoCharacterCatalog.RowJump, "Jump");

            _frameSizeSlider = _root.Q<SliderInt>("frame-size-slider");
            if (_frameSizeSlider != null)
            {
                _frameSizeSlider.value = _bootstrap.FramePixelSize;
                if (_frameSizeValue != null) _frameSizeValue.text = $"{_bootstrap.FramePixelSize} px";
                _frameSizeSlider.RegisterValueChangedCallback(evt =>
                {
                    int snapped = SnapToPowerOfTwo(evt.newValue, 32, 256);
                    if (snapped != evt.newValue)
                    {
                        _frameSizeSlider.SetValueWithoutNotify(snapped);
                    }
                    _bootstrap.SetFramePixelSize(snapped);
                    if (_frameSizeValue != null) _frameSizeValue.text = $"{snapped} px";
                    ShowStatus($"Frame size: {snapped} px (re-baking)", spinning: true);
                });
            }

            _frameRateSlider = _root.Q<SliderInt>("frame-rate-slider");
            if (_frameRateSlider != null)
            {
                _frameRateSlider.value = _bootstrap.FrameRate;
                if (_frameRateValue != null) _frameRateValue.text = $"{_bootstrap.FrameRate} fps";
                _frameRateSlider.RegisterValueChangedCallback(evt =>
                {
                    _bootstrap.SetFrameRate(evt.newValue);
                    if (_frameRateValue != null) _frameRateValue.text = $"{evt.newValue} fps";
                    ShowStatus($"Frame rate: {evt.newValue} fps (re-baking)", spinning: true);
                });
            }

            // Snapped to {1, 4, 8, 16}. Atlas + bake time scale linearly.
            _yawCountSlider = _root.Q<SliderInt>("yaw-count-slider");
            if (_yawCountSlider != null)
            {
                _yawCountSlider.value = _bootstrap.YawCount;
                if (_yawCountValue != null) _yawCountValue.text = _bootstrap.YawCount.ToString();
                _yawCountSlider.RegisterValueChangedCallback(evt =>
                {
                    int snapped = SnapToYawCount(evt.newValue);
                    if (snapped != evt.newValue)
                    {
                        _yawCountSlider.SetValueWithoutNotify(snapped);
                    }
                    _bootstrap.SetYawCount(snapped);
                    if (_yawCountValue != null) _yawCountValue.text = snapped.ToString();
                    ShowStatus($"Yaw angles: {snapped} (re-baking)", spinning: true);
                });
            }
        }

        // {1, 4, 8, 16} = none, cardinal, cardinal+diagonal, 22.5° bins.
        static int SnapToYawCount(int value)
        {
            int[] options = { 1, 4, 8, 16 };
            int best = options[0];
            int bestDist = System.Math.Abs(value - best);
            for (int i = 1; i < options.Length; i++)
            {
                int d = System.Math.Abs(value - options[i]);
                if (d < bestDist) { best = options[i]; bestDist = d; }
            }
            return best;
        }

        // ─── Atlas modal ────────────────────────────────────────────────

        void BindAtlasModal()
        {
            if (_atlasModalClose != null)
                _atlasModalClose.clicked += CloseAtlasModal;

            if (_atlasModalBackdrop != null)
                _atlasModalBackdrop.RegisterCallback<ClickEvent>(_ => CloseAtlasModal());

            // Block world clicks while the modal is open, so closing
            // doesn't re-open another via raycast click-through.
            if (_atlasModal != null)
            {
                _atlasModal.RegisterCallback<PointerEnterEvent>(_ => PointerOverUI = true);
                _atlasModal.RegisterCallback<PointerLeaveEvent>(_ => PointerOverUI = false);
            }
        }

        public void OpenAtlasModal(int bakeKey, string title)
        {
            if (_atlasModal == null) return;
            if (!SpriteAtlasCache.TryGet(bakeKey, out var atlas) || atlas.Atlas == null)
            {
                ShowStatus("Atlas not ready yet — try again in a moment.", spinning: true);
                return;
            }

            _atlasModalKey = bakeKey;
            _atlasModalIsOpen = true;
            if (_atlasModalTitle != null) _atlasModalTitle.text = $"Sprite Atlas — {title}";

            if (_atlasModalImage != null)
            {
                // UI Toolkit doesn't auto-size a VisualElement to its
                // background-image; size manually + cap width.
                _atlasModalImage.style.backgroundImage = new StyleBackground(atlas.Atlas);
                float texW = atlas.Atlas.width;
                float texH = atlas.Atlas.height;
                if (texW > 0f && texH > 0f)
                {
                    const float MAX_DISPLAY_W = 720f;
                    float aspect = texH / texW;
                    float displayW = Mathf.Min(MAX_DISPLAY_W, texW);
                    float displayH = displayW * aspect;
                    _atlasModalImage.style.width  = displayW;
                    _atlasModalImage.style.height = displayH;
                }
            }

            if (_atlasModalMeta != null)
            {
                int rows = atlas.Rows == null ? 0 : atlas.Rows.Length;
                _atlasModalMeta.text =
                    $"{atlas.Atlas.width} × {atlas.Atlas.height} px · " +
                    $"{atlas.AtlasCols} cols × {rows} states × {atlas.YawCount} yaws · " +
                    $"frame {atlas.FramePixelSize} px · " +
                    $"{atlas.QuadWidth:0.00} × {atlas.QuadHeight:0.00} world units";
            }

            _atlasModal.RemoveFromClassList("demo-modal--hidden");
        }

        void CloseAtlasModal()
        {
            if (_atlasModal == null) return;
            _atlasModalIsOpen = false;
            _atlasModal.AddToClassList("demo-modal--hidden");
            // Drop the texture so a re-bake can free the prior atlas.
            if (_atlasModalImage != null)
                _atlasModalImage.style.backgroundImage = new StyleBackground();
            // Unity 6 UI Toolkit doesn't always fire PointerLeave on
            // display:none, so PointerOverUI stays stuck without this.
            PointerOverUI = false;
        }

        void OnRowClicked(int row, string label)
        {
            _bootstrap.SetRowAll(row);
            ShowStatus($"Switched to: {label}");
        }

        // Called by DemoBootstrap.SetRowAll for every row change (button
        // click, hotkey 1/2/3, arrow keys), so the highlight tracks all
        // input paths.
        public void OnRowChangedExternal()
        {
            UpdateRowButtonHighlight();
        }

        void UpdateRowButtonHighlight()
        {
            ToggleClass(_btnIdle, "ds-btn--primary",  _bootstrap.CurrentRow == DemoCharacterCatalog.RowIdle);
            ToggleClass(_btnIdle, "ds-btn--ghost",  !(_bootstrap.CurrentRow == DemoCharacterCatalog.RowIdle));
            ToggleClass(_btnRun,  "ds-btn--primary",  _bootstrap.CurrentRow == DemoCharacterCatalog.RowRun);
            ToggleClass(_btnRun,  "ds-btn--ghost",  !(_bootstrap.CurrentRow == DemoCharacterCatalog.RowRun));
            ToggleClass(_btnJump, "ds-btn--primary",  _bootstrap.CurrentRow == DemoCharacterCatalog.RowJump);
            ToggleClass(_btnJump, "ds-btn--ghost",  !(_bootstrap.CurrentRow == DemoCharacterCatalog.RowJump));
        }

        // Snap to a discrete set so atlas widths stay clean.
        static int SnapToPowerOfTwo(int value, int min, int max)
        {
            int[] options = { 32, 48, 64, 96, 128, 192, 256 };
            int best = options[0];
            int bestDist = System.Math.Abs(value - best);
            for (int i = 1; i < options.Length; i++)
            {
                int d = System.Math.Abs(value - options[i]);
                if (d < bestDist) { best = options[i]; bestDist = d; }
            }
            return Mathf.Clamp(best, min, max);
        }

        // =================================================================
        // Per-frame state sync — disable Idle/Run/Jump while baking
        // =================================================================

        void Update()
        {
            if (_root == null || _bootstrap == null) return;
            bool baking = _bootstrap.IsBaking;
            if (baking != _lastBakingState)
            {
                _lastBakingState = baking;
                UpdateBakingState(baking);
            }
            else if (baking)
            {
                // Defeat any 1.5 s fade scheduled by a slider's value-
                // changed callback while a bake is still in flight.
                _statusFade?.Pause();
            }

            if (_atlasModalIsOpen)
            {
                var kb = Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame)
                    CloseAtlasModal();
            }
        }

        void UpdateBakingState(bool baking)
        {
            // Lock quality sliders mid-bake so the user can't queue a
            // doubling chain of evictions. Row switch stays live.
            SetSlidersInteractive(!baking);

            if (baking)
            {
                ShowStatus("Baking sprites...", spinning: true, persist: true);
            }
            else
            {
                ShowStatus("Ready.", spinning: false);
            }
        }

        void SetSlidersInteractive(bool enabled)
        {
            if (_frameSizeSlider != null) _frameSizeSlider.SetEnabled(enabled);
            if (_frameRateSlider != null) _frameRateSlider.SetEnabled(enabled);
            if (_yawCountSlider  != null) _yawCountSlider.SetEnabled(enabled);
        }

        static void ToggleClass(VisualElement el, string cls, bool on)
        {
            if (el == null) return;
            bool has = el.ClassListContains(cls);
            if (on && !has) el.AddToClassList(cls);
            else if (!on && has) el.RemoveFromClassList(cls);
        }

        // =================================================================
        // Responsive — toggle .mobile on root; the rest is USS
        // =================================================================

        void ApplyResponsive()
        {
            if (_root == null) return;
            float w = _root.layout.width;
            if (w <= 0f || float.IsNaN(w))
            {
                float dpr = GetEffectiveDpr();
                w = Screen.width / Mathf.Max(1f, dpr);
            }

            bool mobile = w < MOBILE_BREAKPOINT;
            if (mobile && !_root.ClassListContains(MOBILE_CLASS))
                _root.AddToClassList(MOBILE_CLASS);
            else if (!mobile && _root.ClassListContains(MOBILE_CLASS))
                _root.RemoveFromClassList(MOBILE_CLASS);
        }

        // =================================================================
        // Status toast
        // =================================================================

        public void ShowStatus(string text, bool spinning = false, bool persist = false)
        {
            if (_statusToast == null || _statusLabel == null) return;
            _statusLabel.text = text;

            ToggleClass(_statusIcon, "ds-icon--info",     !spinning);
            ToggleClass(_statusIcon, "ds-icon--refresh",   spinning);
            ToggleClass(_statusIcon, "is-spinning",        spinning);

            if (!_statusToast.ClassListContains(STATUS_VISIBLE_CLASS))
                _statusToast.AddToClassList(STATUS_VISIBLE_CLASS);
            _statusFade?.Pause();
            if (!persist)
            {
                _statusFade = _statusToast.schedule.Execute(() =>
                    _statusToast.RemoveFromClassList(STATUS_VISIBLE_CLASS)
                ).StartingIn(1500);
            }
        }

        // =================================================================
        // Panel / EventSystem
        // =================================================================

        static PanelSettings MakePanelSettings()
        {
            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.name = "DemoUIPanelSettings";

            var theme = Resources.Load<ThemeStyleSheet>("UnityDefaultRuntimeTheme");
            if (theme != null) ps.themeStyleSheet = theme;
            else Debug.LogWarning("[SpriteBakerDemo] UnityDefaultRuntimeTheme.tss missing in Resources — UI controls may render unstyled.");

            ps.scaleMode = PanelScaleMode.ConstantPixelSize;
            ps.scale = GetEffectiveDpr();
            ps.sortingOrder = 1;
            ps.targetDisplay = 0;
            ps.clearColor = false;
            ps.colorClearValue = new Color(0, 0, 0, 0);
            return ps;
        }

        static float GetEffectiveDpr()
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

        static void EnsureInputSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }
    }
}
