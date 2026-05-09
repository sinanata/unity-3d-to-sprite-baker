# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Loose-clip bake path** (`SpriteBakeRequest.Clips`). When set, the baker drives the SkinnedMeshRenderer via a `PlayableGraph` + `AnimationClipPlayable` per row instead of an `Animator`+`RuntimeAnimatorController`. Same matching rules (case-insensitive, separator-stripped clip-name lookup) so callers can swap pipelines without renaming clip references. Works for any project with bare AnimationClips — Kenney / Mixamo / asset-store packs that ship raw clips with no controller, the common case for FBX-imported animations. The original AnimatorController path stays as the alternative for projects that already have one.
- **`CaptureRotation` field** on `SpriteBakeRequest`. Lets per-prefab orientation be specified instead of the pre-2026 hard-coded `(0, 90, 0)` rotation, which silently captured the back / side of any rig not authored facing +X. Default `Quaternion.identity` matches the prefab's authored orientation; Kenney AC2 wants `Quaternion.Euler(0, 180, 0)`; Mixamo wants identity.
- **Capture-stage lighting rig.** `CaptureLighting` struct on `SpriteBakeRequest` configures a directional key + fill + ambient boost rig, spawned and torn down per bake. Without it, Y=2000 has no scene lights and URP/Lit characters render solid black on the offscreen capture stage. `CaptureLighting.Default` is the recommended starting point; set `DisableDefaultRig = true` and stage your own lights inside `PreCaptureCallback` for custom looks.
- **Bind-state reset on re-bake** in `AnimatedSpriteRenderer.Bind`. Calling `Bind(newKey)` now resets `hasAtlas` so a quality-change re-bake doesn't leave the renderer pointing at the previous (now-destroyed by `SpriteAtlasCache.Evict`) atlas — sampling garbage memory.
- **Pre-bind row request preserved.** `SetRow(r)` before the atlas binds is now remembered, so gameplay code that calls `Bind()` then immediately `SetRow(Run)` doesn't briefly show row 0 (Idle) before snapping to Run.
- **`FrameSampler` strategy interface** in `SpriteAtlasBaker`. Three implementations — `AnimatorSampler`, `PlayableGraphSampler`, `BindPoseSampler` — share the same contract: `GetClipLength`, `PrepareRow`, `SampleFrame`, `Dispose`. Lets the bake loop stay one screen of code regardless of which animation backend drives the SMR.
- **Bind-pose fallback path.** When neither `Clips` nor `AnimatorController` is supplied, the baker still produces a single-column atlas with the prefab's bind-pose snapshot per row. Useful for static "card portrait" captures.
- **Full Unity project skeleton.** `Packages/manifest.json` (URP 17.3.0, Input System 1.18, AI Navigation, Visual Scripting), `ProjectSettings/`, `Assets/Settings/URPRenderPipelineAsset.asset`, default URP global settings + volume profile.
- **WebGL demo scene** at `Assets/Demo/Scenes/SpriteBakerDemo.unity` — four side-by-side cards, each splitting **live 3D character** vs **baked sprite playback** at the same world position. Top-bar tabs (Idle / Run / Jump) flip every card's animation simultaneously, demonstrating silhouette parity between the SMR and the baked atlas across every clip. Sliders for Frame Size + Frame Rate force a fresh re-bake at the new quality.
- **`Assets/Demo/Runtime/`** — `DemoBootstrap.cs` (auto-spawns scene, camera, lighting, 4-card grid), `DemoCharacterCatalog.cs` (4 Kenney AC2 skin definitions + bake-key derivation), `DemoCharacterCard.cs` (per-card live + sprite split, side-by-side), `MaterialFactory.cs` (URP/Lit body materials + ground / pedestal / wall), `DemoUI.cs` (UI Toolkit overlay, ds-* design system styled, mobile breakpoint flip).
- **`Assets/Demo/Resources/UI/DemoUI.uxml`** — UI Toolkit overlay layout consuming the [`unity-ui-document-design-system`](https://github.com/sinanata/unity-ui-document-design-system) submodule (vendored at `Vendor/unity-ui-document-design-system`, junctioned into `Assets/DesignSystem`). Title block with credits row, status toast, controls panel, instructions / hotkeys panel, GitHub promo button.
- **`Assets/Demo/Editor/DemoModelImporter.cs`** — `AssetPostprocessor` that flips Read/Write on imported FBXes (the baker needs `mesh.vertices` access at runtime), strips the auto-generated colliders, sets `materialImportMode = None`, configures Generic rig, force-renames the imported `AnimationClip` to match the FBX basename so `idle.fbx → "idle"` regardless of the FBX-embedded animation name (Maya `"Take 001"` / mixamo `"mixamo.com"` etc would otherwise leak through).
- **`Assets/Demo/Editor/KenneyControllerBuilder.cs`** — auto-generates `CharacterMediumController.controller` linking Idle / Run / Jump as states whose names match the imported clip names. Auto-triggers on FBX import via `DemoAssetWatcher`; menu fallback at **Sprite Baker Demo → Rebuild Kenney Controller**. Used by the live-mesh side of each card; the bake itself goes through the loose-clip path so the controller is purely cosmetic.
- **`Assets/WebGLTemplates/SpriteBakerDemo/index.html`** — HiDPI-aware WebGL loader with branded splash, mobile viewport meta, Apple-style status bar, Brotli decompression fallback. `matchWebGLToCanvasSize: true` for crisp rendering on Retina; `WebGLDevicePixelRatio.jslib` bridges `window.devicePixelRatio` to Unity so `PanelSettings.scale = DPR` keeps a 36 px UI button rendering at 36 CSS-px on every device.
- **`Tools/Build/Build-Demo.ps1`** — Windows orchestrator for the WebGL build. Single PowerShell command builds, optionally serves on `:3000` for smoke-test, optionally force-pushes to `gh-pages` for [the live preview](https://sinanata.github.io/unity-3d-to-sprite-baker/). Mirrors the unity-mesh-fracture orchestrator's recovery logic (Burst-AOT cache retry, native NTSTATUS crash labels, process-tree kill on Ctrl+C, stale `Temp/UnityLockfile` removal, JSON build report, live phase progress from Unity's `DisplayProgressbar:` log markers).
- **`Tools/Build/Deploy-GhPages.ps1`** — single-commit force-push to the `gh-pages` orphan branch via `git worktree`. The branch always contains exactly one commit, so the public repo doesn't accumulate ~10 MB build artefacts per deploy.
- **`Assets/Editor/BuildCli.cs`** — Unity batchmode entry point (`SpriteBakerDemo.BuildTools.BuildCli.BuildWebGL`). Re-asserts `compressionFormat = Brotli`, `decompressionFallback = true`, `template = "PROJECT:SpriteBakerDemo"` defensively at build time. Writes a JSON report to `Tools/Build/output/report-*.json` so the orchestrator validates success without scraping the log.
- **`Assets/Editor/EnsureUrpAssets.cs`** — first-load URP bootstrapper. `git clone && open in Unity` produces a working URP project on the first launch, no manual menu commands required. Idempotent — subsequent runs detect the URP asset is in place and no-op.
- **`Assets/Editor/EnsureBuildShaders.cs`** — adds URP/Lit, URP/Simple Lit, URP/Unlit, and SpriteBaker/AtlasCutout to `GraphicsSettings.AlwaysIncludedShaders` so player builds (especially WebGL with aggressive shader stripping) keep them. Runs at editor load AND immediately before every build via `IPreprocessBuildWithReport`.
- **Vendored Kenney AC2 content** in `Assets/Demo/Resources/Models/`: `characterMedium.fbx` (rig), `Animations/idle.fbx` + `run.fbx` + `jump.fbx` (loose AnimationClips), `Skins/criminalMaleA.png` + `skaterMaleA.png` + `skaterFemaleA.png` + `cyborgFemaleA.png` (4 colour variants), `License.txt` (CC0), `CREDITS.txt` (per-asset attribution + canonical kenney.nl source links).
- **Tap-vs-drag gesture arbitration** in `DemoBootstrap`. Primary-pointer gestures start as candidate-tap and promote to drag once total movement exceeds a DPR-scaled threshold (`DRAG_THRESHOLD_CSSPX = 11`). Same code path handles mouse + touch — no platform branches.
- **Pinch-to-zoom** (touch) and **wheel zoom** (desktop) with platform-aware sensitivity. Wheel handling normalizes Windows mouse-wheel notches (~120/notch) and trackpad smooth-scroll (~1–10/event) to the same effective range, then clamps browser momentum spikes (~300+) to a saturation point. Both write to a target distance and `SmoothDamp` eases the live distance.
- **HiDPI panel scale** (`DemoUI.MakePanelSettings`). `panelSettings.scale = devicePixelRatio` keeps UI components rendering at their declared CSS-pixel size on Retina / mobile devices.

### Changed

- **`SpriteBakeRequest.AnimatorController`** is now optional (was effectively required). Set `Clips` instead, or set neither for a bind-pose-only single-frame atlas.
- **`SpriteAtlasBaker` rotated capture** removed the hard-coded `Quaternion.Euler(0, 90, 0)` and now reads `req.CaptureRotation`. Existing callers depending on the implicit rotation must set `CaptureRotation = Quaternion.Euler(0, 90, 0)` explicitly to preserve their atlas, or — more likely — set the correct rotation for their actual rig orientation.
- **Lighting** is no longer assumed from the surrounding scene. The baker spawns its own rig at the capture stage by default; `Lighting.DisableDefaultRig = true` opts out for callers staging their own lights via `PreCaptureCallback`.
- **README.md**, **CHANGELOG.md**, **CONTRIBUTING.md** comprehensively updated to match the shipped runtime — documented the loose-clip path, the lighting rig, the capture rotation, the demo scene, the build orchestrator, the design-system submodule integration. Cross-linked the sibling unity-mesh-fracture and unity-ui-document-design-system OSS repos.

### Fixed

- **Hard-coded 90° capture rotation** silently captured the back / side of any prefab not authored facing +X. Symptom: the baked atlas showed the character's back of the head while the live SMR rendered the front. Fix: `CaptureRotation` field on the request, defaulting to identity. Kenney AC2 now passes `Quaternion.Euler(0, 180, 0)` explicitly.
- **Solid black baked frames** when the source rig used URP/Lit and the bake was the only thing in the offscreen scene. The Y=2000 capture stage is far from any scene light, so URP/Lit's lambertian term saw only the default ambient (very dark grey on a fresh project). Fix: capture-stage lighting rig spawned per bake (key + fill + ambient boost), torn down at the end. `Lighting.DisableDefaultRig = true` reverts to the old behaviour for callers who stage their own lights.
- **Re-bake left renderer pointing at destroyed atlas.** When `AnimatedSpriteRenderer.Bind(newKey)` was called after a quality-change `SpriteAtlasCache.Evict`, the renderer's `hasAtlas` flag stayed true (from the previous bake) — `Update`'s pre-check skipped `TryBindAtlas`, the renderer kept sampling the old (now destroyed) Texture2D, and frames showed garbage memory. Fix: `Bind` now resets `hasAtlas = false`.
- **`SetRow(r)` before atlas binds was lost.** Gameplay code that called `renderer.Bind(key)` then `renderer.SetRow(Run)` immediately would silently fall back to row 0 (Idle) on the first frame after the bake completed, then snap to Run on the next frame. Fix: `SetRow` now writes `currentRow` even when `!hasAtlas`, so the eventual `TryBindAtlas` lands at the requested row.

## [0.1.0] - 2026-05-02

Initial public release. Extracted from [Leap of Legends](https://leapoflegends.com)' mobile sprite-mode pipeline.

### Added

- `SpriteAtlasBaker` — coroutine-driven worker. Lazy singleton; queue baker requests via `Enqueue`. One bake at a time; one frame capture per Unity frame.
- `SpriteBakeRequest` — input struct. Caller-defined `Key`, prefab, animator controller, frame size, frame rate, list of `SpriteAnimRow` entries, optional `PreCaptureCallback` for skin / attachment hooks.
- `SpriteAnimRow` — per-row spec: row index, clip name (case- and separator-insensitive), loop / single-frame flags, optional blend-shape overrides.
- `SpriteAtlasCache` — global lookup keyed by your int. `TryGet`, `IsReady`, `Evict`, `Clear`.
- `BakedSpriteAtlas` — output struct: atlas Texture2D, shared material, frame size, atlas col count, world-space quad dimensions, per-row metadata.
- `AnimatedSpriteRenderer` — runtime playback MonoBehaviour. Builds a quad sized to `BakedSpriteAtlas.QuadWidth` × `QuadHeight`, plays back via UV updates, supports row switching + facing flip without transform mirroring.
- `SpriteBaker/AtlasCutout` URP shader — alpha-cutout for crisp-edged sprite playback.
- `SpriteBakerDemo` — drop-on-GameObject demo with prefab/controller fields and 1/2/3/4/F runtime controls.
- Async GPU readback pipelined across 3 frames — eliminates per-frame `ReadPixels` stalls.
- Origin-aligned quad framing — playback quad bottom matches model pivot, NOT bounds.min.y.
- Mobile-safe format selection via `SystemInfo.GetCompatibleFormat`.
- Atlas width clamped to 2048 px for low-end mobile texture limits.
- URP detection — auto-disables `renderPostProcessing` on the offscreen capture camera.
