using System.IO;
using UnityEditor;
using UnityEngine;

namespace SpriteBakerDemo.EditorTools
{
    /// <summary>
    /// Fixes import settings on every FBX under
    /// <c>Assets/Demo/Resources/Models</c>: read/write enabled, no
    /// auto-collider, no embedded materials, Generic rig with <c>Root</c>
    /// as the motion node. The motion-node designation is critical — with
    /// <c>applyRootMotion=false</c> the root-rotation curve is consumed
    /// instead of being written onto <c>Root.localRotation</c>, preserving
    /// the FBX importer's Z-up→Y-up axis correction (Kenney AC2 ships
    /// Z-up with <c>bakeAxisConversion=0</c>).
    /// </summary>
    public class DemoModelImporter : AssetPostprocessor
    {
        const string SCOPED_PATH = "/Demo/Resources/Models/";
        const string MOTION_NODE_NAME = "Root";

        void OnPreprocessModel()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.Contains(SCOPED_PATH)) return;
            var importer = (ModelImporter)assetImporter;

            // Required for the baker's per-frame mesh access.
            importer.isReadable = true;

            // Auto-colliders pollute the Y=2000 capture stage.
            importer.addCollider = false;

            // We apply skin materials programmatically.
            importer.materialImportMode = ModelImporterMaterialImportMode.None;

            // Generic, not Humanoid — Unity's avatar retargeting shifts
            // Kenney's stylised proportions slightly.
            importer.animationType = ModelImporterAnimationType.Generic;

            // Treat Root as motion data so the clip's root curve is
            // consumed (with applyRootMotion=false) rather than writing
            // onto Root.localRotation — see type-level XML doc.
            importer.motionNodeName = MOTION_NODE_NAME;

            // Defensive: a contributor disabling the inspector toggle
            // would silently break the bake.
            string lower = path.ToLowerInvariant();
            if (lower.Contains("/animations/"))
            {
                importer.importAnimation = true;

                // Rename the chosen clip to the FBX basename (idle.fbx →
                // "idle"). FBX-embedded names ("Root|Idle", "Take 001")
                // make runtime clip lookup fragile across re-exports.
                // defaultClipAnimations returns a fresh array; assign back
                // to clipAnimations to persist the override.
                string baseName = Path.GetFileNameWithoutExtension(path);
                var clips = importer.defaultClipAnimations;
                if (clips != null && clips.Length > 0)
                {
                    // Skip Kenney AC2's "Root|0.Targeting Pose" static
                    // sub-clip (alphabetically first → would shadow the
                    // real animation in AssetDatabase.LoadAssetAtPath).
                    int chosenIdx = FindBestClipIndex(clips);

                    // Sets clip.isLooping for the LIVE Animator. The bake
                    // ignores this — its source of truth is SpriteAnimRow.Loop.
                    bool expectLoop = ShouldLoopForBaseName(baseName);

                    for (int i = 0; i < clips.Length; i++)
                    {
                        if (i == chosenIdx)
                        {
                            clips[i].name = baseName;
                            clips[i].loopTime = expectLoop;
                            // wrapMode + loopTime: some Unity 6 import
                            // paths honour only one. Setting both is safe.
                            clips[i].wrapMode = expectLoop ? WrapMode.Loop : WrapMode.ClampForever;

                            // Kenney's jump.fbx holds a static landed
                            // pose for the last ~20%. Trim so the loop
                            // doesn't visibly freeze on it.
                            if (baseName.Equals("jump", System.StringComparison.OrdinalIgnoreCase))
                            {
                                float first = clips[i].firstFrame;
                                float last  = clips[i].lastFrame;
                                if (last > first)
                                    clips[i].lastFrame = first + (last - first) * 0.8f;
                            }
                        }
                        else
                        {
                            // Distinct prefix so skipped sub-clips don't
                            // shadow the chosen one in AssetDatabase.
                            clips[i].name = baseName + "_skipped_" + i;
                            clips[i].loopTime = false;
                        }
                    }
                    importer.clipAnimations = clips;
                }
            }
        }

        // Skip "Targeting Pose" / "Pose" sub-clips; index 0 fallback so
        // the controller has SOMETHING to wire.
        static int FindBestClipIndex(UnityEditor.ModelImporterClipAnimation[] clips)
        {
            for (int i = 0; i < clips.Length; i++)
            {
                string n = clips[i].name ?? "";
                if (n.IndexOf("Targeting", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (n.IndexOf("Pose",      System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                return i;
            }
            return 0;
        }

        // All three demo clips loop on the live Animator so the side-by-
        // side card matches the looping sprite playback.
        static bool ShouldLoopForBaseName(string baseName)
        {
            if (string.IsNullOrEmpty(baseName)) return false;
            if (baseName.Equals("idle", System.StringComparison.OrdinalIgnoreCase)) return true;
            if (baseName.Equals("run",  System.StringComparison.OrdinalIgnoreCase)) return true;
            if (baseName.Equals("jump", System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.Contains(SCOPED_PATH)) return;
            var importer = (TextureImporter)assetImporter;

            // Bake renders at a fixed orthographic distance — no LOD chain.
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 512;
        }
    }

    /// <summary>
    /// Re-imports Kenney FBXes when import settings have moved past what
    /// was last applied. Stamp via <see cref="AssetImporter.userData"/>;
    /// bump <see cref="STAMP"/> whenever <see cref="DemoModelImporter"/>
    /// changes a setting existing clones need to pick up.
    /// </summary>
    [InitializeOnLoad]
    public static class KenneyImporterMigrator
    {
        const string STAMP = "spritebakerdemo:v3";

        static readonly string[] s_paths =
        {
            "Assets/Demo/Resources/Models/characterMedium.fbx",
            "Assets/Demo/Resources/Models/Animations/idle.fbx",
            "Assets/Demo/Resources/Models/Animations/run.fbx",
            "Assets/Demo/Resources/Models/Animations/jump.fbx",
        };

        static KenneyImporterMigrator()
        {
            // Defer past static-ctor; AssetDatabase isn't ready yet here.
            EditorApplication.delayCall += MigrateIfNeeded;
        }

        [MenuItem("Sprite Baker Demo/Reimport Kenney FBXes")]
        public static void MigrateForce()
        {
            Migrate(force: true);
        }

        static void MigrateIfNeeded() { Migrate(force: false); }

        static void Migrate(bool force)
        {
            bool any = false;
            foreach (var path in s_paths)
            {
                if (!System.IO.File.Exists(path)) continue;
                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                if (imp == null) continue;
                if (!force && imp.userData == STAMP) continue;
                imp.userData = STAMP;
                imp.SaveAndReimport();
                any = true;
            }
            if (any)
                Debug.Log($"[SpriteBakerDemo] Migrated Kenney FBX importers to {STAMP}.");
        }
    }
}
