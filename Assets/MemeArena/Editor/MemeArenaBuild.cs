using System;
using System.IO;
using System.Linq;
using MemeArena;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MemeArenaEditor
{
    public static partial class MemeArenaBuild
    {
        public const string Root = "Assets/MemeArena";
        public const string Generated = Root + "/Generated";
        static string folder;
        static MemeArenaSettings settings;
        static Material stone, sand, gold, red, dark, cream;
        static Material outline;

        public static GameObject Generate(MemeArenaSettings source = null)
        {
            if (!Shader.Find("Universal Render Pipeline/Lit"))
                throw new InvalidOperationException("The current project needs URP Lit.");
            Directory.CreateDirectory(Generated);
            folder = AssetDatabase.GenerateUniqueAssetPath(Generated + "/Colosseum_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(folder + "/Materials");
            Directory.CreateDirectory(folder + "/Meshes");
            AssetDatabase.Refresh();
            settings = source ? Object.Instantiate(source) : ScriptableObject.CreateInstance<MemeArenaSettings>();
            settings.name = "Meme Colosseum settings";
            settings.Sanitize();
            AssetDatabase.CreateAsset(settings, folder + "/StageSettings.asset");
            MakeMaterials();
            var root = new GameObject("Meme Colosseum Stage");
            try
            {
                var stage = root.AddComponent<MemeArenaStage>();
                stage.settings = settings;
                stage.generatedAssetFolder = folder;
                stage.geometry = Group(root.transform, "Colosseum Architecture");
                stage.crowd = Group(root.transform, "Meme Spectators");
                BuildFloor(stage.geometry);
                BuildSeating(stage.geometry);
                BuildArcades(stage.geometry);
                BuildDecor(stage.geometry);
                BuildBackdrop(stage.geometry);
                CombineStatic(stage.geometry);
                BuildCrowd(stage);
                BuildLightingAndVfx(stage);
                BuildFloorFracture(stage);
                root.AddComponent<MemeArenaCombatAdapter>().crowd =
                    stage.crowd.GetComponent<MemeCrowdReactionManager>();
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/MemeColosseum_Stage.prefab");
                if (!prefab) throw new IOException("Could not save the stage prefab.");
                AssetDatabase.SaveAssets();
                File.WriteAllText(Root + "/Documentation/LatestGenerated.txt", AssetDatabase.GetAssetPath(prefab));
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        internal static Transform Group(Transform parent, string name)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
        }

        static GameObject Shape(Transform parent, string name, PrimitiveType type, Vector3 position,
            Vector3 scale, Material material, Quaternion? rotation = null)
        {
            var item = GameObject.CreatePrimitive(type);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.SetLocalPositionAndRotation(position, rotation ?? Quaternion.identity);
            item.transform.localScale = scale;
            item.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(item.GetComponent<Collider>());
            return item;
        }

        static GameObject Box(Transform parent, string name, Vector3 position, Vector3 scale, Material material,
            Quaternion? rotation = null) => Shape(parent, name, PrimitiveType.Cube, position, scale, material, rotation);

        static void CombineStatic(Transform root)
        {
            var filters = root.GetComponentsInChildren<MeshFilter>().Where(value => value.sharedMesh &&
                value.GetComponent<MeshRenderer>() && !PrefabUtility.IsPartOfPrefabInstance(value) &&
                !value.GetComponent<Renderer>().sharedMaterial.name.Contains("Sky"))
                .ToArray();
            int index = 0;
            foreach (var group in filters.GroupBy(value => value.GetComponent<MeshRenderer>().sharedMaterial))
            {
                var combined = new Mesh { name = "Colosseum " + group.Key.name, indexFormat = IndexFormat.UInt32 };
                combined.CombineMeshes(group.Select(value => new CombineInstance
                {
                    mesh = value.sharedMesh,
                    transform = root.worldToLocalMatrix * value.transform.localToWorldMatrix
                }).ToArray(), true, true);
                combined.RecalculateBounds();
                AssetDatabase.CreateAsset(combined, folder + "/Meshes/Architecture_" + index++ + ".asset");
                var item = new GameObject(group.Key.name + " — combined geometry");
                item.transform.SetParent(root, false);
                item.AddComponent<MeshFilter>().sharedMesh = combined;
                var renderer = item.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = group.Key;
                renderer.receiveShadows = true;
                GameObjectUtility.SetStaticEditorFlags(item, StaticEditorFlags.BatchingStatic);
                if (outline)
                {
                    var ink = Group(root, "Restrained ink silhouette");
                    ink.gameObject.AddComponent<MeshFilter>().sharedMesh = combined;
                    var inkRenderer = ink.gameObject.AddComponent<MeshRenderer>();
                    inkRenderer.sharedMaterial = outline;
                    inkRenderer.shadowCastingMode = ShadowCastingMode.Off;
                }
                foreach (var original in group)
                {
                    Object.DestroyImmediate(original.gameObject);
                }
            }
        }
    }
}
