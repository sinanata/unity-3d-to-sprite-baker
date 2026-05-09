using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SpriteBaker.Demo
{
    /// <summary>
    /// Minimum viable example: drop on a GameObject, assign a character
    /// prefab + an AnimatorController OR a set of loose AnimationClips.
    /// On Play, bakes a sprite atlas and spawns an
    /// <see cref="AnimatedSpriteRenderer"/> playing the captured rows.
    ///
    /// 1–4 switch row, F flips facing.
    ///
    /// The polished 4-card demo lives at <c>Assets/Demo/</c>; this file is
    /// the copy-paste reference for the runtime.
    /// </summary>
    public class SpriteBakerDemo : MonoBehaviour
    {
        [Header("Source")]
        public GameObject CharacterPrefab;

        [Tooltip("If set, the baker uses this AnimatorController to resolve clip names. " +
                 "Leave null and assign clips below for the loose-clip path.")]
        public RuntimeAnimatorController AnimatorController;

        [Tooltip("Optional Avatar override — required for humanoid avatars whose imported " +
                 "avatar isn't on the prefab itself.")]
        public Avatar AvatarOverride;

        [Tooltip("Loose AnimationClips. Used when no AnimatorController is assigned. " +
                 "Names must match the entries in ClipNames below.")]
        public AnimationClip[] LooseClips;

        [Header("Bake settings")]
        public int FramePixelSize = 128;
        public int FrameRate = 12;

        [Tooltip("Composed onto the prefab's authored rotation (axis " +
                 "correction preserved). Identity = natural orientation; " +
                 "set (0, 180, 0) if the bake captures the back instead " +
                 "of the front. The bake camera looks -Z from +Z.")]
        public Vector3 CaptureRotationEuler = Vector3.zero;

        [Header("Rows to capture")]
        public string[] ClipNames = { "Idle", "Run", "Jump", "Fall" };
        public bool[]   LoopFlags = { true,   true,  false,  true   };

        private int bakeKey;
        private GameObject spriteHostGO;
        private AnimatedSpriteRenderer animatedRenderer;
        private bool facing = true;

        private void Start()
        {
            if (CharacterPrefab == null)
            {
                Debug.LogWarning("[SpriteBakerDemo] Assign a CharacterPrefab in the inspector.");
                return;
            }

            int rowCount = Mathf.Min(ClipNames.Length, LoopFlags.Length);
            var rows = new SpriteAnimRow[rowCount];
            for (int i = 0; i < rowCount; i++)
            {
                rows[i] = new SpriteAnimRow
                {
                    Row = i,
                    ClipName = ClipNames[i],
                    Loop = LoopFlags[i],
                };
            }

            // Content-derived key — stable across editor reloads, so
            // re-entering Play mode reuses the cached atlas. Distinct per
            // (prefab × frame size × FPS).
            bakeKey = (CharacterPrefab.name?.GetHashCode() ?? 0)
                       ^ (FramePixelSize * 31)
                       ^ (FrameRate * 17);

            SpriteAtlasBaker.Instance.Enqueue(new SpriteBakeRequest
            {
                Key = bakeKey,
                Prefab = CharacterPrefab,
                AnimatorController = AnimatorController,
                AvatarOverride = AvatarOverride,
                Clips = LooseClips,
                FramePixelSize = FramePixelSize,
                FrameRate = FrameRate,
                CaptureRotation = Quaternion.Euler(CaptureRotationEuler),
                Lighting = CaptureLighting.Default,
                Rows = rows,
            });

            // AnimatedSpriteRenderer hides its mesh until the atlas lands.
            spriteHostGO = new GameObject("SpritePlayback");
            spriteHostGO.transform.position = transform.position;
            animatedRenderer = spriteHostGO.AddComponent<AnimatedSpriteRenderer>();
            animatedRenderer.Bind(bakeKey);
        }

        private void Update()
        {
            if (animatedRenderer == null) return;

#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return; // touch-only mobile

            if (kb.digit1Key.wasPressedThisFrame) animatedRenderer.SetRow(0);
            if (kb.digit2Key.wasPressedThisFrame) animatedRenderer.SetRow(1);
            if (kb.digit3Key.wasPressedThisFrame) animatedRenderer.SetRow(2);
            if (kb.digit4Key.wasPressedThisFrame) animatedRenderer.SetRow(3);
            if (kb.fKey.wasPressedThisFrame)
            {
                facing = !facing;
                animatedRenderer.SetFacing(facing);
            }
#else
            if (Input.GetKeyDown(KeyCode.Alpha1)) animatedRenderer.SetRow(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) animatedRenderer.SetRow(1);
            if (Input.GetKeyDown(KeyCode.Alpha3)) animatedRenderer.SetRow(2);
            if (Input.GetKeyDown(KeyCode.Alpha4)) animatedRenderer.SetRow(3);
            if (Input.GetKeyDown(KeyCode.F))
            {
                facing = !facing;
                animatedRenderer.SetFacing(facing);
            }
#endif
        }
    }
}
