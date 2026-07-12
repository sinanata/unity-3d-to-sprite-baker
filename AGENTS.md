# AGENTS.md

Guidance for AI coding agents (Codex, Cursor, GitHub Copilot, Claude Code, Windsurf, Aider, Zed, and others) working in this repository or wiring this tool into another project. Humans: this is a fast, accurate map. Deeper docs are linked at the bottom.

## What this project is

A drop-in **runtime sprite-atlas baker for Unity 6 + URP**. It takes any 3D animated character prefab and, at game start, captures every requested clip × frame into a single packed `Texture2D` atlas via an offscreen orthographic camera + async GPU readback — then plays the atlas back as a flat textured quad that shares one material across every instance. The runtime is six C# files under `Assets/SpriteBaker/Runtime/`. Namespace: `SpriteBaker`. License: MIT. Battle-tested in Leap of Legends, whose lowest mobile quality preset bakes every character to a sprite at start (a 60-bone skinned mesh → one flat quad).

## Golden rules (do not violate)

1. **Baking is a runtime, async, coroutine-driven process — not editor-only and not synchronous.** `SpriteAtlasBaker.Instance.Enqueue(request)` captures one frame per Unity frame; the atlas is NOT ready the same frame you enqueue. Read results with `SpriteAtlasCache.TryGet(key, out var atlas)`; poll `SpriteAtlasCache.IsReady(key)` / `SpriteAtlasBaker.Instance.IsPending(key)`, or just call `AnimatedSpriteRenderer.Bind(key)` early — it hides its mesh and re-checks every `Update` until the bake lands. To re-bake, `SpriteAtlasCache.Evict(key)` first (a duplicate `Enqueue` on a ready/pending key is silently dropped).
2. **The playback material is URP/Unlit driven from `Resources/SpriteBakerAtlasUnlitTemplate.mat`, NOT the `AtlasCutout.shader`.** The custom `SpriteBaker/AtlasCutout` shader ships as a fallback, but the runtime does `new Material(template)` because a custom URP shader compiled via `new Material(shader)` renders solid black on WebGL2, and the `_ALPHATEST_ON` variant is stripped from the build unless a serialized `.mat` carries the keyword. **Do not "simplify" this to `new Material(Shader.Find("SpriteBaker/AtlasCutout"))`** — that reintroduces the WebGL black-quad bug. Copy the **whole** `Assets/SpriteBaker/` folder (including `Editor/` and the `.mat`), not just `Runtime/`.
3. **Render via URP's `SingleCameraRequest`, not `Camera.Render()`.** `Camera.Render()` happens to work on D3D11 in the editor but is a no-op on WebGL2 URP, producing an empty/black atlas. The baker uses `RenderPipeline.SubmitRenderRequest(cam, new UniversalRenderPipeline.SingleCameraRequest { destination = rt })`.
4. **Every bake needs a capture-stage light rig.** The prefab is instantiated at ~(2000, 2000, 0), far from any scene light; a URP/Lit character there renders solid black without the key + fill + ambient rig the baker spawns (each light needs `UniversalAdditionalLightData`, or WebGL's URP silently drops it). Use `CaptureLighting.Default`, or set `DisableDefaultRig` and stage your own in `PreCaptureCallback`.
5. **`CaptureRotation` composes onto the prefab's authored rotation** (it is not a replacement). `Quaternion.identity` means "natural orientation." Kenney AC2 rigs need `Quaternion.Euler(0, 180, 0)`; Mixamo uses identity. For rigs whose animation curves bind relative to a child transform, set `SampleAnimationTargetPath` (e.g. `"Root"` for Kenney AC2, `"Armature"` / `"Hips"` for Mixamo) or the loose-clip bake captures bind pose.
6. **The per-frame `yield return null` in the bake loop is load-bearing.** It lets Unity's skinning (LateUpdate) re-pose the mesh between captures; remove it and every captured frame shows the identical pose — a silent bug (the atlas "looks almost right"). Do not "optimize" it away.
7. **No `using LeapOfLegends.*` or other product-specific imports.** New runtime code lives in the `SpriteBaker` namespace only; demo-only code lives under `SpriteBaker.Demo`.

## Wire it into your project

The runtime is the `Assets/SpriteBaker/` folder dropped into your `Assets/`. `using SpriteBaker;` then:

```csharp
// 1. At start, enqueue a bake. The cache key is a caller-defined int; derive it so a
//    quality change (size / fps / skin) yields a distinct entry, not a stale atlas.
int key = prefab.name.GetHashCode() ^ (framePixelSize * 31) ^ (frameRate * 17);
SpriteAtlasBaker.Instance.Enqueue(new SpriteBakeRequest {
    Key = key,
    Prefab = characterPrefab,
    Clips = new[] { idle, run, jump },          // raw FBX clips — no AnimatorController required
    FramePixelSize = 128,
    FrameRate = 12,
    CaptureRotation = Quaternion.Euler(0, 180, 0),
    Lighting = CaptureLighting.Default,
    Rows = new[] {
        new SpriteAnimRow { Row = 0, ClipName = "idle", Loop = true },
        new SpriteAnimRow { Row = 1, ClipName = "run",  Loop = true },
        new SpriteAnimRow { Row = 2, ClipName = "jump", SingleFrame = true },
    },
});

// 2. Anywhere in gameplay: bind a flat-quad renderer to the (possibly not-yet-ready) atlas.
var r = host.AddComponent<AnimatedSpriteRenderer>();
r.Bind(key);
r.SetRow(0);            // remembered even if called before the bake lands
r.SetFacing(true);      // flips UVs, no transform mirror
```

The canonical minimal example is `Assets/SpriteBaker/Demo/SpriteBakerDemo.cs` (~110 lines).

## Public API

`SpriteAtlasBaker : MonoBehaviour` (`Assets/SpriteBaker/Runtime/SpriteAtlasBaker.cs`):
```csharp
static SpriteAtlasBaker Instance { get; }    // lazily-created DontDestroyOnLoad singleton
void Enqueue(SpriteBakeRequest req);          // duplicate ready/pending keys are dropped
bool IsPending(int key);
```
`SpriteBakeRequest` (struct — `SpriteBakeRequest.cs`):
```csharp
int Key; GameObject Prefab;
RuntimeAnimatorController AnimatorController;   // path A (optional)
Avatar AvatarOverride;
AnimationClip[] Clips;                          // path B: loose clips, no controller needed
Quaternion CaptureRotation;                     // composed onto authored rotation
int FramePixelSize; int FrameRate;
int CaptureYawCount;                            // 0/1 = single angle; 4/8/16 = turntable rows
SpriteAnimRow[] Rows;
Color BackgroundColor;                          // default transparent
CaptureLighting Lighting;
Action<GameObject> PreCaptureCallback;          // skin / attach hook, before bounds
Action<GameObject> PerFrameCallback;            // after pose write, before render
string SampleAnimationTargetPath;               // e.g. "Root" for Kenney AC2
```
`SpriteAtlasCache` (static — `SpriteAtlasCache.cs`): `bool TryGet(int, out BakedSpriteAtlas)`, `bool IsReady(int)`, `void Evict(int)`, `void Clear()`.
`AnimatedSpriteRenderer : MonoBehaviour` (`AnimatedSpriteRenderer.cs`): `void Bind(int atlasKey)`, `void SetRow(int)`, `void SetFacing(bool right)`, `void SetYaw(float degrees)`, `void SetBillboardYaw(float degrees)`.
`BakedSpriteAtlas` (output): `Texture2D Atlas; Material SharedMaterial; int FramePixelSize; int AtlasCols; float QuadWidth; float QuadHeight; AnimRowInfo[] Rows; int YawCount;`. Also `SpriteAnimRow { int Row; string ClipName; bool Loop; bool SingleFrame; BlendShapeOverride[] BlendShapes; }` and `CaptureLighting` (with `.Default`).

## Repository layout

- `Assets/SpriteBaker/` — **the shippable tool; copy the WHOLE folder.** `Runtime/` (6 files) + `Editor/EnsureAtlasTemplateMaterial.cs` (generates the URP/Unlit template `.mat`; load-bearing for WebGL alpha-clip) + `Resources/` (`Shaders/AtlasCutout.shader` fallback + `SpriteBakerAtlasUnlitTemplate.mat`) + `Demo/SpriteBakerDemo.cs`.
- `Assets/Demo/` — the WebGL 4-card showcase host project (live 3D vs baked sprite). Not part of the tool.
- `Assets/Editor/`, `Assets/Settings/`, `Assets/WebGLTemplates/` — build + URP scaffolding for the demo.
- `Tools/` — the shared build orchestrator (`Tools/.orchestrator` submodule) + a thin `Build-Demo.ps1` shim.
- `Vendor/` — the design-system submodule, used only by the demo UI.

## Conventions when editing

- **Comments answer "why", not "what".** Every `yield return null` in the bake loop earns its keep — document why if you touch one.
- **Loose-clip sampling follows the code, not older prose.** The loose-clip path samples each clip per frame via `AnimationClip.SampleAnimation` (the `SampleAnimationSampler`), chosen because `PlayableGraph.Evaluate(0)` sometimes no-ops in Unity 6 manual time-update and leaves the atlas at bind pose. The three samplers are `AnimatorSampler`, `SampleAnimationSampler`, `BindPoseSampler`. Some README/CHANGELOG prose still says "PlayableGraph" — the code is authoritative.
- **Loop vs one-shot frame timing.** Loop rows sample `[0,1)` as `f / frames`; one-shots sweep `[0,1]` as `f / (frames-1)`.
- **Atlas constraints:** `RGBA32`, `FilterMode.Point`, `TextureWrapMode.Clamp`, width clamped ≤ 2048; `atlas.Apply(false, true)` marks it non-readable.

## Build, preview, validate

Windows-first Unity 6 project (host editor 6000.3.8f1). There is no unit-test suite; validation is visual, through the two demos.

- **Editor preview:** open `Assets/Demo/Scenes/SpriteBakerDemo.unity` and press Play. The minimal example is `Assets/SpriteBaker/Demo/SpriteBakerDemo.cs`.
- **WebGL build, from the repo root in PowerShell:**
  ```powershell
  git submodule update --init --recursive
  copy Tools\Build\config.example.json Tools\Build\config.local.json
  .\Tools\Build\Build-Demo.ps1 -Serve       # builds to build/WebGL/ and serves http://localhost:3000
  ```
  `-Serve` serves locally, `-Deploy` force-pushes to `gh-pages`, `-ClearCache` recovers from a stale Burst-AOT cache.
- **Verify both demos** after any API change (the simple `SpriteBakerDemo.cs` + the WebGL 4-card scene), and confirm the WebGL build matches the editor — that is what catches the black-atlas / stripped-variant regressions.

## Pull request checklist (summary; full list in CONTRIBUTING.md)

- Simple demo still works (assign prefab + clips, Play, the sprite cycles animations on 1/2/3/4 keypress).
- WebGL demo still builds + works (`Tools\Build\Build-Demo.ps1 -Serve`) — 4-card grid, Idle/Run/Jump tabs, sliders re-bake, mobile flip below 768 px.
- No `using LeapOfLegends.*` or product-specific imports.
- Comments answer why, not what; coroutine yields preserved unless a comment explains the replacement.
- If you touched `CaptureRotation` / `Lighting` defaults: regression-check against Kenney AC2 (the demo's anchor).
- `CHANGELOG.md` updated; README updated if the public API or behaviour changed.

## Deeper docs

- Full walkthrough, API, architecture, tuning notes: `README.md`
- Contribution rules and PR checklist: `CONTRIBUTING.md`
- Version history: `CHANGELOG.md`
- Machine-readable index: `llms.txt`
- Sibling tools: [design system](https://github.com/sinanata/unity-ui-toolkit-design-system) · [mesh fracturer](https://github.com/sinanata/unity-mesh-fracture) · [prefab-thumbnail renderer](https://github.com/sinanata/unity-prefab-thumbnail-renderer) · [build orchestrator](https://github.com/sinanata/unity-cross-platform-local-build-orchestrator)
