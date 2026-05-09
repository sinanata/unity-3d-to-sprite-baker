using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SpriteBakerDemo.EditorTools
{
    /// <summary>
    /// Generates the Kenney <c>characterMedium</c>'s AnimatorController
    /// (Idle / Run / Jump states named to match the basenames). Runs on
    /// FBX import via <see cref="DemoAssetWatcher"/>; menu fallback for
    /// recovery. Idempotent — re-runs cleanly.
    /// </summary>
    public static class KenneyControllerBuilder
    {
        const string MODELS_DIR        = "Assets/Demo/Resources/Models";
        const string ANIMATIONS_DIR    = "Assets/Demo/Resources/Models/Animations";
        const string CONTROLLER_PATH   = "Assets/Demo/Resources/Models/CharacterMediumController.controller";

        static readonly string[] s_clipFiles = { "idle.fbx", "run.fbx", "jump.fbx" };

        [MenuItem("Sprite Baker Demo/Rebuild Kenney Controller")]
        public static void RebuildController()
        {
            if (!Directory.Exists(ANIMATIONS_DIR))
            {
                Debug.LogWarning($"[SpriteBakerDemo] {ANIMATIONS_DIR} not found — vendor the Kenney animations first.");
                return;
            }

            // Delete-and-rebuild is cheaper than patching a stale graph.
            if (File.Exists(CONTROLLER_PATH))
                AssetDatabase.DeleteAsset(CONTROLLER_PATH);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(CONTROLLER_PATH);
            var rootStateMachine = controller.layers[0].stateMachine;

            AnimatorState defaultState = null;

            foreach (var file in s_clipFiles)
            {
                string clipPath = $"{ANIMATIONS_DIR}/{file}";
                AnimationClip clip = SelectAnimationClip(clipPath);
                if (clip == null)
                {
                    Debug.LogWarning($"[SpriteBakerDemo] No AnimationClip at {clipPath} — controller will be missing a state.");
                    continue;
                }

                // Basename, not clip.name — AnimatorStateMachine.AddState
                // rejects '.' / '|' in state names (silent sanitization)
                // which would break runtime Play("idle") lookup.
                string stateName = System.IO.Path.GetFileNameWithoutExtension(file);
                var state = rootStateMachine.AddState(stateName);
                state.motion = clip;
                state.writeDefaultValues = true;

                if (defaultState == null && file == "idle.fbx")
                    defaultState = state;
            }

            if (defaultState != null)
                rootStateMachine.defaultState = defaultState;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[SpriteBakerDemo] Wrote {CONTROLLER_PATH} (states: {string.Join(", ", controller.layers[0].stateMachine.states.Length)})");
        }

        // Three-tier fallback for the bistable importer state:
        // basename-match → non-pose clip → first clip.
        static AnimationClip SelectAnimationClip(string clipPath)
        {
            string baseName = System.IO.Path.GetFileNameWithoutExtension(clipPath);
            var assets = AssetDatabase.LoadAllAssetsAtPath(clipPath);
            if (assets == null || assets.Length == 0) return null;

            AnimationClip first = null;
            AnimationClip basenameMatch = null;
            AnimationClip nonPoseMatch = null;

            foreach (var a in assets)
            {
                if (a is not AnimationClip c) continue;
                if (first == null) first = c;

                string n = c.name ?? "";

                if (basenameMatch == null
                    && string.Equals(n, baseName, System.StringComparison.OrdinalIgnoreCase))
                    basenameMatch = c;

                bool looksLikePose =
                    n.IndexOf("Targeting", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Pose", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("_skipped_", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (nonPoseMatch == null && !looksLikePose) nonPoseMatch = c;
            }

            return basenameMatch ?? nonPoseMatch ?? first;
        }
    }

    /// <summary>
    /// Auto-rebuilds the Kenney controller on import — without this a
    /// fresh clone would need the menu command before first Play.
    /// </summary>
    public class DemoAssetWatcher : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (importedAssets == null) return;
            foreach (var p in importedAssets)
            {
                string path = p.Replace('\\', '/');
                if (path.Contains("/Demo/Resources/Models/Animations/")
                    || path.Contains("/Demo/Resources/Models/characterMedium"))
                {
                    EditorApplication.delayCall += KenneyControllerBuilder.RebuildController;
                    return;
                }
            }
        }
    }
}
