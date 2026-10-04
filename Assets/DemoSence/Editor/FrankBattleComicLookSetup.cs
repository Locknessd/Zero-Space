using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        const string ComicAssets = "Assets/Vfx/Battle/Comic";

        static Mesh ComicMesh(string name, bool ring)
        {
            Directory.CreateDirectory(ComicAssets);
            var vertices = new List<Vector3>(); var colors = new List<Color>(); var triangles = new List<int>();
            Band(ring ? .88f : .96f, ring ? .75f : .58f, new Color(.08f, .05f, .09f, .9f));
            Band(ring ? .86f : .92f, ring ? .78f : .64f, new Color(1, .64f, .08f, 1));
            Band(ring ? .835f : .86f, ring ? .805f : .74f, new Color(1, .98f, .72f, 1));
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(ComicAssets + "/" + name + ".asset");
            if (!mesh) { mesh = new Mesh {name = name}; AssetDatabase.CreateAsset(mesh, ComicAssets + "/" + name + ".asset"); }
            // Shuriken's mesh color stream reads packed bytes. Float mesh colors are
            // misinterpreted by the particle renderer and turn the gold bands dark blue.
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetColors(colors.Select(c => (Color32)c).ToList()); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh); return mesh;

            void Band(float outer, float inner, Color color)
            {
                const int segments = 40;
                for (int i = 0; i < segments; i++)
                {
                    float u = i / (float)segments, v = (i + 1) / (float)segments;
                    float a = Mathf.Lerp(ring ? 0 : -120, ring ? 360 : 110, u) * Mathf.Deg2Rad;
                    float b = Mathf.Lerp(ring ? 0 : -120, ring ? 360 : 110, v) * Mathf.Deg2Rad;
                    float taperA = ring ? 1 : Mathf.Pow(Mathf.Sin(u * Mathf.PI), .65f);
                    float taperB = ring ? 1 : Mathf.Pow(Mathf.Sin(v * Mathf.PI), .65f);
                    float ia = outer - (outer - inner) * taperA, ib = outer - (outer - inner) * taperB;
                    int n = vertices.Count;
                    vertices.Add(new Vector3(Mathf.Cos(a) * outer, Mathf.Sin(a) * outer, 0));
                    vertices.Add(new Vector3(Mathf.Cos(a) * ia, Mathf.Sin(a) * ia, 0));
                    vertices.Add(new Vector3(Mathf.Cos(b) * ib, Mathf.Sin(b) * ib, 0));
                    vertices.Add(new Vector3(Mathf.Cos(b) * outer, Mathf.Sin(b) * outer, 0));
                    for (int c = 0; c < 4; c++) colors.Add(color);
                    triangles.AddRange(new[] {n, n + 1, n + 2, n, n + 2, n + 3});
                }
            }
        }

        static void AddComicParticle(GameObject root, Mesh mesh, float lifetime, bool floor)
        {
            var child = new GameObject(floor ? "Expanding comic ground ring" : "Directed comic slash");
            child.transform.SetParent(root.transform, false);
            if (floor) { child.transform.localRotation = Quaternion.Euler(90, 0, 0); child.transform.localPosition = Vector3.up * .025f; }
            var p = child.AddComponent<ParticleSystem>(); p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = p.main; main.duration = .2f; main.loop = main.playOnAwake = false;
            main.startLifetime = lifetime; main.startSpeed = 0; main.startSize = floor ? 1.8f : 1;
            main.maxParticles = 1; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.simulationSpace = ParticleSystemSimulationSpace.Local; main.stopAction = ParticleSystemStopAction.None;
            var emission = p.emission; emission.rateOverTime = 0; emission.SetBursts(new[] {new ParticleSystem.Burst(0, (short)1)});
            var shape = p.shape; shape.enabled = false;
            var size = p.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, floor ? .35f : .8f, 1, floor ? 1.3f : 1.05f));
            var fade = p.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient(); gradient.SetKeys(new[] {new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1)},
                new[] {new GradientAlphaKey(1, 0), new GradientAlphaKey(1, .25f), new GradientAlphaKey(0, 1)});
            fade.color = gradient;
            var material = AssetDatabase.LoadAssetAtPath<Material>(ComicAssets + "/ComicParticle.mat");
            if (!material) { material = new Material(Shader.Find("Battle/Comic Particle")); AssetDatabase.CreateAsset(material, ComicAssets + "/ComicParticle.mat"); }
            var renderer = p.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = mesh; renderer.alignment = ParticleSystemRenderSpace.Local; renderer.sharedMaterial = material;
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> {ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color});
        }

        public static void InstallBattleComicStep2()
        {
            var arc = ComicMesh("SharpSlash", false); var ring = ComicMesh("GroundRing", true);
            ComicScene((game, camera) =>
            {
                var vfx = game.battleVfx;
                var slash = new GameObject("BattleComicSlash");
                try
                {
                    AddComicParticle(slash, arc, .19f, false);
                    vfx.bladeSlash = PrefabUtility.SaveAsPrefabAsset(slash, ComicAssets + "/BattleComicSlash.prefab");
                }
                finally { Object.DestroyImmediate(slash); }
                foreach (int kind in new[] {0, 1, 2})
                {
                    var previous = kind == 0 ? vfx.lightHit : kind == 1 ? vfx.heavyHit : vfx.groundImpact;
                    // Repeated setup starts from the saved pre-comic versions.
                    string baseName = kind == 0 ? "BattlePowerLightHit" : kind == 1 ? "BattlePowerHeavyHit" : "BattlePowerGroundImpact";
                    previous = AssetDatabase.LoadAssetAtPath<GameObject>(VfxFolder + "/" + baseName + ".prefab");
                    var root = Object.Instantiate(previous); root.name = kind == 0 ? "BattleComicLightHit" : kind == 1 ? "BattleComicHeavyHit" : "BattleComicGroundImpact";
                    try
                    {
                        if (kind == 2) AddComicParticle(root, ring, .36f, true);
                        foreach (var p in root.GetComponentsInChildren<ParticleSystem>(true))
                        {
                            if (p.transform.GetComponentsInParent<Transform>().Any(t => t.name.StartsWith("Comic ", StringComparison.Ordinal)))
                            {
                                var main = p.main; main.startLifetimeMultiplier = Mathf.Min(main.startLifetimeMultiplier, .5f);
                            }
                        }
                        var saved = PrefabUtility.SaveAsPrefabAsset(root, ComicAssets + "/" + root.name + ".prefab");
                        if (kind == 0) vfx.lightHit = saved; else if (kind == 1) vfx.heavyHit = saved; else vfx.groundImpact = saved;
                    }
                    finally { Object.DestroyImmediate(root); }
                }
                EditorUtility.SetDirty(vfx);
            });
            File.WriteAllText(ComicReview + "/Step2.txt", "Sharp mesh slash with black edge/gold band/white core follows evaluated weapon direction; ground ring expands in the floor plane. Mage/Archer contact bursts retained; comic hit letters shortened. Shared cue counts/pool unchanged.\n");
        }

        static void BackupComicMaterial(Material material)
        {
            Directory.CreateDirectory(ComicReview + "/MaterialsBefore");
            string path = AssetDatabase.GetAssetPath(material);
            string backup = ComicReview + "/MaterialsBefore/" + Path.GetFileName(path) + ".txt";
            if (!File.Exists(backup)) File.Copy(path, backup);
        }

        public static void InstallBattleComicStep3()
        {
            ComicScene((game, camera) =>
            {
                foreach (var fighter in new[] {game.leftCombat, game.rightCombat})
                foreach (var material in fighter.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                    .Where(m => m && m.shader.name.StartsWith("FlatKit/", StringComparison.Ordinal) &&
                        AssetDatabase.GetAssetPath(m).StartsWith("Assets/Materials/BattleFlatKit/", StringComparison.Ordinal)).Distinct())
                {
                    BackupComicMaterial(material); Undo.RecordObject(material, "Clean battle character shading");
                    ConfigureCleanBattleCharacterMaterial(material, fighter == game.rightCombat);
                    EditorUtility.SetDirty(material);
                }
            });
            File.WriteAllText(ComicReview + "/Step3.txt", "FlatKit soft toon shading and lighter colored shadows, without inverted hull facial overlaps. Dark specular/rim patches removed; character textures retained.\n");
        }

        static void ConfigureCleanBattleCharacterMaterial(Material material, bool pepe)
        {
            // Inverted hulls overlap facial geometry in extreme paired poses.
            material.shader = Shader.Find("FlatKit/Stylized Surface");
            // FlatKit replaces the base color with its specular/rim color. The old
            // dark colors therefore produced black disks instead of highlights.
            material.SetFloat("_SpecularEnabled", 0); material.DisableKeyword("DR_SPECULAR_ON");
            material.SetFloat("_RimEnabled", 0); material.DisableKeyword("DR_RIM_ON");
            material.SetColor("_FlatSpecularColor", new Color(.9f, .92f, .95f, 1));
            material.SetColor("_FlatRimColor", pepe ? new Color(.72f, .9f, .8f, 1) : new Color(.86f, .85f, .8f, 1));
            material.DisableKeyword("_CELPRIMARYMODE_STEPS"); material.DisableKeyword("_CELPRIMARYMODE_CURVE");
            material.EnableKeyword("_CELPRIMARYMODE_SINGLE"); material.SetFloat("_CelPrimaryMode", 1);
            material.SetColor("_ColorDim", pepe ? new Color(.68f, .8f, .7f, 1) : new Color(.72f, .76f, .83f, 1));
            material.SetFloat("_SelfShadingSize", .2f); material.SetFloat("_ShadowEdgeSize", .22f);
            material.SetFloat("_UnityShadowPower", .18f);
            material.SetFloat("_OutlineWidth", .7f);
            material.SetColor("_OutlineColor", new Color(.1f, .11f, .13f, 1));
        }

        public static void InstallBattleComicStep4()
        {
            Directory.CreateDirectory("Assets/Materials/BattleComicBackdrop");
            ComicScene((game, camera) =>
            {
                var shader = Shader.Find("Battle/Muted Lightmapped Backdrop");
                if (!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader)) throw new Exception("Backdrop shader failed to import.");
                var cache = new Dictionary<Material, Material>();
                foreach (var renderer in game.gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true)))
                {
                    if (!(renderer is MeshRenderer) || renderer.GetComponentInParent<CharacterCombat>(true) || renderer.GetComponentInParent<Canvas>(true)) continue;
                    var slots = renderer.sharedMaterials; bool changed = false;
                    for (int i = 0; i < slots.Length; i++)
                    {
                        var source = slots[i];
                        if (!source || source.shader.name != "Custom/UnlitLightMapEmissive") continue;
                        if (!cache.TryGetValue(source, out var material))
                        {
                            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
                            string path = "Assets/Materials/BattleComicBackdrop/Backdrop_" + guid + ".mat";
                            material = AssetDatabase.LoadAssetAtPath<Material>(path);
                            if (!material) { material = new Material(source); AssetDatabase.CreateAsset(material, path); }
                            material.shader = shader; material.SetFloat("_Saturation", .58f); material.SetFloat("_Contrast", .86f); material.SetFloat("_Exposure", .88f);
                            EditorUtility.SetDirty(material); cache.Add(source, material);
                        }
                        slots[i] = material; changed = true;
                    }
                    if (changed) { Undo.RecordObject(renderer, "Mute battle backdrop"); renderer.sharedMaterials = slots; EditorUtility.SetDirty(renderer); }
                }
                File.WriteAllText(ComicReview + "/Step4.txt", "Muted " + cache.Count + " scene-only backdrop materials; original authored textures/lightmaps/emission retained; saturation .58, contrast .86, exposure .88. No full-screen postprocess or camera changes.\n");
            });
        }
    }
}
