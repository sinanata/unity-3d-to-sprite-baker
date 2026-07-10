# GitHub Copilot instructions

This repository follows the cross-tool `AGENTS.md` standard. Read [`/AGENTS.md`](../AGENTS.md) for the full guide: the `SpriteBaker` API, the enqueue → cache → bind flow, the material rules, build and preview commands, and the pull-request checklist.

The rules that matter most:

- **Baking is runtime + async (coroutine), one frame per Unity frame — not editor-only, not synchronous.** Enqueue with `SpriteAtlasBaker.Instance.Enqueue`, then read with `SpriteAtlasCache.TryGet` / `IsReady`, or `AnimatedSpriteRenderer.Bind(key)` early (it waits for the bake).
- **The playback material is URP/Unlit from `Resources/SpriteBakerAtlasUnlitTemplate.mat`, NOT the `AtlasCutout.shader`.** Don't swap in `new Material(Shader.Find("SpriteBaker/AtlasCutout"))` — a custom URP shader compiled at runtime renders black on WebGL2. Copy the whole `Assets/SpriteBaker/` folder (including `Editor/` and the `.mat`).
- **Render with URP's `SingleCameraRequest`, not `Camera.Render()`** (a no-op on WebGL2 URP), and always stage the capture light rig — a URP/Lit character at the (2000, 2000, 0) capture origin renders black without it.
- **`CaptureRotation` composes onto the authored rotation** (Kenney AC2 = `Euler(0,180,0)`); set `SampleAnimationTargetPath` for child-relative rigs.
- **Keep the per-frame `yield return null` in the bake loop** — without it every captured frame shows the same pose.
- **No `using LeapOfLegends.*`.** Runtime code lives in the `SpriteBaker` namespace; demo code under `SpriteBaker.Demo`.
