using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FrankRetarget.Editor
{
    public static partial class PepeShowcaseSetup
    {
        static Material CompleteMotionMaterial()
        {
            string path = Root + "/Materials/Complete motion accents.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                material.name = "Complete motion accents";
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", Color.white);
            material.SetColor("_Color", Color.white);
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 2);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = 3001;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }
    }
}
