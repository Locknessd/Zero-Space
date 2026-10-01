using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void InstallBattleSourceWeapons()
        {
            const string battlePath = "Assets/Scenes/BattleScene.unity";
            Directory.CreateDirectory("Temp/FrankRetarget/BeforeSourceWeapons");
            File.Copy(battlePath, "Temp/FrankRetarget/BeforeSourceWeapons/BattleScene.unity", true);
            var source = EditorSceneManager.OpenPreviewScene("Assets/DemoSence/Frank_Damages_Mankey_Pepe.unity");
            try
            {
                var tester = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FrankCombinationTester>(true)).Single();
                var battle = EditorSceneManager.OpenScene(battlePath, OpenSceneMode.Single);
                var game = battle.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var types = new[] { TrumpWeaponManager.WeaponType.TwoHandedAxe, TrumpWeaponManager.WeaponType.Assassin,
                    TrumpWeaponManager.WeaponType.DualDaggers, TrumpWeaponManager.WeaponType.GreatSword,
                    TrumpWeaponManager.WeaponType.Katana, TrumpWeaponManager.WeaponType.Spear, TrumpWeaponManager.WeaponType.WarriorShield };
                foreach (var fighter in new[] { game.leftCombat, game.rightCombat })
                {
                    var actor = fighter.name == "Mankey" ? tester.mankey : tester.pepe;
                    var rig = fighter.GetComponent<FrankCombatWeaponRig>();
                    if (!rig) rig = fighter.gameObject.AddComponent<FrankCombatWeaponRig>();
                    rig.bindings = types.Select((type, i) => new FrankCombatWeaponRig.Binding { weapon = type, driver = actor.attackDrivers[i] }).ToArray();
                    if (rig.bindings.Any(b => !b.driver || !b.driver.pose || b.driver.pose.weaponRenderers.Length == 0))
                        throw new Exception("Missing source driver or weapon: " + fighter.name);
                    // Only generated containers from the previous migration, never model bones.
                    var old = fighter.GetComponentsInChildren<Transform>(true)
                        .Where(t => t != fighter.transform && t.name.EndsWith("_Visible", StringComparison.Ordinal))
                        .OrderBy(t => AnimationUtility.CalculateTransformPath(t, fighter.transform).Count(c => c == '/')).ToArray();
                    foreach (var t in old) if (t) Object.DestroyImmediate(t.gameObject);
                    foreach (var legacy in fighter.GetComponentsInChildren<TrumpWeaponManager>(true)) Object.DestroyImmediate(legacy);
                    fighter.weaponRig = rig;
                    EditorUtility.SetDirty(fighter);
                    EditorUtility.SetDirty(rig);
                }
                EditorSceneManager.MarkSceneDirty(battle);
                if (!EditorSceneManager.SaveScene(battle)) throw new IOException("BattleScene save failed.");
                File.WriteAllText("Temp/FrankRetarget/source-rig-install.txt", "Installed calibrated source drivers for both fighters; removed baked weapon containers.\n");
            }
            finally { EditorSceneManager.ClosePreviewScene(source); }
        }

        public static void ValidateBattleSourceWeapons()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
            var reference = EditorSceneManager.OpenPreviewScene("Assets/DemoSence/Frank_Damages_Mankey_Pepe.unity");
            var report = new StringBuilder();
            try
            {
                var tester = reference.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FrankCombinationTester>(true)).Single();
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var fighter in fighters)
                {
                    if (!fighter.weaponRig || fighter.GetComponent<TrumpWeaponManager>()) throw new Exception("Invalid rig installation.");
                    if (!fighter.Initialize()) throw new Exception("Combat initialization failed.");
                }
                fighters[0].transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                fighters[1].transform.SetPositionAndRotation(Vector3.forward, Quaternion.Euler(0, 180, 0));
                for (int side = 0; side < 2; side++)
                foreach (var move in fighters[side].heavyCombatMoves)
                {
                    foreach (var f in fighters) f.ResetCombat();
                    var attacker = fighters[side];
                    var scale = attacker.transform.localScale;
                    if (!attacker.ExecuteAttack(move, fighters[1 - side])) throw new Exception("Cannot start " + move.moveName);
                    var rig = attacker.weaponRig;
                    if (!rig.ActiveDriver) throw new Exception("No active source driver.");
                    var pose = rig.ActiveDriver.pose;
                    var actor = side == 0 ? tester.mankey : tester.pepe;
                    int motion = Array.FindIndex(actor.attackDrivers, d => d == rig.bindings.First(b => b.weapon == move.weapon).driver);
                    actor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    actor.Configure(motion, true, false, tester.weapons[motion], motion);
                    float maxGrip = 0, maxDistance = 0, movement = 0, maxSourceError = 0;
                    int samples = 0;
                    Vector3 firstPosition = Vector3.zero;
                    for (int tick = 0; tick < 1800 && fighters.Any(f => f.IsBusy); tick++)
                    {
                        foreach (var f in fighters) f.Animator.Update(1f / 60f);
                        rig.Evaluate();
                        if (!rig.ActiveDriver) continue;
                        actor.Evaluate(rig.SampleTime);
                        var hands = pose.limbs.Where(l => l.alignGrip).ToArray();
                        foreach (var hand in hands) maxGrip = Mathf.Max(maxGrip, Vector3.Distance(hand.SourceGrip, hand.TargetGrip));
                        for (int part = 0; part < pose.weaponRenderers.Length; part++)
                        {
                            var renderer = pose.weaponRenderers[part];
                            if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                                throw new Exception("Inactive weapon renderer: " + move.moveName);
                            if (renderer is SkinnedMeshRenderer skin)
                            {
                                if (!skin.sharedMesh || skin.bones.Any(b => !b)) throw new Exception("Invalid weapon mesh or bones.");
                                var baked = new Mesh();
                                skin.BakeMesh(baked, true);
                                var bounds = baked.bounds;
                                Vector3 center = skin.transform.TransformPoint(bounds.center);
                                // Compare geometry in driver space with the working source scene,
                                // including authored prop travel rather than assuming a fixed socket.
                                var sourceSkin = actor.Pose.weaponRenderers[part] as SkinnedMeshRenderer;
                                var sourceMesh = new Mesh();
                                sourceSkin.BakeMesh(sourceMesh, true);
                                Vector3 sourceCenter = actor.activeDriver.transform.InverseTransformPoint(sourceSkin.transform.TransformPoint(sourceMesh.bounds.center));
                                Vector3 battleCenter = rig.ActiveDriver.transform.InverseTransformPoint(center);
                                maxSourceError = Mathf.Max(maxSourceError, Vector3.Distance(sourceCenter, battleCenter));
                                Object.DestroyImmediate(sourceMesh);
                                if (bounds.size.sqrMagnitude < .000001f || float.IsNaN(center.x)) throw new Exception("Empty weapon geometry.");
                                float distance = hands.Min(h => Vector3.Distance(center, h.TargetGrip));
                                maxDistance = Mathf.Max(maxDistance, distance);
                                if (samples == 0) firstPosition = center;
                                movement = Mathf.Max(movement, Vector3.Distance(center, firstPosition));
                                Object.DestroyImmediate(baked);
                            }
                        }
                        if (tick == 45) CaptureBattleWeapon(scene, attacker, side + "-" + move.moveName);
                        samples++;
                    }
                    actor.Clear();
                    if (samples < 10 || movement < .02f || maxSourceError > .01f || maxGrip > .12f)
                        throw new Exception($"Bad weapon pose {attacker.name} {move.moveName}: samples={samples}, movement={movement}, grip={maxGrip}, sourceError={maxSourceError}, distance={maxDistance}");
                    if (fighters.Any(f => f.IsBusy || !f.LastSequenceSucceeded) || rig.ActiveDriver)
                        throw new Exception("Sequence did not finish/release weapon.");
                    if (attacker.transform.localScale != scale) throw new Exception("Fighter scale changed.");
                    report.AppendLine($"PASS {attacker.name} {move.moveName}: frames={samples}, gripError={maxGrip:F4}m, sourceMeshError={maxSourceError:F5}m, weaponTravel={movement:F3}m, centerToHand={maxDistance:F3}m; completed and hidden");
                }
                report.AppendLine("PASS no legacy manager; source weapon skins valid; battle Animator advances; both fighters complete hit/get-up.");
            }
            catch (Exception e) { report.AppendLine("FAIL " + e); throw; }
            finally
            {
                File.WriteAllText("Temp/FrankRetarget/source-rig-validation.txt", report.ToString());
                EditorSceneManager.ClosePreviewScene(reference);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static void CaptureBattleWeapon(Scene scene, CharacterCombat fighter, string filename)
        {
            var go = new GameObject("Weapon check camera");
            SceneManager.MoveGameObjectToScene(go, scene);
            var camera = go.AddComponent<Camera>();
            camera.scene = scene;
            Vector3 focus = fighter.transform.position + Vector3.up;
            camera.transform.position = focus + new Vector3(3, 1, 4);
            camera.transform.LookAt(focus);
            camera.nearClipPlane = .03f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.18f, .20f, .23f);
            var rt = RenderTexture.GetTemporary(960, 720, 24);
            var previous = RenderTexture.active;
            var texture = new Texture2D(960, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, 960, 720), 0, 0);
                texture.Apply();
                Directory.CreateDirectory("Temp/FrankRetarget/WeaponScreens");
                File.WriteAllBytes("Temp/FrankRetarget/WeaponScreens/" + filename + ".png", texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(go);
            }
        }
    }
}
