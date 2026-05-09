using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
#if UNITY_RENDER_PIPELINE_UNIVERSAL
using UnityEngine.Rendering.Universal;
#endif

namespace SpriteBaker
{
    /// <summary>
    /// Pre-renders 3D animated characters into 2D sprite atlases at game
    /// start, so the runtime can swap a skinned mesh for a flat quad
    /// reading captured frames out of a Texture2D. Useful for mobile LOD,
    /// retro / pixel-art aesthetics, and cutting GPU cost on dense scenes.
    ///
    /// Pattern: <see cref="Enqueue"/> a bake (one frame captured per
    /// Unity frame), then <see cref="SpriteAtlasCache.TryGet"/> at
    /// gameplay time; while the bake is in-flight, keep showing the 3D
    /// model. Two animation paths:
    /// <list type="bullet">
    /// <item><b>AnimatorController</b> — the baker drives an Animator
    ///       with named states. Best if your project already has authored
    ///       controllers.</item>
    /// <item><b>Loose clips</b> — the baker samples
    ///       <see cref="AnimationClip"/>s directly. Best for FBXes from
    ///       Kenney / Mixamo / asset packs that ship raw clips.</item>
    /// </list>
    ///
    /// See <c>Assets/SpriteBaker/Demo/SpriteBakerDemo.cs</c> for a
    /// copy-paste example.
    /// </summary>
    public class SpriteAtlasBaker : MonoBehaviour
    {
        private static SpriteAtlasBaker s_instance;

        /// <summary>Lazily-created baker. Call from anywhere; the GameObject is parented under DontDestroyOnLoad and hidden.</summary>
        public static SpriteAtlasBaker Instance
        {
            get
            {
                if (s_instance != null) return s_instance;
                var go = new GameObject("[SpriteAtlasBaker]");
                DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                s_instance = go.AddComponent<SpriteAtlasBaker>();
                return s_instance;
            }
        }

        private readonly Queue<SpriteBakeRequest> queue = new();
        private readonly HashSet<int> pending = new();
        private bool processing;

        /// <summary>
        /// Queue a bake. Subsequent calls with the same <see cref="SpriteBakeRequest.Key"/>
        /// while one is pending or cached are dropped — to force a re-bake,
        /// call <see cref="SpriteAtlasCache.Evict"/> first.
        /// </summary>
        public void Enqueue(SpriteBakeRequest req)
        {
            if (SpriteAtlasCache.IsReady(req.Key) || pending.Contains(req.Key))
                return;
            pending.Add(req.Key);
            queue.Enqueue(req);
            if (!processing) StartCoroutine(ProcessQueue());
        }

        /// <summary>True if the bake for this key is queued or in-flight.</summary>
        public bool IsPending(int key) => pending.Contains(key);

        private IEnumerator ProcessQueue()
        {
            processing = true;
            while (queue.Count > 0)
            {
                var req = queue.Dequeue();
                yield return StartCoroutine(BakeOne(req));
                pending.Remove(req.Key);
            }
            processing = false;
        }

        private IEnumerator BakeOne(SpriteBakeRequest req)
        {
            int px = Mathf.Max(8, req.FramePixelSize);
            float frameDuration = 1f / Mathf.Max(1, req.FrameRate);

            // Far from any live scene geometry / probes.
            Vector3 capturePos = new Vector3(2000f, 2000f, 0f);

            var model = Instantiate(req.Prefab);
            model.name = "[SpriteCaptureModel]";

            // COMPOSE — don't replace. The prefab's authored localRotation
            // carries the FBX importer's axis correction (Blender Z-up→Y-up
            // is stored as -90° X when bakeAxisConversion=0); replacing it
            // tips the character onto its back. Caller passes identity to
            // mean "natural orientation".
            Quaternion rot = req.CaptureRotation;
            if (rot.x == 0f && rot.y == 0f && rot.z == 0f && rot.w == 0f)
                rot = Quaternion.identity;
            model.transform.position = capturePos;
            model.transform.rotation = rot * model.transform.rotation;
            model.SetActive(true);

            // Runs before bounds calc so attachments / skin are framed in.
            req.PreCaptureCallback?.Invoke(model);

            // Three drivers, all expose the same FrameSampler interface:
            //   - AnimatorController → animator.Play(state, time)
            //   - Loose clips        → AnimationClip.SampleAnimation(model, t)
            //                          (Unity 6 PlayableGraph.Evaluate(0) on
            //                          Manual time-update sometimes no-ops →
            //                          atlas captured at bind pose)
            //   - Bind-pose fallback → no animation, one frame per row
            FrameSampler sampler;
            Animator     animator = null;

            if (req.AnimatorController != null)
            {
                animator = SetupAnimator(model, req.AnimatorController, req.AvatarOverride);
                yield return null; // let Unity finish the first pose evaluation
                sampler = SamplerFromAnimator(animator, req.Rows);
            }
            else if (req.Clips != null && req.Clips.Length > 0)
            {
                // Strip the auto-added Animator's controller so it doesn't
                // fight SampleAnimation between bake frames.
                animator = model.GetComponent<Animator>();
                if (animator != null)
                {
                    animator.runtimeAnimatorController = null;
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }
                sampler = SamplerFromClips(model, req.Clips, req.Rows);
                yield return null;
            }
            else
            {
                animator = model.GetComponentInChildren<Animator>();
                sampler = SamplerForBindPose(req.Rows);
                yield return null;
            }

            Bounds bounds = ComputeAnimatedBounds(model);
            float modelWidth = Mathf.Max(bounds.size.x, bounds.size.z, 0.1f);

            // Measure height from the model's origin (feet / contact
            // point), not bounds.min.y, so the playback quad's bottom
            // matches the model origin and footprints don't jump when
            // swapping 3D ↔ sprite at the same world position.
            float originY = model.transform.position.y;
            float heightAboveOrigin = Mathf.Max(bounds.max.y - originY, 0.1f);

            float maxDim = Mathf.Max(modelWidth, heightAboveOrigin);
            float orthoSize = maxDim * 0.575f; // 15% padding
            float quadWidth = 2f * orthoSize;
            float quadHeight = 2f * orthoSize;

            // Y=2000 is far from any scene light → URP/Lit characters render
            // as black silhouette without an explicit rig.
            var lighting = NormalizeLighting(req.Lighting);
            GameObject lightRigGO = null;
            float originalAmbientIntensity = RenderSettings.ambientIntensity;
            Color   originalAmbientColor   = RenderSettings.ambientLight;
            AmbientMode originalAmbientMode = RenderSettings.ambientMode;
            if (!lighting.DisableDefaultRig)
            {
                lightRigGO = BuildLightRig(capturePos, lighting);
                RenderSettings.ambientMode      = AmbientMode.Flat;
                RenderSettings.ambientLight     = new Color(0.5f, 0.5f, 0.5f, 1f);
                RenderSettings.ambientIntensity = lighting.AmbientIntensity;
            }

            var camGO = new GameObject("[SpriteCaptureCamera]");
            var cam = camGO.AddComponent<Camera>();
            float camY = originY + orthoSize;
            // Camera transform is set per-yaw inside the bake loop (see
            // PositionBakeCamera).
            cam.orthographic = true;
            cam.orthographicSize = orthoSize;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 30f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = req.BackgroundColor.a > 0 ? req.BackgroundColor : new Color(0, 0, 0, 0);
            cam.cullingMask = ~0; // include everything; the Y=2000 stage isolates us
            cam.enabled = false;

#if UNITY_RENDER_PIPELINE_UNIVERSAL
            // Don't run the main camera's bloom / tonemap stack on the
            // offscreen capture pass.
            var urp = cam.GetUniversalAdditionalCameraData();
            if (urp != null) urp.renderPostProcessing = false;
#endif

            // Compatible format avoids ReadPixels rejection on mobile drivers.
            var colorFmt = SystemInfo.GetCompatibleFormat(
                GraphicsFormat.R8G8B8A8_SRGB, GraphicsFormatUsage.ReadPixels);
            var rt = new RenderTexture(px, px, 24, colorFmt) { antiAliasing = 1 };
            cam.targetTexture = rt;

            int yawCount = Mathf.Max(1, req.CaptureYawCount);

            // Compute frame counts per row.
            int rowCount = req.Rows == null ? 0 : req.Rows.Length;
            int maxRowIndex = -1;
            for (int i = 0; i < rowCount; i++)
                if (req.Rows[i].Row > maxRowIndex) maxRowIndex = req.Rows[i].Row;
            int totalRows = maxRowIndex + 1;
            if (totalRows < 1) totalRows = 1;

            int[] frameCounts = new int[totalRows];
            bool[] loops = new bool[totalRows];

            for (int i = 0; i < rowCount; i++)
            {
                var rowSpec = req.Rows[i];
                if (rowSpec.Row < 0) continue;
                loops[rowSpec.Row] = rowSpec.Loop;
                float clipLen = sampler.GetClipLength(i);
                int requested = clipLen <= 0f
                    ? 1
                    : (rowSpec.SingleFrame ? 1 : Mathf.Max(1, Mathf.CeilToInt(clipLen * req.FrameRate)));
                frameCounts[rowSpec.Row] = requested;
            }

            // Compute atlas dimensions, clamping to a sane max width.
            int maxCols = 1;
            for (int i = 0; i < frameCounts.Length; i++)
                maxCols = Mathf.Max(maxCols, frameCounts[i]);
            int maxFramesPerRow = Mathf.Min(maxCols, 2048 / px);
            for (int i = 0; i < frameCounts.Length; i++)
                frameCounts[i] = Mathf.Min(frameCounts[i], maxFramesPerRow);
            maxCols = 1;
            for (int i = 0; i < frameCounts.Length; i++)
                maxCols = Mathf.Max(maxCols, frameCounts[i]);

            // Each state row uses yawCount consecutive texture rows: state r
            // + yaw y → texture row `r * yawCount + y`. Collapses to `r` when
            // yawCount = 1.
            int atlasW = maxCols * px;
            int atlasH = totalRows * yawCount * px;
            var atlas = new Texture2D(atlasW, atlasH, TextureFormat.RGBA32, false);
            atlas.filterMode = FilterMode.Point;
            atlas.wrapMode = TextureWrapMode.Clamp;

            // Pre-clear so unused row/column areas don't pick up RT garbage.
            atlas.SetPixels32(new Color32[atlasW * atlasH]);

            // Async GPU readback — pose+render frame N, collect frame N-2.
            // Cuts the per-frame stall from 7–30 ms (sync ReadPixels) to
            // sub-millisecond. On WebGL2 the catch is that
            // AsyncGPUReadbackRequest.WaitForCompletion deadlocks the
            // single-threaded JS event loop (the GPU fence can't fire while
            // the main thread is blocked sync-waiting on it). The final
            // drain below yields between checks instead of sync-waiting,
            // so WebGL completes the readbacks naturally over the next few
            // frames.
            var pendingReadbacks = new Queue<(int dstX, int dstY, AsyncGPUReadbackRequest req)>();
            var pixelBuffer = new Color32[px * px];

            var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();

            for (int yIdx = 0; yIdx < yawCount; yIdx++)
            {
                float yawDeg = (yawCount > 1) ? (360f / yawCount) * yIdx : 0f;
                PositionBakeCamera(camGO, bounds, camY, yawDeg);

                for (int i = 0; i < rowCount; i++)
                {
                    var rowSpec = req.Rows[i];
                    if (rowSpec.Row < 0) continue;

                    ApplyBlendShapes(smr, rowSpec.BlendShapes, weight: true);

                    sampler.PrepareRow(i);
                    int frames = frameCounts[rowSpec.Row];
                    float clipLen = sampler.GetClipLength(i);

                    for (int f = 0; f < frames; f++)
                    {
                        if (clipLen > 0f)
                        {
                            // Loop rows sample [0, 1) — frame N-1 must NOT
                            // duplicate frame 0, otherwise playback holds
                            // for two frame durations on every cycle (reads
                            // as a "freeze" between loops). One-shots sweep
                            // [0, 1] so the final pose lands on N-1.
                            float normalizedTime;
                            if (rowSpec.Loop && frames > 1)
                                normalizedTime = (float)f / frames;
                            else
                                normalizedTime = frames > 1 ? (float)f / (frames - 1) : 0f;
                            sampler.SampleFrame(i, normalizedTime);
                        }

                        // Yield so Unity propagates bone transforms through
                        // the skinning pipeline (LateUpdate). Without this
                        // every captured frame holds the same pose.
                        yield return null;

                        req.PerFrameCallback?.Invoke(model);

                        cam.Render();

                        int textureRow = rowSpec.Row * yawCount + yIdx;
                        int dstX = f * px;
                        int dstY = textureRow * px;
                        var rb = AsyncGPUReadback.Request(rt, 0);
                        pendingReadbacks.Enqueue((dstX, dstY, rb));
                        DrainReady(pendingReadbacks, atlas, px, pixelBuffer);
                    }

                    ApplyBlendShapes(smr, rowSpec.BlendShapes, weight: false);
                }
            }

            // Final drain: yield each frame until the head of the queue is
            // done, then drain all consecutively-ready ones. WebGL needs the
            // event loop to advance for the GPU fence to fire, so we cannot
            // call WaitForCompletion (deadlock).
            while (pendingReadbacks.Count > 0)
            {
                if (!pendingReadbacks.Peek().req.done)
                {
                    yield return null;
                    continue;
                }
                DrainReady(pendingReadbacks, atlas, px, pixelBuffer);
            }

            atlas.Apply(false, true); // makeNoLongerReadable: drop the CPU copy

            cam.targetTexture = null;
            Destroy(rt);
            Destroy(camGO);
            sampler.Dispose();
            if (lightRigGO != null) Destroy(lightRigGO);
            RenderSettings.ambientIntensity = originalAmbientIntensity;
            RenderSettings.ambientLight     = originalAmbientColor;
            RenderSettings.ambientMode      = originalAmbientMode;
            Destroy(model);

            // Fall back to URP/Unlit when the bundled shader isn't present.
            var shader = Shader.Find("SpriteBaker/AtlasCutout") ?? Shader.Find("Universal Render Pipeline/Unlit");
            var material = new Material(shader);
            material.mainTexture = atlas;
            if (material.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", 0.5f);

            var rowInfos = new AnimRowInfo[totalRows];
            for (int r = 0; r < totalRows; r++)
            {
                rowInfos[r] = new AnimRowInfo
                {
                    FrameCount = frameCounts[r],
                    FrameDuration = frameDuration,
                    Loop = loops[r],
                };
            }

            SpriteAtlasCache.StoreResult(req.Key, new BakedSpriteAtlas
            {
                Atlas = atlas,
                SharedMaterial = material,
                FramePixelSize = px,
                AtlasCols = maxCols,
                QuadWidth = quadWidth,
                QuadHeight = quadHeight,
                Rows = rowInfos,
                YawCount = yawCount,
            });
        }

        // ─── Helpers ─────────────────────────────────────────────────────

        // Yaw 0° = camera on +Z looking -Z; clockwise around world Y.
        // Distance is fixed at 10 (orthographic — only affects near/far).
        private static void PositionBakeCamera(GameObject camGO, Bounds bounds, float camY, float yawDeg)
        {
            float yawRad = yawDeg * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));
            Vector3 pivot = new Vector3(bounds.center.x, camY, bounds.center.z);
            camGO.transform.position = pivot + dir * 10f;
            camGO.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
        }

        private static Animator SetupAnimator(GameObject model, RuntimeAnimatorController controller, Avatar avatar)
        {
            // Search the whole hierarchy: split rigs (e.g. Kenney AC2) need
            // the Animator on the bone-armature child, not the prefab root,
            // for clip-path bindings to resolve. Callers pre-attach via
            // PreCaptureCallback for those rigs.
            var animator = model.GetComponentInChildren<Animator>(true);
            if (animator == null) animator = model.AddComponent<Animator>();
            if (avatar != null) animator.avatar = avatar;
            if (controller != null) animator.runtimeAnimatorController = controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;
            animator.speed = 0f; // we drive time manually via Play(name, 0, t)
            return animator;
        }

        private static Bounds ComputeAnimatedBounds(GameObject go)
        {
            bool initialized = false;
            Bounds b = default;

            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // The bake stage is offscreen — without this the SMR returns
                // stale bounds (last on-screen frame).
                smr.updateWhenOffscreen = true;
                if (!initialized) { b = smr.bounds; initialized = true; }
                else b.Encapsulate(smr.bounds);
            }
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!initialized) { b = mr.bounds; initialized = true; }
                else b.Encapsulate(mr.bounds);
            }
            if (!initialized) return new Bounds(go.transform.position, Vector3.one);
            return b;
        }

        private static Dictionary<string, AnimationClip> BuildClipMap(Animator animator)
        {
            var map = new Dictionary<string, AnimationClip>();
            if (animator?.runtimeAnimatorController == null) return map;
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
                map[NormalizeClipName(clip.name)] = clip;
            return map;
        }

        private static string NormalizeClipName(string s)
            => s == null ? "" : s.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");

        // Match by exact normalized name first, then symmetric substring
        // containment. The latter handles FBX clips prefixed by the source
        // rig's root-bone name ("Root|Idle") and the inverse case.
        private static AnimationClip ResolveByName(
            Dictionary<string, AnimationClip> clipMap, string requestedName)
        {
            if (clipMap == null || string.IsNullOrEmpty(requestedName)) return null;
            var normalized = NormalizeClipName(requestedName);
            if (clipMap.TryGetValue(normalized, out var exact)) return exact;
            foreach (var kv in clipMap)
            {
                if (kv.Key.Contains(normalized) || normalized.Contains(kv.Key))
                    return kv.Value;
            }
            return null;
        }

        private static void ApplyBlendShapes(SkinnedMeshRenderer smr, BlendShapeOverride[] shapes, bool weight)
        {
            if (smr == null || smr.sharedMesh == null || shapes == null) return;
            for (int i = 0; i < shapes.Length; i++)
            {
                int idx = smr.sharedMesh.GetBlendShapeIndex(shapes[i].Name);
                if (idx < 0) continue;
                smr.SetBlendShapeWeight(idx, weight ? shapes[i].Weight : 0f);
            }
        }

        // Drain every readback at the head of the queue that's already
        // done. Doesn't yield, doesn't WaitForCompletion — caller is
        // responsible for yielding between calls if it wants to give the
        // GPU more time to complete in-flight requests. WebGL safety:
        // never invokes WaitForCompletion, so the JS event loop never
        // deadlocks against the GPU fence.
        private static void DrainReady(
            Queue<(int dstX, int dstY, AsyncGPUReadbackRequest req)> q,
            Texture2D atlas, int px, Color32[] pixelBuffer)
        {
            while (q.Count > 0 && q.Peek().req.done)
            {
                var item = q.Dequeue();
                if (item.req.hasError) continue;

                var data = item.req.GetData<Color32>();
                data.CopyTo(pixelBuffer);
                atlas.SetPixels32(item.dstX, item.dstY, px, px, pixelBuffer);
            }
        }

        // ── Lighting rig ─────────────────────────────────────────────────

        private static CaptureLighting NormalizeLighting(CaptureLighting input)
        {
            // Treat default(CaptureLighting) as "use defaults". A 0 key
            // intensity is never useful, so the all-zero check is enough.
            if (!input.DisableDefaultRig &&
                input.KeyIntensity == 0f && input.FillIntensity == 0f
                && input.AmbientIntensity == 0f
                && input.KeyEuler == Vector3.zero && input.FillEuler == Vector3.zero
                && input.KeyColor.a == 0f && input.FillColor.a == 0f)
            {
                return CaptureLighting.Default;
            }
            return input;
        }

        private static GameObject BuildLightRig(Vector3 atPosition, CaptureLighting lighting)
        {
            var rig = new GameObject("[SpriteBakerLightRig]");
            rig.transform.position = atPosition;

            BuildOneLight(rig.transform, "Key",  lighting.KeyEuler,  lighting.KeyColor,  lighting.KeyIntensity);
            BuildOneLight(rig.transform, "Fill", lighting.FillEuler, lighting.FillColor, lighting.FillIntensity);
            return rig;
        }

        private static void BuildOneLight(Transform parent, string name, Vector3 euler, Color color, float intensity)
        {
            var go = new GameObject($"[SpriteBakerLight_{name}]");
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(euler);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color.a > 0f ? new Color(color.r, color.g, color.b, 1f) : Color.white;
            light.intensity = intensity > 0f ? intensity : 1f;
            light.shadows = LightShadows.None;
            light.cullingMask = ~0;
        }

        // ── Frame samplers ───────────────────────────────────────────────
        // Strategy interface that hides whether the SMR is posed via an
        // Animator+Controller, AnimationClip.SampleAnimation, or bind-pose.
        private abstract class FrameSampler : System.IDisposable
        {
            public abstract float GetClipLength(int rowSpecIndex);
            public abstract void  PrepareRow(int rowSpecIndex);
            public abstract void  SampleFrame(int rowSpecIndex, float normalizedTime);
            public virtual  void  Dispose() {}
        }

        private static FrameSampler SamplerFromAnimator(Animator animator, SpriteAnimRow[] rows)
        {
            return new AnimatorSampler(animator, rows);
        }

        private static FrameSampler SamplerFromClips(
            GameObject model, AnimationClip[] clips, SpriteAnimRow[] rows)
        {
            return new SampleAnimationSampler(model, clips, rows);
        }

        private static FrameSampler SamplerForBindPose(SpriteAnimRow[] rows)
        {
            return new BindPoseSampler(rows);
        }

        private sealed class AnimatorSampler : FrameSampler
        {
            private readonly Animator _animator;
            private readonly AnimationClip[] _resolved;
            private readonly string[]        _stateNames;

            public AnimatorSampler(Animator animator, SpriteAnimRow[] rows)
            {
                _animator = animator;
                int count = rows == null ? 0 : rows.Length;
                _resolved = new AnimationClip[count];
                _stateNames = new string[count];
                var clipMap = BuildClipMap(animator);
                for (int i = 0; i < count; i++)
                {
                    var name = rows[i].ClipName;
                    if (string.IsNullOrEmpty(name)) continue;
                    _stateNames[i] = name;
                    _resolved[i] = ResolveByName(clipMap, name);
                }
            }

            public override float GetClipLength(int i) =>
                (i >= 0 && i < _resolved.Length && _resolved[i] != null) ? _resolved[i].length : 0f;

            public override void PrepareRow(int i) {}

            public override void SampleFrame(int i, float normalizedTime)
            {
                if (i < 0 || i >= _resolved.Length) return;
                if (_resolved[i] == null || _animator == null) return;
                // Play by the request's ClipName (= user's authored state
                // name). FBX-embedded clip names like "Armature|Take 001"
                // get sanitized when added to a state machine and won't
                // resolve here.
                _animator.Play(_stateNames[i], 0, normalizedTime);
                _animator.Update(0f);
            }
        }

        private sealed class SampleAnimationSampler : FrameSampler
        {
            // AnimationClip.SampleAnimation walks the clip's curve bindings
            // and writes them directly into the hierarchy — no Animator,
            // no PlayableGraph. Chosen over PlayableGraph.Evaluate(0)
            // because the latter sometimes no-ops in Unity 6 Manual time-
            // update, leaving the captured atlas at bind pose. "Legacy" by
            // docs but reliable for an offline one-shot bake.
            private readonly GameObject _model;
            private readonly AnimationClip[] _resolved;

            public SampleAnimationSampler(
                GameObject model, AnimationClip[] availableClips, SpriteAnimRow[] rows)
            {
                _model = model;

                int rowCount = rows == null ? 0 : rows.Length;
                _resolved = new AnimationClip[rowCount];

                var clipMap = new Dictionary<string, AnimationClip>();
                if (availableClips != null)
                {
                    foreach (var c in availableClips)
                        if (c != null) clipMap[NormalizeClipName(c.name)] = c;
                }

                for (int i = 0; i < rowCount; i++)
                {
                    var name = rows[i].ClipName;
                    if (string.IsNullOrEmpty(name)) continue;
                    _resolved[i] = ResolveByName(clipMap, name);
                }
            }

            public override float GetClipLength(int i) =>
                (i >= 0 && i < _resolved.Length && _resolved[i] != null) ? _resolved[i].length : 0f;

            public override void PrepareRow(int i) {}

            public override void SampleFrame(int i, float normalizedTime)
            {
                if (i < 0 || i >= _resolved.Length) return;
                var clip = _resolved[i];
                if (clip == null || _model == null) return;
                float t = clip.length * normalizedTime;
                clip.SampleAnimation(_model, t);
            }
        }

        private sealed class BindPoseSampler : FrameSampler
        {
            // GetClipLength = 0 forces the bake loop's SingleFrame branch
            // → one column per row, captured at bind pose.
            public BindPoseSampler(SpriteAnimRow[] _) {}
            public override float GetClipLength(int i) => 0f;
            public override void  PrepareRow(int i) {}
            public override void  SampleFrame(int i, float t) {}
        }
    }
}
