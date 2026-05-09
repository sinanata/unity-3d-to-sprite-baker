using System;

namespace SpriteBaker
{
    /// <summary>
    /// One animation row in the baked sprite atlas. Rows correspond to
    /// distinct gameplay states (Idle, Run, Jump, etc.); columns within a
    /// row are the individual frames captured from the clip.
    ///
    /// The runtime <see cref="AnimatedSpriteRenderer"/> indexes into the
    /// atlas by (row, frame) to play back the captured animation. The
    /// row index is whatever you pass in — the baker doesn't care if you
    /// use ints, an enum cast, or string-derived hashes.
    /// </summary>
    [Serializable]
    public struct SpriteAnimRow
    {
        /// <summary>Row index in the output atlas. 0 is the bottom row, N-1 is the top. Caller-defined; pick a stable convention (e.g. <c>(int)PlayerState.Idle</c>).</summary>
        public int Row;

        /// <summary>Animation clip name to find in the model's <see cref="UnityEngine.RuntimeAnimatorController"/>. Matched by lower-cased / spaces-and-underscores-stripped comparison so "Run", "run", and "Run_A" all match a clip named "run_a".</summary>
        public string ClipName;

        /// <summary>True if this animation cycles when played back (Idle / Run / Walk). False for one-shots (Jump / Land / Hit).</summary>
        public bool Loop;

        /// <summary>True for static poses — captures only the first frame of the clip. Use for poses like "JumpPose" where you want one rotated/extended pose rather than animation.</summary>
        public bool SingleFrame;

        /// <summary>
        /// Optional blend shapes to set BEFORE capturing this row. Each
        /// entry's <c>Name</c> is the shape name on the model's
        /// <see cref="UnityEngine.SkinnedMeshRenderer.sharedMesh"/>;
        /// <c>Weight</c> is the blend weight (0–100). Lets you bake
        /// expression variants of the same animation by reusing the same
        /// clip + different blend shapes.
        /// </summary>
        public BlendShapeOverride[] BlendShapes;
    }

    /// <summary>One blend-shape weight applied during a row's capture.</summary>
    [Serializable]
    public struct BlendShapeOverride
    {
        public string Name;
        public float Weight;
    }
}
