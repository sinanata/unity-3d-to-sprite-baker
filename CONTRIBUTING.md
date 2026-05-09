# Contributing

Thanks for considering a contribution. This project is small (~1.1k lines C# + ~60 lines HLSL) and intentionally so — every line earns its keep against the demo, against the live game, or against a Unity quirk worth documenting. Read the existing files before adding new ones; half of the comments are documentation.

## Ground rules

1. **One feature, one PR.** A "compute-shader composition" should not also "rewrite the bake loop". Small PRs land faster.
2. **Comments answer "why", not "what".** A reader can see `yield return null`. The comment should explain *why we yield here* — "let Unity finish re-skinning the mesh between frame captures, otherwise every frame shows the same pose".
3. **Two demos, both must keep working.** The simple `Assets/SpriteBaker/Demo/SpriteBakerDemo.cs` (drop-on-GameObject, inspector-driven) is the minimum viable example for the runtime. The WebGL demo at `Assets/Demo/` (4-card live-vs-sprite split, UI Toolkit overlay, mobile-aware UI) is the integration test. PRs that change the API must update both; PRs that break either get bounced.
4. **No `using LeapOfLegends.*`** — this project is decoupled from the game by design. New runtime code lives in the `SpriteBaker` namespace only. Demo-only code lives under `SpriteBakerDemo.*`.
5. **Each yield earns its keep.** Coroutine `yield return null` calls in the bake loop are load-bearing for animator re-skinning. Removing them looks like a perf optimization but breaks the bake. If you remove a yield, document why in a comment.

## Where to start

| Goal | File |
| --- | --- |
| Tweak the bake loop / animator stepping / GPU readback pipeline | `Assets/SpriteBaker/Runtime/SpriteAtlasBaker.cs` |
| Change cache lifecycle, evict on quality change | `Assets/SpriteBaker/Runtime/SpriteAtlasCache.cs` |
| Adjust runtime playback (UV update, facing flip, frame timing) | `Assets/SpriteBaker/Runtime/AnimatedSpriteRenderer.cs` |
| Add a new request field (e.g. exposure, post-FX volume) | `Assets/SpriteBaker/Runtime/SpriteBakeRequest.cs` + plumbing in `SpriteAtlasBaker` |
| Edit the atlas shader | `Assets/SpriteBaker/Resources/Shaders/AtlasCutout.shader` |
| Update the simple "drop-on-GameObject" demo | `Assets/SpriteBaker/Demo/SpriteBakerDemo.cs` |
| Update the WebGL preview demo | `Assets/Demo/Runtime/` (`DemoBootstrap`, `DemoCharacterCatalog`, `DemoCharacterCard`, `DemoUI`, `MaterialFactory`) |
| Adjust the Kenney importer / controller builder | `Assets/Demo/Editor/DemoModelImporter.cs`, `Assets/Demo/Editor/KenneyControllerBuilder.cs` |
| Tune the WebGL build + deploy pipeline | `Tools/Build/Build-Demo.ps1`, `Tools/Build/Deploy-GhPages.ps1` |

## Adding a new feature

1. **Sketch the API.** Write the call site you wish you had. If it's awkward, the design is wrong — iterate before implementing.
2. **Implement.** Match the existing style: small fields, comment the *why*, no defensive null-checks in inner loops.
3. **Update both demos.** New flag → expose in the simple demo's inspector AND in the WebGL demo's UI panel where appropriate. New API → call it from at least one. Both demos should always render the full pipeline you intend users to wire.
4. **Verify the WebGL build.** `Tools\Build\Build-Demo.ps1 -Serve` builds and serves on `:3000` — catches WebGL2-specific regressions (texture-default binding, shadow-keyword fallout, format compatibility) that the editor lets pass.
5. **Update the README.** Architecture section, what-makes-this-robust section, when-not-to-use section — pick whichever fits your change.
6. **Update CHANGELOG.md** under the unreleased section.

## Pull request checklist

- [ ] Simple demo still works end-to-end (`Assets/SpriteBaker/Demo/SpriteBakerDemo.cs`: assign prefab + clips, hit Play, sprite cycles animations on 1/2/3/4 keypress).
- [ ] WebGL demo still builds + works (`Tools\Build\Build-Demo.ps1 -Serve`) — 4-card grid renders, Idle/Run/Jump tabs flip every card, sliders re-bake, mobile flip below 768 px.
- [ ] No new `using LeapOfLegends.*` or other product-specific imports.
- [ ] Comments answer *why*, not *what*.
- [ ] Coroutine yields preserved unless you've added a comment explaining why your version is correct without one.
- [ ] If you touched `CaptureRotation` / `Lighting` defaults: regression-checked against Kenney AC2 (the demo's anchor). New defaults must keep the atlas readable on a fresh-cloned project.
- [ ] CHANGELOG.md updated under Unreleased.
- [ ] README updated if the public API or behaviour changed.

## Reporting bugs

If atlases look wrong:

1. **Reproduce in one of the demos** if possible — preferably the WebGL preview at [`https://sinanata.github.io/unity-3d-to-sprite-baker/`](https://sinanata.github.io/unity-3d-to-sprite-baker/), since most platform-specific bugs show up there. If the bug only appears in your project, attach a minimal repro: prefab + scripts.
2. **Save the atlas.** Add a `File.WriteAllBytes("atlas.png", atlas.EncodeToPNG())` in `SpriteAtlasBaker` and attach the PNG. That separates "render is wrong" (atlas itself bad) from "playback UVs are wrong" (atlas correct, runtime renderer wrong).
3. **Include Unity version + URP version + platform.** Both move quickly; `6000.0.x` patch releases sometimes break shader compilation. Mobile (Adreno / Mali) sometimes silently drops shader features the desktop validates. WebGL2 has its own long-tail set of cross-compiler quirks.
4. **Screenshots for visual bugs.** Side-by-side "expected vs actual" beats words. The 4-card demo is purpose-built for this — show the live mesh next to the baked sprite playing the same row.

## Licence

By contributing you agree your contributions are released under the project's MIT licence. See [LICENSE](LICENSE).
