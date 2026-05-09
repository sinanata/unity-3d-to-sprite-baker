using UnityEngine;

namespace SpriteBaker
{
    /// <summary>
    /// Plays back a baked sprite atlas as an animated quad in the scene.
    /// Drop on a GameObject, call <see cref="Bind"/> with the cache key, then
    /// drive animation state from your gameplay code via
    /// <see cref="SetRow"/> + <see cref="SetFacing"/>.
    ///
    /// The renderer creates its own <see cref="MeshFilter"/> + <see cref="MeshRenderer"/>
    /// on the same GameObject, sharing the atlas's material so SRP Batcher
    /// batches multiple sprite instances. World-space units match the
    /// <see cref="BakedSpriteAtlas.QuadWidth"/> / <see cref="BakedSpriteAtlas.QuadHeight"/>
    /// set during the bake — bottom-aligned to the GameObject's pivot, so
    /// you can swap between 3D and sprite rendering at the same world
    /// position without footprints jumping.
    /// </summary>
    public class AnimatedSpriteRenderer : MonoBehaviour
    {
        private BakedSpriteAtlas atlas;
        private bool hasAtlas;
        private int boundKey;

        private int currentRow;
        private int currentFrame;
        private float animTimer;
        private bool facingRight = true;

        // Multi-angle playback. pendingYawDegrees holds the latest SetYaw
        // call until the atlas binds, so a SetYaw before the bake lands
        // isn't lost.
        private int   yawIndex;
        private int   yawCount = 1;
        private float pendingYawDegrees;
        private bool  hasPendingYaw;

        private Mesh quadMesh;
        private MeshRenderer meshRenderer;
        private Vector2[] uvBuffer = new Vector2[4];
        private float frameU, frameV;

        /// <summary>
        /// Set up the quad and try to bind to the atlas. If the atlas isn't
        /// yet baked, the renderer stays hidden and re-checks every frame
        /// until the bake lands.
        /// </summary>
        public void Bind(int atlasKey)
        {
            boundKey = atlasKey;
            EnsureQuadMesh();

            // Force a fresh re-bind so a Bind(newKey) after an Evict
            // doesn't leave the renderer sampling the destroyed atlas.
            hasAtlas = false;

            // Hide until the atlas lands — otherwise a slow bake would
            // briefly show the default magenta error material.
            if (meshRenderer != null) meshRenderer.enabled = false;

            TryBindAtlas();
        }

        /// <summary>
        /// Switch the playback row. <paramref name="row"/> must be one of
        /// the indices used in the original <see cref="SpriteBakeRequest.Rows"/>.
        /// Rows with zero frames are silently ignored. Calls before the
        /// atlas binds are remembered, so <c>Bind() + SetRow(Run)</c>
        /// doesn't briefly snap to row 0 once the bake lands.
        /// </summary>
        public void SetRow(int row)
        {
            // Stored even when !hasAtlas — TryBindAtlas reads it on landing.
            currentRow = row;
            currentFrame = 0;
            animTimer = 0f;

            if (!hasAtlas) return;
            if (row < 0 || row >= atlas.Rows.Length) return;
            if (atlas.Rows[row].FrameCount <= 0) return;
            UpdateUVs();
        }

        /// <summary>True = right (the default capture direction); false = left, achieved by flipping U coordinates.</summary>
        public void SetFacing(bool right) => facingRight = right;

        /// <summary>
        /// Pick the closest baked yaw for playback. <paramref name="degrees"/>
        /// is the world-Y angle from character to camera; 0° = camera on +Z
        /// (character's "front"). Calls before bind are remembered so the
        /// first post-bind frame doesn't default to the front view.
        /// </summary>
        public void SetYaw(float degrees)
        {
            if (!hasAtlas)
            {
                pendingYawDegrees = degrees;
                hasPendingYaw = true;
                return;
            }
            int idx = ComputeYawIndex(degrees);
            if (idx == yawIndex) return;
            yawIndex = idx;
            UpdateUVs();
        }

        /// <summary>
        /// Y-axis billboard rotation. <paramref name="degrees"/> = world-Y
        /// angle from the sprite to the camera (same convention as
        /// <see cref="SetYaw"/>). The quad's textured face is its local -Z,
        /// so the transform rotates by <c>degrees + 180°</c>. Pitch is
        /// ignored to avoid the "card edge" reveal full LookAt produces.
        /// </summary>
        public void SetBillboardYaw(float degrees)
        {
            transform.rotation = Quaternion.Euler(0f, degrees + 180f, 0f);
        }

        private int ComputeYawIndex(float degrees)
        {
            if (yawCount <= 1) return 0;
            float wrapped = Mathf.Repeat(degrees, 360f);
            float bin = 360f / yawCount;
            int idx = Mathf.RoundToInt(wrapped / bin);
            if (idx >= yawCount) idx -= yawCount;
            if (idx < 0) idx += yawCount;
            return idx;
        }

        private void Update()
        {
            if (!hasAtlas)
            {
                TryBindAtlas();
                if (!hasAtlas) return;
            }

            var info = atlas.Rows[currentRow];
            if (info.FrameCount > 1)
            {
                animTimer += Time.deltaTime;
                if (animTimer >= info.FrameDuration)
                {
                    animTimer -= info.FrameDuration;
                    if (info.Loop)
                    {
                        currentFrame = (currentFrame + 1) % info.FrameCount;
                    }
                    else if (currentFrame < info.FrameCount - 1)
                    {
                        // Non-looping rows freeze on the last frame; caller
                        // switches back via SetRow.
                        currentFrame++;
                    }
                }
            }
            else
            {
                currentFrame = 0;
            }

            UpdateUVs();
        }

        private void TryBindAtlas()
        {
            if (!SpriteAtlasCache.TryGet(boundKey, out atlas)) return;

            // Resize from the placeholder unit quad to the baked frame's
            // world dimensions.
            float hw = atlas.QuadWidth * 0.5f;
            float hh = atlas.QuadHeight;
            quadMesh.vertices = new[]
            {
                new Vector3(-hw, 0, 0),
                new Vector3( hw, 0, 0),
                new Vector3( hw, hh, 0),
                new Vector3(-hw, hh, 0),
            };
            quadMesh.RecalculateBounds();

            meshRenderer.sharedMaterial = atlas.SharedMaterial;
            meshRenderer.enabled = true;

            // frameV's denominator is the TEXTURE row count (state rows ×
            // yaws), since multi-angle atlases stack yaws contiguously.
            yawCount = Mathf.Max(1, atlas.YawCount);
            int textureRows = Mathf.Max(1, atlas.Rows.Length) * yawCount;

            frameU = 1f / Mathf.Max(1, atlas.AtlasCols);
            frameV = 1f / textureRows;

            hasAtlas = true;

            if (hasPendingYaw)
            {
                yawIndex = ComputeYawIndex(pendingYawDegrees);
                hasPendingYaw = false;
            }

            UpdateUVs();
        }

        private void EnsureQuadMesh()
        {
            if (quadMesh != null) return;
            quadMesh = new Mesh { name = "SpriteQuad" };
            quadMesh.vertices = new[]
            {
                new Vector3(-0.5f, 0,  0), new Vector3(0.5f, 0,  0),
                new Vector3(0.5f,  1,  0), new Vector3(-0.5f, 1, 0),
            };
            quadMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            quadMesh.normals = new[] { -Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward };
            quadMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            quadMesh.RecalculateBounds();

            var mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = quadMesh;

            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.enabled = false;
        }

        private void UpdateUVs()
        {
            float u0 = currentFrame * frameU;
            float u1 = u0 + frameU;
            int textureRow = currentRow * yawCount + yawIndex;
            float v0 = textureRow * frameV;
            float v1 = v0 + frameV;

            // Mirror via UVs, not transform.scale — keeps shadow projection
            // and scale-reading physics queries stable.
            if (!facingRight)
            {
                float tmp = u0; u0 = u1; u1 = tmp;
            }

            uvBuffer[0] = new Vector2(u0, v0);
            uvBuffer[1] = new Vector2(u1, v0);
            uvBuffer[2] = new Vector2(u1, v1);
            uvBuffer[3] = new Vector2(u0, v1);
            quadMesh.uv = uvBuffer;
        }

        private void OnDestroy()
        {
            if (quadMesh != null) Destroy(quadMesh);
        }
    }
}
