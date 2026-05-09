using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpriteBakerDemo.EditorTools
{
    /// <summary>
    /// First-launch URP bootstrap: creates the
    /// <see cref="UniversalRenderPipelineAsset"/> + renderer if missing,
    /// so <c>git clone &amp;&amp; open in Unity</c> just works. We let Unity
    /// instantiate the ScriptableObject (rather than committing YAML)
    /// because the URP asset's internal fields shift between versions.
    /// </summary>
    public static class EnsureUrpAssets
    {
        const string SETTINGS_DIR  = "Assets/Settings";
        const string URP_PATH      = "Assets/Settings/URPRenderPipelineAsset.asset";
        const string RENDERER_PATH = "Assets/Settings/URPRenderPipelineAsset_Renderer.asset";

        [InitializeOnLoadMethod]
        static void Init()
        {
            // Defer past static-ctor; AssetDatabase isn't ready, and
            // modifying GraphicsSettings can deadlock on first import.
            EditorApplication.delayCall += Run;
        }

        static void Run()
        {
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset) return;

            if (!Directory.Exists(SETTINGS_DIR))
                Directory.CreateDirectory(SETTINGS_DIR);

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RENDERER_PATH);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, RENDERER_PATH);
            }

            var urpAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(URP_PATH);
            if (urpAsset == null)
            {
                urpAsset = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
                AttachRenderer(urpAsset, rendererData);
                AssetDatabase.CreateAsset(urpAsset, URP_PATH);
            }
            else
            {
                AttachRenderer(urpAsset, rendererData);
                EditorUtility.SetDirty(urpAsset);
            }

            AssetDatabase.SaveAssets();

            // Assign on every level so WebGL (Mobile default) and
            // Standalone (PC) share the same pipeline. Iterating
            // QualitySettings is the only public API that writes to
            // levels other than the current one.
            int currentLevel = QualitySettings.GetQualityLevel();
            int levelCount   = QualitySettings.names.Length;
            for (int i = 0; i < levelCount; i++)
            {
                QualitySettings.SetQualityLevel(i, applyExpensiveChanges: false);
                QualitySettings.renderPipeline = urpAsset;
            }
            QualitySettings.SetQualityLevel(currentLevel, applyExpensiveChanges: false);

            GraphicsSettings.defaultRenderPipeline = urpAsset;

            Debug.Log($"[SpriteBakerDemo] Bootstrapped URP RenderPipelineAsset at {URP_PATH} — pipeline is now active.");
        }

        // SerializedObject avoids depending on URP's internal API surface
        // (m_RendererDataList shape varies across 14.x → 17.x).
        static void AttachRenderer(UniversalRenderPipelineAsset urpAsset, UniversalRendererData rendererData)
        {
            var so = new SerializedObject(urpAsset);
            var listProp = so.FindProperty("m_RendererDataList");
            if (listProp == null) return;
            if (listProp.arraySize < 1) listProp.arraySize = 1;
            var elem = listProp.GetArrayElementAtIndex(0);
            if (elem.objectReferenceValue != rendererData)
                elem.objectReferenceValue = rendererData;
            var idxProp = so.FindProperty("m_DefaultRendererIndex");
            if (idxProp != null) idxProp.intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
