using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SpriteBakerDemo.EditorTools
{
    /// <summary>
    /// Adds the runtime-resolved URP shaders + atlas shader to
    /// <c>AlwaysIncludedShaders</c>. The demo ships zero .mat assets;
    /// without this, WebGL shader stripping leaves <c>Shader.Find</c>
    /// returning null and every primitive renders magenta.
    /// Runs at editor load AND in <c>OnPreprocessBuild</c> (belt-and-
    /// suspenders for batchmode where <c>delayCall</c> races the build).
    /// </summary>
    public class EnsureBuildShaders : IPreprocessBuildWithReport
    {
        const string GRAPHICS_SETTINGS_PATH = "ProjectSettings/GraphicsSettings.asset";

        static readonly string[] RequiredShaderNames =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "Universal Render Pipeline/Unlit",
            "SpriteBaker/AtlasCutout",
        };

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => Run();

        [InitializeOnLoadMethod]
        static void Init() => EditorApplication.delayCall += Run;

        static void Run()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(GRAPHICS_SETTINGS_PATH);
            if (assets == null || assets.Length == 0)
            {
                Debug.LogWarning($"[SpriteBakerDemo] Could not load {GRAPHICS_SETTINGS_PATH} — skipping AlwaysIncludedShaders patch.");
                return;
            }

            var so = new SerializedObject(assets[0]);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            if (arr == null || !arr.isArray)
            {
                Debug.LogWarning("[SpriteBakerDemo] m_AlwaysIncludedShaders property not found on GraphicsSettings — Unity layout may have changed.");
                return;
            }

            var present = new HashSet<Object>();
            for (int i = 0; i < arr.arraySize; i++)
            {
                var elem = arr.GetArrayElementAtIndex(i);
                if (elem.objectReferenceValue != null)
                    present.Add(elem.objectReferenceValue);
            }

            var added = new List<string>();
            foreach (var name in RequiredShaderNames)
            {
                var shader = Shader.Find(name);
                if (shader == null) continue; // not yet compiled / not installed
                if (present.Contains(shader)) continue;

                int newIdx = arr.arraySize;
                arr.arraySize = newIdx + 1;
                arr.GetArrayElementAtIndex(newIdx).objectReferenceValue = shader;
                added.Add(name);
            }

            if (added.Count > 0)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
                Debug.Log($"[SpriteBakerDemo] Added {added.Count} shader(s) to GraphicsSettings.AlwaysIncludedShaders so player builds keep them: {string.Join(", ", added)}");
            }
        }
    }
}
