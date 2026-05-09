using System.Collections.Generic;
using UnityEngine;

namespace SpriteBaker
{
    /// <summary>
    /// Lookup + lifecycle for finished atlases. <see cref="SpriteAtlasBaker"/>
    /// stores results here when each bake completes; <see cref="AnimatedSpriteRenderer"/>
    /// (and your own gameplay code) reads from here at runtime.
    ///
    /// Keys are caller-defined ints — typically a hash of (model, skin,
    /// hat, resolution, frameRate) so the same character at different
    /// graphics-quality settings produces distinct cache entries.
    /// </summary>
    public static class SpriteAtlasCache
    {
        private static readonly Dictionary<int, BakedSpriteAtlas> s_cache = new();

        /// <summary>True if a finished bake exists for this key.</summary>
        public static bool TryGet(int key, out BakedSpriteAtlas data)
            => s_cache.TryGetValue(key, out data);

        /// <summary>True if any bake is cached for this key.</summary>
        public static bool IsReady(int key) => s_cache.ContainsKey(key);

        internal static void StoreResult(int key, BakedSpriteAtlas data)
        {
            // Free any prior atlas for this key (re-bake on quality change).
            if (s_cache.TryGetValue(key, out var existing))
            {
                if (existing.Atlas != null) Object.Destroy(existing.Atlas);
                if (existing.SharedMaterial != null) Object.Destroy(existing.SharedMaterial);
            }
            s_cache[key] = data;
        }

        /// <summary>Drop all cached atlases. Use on quality-setting changes or scene unload — destroyed textures stay valid for any renderer still holding references until the GC actually frees them.</summary>
        public static void Clear()
        {
            foreach (var entry in s_cache.Values)
            {
                if (entry.Atlas != null) Object.Destroy(entry.Atlas);
                if (entry.SharedMaterial != null) Object.Destroy(entry.SharedMaterial);
            }
            s_cache.Clear();
        }

        /// <summary>Drop a single cached atlas. Use when the underlying prefab/skin changes.</summary>
        public static void Evict(int key)
        {
            if (s_cache.TryGetValue(key, out var entry))
            {
                if (entry.Atlas != null) Object.Destroy(entry.Atlas);
                if (entry.SharedMaterial != null) Object.Destroy(entry.SharedMaterial);
                s_cache.Remove(key);
            }
        }
    }
}
