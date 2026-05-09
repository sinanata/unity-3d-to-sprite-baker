# Unity 3D-to-Sprite Baker

Drop-in **runtime sprite-atlas baker for Unity 6 / URP**. Take any 3D animated character and bake every clip × frame into a single packed atlas at game start — orthographic capture, async GPU readback, multi-row animation, blend-shape variants, all behind a 3-line API. Then play the atlas back as a flat textured quad that shares one material across every instance. Open-sourced as part of a small giving-back set of Unity tools — alongside the [UI Toolkit design system](https://github.com/sinanata/unity-ui-document-design-system), the [mesh-fracture pipeline](https://github.com/sinanata/unity-mesh-fracture), and the [cross-platform build orchestrator](https://github.com/sinanata/unity-cross-platform-local-build-orchestrator).

<blockquote>
<a href="https://store.steampowered.com/app/2269500/"><img src="docs/leap-of-legends-icon.png" align="left" width="70" height="70" alt="Leap of Legends"></a>
Built for and battle-tested in <strong><a href="https://leapoflegends.com">Leap of Legends</a></strong> — a cross-platform multiplayer game in active development on Steam, Google Play (internal testing), TestFlight, and macOS. The mobile build's lowest quality preset uses this pipeline to bake every animal-character into a sprite atlas at start, dropping the per-character GPU cost from a 60-bone skinned mesh to a flat textured quad. <a href="https://store.steampowered.com/app/2269500/">Wishlist on Steam</a> — public mobile store pages coming soon.
</blockquote>

---

```
SpriteAtlasBaker.Instance.Enqueue(new SpriteBakeRequest {
    Key = id,
    Prefab = characterPrefab,
    Clips = new[] { idle, run, jump },          // raw FBX clips — no controller required
    FramePixelSize = 128,
    FrameRate = 12,
    CaptureRotation = Quaternion.Euler(0, 180, 0),
    Lighting = CaptureLighting.Default,
    Rows = new[] {
        new SpriteAnimRow { Row = 0, ClipName = "idle", Loop = true  },
        new SpriteAnimRow { Row = 1, ClipName = "run",  Loop = true  },
        new SpriteAnimRow { Row = 2, ClipName = "jump", SingleFrame = true },
    },
});

// Anywhere in your gameplay code:
host.AddComponent<AnimatedSpriteRenderer>().Bind(id);
```

## Demo

**[▶ Live WebGL preview](https://sinanata.github.io/unity-3d-to-sprite-baker/)** — four side-by-side cards, each splitting **live 3D character** (left) vs **baked sprite playback** (right). Click Idle / Run / Jump in the top bar to flip every card's animation simultaneously; tweak Frame Size / Frame Rate sliders to re-bake at different qualities and watch the atlas update.

[![Unity 3D-to-Sprite Baker — interactive WebGL preview. Four side-by-side cards split live 3D character (left) vs baked sprite playback (right); click Idle / Run / Jump to flip animations and drag the Frame Size / Frame Rate sliders to re-bake at different qualities.](docs/screenshots/sprite_baker_showcase.gif)](https://sinanata.github.io/unity-3d-to-sprite-baker/)

The repo is a complete Unity project — clone, open in Unity 6, press Play. The demo scene auto-spawns:

- 4 [Kenney animated-characters-2](https://kenney.nl/assets/animated-characters-2) skin variants on the same rig (skater male / skater female / criminal / cyborg). Each card stages the live SkinnedMeshRenderer + the baked sprite playback at the same world position so the silhouette parity is visible at a glance.
- The **loose-clip bake path** (`SpriteBakeRequest.Clips`) feeds the three Kenney FBX animations (`idle.fbx`, `run.fbx`, `jump.fbx`) directly into the baker via a `PlayableGraph` + `AnimationClipPlayable` — no AnimatorController authoring required. The same path also runs an AnimatorController for the live mesh on the left half of each card, demonstrating both APIs work side by side.
- A UI Toolkit overlay (frame-size slider, frame-rate slider, Idle/Run/Jump tabs, status toast, hotkey legend) authored against the [Unity UI Toolkit Design System](https://github.com/sinanata/unity-ui-document-design-system) — same dark token palette, same `.ds-btn` / `.ds-slider` / `.ds-toast` components used in [Leap of Legends](https://leapoflegends.com).

### Cloning this demo project

The demo's UI consumes the design system as a git submodule (vendored at `Vendor/unity-ui-document-design-system`) and links the drop-in folder into `Assets/DesignSystem` via a per-clone OS link. Pure-runtime consumers of the baker (the recipe in [Installation](#installation) below) don't need the design system — only this repo's demo scene does.

```bash
git clone --recurse-submodules https://github.com/sinanata/unity-3d-to-sprite-baker
cd unity-3d-to-sprite-baker
```

Then create the link from `Assets/DesignSystem` to the vendored copy:

```powershell
# Windows — directory junction (no admin / Developer Mode required)
cmd /c mklink /J Assets\DesignSystem Vendor\unity-ui-document-design-system\Assets\DesignSystem
```

```bash
# macOS / Linux — symbolic link
ln -s ../Vendor/unity-ui-document-design-system/Assets/DesignSystem Assets/DesignSystem
```

The junction / symlink itself is gitignored; each contributor re-runs the command after their first clone. Open in Unity 6000.3.8f1 (or compatible) and press Play in `Assets/Demo/Scenes/SpriteBakerDemo.unity`.

If you forgot `--recurse-submodules`, run `git submodule update --init` after the fact, then create the link.

For a minimum-viable example to wire the baker into your own scene, read `Assets/SpriteBaker/Demo/SpriteBakerDemo.cs` — ~110 lines, shows the full pipeline (queue at `Start`, look up at `Bind`).

### Build the WebGL preview locally

The build flow lives in a shared cross-platform orchestrator vendored as a submodule at `Tools/.orchestrator/` — clone with submodules so `Build-Demo.ps1`'s shim resolves:

```powershell
git clone --recurse-submodules https://github.com/sinanata/unity-3d-to-sprite-baker
# or, after a plain clone:
git submodule update --init --recursive

copy Tools\Build\config.example.json Tools\Build\config.local.json
# Edit unity.windowsEditorPath if Unity isn't in C:\Program Files\Unity\Hub\Editor\6000.3.8f1\
.\Tools\Build\Build-Demo.ps1 -Serve     # build + serve at http://localhost:3000
.\Tools\Build\Build-Demo.ps1 -Deploy    # build + force-push to gh-pages
```

`Build-Demo.ps1` is a thin shim — the heavy lifting (lockfile cleanup, Burst-AOT auto-retry, live progress, deploy worktree) lives in [`unity-cross-platform-local-build-orchestrator`](https://github.com/sinanata/unity-cross-platform-local-build-orchestrator). See `Tools/Build/README.md` for daily usage and the orchestrator's README for the full reference.

## Why this exists

3D characters in Unity bring real costs: skinning compute, bone-matrix uploads, multi-pass lighting on every fragment, shadow casts. On mobile, even modest characters can eat 10–15% of frame time per visible instance. The traditional fix is "ship 2D sprites instead", but then you give up your 3D pipeline — every animation update means re-exporting sprites from Maya / Blender, every art revision is a manual re-bake.

This pipeline keeps your 3D pipeline AND gives you sprites. At game start (or quality-setting change), the baker captures every requested clip × frame into a single atlas. Runtime swaps the SkinnedMeshRenderer for a flat quad reading UVs out of the atlas — same world position, same animations, ~5% the GPU cost.

What you get on day one:

- **Render once, play many.** One bake per (character, skin, hat) combo. Hundreds of identical sprites on screen share one Texture2D + one material — SRP Batcher kicks in trivially.
- **Multi-row atlases.** Idle / Run / Jump / Swim / Fall — pack as many rows as you have animation states. Caller defines the row indices; the runtime renderer indexes into them by name + frame.
- **Two animation pipelines.** Use an `AnimatorController` if your project already has one; or pass loose `AnimationClip`s directly via `Clips[]` and the baker drives a `PlayableGraph` + `AnimationClipPlayable` per row — no controller required. Best for FBXes from Kenney / Mixamo / asset-store packs that ship raw clips.
- **Blend-shape expression variants.** Want "Idle with eyes closed" as a separate row? Add a `BlendShapes` override on that row and the baker sets the weight before capturing. Same animation clip, different facial state — without authoring a second clip.
- **Async GPU readback.** Captures happen at one frame per Unity frame. No `Texture2D.ReadPixels` stalls. The baker pipelines render → GPU readback → CPU copy across three frames so the bake's per-frame stall is dominated by the animator's bone-matrix update, not by GPU sync.
- **Origin-aligned quads.** The output quad's bottom edge sits at the model's pivot point, NOT the bottom of the model's bounds. So a 3D character standing at world (0, 0, 0) and a sprite character standing at the same position have identical foot positions — you can swap renderers at runtime (LOD by distance / quality) without footprints jumping.
- **Capture-stage lighting.** A configurable directional key + fill rig gets spawned at the Y=2000 capture origin and torn down with the camera — without it, URP/Lit characters render solid black on the offscreen stage. Defaults produce a neutral 3-quarter front-key + soft-fill look that flatters most stylised characters.

## Requirements

| Requirement | Notes |
| --- | --- |
| **Unity 6** (6000.x or newer) | Tested on 6000.0 and 6000.3. Should work on 2022.3 LTS — `AsyncGPUReadback` is older. |
| **URP** (Universal Render Pipeline) | The bundled `AtlasCutout.shader` targets URP. Built-in / HDRP work; you'll just need to author your own atlas shader (the C# is pipeline-agnostic). |
| A prefab with a `SkinnedMeshRenderer` | Or a `MeshRenderer` for static models. Multi-renderer setups (body + head + hat) are bounds-encapsulated automatically. |
| Either an `AnimatorController` OR an `AnimationClip[]` | Or neither for a bind-pose-only single-frame atlas. |

No NuGet, no asmdef requirements, no external native libraries.

## Installation

The repo is a complete Unity project (the demo scene above lives here), but the runtime is **one folder** you drop into `Assets/`:

```
your-unity-project/
└── Assets/
    └── SpriteBaker/                      ← drop the whole folder
        ├── Runtime/
        │   ├── SpriteAnimRow.cs
        │   ├── SpriteBakeRequest.cs
        │   ├── BakedSpriteAtlas.cs
        │   ├── SpriteAtlasCache.cs
        │   ├── SpriteAtlasBaker.cs
        │   └── AnimatedSpriteRenderer.cs
        ├── Resources/Shaders/
        │   └── AtlasCutout.shader
        └── Demo/
            └── SpriteBakerDemo.cs        ← single-script "drop on a GameObject" example
```

`Assets/Demo/` is the WebGL preview scene with the 4-card 3D-vs-sprite split — leave it behind when you copy `Assets/SpriteBaker/` into your own project.

**Option A — copy files:**

```powershell
git clone https://github.com/sinanata/unity-3d-to-sprite-baker ../sprite-baker-src
cp -r ../sprite-baker-src/Assets/SpriteBaker Assets/SpriteBaker
```

**Option B — git submodule:**

```bash
cd your-unity-project
git submodule add https://github.com/sinanata/unity-3d-to-sprite-baker Assets/SpriteBaker-src
# Symlink or copy Assets/SpriteBaker-src/Assets/SpriteBaker → Assets/SpriteBaker
```

Unity reimports automatically — the AtlasCutout shader compiles on first import and the rest is pure C# with zero `.mat` GUIDs to migrate.

## Quick start

```csharp
using SpriteBaker;
using UnityEngine;

public class CharacterSpawner : MonoBehaviour
{
    public GameObject characterPrefab;
    public AnimationClip clipIdle, clipRun, clipJump;  // loose clips — most common case
    public Material skinMaterialOverride;              // applied via the PreCaptureCallback

    private const int Rows_Idle = 0;
    private const int Rows_Run  = 1;
    private const int Rows_Jump = 2;

    private int bakeKey;

    private void Start()
    {
        // Content-derived key. Stable across editor reloads, distinct
        // per (skin × resolution × FPS) combo so the cache doesn't hand
        // out a stale atlas after a quality-setting change.
        bakeKey = (characterPrefab.name?.GetHashCode() ?? 0)
                  ^ (skinMaterialOverride.name?.GetHashCode() ?? 0)
                  ^ (128 * 31) ^ (12 * 17);

        SpriteAtlasBaker.Instance.Enqueue(new SpriteBakeRequest
        {
            Key = bakeKey,
            Prefab = characterPrefab,
            Clips = new[] { clipIdle, clipRun, clipJump },
            FramePixelSize = 128,
            FrameRate = 12,
            CaptureRotation = Quaternion.Euler(0, 180, 0),  // Kenney AC2 faces -Z
            Lighting = CaptureLighting.Default,
            Rows = new[]
            {
                new SpriteAnimRow { Row = Rows_Idle, ClipName = "idle", Loop = true  },
                new SpriteAnimRow { Row = Rows_Run,  ClipName = "run",  Loop = true  },
                new SpriteAnimRow { Row = Rows_Jump, ClipName = "jump", SingleFrame = true },
            },
            PreCaptureCallback = inst =>
            {
                // Apply the per-instance skin material before bounds calc.
                var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
                if (smr != null) smr.material = skinMaterialOverride;
            },
        });
    }

    public void SpawnSprite(Vector3 pos)
    {
        var go = new GameObject("Sprite");
        go.transform.position = pos;
        var renderer = go.AddComponent<AnimatedSpriteRenderer>();
        renderer.Bind(bakeKey);
        renderer.SetRow(Rows_Idle);

        // Drive from gameplay code:
        //   When velocity > 0:  renderer.SetRow(Rows_Run);
        //   On jump:            renderer.SetRow(Rows_Jump);
        //   Facing change:      renderer.SetFacing(velocity.x > 0);
    }
}
```

That's the entire gameplay-side wiring.

## API

```csharp
public class SpriteAtlasBaker : MonoBehaviour
{
    public static SpriteAtlasBaker Instance { get; }      // lazily-created singleton
    public void Enqueue(SpriteBakeRequest req);           // queue a bake
    public bool IsPending(int key);
}

public struct SpriteBakeRequest
{
    public int Key;                                       // your identity
    public GameObject Prefab;
    public RuntimeAnimatorController AnimatorController;  // path A: controller-based
    public AnimationClip[] Clips;                         // path B: loose clips → PlayableGraph
    public Avatar AvatarOverride;
    public int FramePixelSize;                            // 64 / 128 / 192 / 256 are common
    public int FrameRate;                                 // 12 = retro, 24 = smooth
    public Quaternion CaptureRotation;                    // identity, or 180 around Y for -Z-facing rigs
    public CaptureLighting Lighting;                      // default = key + fill + ambient
    public SpriteAnimRow[] Rows;                          // what to capture
    public Color BackgroundColor;                         // default = transparent
    public Action<GameObject> PreCaptureCallback;         // skin / pose / attach hook
}

public struct SpriteAnimRow
{
    public int Row;                                       // index into the output atlas
    public string ClipName;                               // resolved by NormalizedClipName comparison
    public bool Loop;                                     // metadata for runtime playback
    public bool SingleFrame;                              // true = capture only frame 0
    public BlendShapeOverride[] BlendShapes;              // per-row blend-shape weights
}

public struct CaptureLighting
{
    public float KeyIntensity, FillIntensity, AmbientIntensity;
    public Vector3 KeyEuler, FillEuler;
    public Color   KeyColor, FillColor;
    public bool    DisableDefaultRig;                     // skip rig entirely; stage your own in PreCaptureCallback
    public static CaptureLighting Default { get; }
}

public static class SpriteAtlasCache
{
    public static bool TryGet(int key, out BakedSpriteAtlas data);
    public static bool IsReady(int key);
    public static void Evict(int key);
    public static void Clear();
}

public class AnimatedSpriteRenderer : MonoBehaviour
{
    public void Bind(int atlasKey);                       // attach to a baked atlas
    public void SetRow(int row);                          // switch animation
    public void SetFacing(bool right);                    // flip via UVs (no transform mirror)
}
```

## Architecture

```
SpriteAtlasBaker.cs               ← coroutine-driven worker; one capture per frame
SpriteAtlasCache.cs               ← static dictionary keyed by your int
BakedSpriteAtlas.cs               ← result struct (texture + material + per-row metadata)
SpriteBakeRequest.cs              ← input struct
SpriteAnimRow.cs                  ← per-row spec (clip name, loop, blend shapes)
AnimatedSpriteRenderer.cs         ← runtime playback — meshfilter + meshrenderer + UV ticker
AtlasCutout.shader                ← URP alpha-cutout shader for the atlas material
```

The bake pipeline:

1. **Instantiate** the prefab at a far origin (default 2000, 2000, 0).
2. **Apply** caller's `PreCaptureCallback` — skin material, attachments, blend shapes.
3. **Choose driver:** AnimatorController (path A) or PlayableGraph + AnimationClipPlayable (path B). Bind the chosen driver to the SMR's bone hierarchy.
4. **Compute bounds** from the SkinnedMeshRenderer's posed pose (NOT bind-pose).
5. **Spawn capture lighting** — directional key + fill + ambient boost, all destroyed at teardown. The Y=2000 stage has no scene lights, so URP/Lit characters render solid black without this.
6. **Set up an orthographic camera** sized to the larger of (model width, height-above-origin) plus 15% padding. Vertical fit measures from the model origin, NOT bounds.min.y, so the playback quad's bottom matches the model's foot position — critical for swapping between 3D and sprite rendering.
7. **For each row:**
   - Apply blend-shape weights.
   - Step the animator (or graph) through `frameCount = clip.length × frameRate` normalised times.
   - For each frame, `cam.Render()` and `AsyncGPUReadback.Request`.
   - Drain completed readbacks into the atlas's pixel buffer.
8. **Finalise** the atlas, build the shared material, store in `SpriteAtlasCache`, restore the previous ambient/light/RT state.

The whole pipeline yields back to Unity between every frame capture so the animator finishes re-skinning before the camera renders. Without that yield, every captured frame would show the same pose — `animator.Play(clip, time)` doesn't re-skin within a single Unity frame.

## What makes this robust

- **Origin-aligned quads, not bounds-aligned.** The playback quad's bottom edge sits at the model's pivot, so a 3D-rendered character at (0, 0, 0) and a sprite-rendered character at (0, 0, 0) overlay perfectly. This is non-obvious — most "render to texture" pipelines crop to bounds, which makes feet float when you swap to a sprite. The 4-card demo's left/right halves prove the parity at a glance.
- **Two animation pipelines.** AnimatorController (Animator.Play with state-name lookup) AND PlayableGraph + AnimationClipPlayable (per-row playable, manual time stepping). Same `FrameSampler` interface drives the bake loop; pick whichever fits your project. Loose-clip is the more portable demonstration — works for FBXes from Kenney / Mixamo / any asset pack that ships raw clips with no controller.
- **Async GPU readback pipelined over 3 frames.** Without it, each captured frame's `ReadPixels` stalls 7–30 ms on iOS Metal. With it, the per-frame stall is dominated by the animator update.
- **Animator step yields per frame.** `Play(clip, time) + Update(0)` doesn't re-skin within a single Unity frame. Every captured frame requires one `yield return null` between them or every captured frame shows the same pose. This bug is silent — atlases look "almost right" with maybe one frame's animation captured.
- **Configurable capture rotation.** Kenney AC2 ships facing -Z; Mixamo characters face +Z; asset-store packs vary. The `CaptureRotation` field on the request lets per-prefab orientation be specified instead of being baked into the baker code. The pre-2026 hard-coded `(0, 90, 0)` happened to work for one orientation and silently captured the back / side of any other.
- **Capture-stage lighting rig.** Y=2000 is far from any scene light; without an explicit rig URP/Lit characters render solid black on the offscreen stage. The default rig (key + fill + ambient boost) is spawned and torn down per bake; callers can disable it via `Lighting.DisableDefaultRig` and stage their own lights in `PreCaptureCallback`.
- **Format selection via `SystemInfo.GetCompatibleFormat`.** RGBA32 maps to formats some mobile drivers reject for ReadPixels; this picks a format that works.
- **Atlas width clamp.** Maxes out at 2048 pixels — texture size limits on low-end mobile GPUs. Frame counts get clamped if a clip would push past it.
- **`Texture2D.Apply(false, true)`.** Marks the atlas non-readable after upload; saves ~256 KB per 256² atlas at scale.
- **Blend-shape captures share the source clip.** "Idle with eyes closed" doesn't need a second animation file — set `BlendShapes` on the row, baker handles the rest.
- **Bind-state reset on re-bake.** `AnimatedSpriteRenderer.Bind(newKey)` resets `hasAtlas` so a quality-change re-bake doesn't leave the renderer pointing at the previous (now-destroyed) atlas — sampling garbage memory.
- **Pre-bind row request preserved.** `SetRow(r)` before the atlas binds is remembered, so gameplay code that calls `Bind()` then immediately `SetRow(Run)` doesn't briefly show row 0 (Idle) before snapping to Run.

The whole runtime is ~1.1k lines of C# (`SpriteAtlasBaker` + `FragmentCache` siblings + `AnimatedSpriteRenderer`) + ~60 lines of HLSL — small enough to read in one sitting. Half of the comments are documentation of the trade-offs.

## Tuning notes

| Knob | Default | When to change |
| --- | --- | --- |
| `FramePixelSize` | 128 | 64 = "pixel art crisp at 1080p"; 192 = "rendered HUD art"; 256 = "AAA quality on Steam Deck". Atlas memory cost = `cols × rows × size² × 4 bytes`. |
| `FrameRate` | 12 | 12 = old-school cel anim; 24 = smooth PS1 sprite; 60 = full smoothness, 5× the atlas cost. |
| `CaptureRotation` | identity | Kenney AC2 needs `(0, 180, 0)`; Mixamo `(0, 0, 0)`. Set per-rig — captured atlas's first column should be the **front** of the character. |
| `Lighting` | `Default` | Tweak key/fill colour + intensity for stylised palettes. Set `DisableDefaultRig` and supply your own lights via `PreCaptureCallback` for non-trivial scenes. |
| `BackgroundColor` | clear | Set to a solid colour for baked card backgrounds — saves a layer in your UI. |
| Atlas filter mode | Point | Hard-coded for crisp pixel-perfect playback. Edit `SpriteAtlasBaker.cs` if you want bilinear. |

## When NOT to use this

- **Per-bone procedural animation.** The atlas captures a clip; if your character's hat tracks the cursor or the sprite's eyes follow another object, the baked frames don't move. Sprite playback is "play recorded animation forward", not "rerun the animator with new bone poses".
- **Lots of cosmetic combinations.** Each (character, skin, hat) combo is a separate atlas. With 10 characters × 8 skins × 5 hats = 400 atlases at 128² each that's ~800 MB. The pipeline scales to tens of combos cleanly; hundreds need a different approach (instanced sprite ID arrays, sprite atlas atlases).
- **Heavy GPU effects baked in.** The capture camera disables URP post-FX explicitly. If you want bloom / outline / colour grading in the atlas, build a Volume around the capture origin or pre-rig your post-FX inside `PreCaptureCallback`.
- **Complex shader networks.** `AtlasCutout` is alpha-cutout only — no recolouring, palette swaps, or 2-tone effects. Fork the shader and the SpriteBaker code only reads `_MainTex` / `_Cutoff`.

## Contributing

Issues and PRs welcome. The pipeline is small enough to read in one sitting (~1.1k lines C#).

Areas where help is especially useful:

- **Editor tool** that runs `BakeOne` once and writes the atlas + metadata as a `.asset` for source-control. Eliminates the runtime bake when your character roster is fixed.
- **Compute-shader composition** — bake into a single bigger atlas across N characters, indexed via a SpriteSheet ID array, for a single draw call across all sprite instances on screen.
- **Built-in pipeline shader.** The C# is pipeline-agnostic, but the bundled `AtlasCutout.shader` is URP only.
- **Unit tests** for `SpriteAtlasBaker` — feed a mock controller / clip set + simple cube prefab and verify the atlas dimensions / per-row frame counts come out right.

See [CONTRIBUTING.md](CONTRIBUTING.md) for the PR checklist.

## Credits & support

Made for **[Leap of Legends](https://leapoflegends.com)** — a cross-platform physics-heavy multiplayer game in active development, targeting Steam, iOS, Android, and Mac. If this saved you time:

- ⭐ Star the repo
- 🎮 [Wishlist Leap of Legends on Steam](https://store.steampowered.com/app/2269500/) — mobile store pages coming soon
- 🐦 Shout out [@sinanata](https://x.com/sinanata)

## Licence

MIT — see [LICENSE](LICENSE). Free for commercial use. No warranty.

The 3D models / animations / skin textures in `Assets/Demo/Resources/Models/` are by [Kenney](https://kenney.nl) and licensed under [CC0](https://creativecommons.org/publicdomain/zero/1.0/) — see `Assets/Demo/Resources/Models/CREDITS.txt` for the per-asset attribution. The demo scene is independent of the baker; if you only want the runtime, copy `Assets/SpriteBaker/` and ignore `Assets/Demo/`.

---

**[Leap of Legends](https://leapoflegends.com)** · physics · multiplayer · cross-platform · in development · the mobile build's lowest quality preset uses this pipeline to bake every animal-character into a sprite at start.
