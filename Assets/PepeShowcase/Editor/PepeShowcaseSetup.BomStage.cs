using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using ZeroSpace.Showcase;

namespace FrankRetarget.Editor
{
    public static partial class PepeShowcaseSetup
    {
        static void StyleBomStage(Scene scene, PepeAttackShowcase showcase)
        {
            LightBomCharacter(scene);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer.transform.IsChildOf(showcase.character.transform)) continue;
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        var source = materials[i];
                        if (!source) continue;
                        string name = source.name;
                        if (name.StartsWith("BOM ")) name = name.Substring(4);
                        Color color;
                        if (name.Contains("Floor pearl")) color = new Color(.19f, .24f, .29f);
                        else if (name.Contains("Floor graphite")) color = new Color(.065f, .1f, .15f);
                        else if (name.Contains("Cyan panels")) color = new Color(.12f, .31f, .36f);
                        else if (name.Contains("concrete") || name.Contains("Porcelain"))
                            color = new Color(.09f, .15f, .22f);
                        else continue;
                        string path = Root + "/Materials/BOM " + name + ".mat";
                        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (!material)
                        {
                            material = UnityEngine.Object.Instantiate(source);
                            material.name = "BOM " + name;
                            AssetDatabase.CreateAsset(material, path);
                        }
                        material.SetColor("_BaseColor", color);
                        EditorUtility.SetDirty(material);
                        AssetDatabase.SaveAssetIfDirty(material);
                        materials[i] = material;
                    }
                    renderer.sharedMaterials = materials;
                }
            RenderSettings.ambientLight = new Color(.32f, .4f, .5f);
            RenderSettings.fogColor = new Color(.045f, .075f, .12f);
            RenderSettings.fogDensity = .018f;
            showcase.director.output.backgroundColor = RenderSettings.fogColor;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var volume in root.GetComponentsInChildren<Volume>(true))
                {
                    string path = Root + "/BOMTrailerProfile.asset";
                    var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
                    if (!profile)
                    {
                        profile = ScriptableObject.CreateInstance<VolumeProfile>();
                        AssetDatabase.CreateAsset(profile, path);
                        foreach (var component in volume.sharedProfile.components)
                        {
                            var clone = UnityEngine.Object.Instantiate(component);
                            profile.components.Add(clone);
                            AssetDatabase.AddObjectToAsset(clone, profile);
                        }
                    }
                    volume.sharedProfile = profile;
                    if (profile.TryGet<Bloom>(out var bloom))
                    {
                        bloom.intensity.Override(.85f);
                        bloom.threshold.Override(.95f);
                        EditorUtility.SetDirty(bloom);
                    }
                    if (profile.TryGet<DepthOfField>(out var depth))
                    {
                        depth.active = false;
                        EditorUtility.SetDirty(depth);
                    }
                    EditorUtility.SetDirty(profile);
                    AssetDatabase.SaveAssetIfDirty(profile);
                }
        }

        static void LightBomCharacter(Scene scene)
        {
            GameObject root = null;
            foreach (var candidate in scene.GetRootGameObjects())
                if (candidate.name == "BOM soft front key") root = candidate;
            if (!root)
            {
                root = new GameObject("BOM soft front key");
                SceneManager.MoveGameObjectToScene(root, scene);
            }
            var light = root.GetComponent<Light>();
            if (!light) light = root.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = .85f;
            light.color = new Color(.8f, .9f, 1);
            light.shadows = LightShadows.None;
            root.transform.rotation = Quaternion.Euler(25, 180, 0);
        }
    }
}
