using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        const string WeaponVfxReview = "GeneratedAssets/WeaponVfxAlignmentReview";
        const string WeaponVfxAssets = "Assets/Vfx/Battle/WeaponTrails";

        [MenuItem("Tools/Battle/VFX/Align weapon trails and contact styles")]
        public static void InstallAlignedWeaponVfx()
        {
            Directory.CreateDirectory(WeaponVfxAssets); Directory.CreateDirectory(WeaponVfxReview);
            AssetDatabase.Refresh();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(VfxFolder + "/Shaders/WeaponRibbon.shader");
            if (!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader)) throw new Exception("Invalid weapon ribbon shader.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(WeaponVfxAssets + "/WeaponRibbon.mat");
            if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, WeaponVfxAssets + "/WeaponRibbon.mat"); }
            material.shader = shader;
            string bankBefore = File.ReadAllText("Assets/Audio/Battle/BattleSfxBank.asset");
            var report = new StringBuilder();
            var calibratedBlades = CalibrateWeaponEndpoints(report);
            ComicScene((game, camera) =>
            {
                var vfx = game.battleVfx;
                var trails = game.GetComponent<BattleWeaponTrails>();
                if (!trails) trails = Undo.AddComponent<BattleWeaponTrails>(game.gameObject);
                Undo.RecordObjects(new Object[] { vfx, trails }, "Align weapon VFX");
                trails.material = material; trails.maxTrails = 16;
                trails.styles = new[] {
                    TrailStyle(TrumpWeaponManager.WeaponType.WarriorShield, new Color(1,.82f,.3f,.65f), .09f, .3f),
                    TrailStyle(TrumpWeaponManager.WeaponType.GreatSword, new Color(1,.58f,.08f,.8f), .13f, .2f),
                    TrailStyle(TrumpWeaponManager.WeaponType.TwoHandedAxe, new Color(1,.18f,.06f,.85f), .15f, .64f),
                    TrailStyle(TrumpWeaponManager.WeaponType.Katana, new Color(1,.78f,.5f,.72f), .085f, .24f),
                    TrailStyle(TrumpWeaponManager.WeaponType.Spear, new Color(.22f,1,.82f,.75f), .085f, .85f),
                    TrailStyle(TrumpWeaponManager.WeaponType.DualDaggers, new Color(.18f,.75f,1,.8f), .075f, .28f),
                    TrailStyle(TrumpWeaponManager.WeaponType.Assassin, new Color(.78f,.35f,1,.78f), .095f, .26f)
                };
                ConfigureAssassinSlashStyle(trails.styles.Single(s => s.weapon == TrumpWeaponManager.WeaponType.Assassin));
                vfx.weaponTrails = trails;
                foreach (var variant in vfx.impactVariants)
                    if (variant.weapon == TrumpWeaponManager.WeaponType.Spear || variant.weapon == TrumpWeaponManager.WeaponType.DualDaggers ||
                        variant.weapon == TrumpWeaponManager.WeaponType.Assassin)
                    {
                        var color = trails.styles.Single(s => s.weapon == variant.weapon).color;
                        variant.stab = WeaponContact(variant.weapon + "PierceContact", color, false, variant.light);
                    }
                vfx.shieldImpact = WeaponContact("ShieldBashContact", new Color(1,.83f,.36f), true,
                    vfx.impactVariants.Single(v => v.weapon == TrumpWeaponManager.WeaponType.WarriorShield).heavy);
                trails.staticBlades = calibratedBlades;
                EditorUtility.SetDirty(trails); EditorUtility.SetDirty(vfx); EditorUtility.SetDirty(material);
            });
            if (bankBefore != File.ReadAllText("Assets/Audio/Battle/BattleSfxBank.asset")) throw new Exception("Contact/audio bank changed.");
            report.AppendLine("Seven motion-aligned weapon styles, shield bash, three piercing contact styles, limb-aware impacts. No new textures.");
            report.AppendLine("Short sweeps sampled once per cue; frame updates interpolate; contact/audio/damage cue times retained.");
            File.WriteAllText(WeaponVfxReview + "/Installation.txt", report.ToString());
        }

        static BattleWeaponTrails.Style TrailStyle(TrumpWeaponManager.WeaponType weapon, Color color, float life, float start) =>
            new BattleWeaponTrails.Style { weapon = weapon, color = color, lifetime = life, bladeStart = start };

        // Sampling fighters can create temporary override controllers and alter poses.
        // Keep that work in a disposable scene so installation saves only VFX settings.
        static BattleWeaponTrails.StaticBlade[] CalibrateWeaponEndpoints(StringBuilder report)
        {
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var result = new List<BattleWeaponTrails.StaticBlade>();
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var f in fighters) { if (!f.Initialize()) throw new Exception("Fighter not initialized."); f.battleVfx = null; f.battleSfx = null; f.hitEffect = null; }
                foreach (var move in game.leftCombat.heavyCombatMoves)
                {
                    foreach (var f in fighters) f.ResetCombat();
                    game.leftCombat.transform.position = Vector3.zero;
                    game.rightCombat.transform.position = Vector3.right * move.attackRange;
                    if (!game.leftCombat.ExecuteAttack(move, game.rightCombat)) throw new Exception("Calibration attack rejected.");
                    var pair = game.leftCombat.SourcePlayback;
                    try
                    {
                        var cue = game.battleVfx.timeline.FindMove(move).cues.First(c => c.group.EndsWith("swing", StringComparison.Ordinal));
                        pair.EvaluateAt(cue.seconds);
                        foreach (var renderer in pair.AttackerActor.Pose.weaponRenderers)
                        {
                            if (!renderer || !renderer.enabled || renderer.name.IndexOf("case",StringComparison.OrdinalIgnoreCase) >= 0) continue;
                            var skin = renderer as SkinnedMeshRenderer; var filter = renderer.GetComponent<MeshFilter>();
                            var mesh = skin ? skin.sharedMesh : filter ? filter.sharedMesh : null;
                            if (!mesh || result.Any(b => b.mesh == mesh)) continue;
                            var positions = ReadWeaponVertices(mesh); var posed = positions;
                            if (skin)
                            {
                                var sampled = new Mesh();
                                try { skin.BakeMesh(sampled,true); posed = sampled.vertices; }
                                finally { Object.DestroyImmediate(sampled); }
                            }
                            Vector3 left = game.leftCombat.Animator.GetBoneTransform(HumanBodyBones.LeftHand).position;
                            Vector3 right = game.leftCombat.Animator.GetBoneTransform(HumanBodyBones.RightHand).position;
                            var matrix = renderer.transform.localToWorldMatrix;
                            Vector3 center = matrix.MultiplyPoint3x4(posed[0]);
                            Vector3 grip = (left-center).sqrMagnitude < (right-center).sqrMagnitude ? left : right;
                            int near = 0, far = 0; float nearest = float.PositiveInfinity, furthest = -1;
                            for (int i = 0; i < positions.Length; i++)
                            {
                                float d = (matrix.MultiplyPoint3x4(posed[i])-grip).sqrMagnitude;
                                if (d < nearest) { nearest = d; near = i; } if (d > furthest) { furthest = d; far = i; }
                            }
                            var record = new BattleWeaponTrails.StaticBlade { mesh = mesh, bladeBase = positions[near], bladeTip = positions[far] };
                            if (skin)
                            {
                                var weights = mesh.boneWeights; var bind = mesh.bindposes;
                                record.baseBones = BindWeaponPoint(positions[near],weights[near],bind);
                                record.tipBones = BindWeaponPoint(positions[far],weights[far],bind);
                            }
                            result.Add(record);
                            report.AppendLine($"Blade {mesh.name}: two calibrated endpoints, Read/Write={mesh.isReadable}, bone weights={record.tipBones.Length}.");
                        }
                    }
                    finally { pair.Cancel(); }
                }
                return result.ToArray();
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        static Vector3[] ReadWeaponVertices(Mesh mesh)
        {
            using (var data = MeshUtility.AcquireReadOnlyMeshData(mesh))
            using (var positions = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp))
            { data[0].GetVertices(positions); return positions.ToArray(); }
        }

        static BattleWeaponTrails.BonePoint[] BindWeaponPoint(Vector3 point, BoneWeight weight, Matrix4x4[] bind)
        {
            var bones = new[] { weight.boneIndex0,weight.boneIndex1,weight.boneIndex2,weight.boneIndex3 };
            var weights = new[] { weight.weight0,weight.weight1,weight.weight2,weight.weight3 };
            return Enumerable.Range(0,4).Where(i => weights[i] > 0).Select(i => new BattleWeaponTrails.BonePoint {
                bone = bones[i], point = bind[bones[i]].MultiplyPoint3x4(point), weight = weights[i] }).ToArray();
        }

        static GameObject WeaponContact(string name, Color color, bool shield, GameObject source)
        {
            if (!source) throw new Exception("Missing shared contact template.");
            var root = Object.Instantiate(source); root.name = name;
            string prefabPath = AccentAssets + "/" + name + ".prefab";
            try
            {
                var core = root.transform.Find("Contact core").GetComponent<ParticleSystemRenderer>();
                // The small contact marker remains readable when a shield covers
                // the collision point; sparks and the actual ribbon retain depth testing.
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(VfxFolder + "/Shaders/WeaponContact.shader");
                if (!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader)) throw new Exception("Invalid weapon contact shader.");
                string materialPath = WeaponVfxAssets + "/" + name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material,materialPath); }
                material.shader = shader; material.SetColor("_TintColor",new Color(color.r,color.g,color.b,1));
                core.sharedMaterial = material; EditorUtility.SetDirty(material);
                var mesh = Object.Instantiate(core.mesh); mesh.name = name + "Shape";
                var positions = mesh.vertices;
                if (shield)
                {
                    var ring = new List<Vector3>(); var tint = new List<Color>(); var indices = new List<int>();
                    for (int i = 0; i <= 32; i++)
                    {
                        float angle = i * Mathf.PI * 2 / 32; var axis = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
                        ring.Add(axis * .18f); ring.Add(axis * .13f); tint.Add(Color.white); tint.Add(color);
                        if (i < 32) { int n = i * 2; indices.AddRange(new[] { n,n+1,n+3,n,n+3,n+2 }); }
                    }
                    mesh.Clear(); mesh.SetVertices(ring); mesh.SetColors(tint); mesh.SetTriangles(indices, 0);
                }
                else
                {
                    for (int i = 0; i < positions.Length; i++) positions[i] = Vector3.Scale(positions[i], new Vector3(1.7f,.4f,1));
                    mesh.vertices = positions;
                }
                mesh.RecalculateBounds();
                string meshPath = WeaponVfxAssets + "/" + name + "Shape.asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (existing) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; }
                else AssetDatabase.CreateAsset(mesh, meshPath);
                core.mesh = mesh;
                foreach (var p in root.GetComponentsInChildren<ParticleSystem>(true)) SingleBurst(p, shield ? .18f : .14f);
                return PrefabUtility.SaveAsPrefabAsset(root,prefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        public static void CaptureWeaponVfxBefore() => CaptureWeaponVfx("Before");
        public static void CaptureWeaponVfxAfter() => CaptureWeaponVfx("After");
        static void CaptureWeaponVfx(string stage)
        {
            Directory.CreateDirectory(WeaponVfxReview + "/" + stage);
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            BattleLightingRig lighting = null;
            var rt = new RenderTexture(1280, 720, 24);
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                if (stage == "Before")
                {
                    if (game.battleVfx.weaponTrails) game.battleVfx.weaponTrails.enabled = false;
                    game.battleVfx.shieldImpact = null;
                    foreach (var variant in game.battleVfx.impactVariants) variant.stab = null;
                }
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single(c => c.CompareTag("MainCamera"));
                camera.scene = scene; camera.targetTexture = rt;
                foreach (var canvas in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)))
                { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
                var fighters = new[] { game.leftCombat, game.rightCombat }; foreach (var f in fighters) f.Initialize();
                lighting = game.GetComponent<BattleLightingRig>(); lighting.InitializeRig();
                foreach (var attacker in fighters)
                foreach (var move in attacker.heavyCombatMoves)
                {
                    foreach (var f in fighters) f.ResetCombat(); game.battleVfx.ResetForMatch();
                    var receiver = fighters.Single(f => f != attacker);
                    bool reverse = attacker == game.rightCombat;
                    attacker.transform.position = new Vector3(reverse ? .5f : -.5f,0,0);
                    receiver.transform.position = attacker.transform.position + Vector3.right * (reverse ? -move.attackRange : move.attackRange);
                    var effects = new Dictionary<GameObject, float>();
                    Action<string, GameObject> watch = (id, root) => effects[root] = attacker.SourcePlayback.SampleTime;
                    game.battleVfx.EffectPlayed += watch;
                    try
                    {
                        if (!attacker.ExecuteAttack(move, receiver)) throw new Exception("Capture attack rejected.");
                        var pair = attacker.SourcePlayback; var profile = game.battleVfx.timeline.FindMove(move);
                        var hit = profile.cues.First(c => c.hasContactPoint);
                        foreach (string phase in new[] { "Sweep", "Contact" })
                        {
                            float time = hit.seconds - (phase == "Sweep" ? .045f : 0);
                            pair.EvaluateAt(time); game.battleVfx.AdvanceSequence(pair, time);
                            foreach (var effect in effects)
                            foreach (var p in effect.Key.GetComponentsInChildren<ParticleSystem>())
                            {
                                p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); p.useAutoRandomSeed = false; p.randomSeed = 321;
                                p.Simulate(Mathf.Max(0,time-effect.Value) + .02f, false, true, false);
                            }
                            lighting.RefreshLighting(.22f); var director = camera.GetComponent<FrankCinematicCamera>(); director.ResetView(); director.Apply(0, true);
                            Canvas.ForceUpdateCanvases();
                            CaptureWeaponPose(camera, fighters, pair, WeaponVfxReview + "/" + stage + "/" + attacker.name + "_" + move.weapon + "_" + phase + ".png");
                        }
                        pair.Cancel();
                    }
                    finally { game.battleVfx.EffectPlayed -= watch; }
                }
            }
            finally { if (lighting) lighting.ShutdownRig(); Object.DestroyImmediate(rt); EditorSceneManager.ClosePreviewScene(scene); }
        }

        static void CaptureWeaponPose(Camera camera, CharacterCombat[] fighters, FrankBattlePairPlayback pair, string path)
        {
            var skins = fighters.SelectMany(f => f.Animator.GetComponentsInChildren<SkinnedMeshRenderer>())
                .Concat(pair.AttackerActor.GetComponentsInChildren<SkinnedMeshRenderer>()).Where(s => s.enabled && s.sharedMesh).Distinct().ToArray();
            var clones = new List<GameObject>(); var meshes = new List<Mesh>();
            try
            {
                foreach (var skin in skins)
                {
                    var mesh = new Mesh(); skin.BakeMesh(mesh, true); meshes.Add(mesh);
                    var clone = new GameObject("Weapon review pose"); SceneManager.MoveGameObjectToScene(clone, camera.gameObject.scene); clones.Add(clone);
                    clone.transform.SetPositionAndRotation(skin.transform.position, skin.transform.rotation); clone.transform.localScale = skin.transform.lossyScale;
                    clone.AddComponent<MeshFilter>().sharedMesh = mesh; clone.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials; skin.enabled = false;
                }
                CaptureBattleCamera(camera, path);
            }
            finally
            {
                foreach (var skin in skins) if (skin) skin.enabled = true;
                foreach (var clone in clones) Object.DestroyImmediate(clone); foreach (var mesh in meshes) Object.DestroyImmediate(mesh);
            }
        }
    }
}
