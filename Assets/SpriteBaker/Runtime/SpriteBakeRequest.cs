using System;
using UnityEngine;

namespace SpriteBaker
{
    /// <summary>
    /// One full atlas-bake job. Drives <see cref="SpriteAtlasBaker"/>:
    /// instantiate the prefab, apply the optional <see cref="PreCaptureCallback"/>
    /// (skin / attachment hooks), step through every row's clip + frames,
    /// produce a packed atlas + animation-info table.
    /// </summary>
    public struct SpriteBakeRequest
    {
        /// <summary>Caller-defined identity. Used as the cache key by <see cref="SpriteAtlasCache"/>.</summary>
        public int Key;

        /// <summary>The 3D prefab to capture. The baker instantiates it, applies skinning / animation, captures, then destroys the instance.</summary>
        public GameObject Prefab;

        /// <summary>
        /// Animator controller whose clips back the rows. Optional — leave null
        /// when using the loose-clip path via <see cref="Clips"/>. When set,
        /// the baker overrides whatever Animator is on the prefab and resolves
        /// each <see cref="SpriteAnimRow.ClipName"/> against the controller's
        /// animationClips list.
        /// </summary>
        public RuntimeAnimatorController AnimatorController;

        /// <summary>
        /// Optional <see cref="UnityEngine.Avatar"/> override. Required for
        /// humanoid avatars whose imported avatar isn't on the prefab itself.
        /// </summary>
        public Avatar AvatarOverride;

        /// <summary>
        /// Optional loose-clip array. Use this path when your project has
        /// imported AnimationClips but no AnimatorController — the common
        /// case for FBXes from Kenney / Mixamo / asset-store packs that
        /// ship raw clips. The baker drives the SkinnedMeshRenderer via a
        /// PlayableGraph + AnimationClipPlayable per frame, so no
        /// controller asset is needed.
        ///
        /// When both <see cref="Clips"/> and <see cref="AnimatorController"/>
        /// are set, the controller path wins — set whichever fits your
        /// project's animation pipeline.
        /// </summary>
        public AnimationClip[] Clips;

        /// <summary>
        /// World-space rotation applied to the captured prefab before the
        /// orthographic camera frames it. Default <see cref="Quaternion.identity"/>
        /// captures the prefab in its authored orientation. For models authored
        /// facing -Z (Kenney's default), set this to <c>Quaternion.Euler(0, 180, 0)</c>
        /// so the captured atlas's first column is the character's front;
        /// the runtime renderer then flips UVs for left-facing.
        /// </summary>
        public Quaternion CaptureRotation;

        /// <summary>Pixel size of one frame (square). 64–128 reads as crisp sprite-pixel art at 1080p; 192–256 looks like rendered HUD art.</summary>
        public int FramePixelSize;

        /// <summary>Frame rate of the captured animation in frames per second. 12 = "old-school cel anim" feel; 24 = "smooth PS1 sprite". Determines how many frames are captured per clip.</summary>
        public int FrameRate;

        /// <summary>
        /// Number of evenly-spaced yaw angles to capture around the model.
        /// 0 or 1 = single-angle atlas. Higher values (4, 8, 16) produce a
        /// multi-angle atlas so the runtime renderer can pick the baked
        /// direction matching the live camera's view of the character.
        /// Yaw 0° = camera on +Z looking -Z (prefab's "front" if authored
        /// facing +Z); yaws rotate clockwise around world Y. Atlas memory
        /// scales linearly with yaw count.
        /// </summary>
        public int CaptureYawCount;

        /// <summary>Animation rows to capture. The output atlas has one row per entry, each containing the captured frames for that clip.</summary>
        public SpriteAnimRow[] Rows;

        /// <summary>Background color of the atlas. Default <see cref="Color.clear"/> for transparent sprites.</summary>
        public Color BackgroundColor;

        /// <summary>
        /// Lighting setup applied to the offscreen capture stage. Y=2000 is
        /// far from any scene light, so a URP/Lit character without explicit
        /// rig lighting renders dark grey or solid black. The baker spawns
        /// this rig at the capture origin and tears it down with the camera.
        /// Default values produce a neutral 3-quarter front-key + soft-fill
        /// look that flatters most stylised characters.
        /// </summary>
        public CaptureLighting Lighting;

        /// <summary>
        /// Runs immediately after the prefab is instantiated, before any
        /// frame capture. Use to:
        /// <list type="bullet">
        /// <item>Apply skin/material variants.</item>
        /// <item>Attach hats, weapons, accessories to bones.</item>
        /// <item>Toggle child renderers.</item>
        /// </list>
        /// Runs BEFORE bounds calculation so attachments are framed in.
        /// </summary>
        public Action<GameObject> PreCaptureCallback;

        /// <summary>
        /// Runs each frame after the animator/sampler writes the pose and
        /// before the camera renders. Use to clamp transforms the Animator
        /// overwrites (e.g. an axis-correction localRotation a clip resets).
        /// Most callers leave this null.
        /// </summary>
        public Action<GameObject> PerFrameCallback;
    }

    /// <summary>
    /// Per-bake lighting rig: one directional key, one directional fill,
    /// one ambient term. Set <see cref="DisableDefaultRig"/> and spawn
    /// your own lights inside
    /// <see cref="SpriteBakeRequest.PreCaptureCallback"/> for custom looks.
    /// </summary>
    [Serializable]
    public struct CaptureLighting
    {
        public float   KeyIntensity;
        public Vector3 KeyEuler;
        public Color   KeyColor;
        public float   FillIntensity;
        public Vector3 FillEuler;
        public Color   FillColor;
        /// <summary>Multiplier on URP global ambient during capture.</summary>
        public float AmbientIntensity;
        /// <summary>Skip the default rig and rely entirely on lights staged in <see cref="SpriteBakeRequest.PreCaptureCallback"/>.</summary>
        public bool DisableDefaultRig;

        /// <summary>3-quarter front-key + cool fill + neutral ambient.</summary>
        public static CaptureLighting Default => new CaptureLighting
        {
            KeyIntensity     = 1.4f,
            KeyEuler         = new Vector3(50f, -30f, 0f),
            KeyColor         = new Color(1f, 0.96f, 0.88f),
            FillIntensity    = 0.4f,
            FillEuler        = new Vector3(-25f, 150f, 0f),
            FillColor        = new Color(0.7f, 0.78f, 0.95f),
            AmbientIntensity = 0.6f,
            DisableDefaultRig = false,
        };
    }
}
