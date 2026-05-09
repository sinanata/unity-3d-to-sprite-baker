using UnityEngine;

namespace SpriteBakerDemo
{
    /// <summary>
    /// Programmatic material factory. The demo ships zero .mat assets —
    /// avoids GUID drift and "material is pink" import errors.
    /// </summary>
    public static class MaterialFactory
    {
        const string LIT_SHADER       = "Universal Render Pipeline/Lit";
        const string SIMPLE_LIT       = "Universal Render Pipeline/Simple Lit";
        const string UNLIT_SHADER     = "Universal Render Pipeline/Unlit";
        const string ATLAS_SHADER     = "SpriteBaker/AtlasCutout";

        static Material _groundMat;
        static Material _pedestalMat;
        static Material _wallMat;

        // ── Pedestal / ground / wall (shared) ─────────────────────────────

        public static Material PedestalMaterial()
        {
            if (_pedestalMat != null) return _pedestalMat;
            _pedestalMat = MakeLit(new Color(0.18f, 0.21f, 0.27f), smoothness: 0.15f);
            _pedestalMat.name = "Pedestal";
            return _pedestalMat;
        }

        public static Material GroundMaterial()
        {
            if (_groundMat != null) return _groundMat;
            _groundMat = MakeLit(new Color(0.083f, 0.108f, 0.158f), smoothness: 0.0f);
            _groundMat.name = "Ground";
            return _groundMat;
        }

        public static Material WallMaterial()
        {
            if (_wallMat != null) return _wallMat;
            _wallMat = MakeLit(new Color(0.32f, 0.34f, 0.40f), smoothness: 0.10f);
            _wallMat.name = "ShowcaseWall";
            return _wallMat;
        }

        // ── Character body (URP/Lit textured) ─────────────────────────────

        /// <summary>
        /// Per-character URP/Lit material. New per call so a property
        /// override on one card doesn't smear to others.
        /// </summary>
        public static Material CharacterBodyMaterial(Texture2D skin)
        {
            var mat = MakeLit(Color.white, smoothness: 0.18f);
            mat.name = skin != null ? $"Body_{skin.name}" : "Body";
            if (skin != null)
            {
                mat.SetTexture("_BaseMap", skin);
                skin.filterMode = FilterMode.Bilinear;
            }
            else
            {
                // Explicit white bind — runtime-created URP/Lit on WebGL2
                // can sample (0,0,0,0) from the shader's "white" default
                // and render solid black despite a tinted _BaseColor.
                mat.SetTexture("_BaseMap", Texture2D.whiteTexture);
            }
            mat.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, 0f));
            return mat;
        }

        /// <summary>
        /// URP/Unlit body material for the offscreen capture stage. Bakes
        /// the skin texture without any lighting term so the atlas is a
        /// clean texture-only render — predictable across platforms (WebGL2
        /// in particular is brittle when URP/Lit runs against an offscreen
        /// camera with shader-stripped lighting passes) and conceptually
        /// correct: a sprite atlas shouldn't double-bake lighting that the
        /// runtime sprite shader doesn't apply anyway.
        /// </summary>
        public static Material CharacterBodyMaterialUnlit(Texture2D skin)
        {
            var shader = Shader.Find(UNLIT_SHADER) ?? Shader.Find(LIT_SHADER);
            if (shader == null)
            {
                Debug.LogError($"[SpriteBakerDemo] {UNLIT_SHADER} shader not found; falling back to lit (atlas may be black on WebGL).");
                return CharacterBodyMaterial(skin);
            }
            var mat = new Material(shader);
            mat.name = skin != null ? $"BodyUnlit_{skin.name}" : "BodyUnlit";

            // _BaseColor white so the texture sampling result isn't tinted
            // dark. Default URP/Unlit's _BaseColor is white but a runtime-
            // created instance occasionally lands at black; explicit set is
            // a one-line guarantee.
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);

            if (skin != null)
            {
                mat.SetTexture("_BaseMap", skin);
                skin.filterMode = FilterMode.Bilinear;
            }
            else
            {
                mat.SetTexture("_BaseMap", Texture2D.whiteTexture);
            }
            mat.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, 0f));
            return mat;
        }

        /// <summary>Hat / accessory material; not currently used.</summary>
        public static Material CharacterAccessoryMaterial(Color tint)
        {
            var mat = MakeLit(tint, smoothness: 0.25f);
            mat.name = "Accessory";
            return mat;
        }

        // ── Internals ──────────────────────────────────────────────────────

        static Material MakeLit(Color color, float smoothness)
        {
            var shader = Shader.Find(LIT_SHADER) ?? Shader.Find(SIMPLE_LIT);
            if (shader == null)
            {
                Debug.LogError($"[SpriteBakerDemo] URP Lit shader not found ({LIT_SHADER} or {SIMPLE_LIT}). Confirm com.unity.render-pipelines.universal is installed and the active pipeline.");
                shader = Shader.Find("Standard");
            }
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            return mat;
        }
    }
}
