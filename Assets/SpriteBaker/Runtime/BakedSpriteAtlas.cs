using UnityEngine;

namespace SpriteBaker
{
    /// <summary>
    /// Result of one bake — a packed atlas + per-row animation metadata + a
    /// shared material that <see cref="AnimatedSpriteRenderer"/> can apply to
    /// any number of quad-mesh instances. Treat as immutable after the bake
    /// completes; multiple renderers safely share one atlas.
    /// </summary>
    public struct BakedSpriteAtlas
    {
        /// <summary>Packed sprite atlas. Rows = <see cref="SpriteAnimRow.Row"/>, columns = frames within each clip.</summary>
        public Texture2D Atlas;

        /// <summary>Material with <see cref="Atlas"/> bound. Use this on every sprite renderer that plays this character — sharing the material lets SRP Batcher / GPU Instancing kick in.</summary>
        public Material SharedMaterial;

        /// <summary>Pixel size of one frame (square). Same value the baker was asked to use.</summary>
        public int FramePixelSize;

        /// <summary>Maximum column count across all rows — atlas width = AtlasCols × FramePixelSize.</summary>
        public int AtlasCols;

        /// <summary>World-space width of the playback quad — sized to match the captured camera frustum so 1 atlas pixel = 1 game unit at the configured resolution.</summary>
        public float QuadWidth;

        /// <summary>World-space height of the playback quad.</summary>
        public float QuadHeight;

        /// <summary>Per-row animation metadata. Indexed by the same row indices the request used. Slots for rows that weren't requested have <c>FrameCount = 0</c>.</summary>
        public AnimRowInfo[] Rows;

        /// <summary>
        /// Number of distinct yaw angles captured into <see cref="Atlas"/>.
        /// 1 for single-angle atlases (legacy). For multi-angle atlases the
        /// texture has <c>Rows.Length × YawCount</c> texture rows total —
        /// for state row <c>r</c> and yaw index <c>y</c>, the texture row
        /// is <c>r × YawCount + y</c>. Yaw indices are evenly spaced
        /// starting from 0° (camera on +Z, looking -Z).
        /// </summary>
        public int YawCount;
    }

    /// <summary>Metadata for one animation row in the baked atlas.</summary>
    public struct AnimRowInfo
    {
        /// <summary>Number of frames captured for this row. 0 means the row was never written.</summary>
        public int FrameCount;
        /// <summary>Seconds per frame at playback time = 1 / requested frame rate.</summary>
        public float FrameDuration;
        /// <summary>True if this row should cycle on playback. Mirrors <see cref="SpriteAnimRow.Loop"/>.</summary>
        public bool Loop;
    }
}
