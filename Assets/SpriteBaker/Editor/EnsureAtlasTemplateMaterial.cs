using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SpriteBaker.EditorTools
{
    /// <summary>
    /// Creates and persists a placeholder URP/Unlit material with
    /// <c>_AlphaClip = 1</c> + the <c>_ALPHATEST_ON</c> keyword enabled.
    ///
    /// Why: URP/Unlit declares <c>_ALPHATEST_ON</c> as
    /// <c>shader_feature_local_fragment</c>, so the alpha-clip shader
    /// variant is STRIPPED from a player build whenever no project-asset
    /// material has the keyword set at build time. The library creates
    /// playback materials at runtime via <c>new Material(URP/Unlit)</c>
    /// and calls <c>EnableKeyword("_ALPHATEST_ON")</c> — but that's a
    /// no-op once the variant has been stripped. The cutout silently
    /// stops clipping on WebGL builds, characters render with their full
    /// quad opaque, the area around the silhouette renders solid black.
    ///
    /// Force-shipping this template material as a serialized asset means
    /// the build pipeline sees a consumer of the variant and compiles it.
    /// At runtime, <see cref="SpriteAtlasBaker.CreateAtlasPlaybackMaterial"/>
    /// instantiates from this template (rather than <c>new Material(shader)</c>)
    /// to inherit the keyword set.
    /// </summary>
    public class EnsureAtlasTemplateMaterial : IPreprocessBuildWithReport
    {
        public const string TEMPLATE_RESOURCE_NAME = "SpriteBakerAtlasUnlitTemplate";
        const string TEMPLATE_ASSET_PATH = "Assets/SpriteBaker/Resources/" + TEMPLATE_RESOURCE_NAME + ".mat";
        const string URP_UNLIT_SHADER   = "Universal Render Pipeline/Unlit";

        // Run before EnsureBuildShaders (callbackOrder 0). Order doesn't
        // strictly matter — both are idempotent — but keeping templates
        // generated first means the new asset is in AssetDatabase before
        // the AlwaysIncludedShaders patch runs.
        public int callbackOrder => -10;

        public void OnPreprocessBuild(BuildReport report) => EnsureExists();

        [InitializeOnLoadMethod]
        static void EnsureOnEditorLoad()
        {
            // delayCall so AssetDatabase / Resources are warm before we
            // run. The build pipeline doesn't rely on this — it calls
            // EnsureExists directly via OnPreprocessBuild — but we still
            // generate the asset on first editor load so it's committed
            // to the repo and visible to git.
            EditorApplication.delayCall += EnsureExists;
        }

        public static void EnsureExists()
        {
            if (File.Exists(TEMPLATE_ASSET_PATH)) return;

            var shader = Shader.Find(URP_UNLIT_SHADER);
            if (shader == null)
            {
                Debug.LogWarning($"[SpriteBaker] {URP_UNLIT_SHADER} shader not found — cannot create alpha-cutout template material. WebGL build will render sprite atlases with opaque background.");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(TEMPLATE_ASSET_PATH));

            var mat = new Material(shader)
            {
                name = TEMPLATE_RESOURCE_NAME,
            };

            // Mirror what URP/Unlit's inspector "Alpha Clipping" toggle emits
            // when enabled. Property writes alone aren't enough — the
            // _ALPHATEST_ON keyword has to be live on the material asset for
            // the build pipeline to see it as a consumer of the variant.
            if (mat.HasProperty("_BaseColor"))  mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_BaseMap_ST")) mat.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, 0f));
            if (mat.HasProperty("_Surface"))    mat.SetFloat("_Surface", 0f);   // Opaque
            if (mat.HasProperty("_Blend"))      mat.SetFloat("_Blend",   0f);
            if (mat.HasProperty("_AlphaClip"))  mat.SetFloat("_AlphaClip", 1f); // Alpha Clipping ON
            if (mat.HasProperty("_Cutoff"))     mat.SetFloat("_Cutoff",  0.5f);
            if (mat.HasProperty("_Cull"))       mat.SetFloat("_Cull",    0f);   // Off — billboard quads
            if (mat.HasProperty("_ZWrite"))     mat.SetFloat("_ZWrite",  1f);
            if (mat.HasProperty("_SrcBlend"))   mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_DstBlend"))   mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);

            mat.EnableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.DisableKeyword("_ALPHAMODULATE_ON");
            mat.SetOverrideTag("RenderType", "TransparentCutout");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;

            AssetDatabase.CreateAsset(mat, TEMPLATE_ASSET_PATH);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SpriteBaker] Created {TEMPLATE_ASSET_PATH} so the URP/Unlit alpha-clip variant survives WebGL build stripping. Commit this asset.");
        }
    }
}
