using System;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class BattleComboAuthoring
    {
        public static void Study()
        {
            Directory.CreateDirectory(Report);
            var csv = new StringBuilder("combo,seconds,sword,leftFoot,rightFoot,leftHand,rightHand,floor,gunTurn\n");
            WithStudy((scene, game, fighters) =>
            {
                foreach (var fighter in fighters)
                {
                    var target = fighters.Single(f => f != fighter);
                    fighter.heavyCombatMoves = fighter.heavyCombatMoves.Concat(Library.pairs.Where(p => p.step == 0)
                        .Select(p => MakeMove(fighter, target, p))).ToArray();
                }
                RepairWeaponMaterials(game);
                var source = fighters.Single(f => f.name == "Mankey");
                var victim = fighters.Single(f => f != source);
                foreach (var data in Library.pairs.Where(p => p.step == 0))
                {
                    foreach (var fighter in fighters)
                        fighter.ResetCombat();
                    var move = MakeMove(source, victim, data);
                    source.transform.position = Vector3.zero;
                    victim.transform.position = Vector3.right * move.attackRange;
                    if (!source.ExecuteAttack(move, victim))
                        throw new InvalidOperationException("Cannot inspect " + move.moveName);
                    var playback = source.SourcePlayback;
                    try
                    {
                        var sword = playback.AttackerActor.Pose.weaponRenderers.Single(r => r.name == "WP_Sword");
                        var gun = playback.AttackerActor.Pose.weaponRenderers.Single(r => r.name == "WP_gun");
                        var mesh = sword.GetComponent<MeshFilter>().sharedMesh;
                        Vector3[] points;
                        using (var view = MeshUtility.AcquireReadOnlyMeshData(mesh))
                        using (var vertices = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp))
                        {
                            view[0].GetVertices(vertices);
                            points = vertices.ToArray();
                        }
                        Quaternion previousGun = gun.transform.rotation;
                        for (int frame = 0; frame <= Mathf.CeilToInt(playback.Duration * 60); frame++)
                        {
                            float time = Mathf.Min(frame / 60f, playback.Duration);
                            playback.EvaluateAt(time);
                            var body = new BattlePresentationContactSetup.ContactProbe(playback, source, victim, "Gun");
                            float bladeGap = points.Min(p => body.BodyDistance(sword.transform.TransformPoint(p)));
                            var distances = new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
                                HumanBodyBones.LeftHand, HumanBodyBones.RightHand }
                                .Select(b => body.BodyDistance(source.Animator.GetBoneTransform(b).position)).ToArray();
                            float turn = Quaternion.Angle(previousGun, gun.transform.rotation);
                            previousGun = gun.transform.rotation;
                            csv.AppendLine($"{data.sourceName},{time:R},{bladeGap:R},{distances[0]:R}," +
                                $"{distances[1]:R},{distances[2]:R},{distances[3]:R},{body.FloorHeight:R},{turn:R}");
                        }
                        BattleAttackReview.Capture(scene, playback,
                            Enumerable.Range(0, 40).Select(i => playback.Duration * i / 39).ToArray(),
                            Report + "/" + data.sourceName + "_study.png");
                    }
                    finally
                    {
                        playback.Cancel();
                    }
                    File.WriteAllText(Report + "/ComboStudy.csv", csv.ToString());
                }
            });
        }
    }
}
