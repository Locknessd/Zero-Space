using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static class CombatExpansionVol10Study
    {
        static readonly HumanBodyBones[] Bones =
        {
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
            HumanBodyBones.Head, HumanBodyBones.Chest, HumanBodyBones.Hips
        };

        public static CombatTripletData MakeMove(CharacterCombat source, CharacterCombat target, int index)
        {
            var library = AssetDatabase.LoadAssetAtPath<FrankUnarmedLibrary>(
                "Assets/DemoSence/Vol10/UnarmedLibrary.asset");
            var original = library.pairs[index];
            var basis = source.lightCombatMoves.First(m => m.sourcePair?.unarmedIndex >= 0).sourcePair;
            return new CombatTripletData
            {
                moveName = "Vol10_" + original.sourceName,
                attackAnim = original.attacker,
                hitAnim = original.receiver,
                attackRange = Mathf.Abs(original.receiverOffset.z),
                sourcePair = new FrankBattlePair
                {
                    attackerDriver = basis.attackerDriver,
                    receiverDriver = basis.receiverDriver,
                    attack = original.attacker,
                    reaction = original.receiver,
                    receiverOffset = original.receiverOffset,
                    receiverRotation = original.receiverRotation,
                    spacing = basis.spacing,
                    bodySpacing = basis.bodySpacing,
                    unarmedIndex = index,
                    pepeAttacks = source.name == "Pepe"
                }
            };
        }

        [MenuItem("Tools/Battle/Combat Expansion/Study required Vol10 interactions")]
        public static void StudyRequired()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before sampling the isolated preview.");
            string output = CombatExpansionInventory.Output + "/Vol10Study";
            Directory.CreateDirectory(output);
            var scene = EditorSceneManager.OpenPreviewScene(CombatExpansionInventory.Battle);
            var csv = new StringBuilder("fighter,move,seconds,role,bone,x,y,z\n");
            var cameraTarget = RenderTexture.GetTemporary(480, 320, 24);
            var stamp = new Texture2D(480, 320, TextureFormat.RGB24, false);
            var sheet = new Texture2D(480 * 4, 320 * 3, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                var game = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var camera = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<Camera>(true)).First(c => c.CompareTag("MainCamera"));
                camera.scene = scene;
                camera.targetTexture = cameraTarget;
                camera.aspect = 1.5f;
                var framing = camera.GetComponent<FrankCinematicCamera>();
                if (!framing)
                    framing = scene.GetRootGameObjects()
                        .SelectMany(r => r.GetComponentsInChildren<FrankCinematicCamera>(true)).First();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var fighter in fighters)
                {
                    fighter.battleSfx = null;
                    fighter.battleVfx = null;
                    fighter.hitEffect = null;
                    fighter.Initialize();
                }
                var times = new StringBuilder("Motion study only; no gameplay integration claim.\n");
                using var skinSnapshot = new CombatExpansionPreviewSkin(fighters.Select(f => f.gameObject).ToArray());
                times.AppendLine("Each sheet is chronological left to right, top to bottom; exact seconds follow.\n");
                foreach (var source in fighters)
                foreach (int index in new[] { 3, 4, 5, 8, 9, 12 })
                {
                    var target = fighters.Single(f => f != source);
                    foreach (var fighter in fighters)
                        fighter.ResetCombat();
                    var move = MakeMove(source, target, index);
                    float direction = source == fighters[0] ? 1 : -1;
                    source.Animator.transform.position = Vector3.left * direction * move.attackRange * .5f;
                    target.Animator.transform.position = Vector3.right * direction * move.attackRange * .5f;
                    if (!source.ExecuteAttack(move, target))
                        throw new InvalidOperationException("Study could not begin " + move.moveName);
                    var pair = source.SourcePlayback;
                    try
                    {
                        int samples = Mathf.CeilToInt(pair.Duration * 60);
                        for (int frame = 0; frame <= samples; frame++)
                        {
                            float seconds = Mathf.Min(frame / 60f, pair.Duration);
                            pair.EvaluateAt(seconds);
                            AppendBones(csv, source, source, move, "attacker", seconds);
                            AppendBones(csv, source, target, move, "receiver", seconds);
                        }
                        times.Append(source.name + "/" + move.moveName + ": ");
                        for (int frame = 0; frame < 12; frame++)
                        {
                            float seconds = pair.Duration * frame / 11;
                            pair.EvaluateAt(seconds);
                            skinSnapshot.Sample();
                            framing.Apply(0, true);
                            camera.Render();
                            RenderTexture.active = cameraTarget;
                            stamp.ReadPixels(new Rect(0, 0, 480, 320), 0, 0);
                            stamp.Apply();
                            sheet.SetPixels(frame % 4 * 480, (2 - frame / 4) * 320, 480, 320, stamp.GetPixels());
                            times.Append(seconds.ToString("F4") + " ");
                        }
                        sheet.Apply();
                        File.WriteAllBytes(output + "/" + source.name + "_" + move.moveName + ".png",
                            sheet.EncodeToPNG());
                        times.AppendLine();
                    }
                    finally
                    {
                        pair.Cancel();
                    }
                }
                File.WriteAllText(output + "/Times.txt", times.ToString());
                File.WriteAllText(output + "/Trajectories.csv", csv.ToString());
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(stamp);
                Object.DestroyImmediate(sheet);
                RenderTexture.ReleaseTemporary(cameraTarget);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static void AppendBones(StringBuilder csv, CharacterCombat attacker, CharacterCombat actor,
            CombatTripletData move, string role, float seconds)
        {
            foreach (var bone in Bones)
            {
                var transform = actor.Animator.GetBoneTransform(bone);
                if (!transform)
                    continue;
                var p = transform.position;
                csv.AppendLine(FormattableString.Invariant(
                    $"{attacker.name},{move.moveName},{seconds:R},{role},{bone},{p.x:R},{p.y:R},{p.z:R}"));
            }
        }

        public static void MeasureAtemiContacts()
        {
            var scene = EditorSceneManager.OpenPreviewScene(CombatExpansionInventory.Battle);
            var csv = new StringBuilder("fighter,move,seconds,striker,anchor,gap,offsetX,offsetY,offsetZ\n");
            try
            {
                var game = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var fighter in fighters)
                {
                    fighter.battleSfx = null;
                    fighter.battleVfx = null;
                    fighter.hitEffect = null;
                }
                foreach (var source in fighters)
                foreach (int index in new[] { 3, 4, 5 })
                {
                    var target = fighters.Single(f => f != source);
                    foreach (var fighter in fighters)
                        fighter.ResetCombat();
                    var move = MakeMove(source, target, index);
                    source.Animator.transform.position = Vector3.zero;
                    target.Animator.transform.position = Vector3.right * move.attackRange;
                    if (!source.ExecuteAttack(move, target))
                        throw new InvalidOperationException("Measurement could not begin " + move.moveName);
                    var pair = source.SourcePlayback;
                    try
                    {
                        foreach (int frame in index == 5 ? new[] { 22, 24, 26 } : new[] { 29, 30, 31 })
                        foreach (string striker in new[] { "LeftHand", "RightHand" })
                        {
                            pair.EvaluateAt(frame / 60f);
                            BattlePresentationContactSetup.TryMeasureContact(pair, source, target, striker,
                                out var anchor, out var offset, out float gap, out _);
                            csv.Append(FormattableString.Invariant(
                                $"{source.name},{move.moveName},{frame / 60f:R},{striker},{anchor},{gap:R},"));
                            csv.AppendLine(FormattableString.Invariant($"{offset.x:R},{offset.y:R},{offset.z:R}"));
                        }
                    }
                    finally
                    {
                        pair.Cancel();
                    }
                }
            }
            finally
            {
                File.WriteAllText(CombatExpansionInventory.Output + "/Vol10Study/ContactGeometry.csv", csv.ToString());
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
