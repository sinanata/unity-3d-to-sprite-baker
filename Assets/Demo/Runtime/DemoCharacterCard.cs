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
        RuntimeAnimatorController _liveController;
        Animator                  _liveAnimator;
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

            _liveController = Resources.Load<RuntimeAnimatorController>(DemoCharacterCatalog.ControllerPath);
            if (_liveController == null)
                Debug.LogWarning($"[SpriteBakerDemo] Missing AnimatorController at Resources/{DemoCharacterCatalog.ControllerPath} — live mesh will render in bind pose and bake will fall back to bind-pose frames. Run \"Sprite Baker Demo > Rebuild Kenney Controller\" to regenerate it.");

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
            PlayLiveAnimatorRow(row);
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

            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.updateWhenOffscreen = true;

            ApplyCharacterSkin(go, _skinTexture);

            _liveAnimator = AttachAnimatorToRig(go);
            if (_liveAnimator != null)
            {
                if (_liveController != null) _liveAnimator.runtimeAnimatorController = _liveController;
                _liveAnimator.applyRootMotion = false;
                _liveAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            PlayLiveAnimatorRow(_currentRow);
        }

        // Attach the Animator to the bone-armature root, not the prefab
        // root (see type-level XML doc).
        public static Animator AttachAnimatorToRig(GameObject prefabInstance)
        {
            var rigRoot = prefabInstance.transform.Find(RIG_ROOT_BONE_PATH);
            GameObject animatorHost;
            if (rigRoot != null)
            {
                // Strip the FBX importer's auto-added Animator on the
                // prefab root so two animators don't race on the same rig.
                var stray = prefabInstance.GetComponent<Animator>();
                if (stray != null) Object.Destroy(stray);
                animatorHost = rigRoot.gameObject;
            }
            else
            {
                Debug.LogWarning($"[SpriteBakerDemo] Could not find '{RIG_ROOT_BONE_PATH}' under {prefabInstance.name} — attaching Animator to the prefab root. Hierarchy may have been refactored; update RIG_ROOT_BONE_PATH.");
                animatorHost = prefabInstance;
            }

            var animator = animatorHost.GetComponent<Animator>();
            if (animator == null) animator = animatorHost.AddComponent<Animator>();
            return animator;
        }

        void PlayLiveAnimatorRow(int row)
        {
            if (_liveAnimator == null) return;
            string state = StateNameForRow(row);
            _liveAnimator.Play(state, 0, 0f);
            _liveAnimator.Update(0f);
        }

        // Loop fallback for the case where the importer hasn't applied
        // loopTime yet (cached FBX with stale settings). Gated on
        // !info.loop because a properly-looping state's normalizedTime
        // grows past 1.0 by design — restarting in that case would stutter.
        void Update()
        {
            if (_liveAnimator == null) return;
            if (_liveAnimator.runtimeAnimatorController == null) return;

            var info = _liveAnimator.GetCurrentAnimatorStateInfo(0);
            if (!info.loop && info.normalizedTime >= 1f)
                _liveAnimator.Play(StateNameForRow(_currentRow), 0, 0f);
        }

        static string StateNameForRow(int row)
        {
            switch (row)
            {
                case DemoCharacterCatalog.RowIdle: return "idle";
                case DemoCharacterCatalog.RowRun:  return "run";
                case DemoCharacterCatalog.RowJump: return "jump";
                default:                            return "idle";
            }
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

        static void ApplyCharacterSkin(GameObject root, Texture2D skin)
        {
            var mat = MaterialFactory.CharacterBodyMaterial(skin);

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
            var controller = _liveController;

            SpriteAtlasBaker.Instance.Enqueue(new SpriteBakeRequest
            {
                Key = _bakeKey,
                Prefab = prefab,
                AnimatorController = controller,
                FramePixelSize = size,
                FrameRate = rate,
                CaptureYawCount = yaws,
                Lighting = CaptureLighting.Default,
                Rows = new[]
                {
                    // Jump's static landed-pose tail is trimmed in the
                    // importer so this loop wraps cleanly.
                    new SpriteAnimRow { Row = DemoCharacterCatalog.RowIdle, ClipName = "idle", Loop = true },
                    new SpriteAnimRow { Row = DemoCharacterCatalog.RowRun,  ClipName = "run",  Loop = true },
                    new SpriteAnimRow { Row = DemoCharacterCatalog.RowJump, ClipName = "jump", Loop = true },
                },
                PreCaptureCallback = inst =>
                {
                    inst.transform.localScale = Vector3.one * DemoCharacterCatalog.LiveScale;
                    ApplyCharacterSkin(inst, skinTex);
                    // Move Animator before the baker's SetupAnimator runs.
                    AttachAnimatorToRig(inst);
                },
            });
        }
    }
}
