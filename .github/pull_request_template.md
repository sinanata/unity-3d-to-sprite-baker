## Summary

<!-- What does this PR add or fix? Link any related issue. -->

## Screenshots / clip

<!-- The baked atlas and/or the sprite playback for visual changes. A short clip is ideal for animation tweaks. -->

## Checklist

- [ ] Simple demo still works end-to-end (`Assets/SpriteBaker/Demo/SpriteBakerDemo.cs`: assign prefab + clips, hit Play, the sprite cycles animations on 1/2/3/4 keypress).
- [ ] WebGL demo still builds + works (`Tools\Build\Build-Demo.ps1 -Serve`) — 4-card grid renders, Idle/Run/Jump tabs flip every card, sliders re-bake, mobile flip below 768 px.
- [ ] No new `using LeapOfLegends.*` or other product-specific imports.
- [ ] Comments answer *why*, not *what*.
- [ ] Coroutine yields preserved unless you've added a comment explaining why your version is correct without one.
- [ ] If you touched `CaptureRotation` / `Lighting` defaults: regression-checked against Kenney AC2 (the demo's anchor). New defaults must keep the atlas readable on a fresh clone.
- [ ] `CHANGELOG.md` updated under Unreleased.
- [ ] README updated if the public API or behaviour changed.
