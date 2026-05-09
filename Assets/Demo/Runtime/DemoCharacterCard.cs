using SpriteBaker;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpriteBakerDemo
{
    /// <summary>
    /// One demo card — left half: live 3D mesh; right half:
    /// <see cref="AnimatedSpriteRenderer"/> playing the baked atlas.
    ///
    /// Kenney AC2 quirk: the prefab splits into <c>characterMedium</c>
    /// (mesh) and <c>Root</c> (bone armature) as siblings, and clip
    /// bindings are authored relative to the bone-armature root. The
    /// Animator MUST sit on the <c>Root</c> child:
    /// <list type="bullet">
    /// <item>On the prefab root, body-bone paths like <c>HipsCtrl/...</c>
    ///       don't resolve (bones don't animate) AND the clip's
    ///       <c>"Root"</c> path overwrites Root.localRotation — wiping
    ///       the FBX importer's Z-up→Y-up axis correction → character
    ///       tips onto its back.</item>
    /// <item>On the <c>Root</c> child, paths resolve and Root's authored
    ///       axis correction is preserved.</item>
    /// </list>
    /// </summary>
    public class DemoCharacterCard : MonoBehaviour
    {
        public DemoCharacterCatalog.Definition Definition { get; private set; }
        public bool IsBakeReady => SpriteAtlasCache.IsReady(_bakeKey);

        DemoBootstrap _bootstrap;
        AnimatedSpriteRenderer    _spriteRenderer;
        Transform                 _liveTransform;
        Collider                  _spriteCollider;
        Texture2D                 _skinTexture;

        // Clips loaded directly via Resources.LoadAll, NOT via the
        // AnimatorController. The controller's serialized clip references
        // are unreliable on WebGL builds: prior incident logs show
        // animator.runtimeAnimatorController.animationClips returning
        // entries that load as null even though the controller asset
        // itself loads fine. Direct clip loading from FBX sub-assets
        // (placed in a Resources folder, so Unity's build pipeline keeps
        // them) sidesteps the issue. Both the live mesh AND the bake
        // share the same clip references; controllers are no longer in
        // the picture.
        AnimationClip _idleClip;
        AnimationClip _runClip;
        AnimationClip _jumpClip;

        // Manual SampleAnimation playback for the live mesh — the rig
        // child whose transform path is what every clip's curves are
        // bound relative to (Kenney AC2's `Root`).
        Transform     _liveSampleTarget;
        float         _liveAnimTime;

        int                       _bakeKey;
        int                       _bakedFrameSize;
        int                       _bakedFrameRate;
        int                       _bakedYawCount;
        int                       _currentRow = DemoCharacterCatalog.RowIdle;

        public int BakeKey => _bakeKey;
        public Collider SpriteClickCollider => _spriteCollider;

        // Live on left (-HALF_OFFSET), sprite on right (+HALF_OFFSET).
        const float HALF_OFFSET       = 0.95f;
        const float PEDESTAL_WIDTH    = 1.5f;
        const float PEDESTAL_DEPTH    = 1.4f;
        const float PEDESTAL_HEIGHT   = 0.4f;

        // Bone-armature root inside the Kenney AC2 prefab.
        const string RIG_ROOT_BONE_PATH = "Root";

        public void Initialize(DemoCharacterCatalog.Definition def, DemoBootstrap bootstrap)
        {
            Definition = def;
            _bootstrap = bootstrap;
            _skinTexture = Resources.Load<Texture2D>(def.SkinResourcePath);
            if (_skinTexture == null)
                Debug.LogWarning($"[SpriteBakerDemo] Missing skin texture at Resources/{def.SkinResourcePath} — character body will render with the default white texture.");

            // Load AnimationClips directly from FBX sub-assets. Bypasses
            // the AnimatorController, whose own clip references are
            // unreliable on WebGL builds (load as null even when the
            // controller itself loads).
            _idleClip = DemoCharacterCatalog.LoadAnimationByKeyword(DemoCharacterCatalog.AnimIdlePath, "idle");
            _runClip  = DemoCharacterCatalog.LoadAnimationByKeyword(DemoCharacterCatalog.AnimRunPath,  "run");
            _jumpClip = DemoCharacterCatalog.LoadAnimationByKeyword(DemoCharacterCatalog.AnimJumpPath, "jump");
            if (_idleClip == null || _runClip == null || _jumpClip == null)
                Debug.LogWarning($"[SpriteBakerDemo] Missing one or more AnimationClips (idle={_idleClip != null}, run={_runClip != null}, jump={_jumpClip != null}) — characters will stay at bind pose.");

            BuildPedestal();
            BuildLiveCharacter();
            BuildSpritePlayback();
            BakeIfNeeded();
        }

        public void OnQualitySettingsChanged()
        {
            int newSize = _bootstrap.FramePixelSize;
            int newRate = _bootstrap.FrameRate;
            int newYaw  = _bootstrap.YawCount;
            if (newSize == _bakedFrameSize && newRate == _bakedFrameRate && newYaw == _bakedYawCount) return;

            SpriteAtlasCache.Evict(_bakeKey);
            BakeIfNeeded();
        }

        public void SetRow(int row)
        {
            _currentRow = row;
            _liveAnimTime = 0f;
            if (_spriteRenderer != null) _spriteRenderer.SetRow(row);
        }

        // Two yaws: atlas yaw (live → camera, picks baked direction) and
        // billboard yaw (sprite → camera, rotates the quad). Using the LIVE
        // position for atlas yaw means each card picks its own baked
        // direction, mirroring sparsely-placed gameplay sprites.
        public void UpdateFromCamera(Vector3 cameraPos)
        {
            if (_spriteRenderer == null) return;

            Transform liveAnchor = _liveTransform != null ? _liveTransform : transform;
            Vector3 fromLiveToCam = cameraPos - liveAnchor.position;
            float liveYawDeg = Mathf.Atan2(fromLiveToCam.x, fromLiveToCam.z) * Mathf.Rad2Deg;
            _spriteRenderer.SetYaw(liveYawDeg);

            Vector3 fromSpriteToCam = cameraPos - _spriteRenderer.transform.position;
            float billboardYawDeg = Mathf.Atan2(fromSpriteToCam.x, fromSpriteToCam.z) * Mathf.Rad2Deg;
            _spriteRenderer.SetBillboardYaw(billboardYawDeg);
        }

        // =================================================================
        // Build helpers
        // =================================================================

        void BuildPedestal()
        {
            BuildOnePedestal($"Pedestal_Live_{Definition.DisplayName}",   -HALF_OFFSET);
            BuildOnePedestal($"Pedestal_Sprite_{Definition.DisplayName}", +HALF_OFFSET);
        }

        void BuildOnePedestal(string name, float xOffset)
        {
            var ped = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ped.name = name;
            ped.transform.SetParent(transform, false);
            ped.transform.localPosition = new Vector3(xOffset, -PEDESTAL_HEIGHT * 0.5f, 0f);
            ped.transform.localScale    = new Vector3(PEDESTAL_WIDTH, PEDESTAL_HEIGHT, PEDESTAL_DEPTH);

            var mr = ped.GetComponent<MeshRenderer>();
            mr.sharedMaterial = MaterialFactory.PedestalMaterial();
            mr.shadowCastingMode = ShadowCastingMode.On;

            var col = ped.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        void BuildLiveCharacter()
        {
            var prefab = Resources.Load<GameObject>(DemoCharacterCatalog.CharacterPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[SpriteBakerDemo] Missing character prefab at Resources/{DemoCharacterCatalog.CharacterPrefabPath} — falling back to a capsule.");
                var fallback = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                fallback.name = "LiveCharacter (fallback)";
                fallback.transform.SetParent(transform, false);
                fallback.transform.localPosition = new Vector3(-HALF_OFFSET, 0f, 0f);
                Destroy(fallback.GetComponent<Collider>());
                return;
            }

            var go = Instantiate(prefab);
            go.name = $"Live_{Definition.DisplayName}";
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(-HALF_OFFSET, 0f, 0f);
            go.transform.localScale    = Vector3.one * DemoCharacterCatalog.LiveScale;
            _liveTransform = go.transform;

            // Strip AudioListeners from the prefab copy. Kenney AC2 ships
            // one on the rig root; with 4 cards plus the main camera we'd
            // end up with 5 listeners, which Web Audio rejects on WebGL2
            // (the editor only warns).
            foreach (var al in go.GetComponentsInChildren<AudioListener>(true))
                Destroy(al);

            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.updateWhenOffscreen = true;

            ApplyCharacterSkin(go, _skinTexture);

            // Resolve the SampleAnimation target. Kenney AC2 clips are
            // authored relative to the `Root` child; sampling against the
            // prefab root silently no-ops every curve binding.
            var rigRoot = go.transform.Find(RIG_ROOT_BONE_PATH);
            _liveSampleTarget = rigRoot != null ? rigRoot : go.transform;
            if (rigRoot == null)
                Debug.LogWarning($"[SpriteBakerDemo] Could not find '{RIG_ROOT_BONE_PATH}' under {go.name} — sampling against prefab root. Curves bound relative to that path will not resolve and the live mesh will stay at bind pose.");

            // Strip Animators from EVERY descendant EXCEPT the
            // SampleAnimation target. Generic/Humanoid clips can't be
            // sampled outside the editor without an Animator on the
            // target GameObject (Unity warns and refuses the sample);
            // a controller-less Animator is inert and just satisfies
            // the API requirement. Stripping the others (FBX-importer
            // auto-adds one on the prefab root) keeps any latent state
            // machine logic from racing our manual sample.
            foreach (var a in go.GetComponentsInChildren<Animator>(true))
            {
                if (a.gameObject == _liveSampleTarget.gameObject)
                {
                    a.runtimeAnimatorController = null;
                    a.applyRootMotion = false;
                    a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }
                else
                {
                    Destroy(a);
                }
            }
            if (_liveSampleTarget.gameObject.GetComponent<Animator>() == null)
            {
                var a = _liveSampleTarget.gameObject.AddComponent<Animator>();
                a.runtimeAnimatorController = null;
                a.applyRootMotion = false;
                a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            // Land the first frame so the live mesh isn't bind-pose for a
            // single Update tick before our coroutine drives time forward.
            ApplyLiveSamplePose(0f);
        }

        AnimationClip CurrentClip()
        {
            switch (_currentRow)
            {
                case DemoCharacterCatalog.RowRun:  return _runClip;
                case DemoCharacterCatalog.RowJump: return _jumpClip;
                default:                            return _idleClip;
            }
        }

        void ApplyLiveSamplePose(float t)
        {
            if (_liveSampleTarget == null) return;
            var clip = CurrentClip();
            if (clip == null) return;
            clip.SampleAnimation(_liveSampleTarget.gameObject, t);
        }

        void Update()
        {
            if (_liveSampleTarget == null) return;
            var clip = CurrentClip();
            if (clip == null) return;

            _liveAnimTime += Time.deltaTime;
            float len = clip.length > 0f ? clip.length : 1f;
            if (clip.isLooping || (clip.wrapMode == WrapMode.Loop || clip.wrapMode == WrapMode.Default))
                _liveAnimTime %= len;
            else if (_liveAnimTime > len)
                _liveAnimTime = len;

            ApplyLiveSamplePose(_liveAnimTime);
        }

        void BuildSpritePlayback()
        {
            var go = new GameObject($"Sprite_{Definition.DisplayName}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(HALF_OFFSET, 0f, 0f);

            _spriteRenderer = go.AddComponent<AnimatedSpriteRenderer>();
            _spriteRenderer.Bind(_bakeKey);
            _spriteRenderer.SetRow(_currentRow);

            // Humanoid-silhouette hitbox for the "view atlas" modal click.
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.9f, 0f);
            col.size   = new Vector3(1.2f, 1.8f, 0.2f);
            col.isTrigger = true;
            _spriteCollider = col;
        }

        // forBake=true uses an Unlit material so the offscreen capture
        // stage produces a clean texture-only atlas (predictable on
        // WebGL2 where URP/Lit on an offscreen camera can render solid
        // black — light contribution drops to zero through stripped
        // lighting passes). The live mesh keeps the lit material so the
        // 3D-shaded vs sprite-flat contrast still reads on screen.
        static void ApplyCharacterSkin(GameObject root, Texture2D skin, bool forBake = false)
        {
            var mat = forBake
                ? MaterialFactory.CharacterBodyMaterialUnlit(skin)
                : MaterialFactory.CharacterBodyMaterial(skin);

            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = new Material[smr.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                smr.sharedMaterials = mats;
            }
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = new Material[mr.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                mr.sharedMaterials = mats;
            }
        }

        // =================================================================
        // Bake plumbing
        // =================================================================

        void BakeIfNeeded()
        {
            int size = _bootstrap.FramePixelSize;
            int rate = _bootstrap.FrameRate;
            int yaws = _bootstrap.YawCount;
            _bakedFrameSize = size;
            _bakedFrameRate = rate;
            _bakedYawCount  = yaws;
            _bakeKey = DemoCharacterCatalog.BuildBakeKey(Definition, size, rate, yaws);

            if (_spriteRenderer != null)
            {
                _spriteRenderer.Bind(_bakeKey);
                _spriteRenderer.SetRow(_currentRow);
            }

            if (SpriteAtlasCache.IsReady(_bakeKey)) return;

            var prefab = Resources.Load<GameObject>(DemoCharacterCatalog.CharacterPrefabPath);
            if (prefab == null) return;

            var skinTex = _skinTexture;

            // Pass loose clips, NOT a controller. On WebGL builds, the
            // controller's animationClips array can deserialize with null
            // entries, so the bake's clip-name resolution silently produces
            // a clip-less sampler and every captured frame is bind pose.
            // Direct AnimationClip references via Resources.LoadAll bypass
            // that — the FBX sub-assets are kept by the build pipeline
            // because we hold strong references to them.
            var clips = new System.Collections.Generic.List<AnimationClip>(3);
            if (_idleClip != null) clips.Add(_idleClip);
            if (_runClip  != null) clips.Add(_runClip);
            if (_jumpClip != null) clips.Add(_jumpClip);

            string idleName = _idleClip != null ? _idleClip.name : "idle";
            string runName  = _runClip  != null ? _runClip.name  : "run";
            string jumpName = _jumpClip != null ? _jumpClip.name : "jump";

            SpriteAtlasBaker.Instance.Enqueue(new SpriteBakeRequest
            {
                Key = _bakeKey,
                Prefab = prefab,
                AnimatorController = null,
                Clips = clips.ToArray(),
                SampleAnimationTargetPath = RIG_ROOT_BONE_PATH,
                FramePixelSize = size,
                FrameRate = rate,
                CaptureYawCount = yaws,
                Lighting = CaptureLighting.Default,
                Rows = new[]
                {
                    // Jump's static landed-pose tail is trimmed in the
                    // importer so this loop wraps cleanly.
                    new SpriteAnimRow { Row = DemoCharacterCatalog.RowIdle, ClipName = idleName, Loop = true },
                    new SpriteAnimRow { Row = DemoCharacterCatalog.RowRun,  ClipName = runName,  Loop = true },
                    new SpriteAnimRow { Row = DemoCharacterCatalog.RowJump, ClipName = jumpName, Loop = true },
                },
                PreCaptureCallback = inst =>
                {
                    inst.transform.localScale = Vector3.one * DemoCharacterCatalog.LiveScale;
                    ApplyCharacterSkin(inst, skinTex, forBake: true);
                },
            });
        }
    }
}
