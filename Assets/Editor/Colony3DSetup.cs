using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Colonia09.EditorTools
{
    /// <summary>
    /// Sets the project up for the 3D colony island: a forward (3D) renderer beside the 2D one the battle board
    /// uses, and the outside building models' materials moved from the built-in Standard shader to URP Lit.
    /// Run once from the menu or in batch mode (-executeMethod Colonia09.EditorTools.Colony3DSetup.Run).
    /// </summary>
    public static class Colony3DSetup
    {
        const string PipelinePath = "Assets/Settings/UniversalRP.asset";
        const string RendererPath = "Assets/Settings/UniversalRenderer3D.asset";
        const string ModelsRoot = "Assets/Resources/Colonia09";

        [MenuItem("Colonia 09/Configurar isla 3D")]
        public static void Run()
        {
            EnsureRenderer();
            ConvertMaterials();
            AssetDatabase.SaveAssets();
            Debug.Log("COLONY3D_SETUP_OK");
        }

        /// <summary>The 3D renderer goes second in the pipeline's list; the colony camera picks it by index.</summary>
        static void EnsureRenderer()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(data, RendererPath);
            }
            var so = new SerializedObject(pipeline);
            var list = so.FindProperty("m_RendererDataList");
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == data) return;
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = data;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
        }

        static void ConvertMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { ModelsRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == lit) continue;
                var baseMap = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                var color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                var emissionMap = mat.HasProperty("_EmissionMap") ? mat.GetTexture("_EmissionMap") : null;
                var emission = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;
                float metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0;
                float smooth = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : .5f;
                bool glass = path.ToLowerInvariant().Contains("vidrio") || color.a < .99f;
                mat.shader = lit;
                mat.SetTexture("_BaseMap", baseMap);
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Metallic", metallic);
                mat.SetFloat("_Smoothness", glass ? .9f : smooth);
                if (emissionMap != null || emission.maxColorComponent > .01f)
                {
                    mat.SetTexture("_EmissionMap", emissionMap);
                    mat.SetColor("_EmissionColor", emissionMap != null && emission.maxColorComponent < .01f ? Color.white * .8f : emission);
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }
                if (glass)
                {
                    mat.SetFloat("_Surface", 1);
                    mat.SetFloat("_Blend", 0);
                    mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                    mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                    mat.SetFloat("_ZWrite", 0);
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.renderQueue = (int)RenderQueue.Transparent;
                }
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
                Debug.Log("COLONY3D_MATERIAL " + Path.GetFileName(path) + (glass ? " (glass)" : ""));
            }
        }
    }
}
