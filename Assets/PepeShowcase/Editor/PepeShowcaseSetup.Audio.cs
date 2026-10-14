using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZeroSpace.Showcase;

namespace FrankRetarget.Editor
{
    public static partial class PepeShowcaseSetup
    {
        [MenuItem("Tools/Pepe Showcase/Add Battle weapon VFX")]
        public static void InstallWeaponVfx()
        {
            var library = AssetDatabase.LoadAssetAtPath<PepeShowcaseLibrary>(Root + "/ShowcaseLibrary.asset");
            if (!library) throw new InvalidOperationException("Create the showcase first.");
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
            var report = new StringBuilder();
            try
            {
                var game = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<GameManager>(true)).Single();
                var fighter = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<CharacterCombat>(true))
                    .Single(item => item.name == "Pepe");
                var trails = game.GetComponent<BattleWeaponTrails>();
                if (!trails || !trails.material) throw new InvalidOperationException("Battle weapon trails are missing.");
                library.weaponTrailMaterial = trails.material;
                library.bladeGeometry = trails.staticBlades;
                library.excludedBladeMeshes = trails.excludedMeshes;
                library.muzzlePrefab = CreateShowcaseMuzzle();
                for (int i = 0; i < library.takes.Length; i++)
                {
                    var take = library.takes[i];
                    if (take.nativeWeaponEffects) continue;
                    var move = fighter.heavyCombatMoves.Single(item => item.sourcePair != null &&
                        item.sourcePair.attack == take.animation);
                    var profile = game.battleVfx.timeline.FindMove(move);
                    var style = trails.styles.Single(item => item.weapon == move.weapon);
                    var variant = game.battleVfx.bladeSlashVariants.FirstOrDefault(item => item.weapon == move.weapon);
                    take.slashPrefab = variant?.prefab ? variant.prefab : game.battleVfx.bladeSlash;
                    take.slashScale = (variant?.scale ?? 1) * .7f;
                    take.trailColor = style.color;
                    take.trailLifetime = style.lifetime;
                    take.bladeStart = style.bladeStart;
                    var assignedHits = new HashSet<BattleSfxBank.Cue>();
                    foreach (var cue in take.cues)
                    {
                        cue.effectStart = cue.effectEnd = 0;
                        if (!cue.swing) continue;
                        var timing = profile.cues.Where(item => item.group.EndsWith("swing"))
                            .OrderBy(item => Mathf.Abs(item.seconds - cue.seconds)).First();
                        var hit = profile.cues.FirstOrDefault(item => item.seconds > timing.seconds &&
                            item.seconds - timing.seconds <= .65f && item.group.EndsWith("hit") &&
                            !assignedHits.Contains(item));
                        if (hit != null && hit.contactSource != "Weapon") continue;
                        if (hit == null && !string.IsNullOrEmpty(timing.contactSource) &&
                            timing.contactSource != "Weapon") continue;
                        if (hit != null) assignedHits.Add(hit);
                        cue.effectStart = Mathf.Max(0, cue.seconds - 1f / 30);
                        cue.effectEnd = Mathf.Min(take.animation.length,
                            (hit?.seconds ?? cue.seconds + style.lifetime) + style.followThroughSeconds);
                        var next = take.cues.FirstOrDefault(item => item.swing && item.seconds > cue.seconds);
                        if (next != null) cue.effectEnd = Mathf.Min(cue.effectEnd, next.seconds);
                        report.AppendLine(take.title + " sweep " + cue.effectStart.ToString("F3") + ".." +
                            cue.effectEnd.ToString("F3"));
                    }
                    report.AppendLine(take.title + " slash=" + AssetDatabase.GetAssetPath(take.slashPrefab));
                }
                EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssetIfDirty(library);
                Directory.CreateDirectory(Output);
                File.WriteAllText(Output + "/WeaponVfxSetup.txt", report.ToString());
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static GameObject CreateShowcaseMuzzle()
        {
            string folder = Root + "/Effects";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Root, "Effects");
            string meshPath = folder + "/MuzzleStar.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (!mesh)
            {
                mesh = new Mesh { name = "Soft muzzle star" };
                var vertices = new Vector3[17];
                var colors = new Color[17];
                var triangles = new int[48];
                colors[0] = Color.white;
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * Mathf.PI / 8;
                    float radius = i % 2 == 0 ? .5f : .12f;
                    vertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius;
                    colors[i + 1] = new Color(1, 1, 1, 0);
                    triangles[i * 3] = 0;
                    triangles[i * 3 + 1] = i + 1;
                    triangles[i * 3 + 2] = (i + 1) % 16 + 1;
                }
                mesh.vertices = vertices;
                mesh.colors = colors;
                mesh.triangles = triangles;
                mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            string materialPath = folder + "/MuzzleGlow.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material)
            {
                material = new Material(Shader.Find("Battle/Weapon Contact"));
                material.SetColor("_TintColor", new Color(1, .86f, .4f, 1));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            var root = new GameObject("Showcase muzzle flash");
            try
            {
                var particles = root.AddComponent<ParticleSystem>();
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main;
                main.loop = false;
                main.playOnAwake = false;
                main.duration = .12f;
                main.startLifetime = .1f;
                main.startSpeed = .65f;
                main.startSize = .34f;
                main.startColor = Color.white;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                var emission = particles.emission;
                emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0, 1) });
                var shape = particles.shape;
                shape.enabled = false;
                var size = particles.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(
                    new Keyframe(0, .7f), new Keyframe(.2f, 1), new Keyframe(1, 0)));
                var renderer = particles.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.mesh = mesh;
                renderer.alignment = ParticleSystemRenderSpace.View;
                renderer.sharedMaterial = material;
                return PrefabUtility.SaveAsPrefabAsset(root, folder + "/MuzzleFlash.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static PepeShowcaseLibrary.Cue[] AttackCues(BattleSfxBank bank, CombatTripletData move,
            BattleSfxBank.Move profile)
        {
            if (profile?.cues == null)
                throw new InvalidOperationException("Missing attack sound timeline for " + move.moveName);
            int index = 0;
            return profile.cues.Where(cue => cue.group.EndsWith("swing") || cue.group == "gun_shot")
                .Select(cue =>
                {
                    string id = bank.ResolveWeaponGroup(move, cue, profile);
                    var sounds = new List<PepeShowcaseLibrary.Sound>();
                    AddSound(bank, id, index++, sounds, true);
                    return new PepeShowcaseLibrary.Cue
                    {
                        seconds = cue.seconds,
                        group = id,
                        gun = cue.group == "gun_shot",
                        swing = cue.group.EndsWith("swing"),
                        sounds = sounds.ToArray()
                    };
                }).OrderBy(cue => cue.seconds).ToArray();
        }

        static void AddSound(BattleSfxBank bank, string id, int variation,
            List<PepeShowcaseLibrary.Sound> sounds, bool layers)
        {
            var group = bank.FindGroup(id);
            if (group?.clips == null || group.clips.Length == 0)
                throw new InvalidOperationException("Missing showcase sound group " + id);
            int index = variation % group.clips.Length;
            sounds.Add(new PepeShowcaseLibrary.Sound
            {
                clip = group.clips[index],
                offset = index < group.startOffsets.Length ? group.startOffsets[index] : 0,
                volume = group.volume * .85f * (index < group.clipGains.Length ? group.clipGains[index] : 1),
                pitch = (group.pitch.x + group.pitch.y) * .5f
            });
            if (layers)
                foreach (string layer in group.layers ?? Array.Empty<string>())
                    AddSound(bank, layer, variation, sounds, false);
        }
    }
}
