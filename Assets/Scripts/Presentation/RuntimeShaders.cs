using UnityEngine;

namespace BizarreChess.Presentation
{
    /// <summary>
    /// Same material path the editor always used: URP Lit via Shader.Find.
    /// WebGL player also clones Resources/Mat if Find was stripped, and
    /// retargets Built-in glTF shaders to URP Lit.
    /// </summary>
    public static class RuntimeShaders
    {
        private static Shader _lit;
        private static Shader _forcefield;

        public static Shader Lit
        {
            get
            {
                if (_lit != null)
                    return _lit;

                _lit = Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Universal Render Pipeline/Simple Lit");

#if UNITY_WEBGL && !UNITY_EDITOR
                if (_lit == null)
                {
                    var template = Resources.Load<Material>("Mat");
                    if (template != null)
                        _lit = template.shader;
                }
#endif

                if (_lit == null)
                    _lit = Shader.Find("Unlit/Color");

                return _lit;
            }
        }

        public static Shader Forcefield
        {
            get
            {
                if (_forcefield != null)
                    return _forcefield;

                _forcefield = Shader.Find("BizarreChess/Forcefield");
                return _forcefield;
            }
        }

        public static Material Create(Color color, Texture texture = null, float smoothness = 0.3f)
        {
            var shader = Lit;
            if (shader == null)
            {
                Debug.LogError("[RuntimeShaders] No usable shader.");
                return new Material(Shader.Find("Hidden/InternalErrorShader"));
            }

            var mat = new Material(shader);
            if (texture != null)
            {
                mat.mainTexture = texture;
                if (mat.HasProperty("_BaseMap"))
                    mat.SetTexture("_BaseMap", texture);
            }

            mat.color = color;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", smoothness);
            return mat;
        }

        public static void EnsureUrpCompatible(Material mat)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (mat == null || mat.shader == null)
                return;

            string name = mat.shader.name;
            if (name.Contains("Universal Render Pipeline") || name.StartsWith("Shader Graphs/glTF"))
                return;

            var shader = Lit;
            if (shader == null)
                return;

            Color color = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                : mat.HasProperty("_Color") ? mat.GetColor("_Color")
                : mat.color;
            Texture tex = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : mat.mainTexture;
            float smoothness = mat.HasProperty("_Smoothness") ? mat.GetFloat("_Smoothness") : 0.3f;

            mat.shader = shader;
            if (tex != null)
            {
                mat.mainTexture = tex;
                if (mat.HasProperty("_BaseMap"))
                    mat.SetTexture("_BaseMap", tex);
            }
            mat.color = color;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", smoothness);
#else
            _ = mat;
#endif
        }
    }
}
