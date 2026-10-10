using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FrankRetarget;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class BattleComboAuthoring
    {
        // Source seconds are refined against visible skin and native weapon geometry below.
        static readonly float[][] StrikeTimes =
        {
            new[] { .3f, .85f, 1.6333333f },
            new[] { 12f / 30f, 17f / 30f, 1.0666667f, 1.3666667f, 1.6666667f, 1.8166667f, 3.0333333f },
            new[] { .3833333f, 1.0333333f, 1.2333333f, 1.75f, 2.3833333f }
        };

        static readonly string[][] StrikeSources =
        {
            new[] { "Weapon", "Weapon", "Gun" },
            new[] { "Gun", "Gun", "Weapon", "Weapon", "LeftFoot", "Weapon", "Gun" },
            new[] { "Weapon", "Gun", "Gun", "Weapon", "Gun" }
        };

        internal static BattleSfxBank.Move[] MeasureProfiles()
        {
            var profiles = new List<BattleSfxBank.Move>();
            var report = new StringBuilder("attacker,combo,source,seconds,gap,bone,offset\n");
            WithStudy((scene, game, fighters) =>
            {
                foreach (var source in fighters)
                foreach (var data in Library.pairs.Where(p => p.step == 0))
                {
                    foreach (var fighter in fighters)
                        fighter.ResetCombat();
                    var target = fighters.Single(f => f != source);
                    var move = MakeMove(source, target, data);
                    source.transform.position = Vector3.zero;
                    target.transform.position = Vector3.right * move.attackRange;
                    if (!source.ExecuteAttack(move, target))
                        throw new InvalidOperationException("Cannot measure " + move.moveName);
                    var playback = source.SourcePlayback;
                    var cues = new List<BattleSfxBank.Cue>();
                    try
                    {
                        for (int index = 0; index < StrikeTimes[data.combo - 1].Length; index++)
                        {
                            string selection = StrikeSources[data.combo - 1][index];
                            float seconds = StrikeTimes[data.combo - 1][index];
                            float best = float.PositiveInfinity;
                            Vector3 contact = Vector3.zero;
                            HumanBodyBones bone = HumanBodyBones.Chest;
                            Vector3 offset = Vector3.zero;
                            if (selection != "Gun")
                            {
                                for (int frame = -12; frame <= 12; frame++)
                                {
                                    float time = Mathf.Clamp(StrikeTimes[data.combo - 1][index] + frame / 120f,
                                        .08f, playback.Duration);
                                    playback.EvaluateAt(time);
                                    var probe = new BattlePresentationContactSetup.ContactProbe(playback,
                                        source, target, selection);
                                    float gap = probe.MeasureQuick(out var point, out var anchor, out var local);
                                    if (gap >= best)
                                        continue;
                                    seconds = time;
                                    best = gap;
                                    contact = point;
                                    bone = anchor;
                                    offset = local;
                                }
                            }
                            else
                            {
                                playback.EvaluateAt(seconds);
                                var probe = new BattlePresentationContactSetup.ContactProbe(playback,
                                    source, target, "Gun");
                                var chest = target.Animator.GetBoneTransform(bone);
                                contact = probe.BodyPoint(chest.position);
                                offset = chest.InverseTransformPoint(contact);
                                best = 0;
                            }
                            var hit = new BattleSfxBank.Cue
                            {
                                seconds = seconds,
                                group = data.combo == 1 && index == 0 ? "stab_hit" :
                                    selection.EndsWith("Foot", StringComparison.Ordinal) ? "light_hit" : "heavy_hit",
                                hasContactPoint = true,
                                contactSource = selection,
                                contactBone = bone,
                                contactOffset = offset,
                                avatarContacts = new[] { new BattleSfxBank.ContactAnchor
                                    { avatar = target.Animator.avatar, bone = bone, offset = offset } }
                            };
                            cues.Add(hit);
                            cues.Add(new BattleSfxBank.Cue
                            {
                                seconds = selection == "Gun" ? seconds : Mathf.Max(.08f, seconds - .1f),
                                group = data.combo == 1 && index == 0 ? "thrust_swing" :
                                    selection == "Gun" ? "gun_shot" :
                                    selection == "Weapon" ? "blade_swing" : "light_swing",
                                contactSource = selection,
                                contactBone = bone,
                                contactOffset = offset,
                                hasContactPoint = selection == "Gun"
                            });
                            report.AppendLine($"{source.name},{data.sourceName},{selection},{seconds:R}," +
                                $"{best:R},{bone},{offset.ToString("R")}");
                        }
                        float landing = data.combo == 1 ? 1.9166667f : data.combo == 2 ? 4.1333333f : 3.0666667f;
                        cues.Add(new BattleSfxBank.Cue { seconds = landing, group = "body_fall", finalLanding = true });
                        profiles.Add(new BattleSfxBank.Move
                        {
                            label = data.sourceName,
                            fighter = source.name == "Pepe" ? BattleSfxBank.Fighter.Pepe : BattleSfxBank.Fighter.Mankey,
                            attack = data.attack,
                            reaction = data.reaction,
                            cues = cues.OrderBy(c => c.seconds).ThenBy(c => c.group.EndsWith("hit") ? 1 : 0).ToArray()
                        });
                    }
                    finally
                    {
                        playback.Cancel();
                    }
                }
            });
            File.WriteAllText(Report + "/ComboContacts.csv", report.ToString());
            return profiles.ToArray();
        }

        internal static void WithStudy(Action<UnityEngine.SceneManagement.Scene, GameManager,
            CharacterCombat[]> action)
        {
            var previous = CombatPositioningController.Instance;
            var property = typeof(CombatPositioningController).GetProperty("Instance");
            var scene = EditorSceneManager.OpenPreviewScene(Battle);
            try
            {
                property.SetValue(null, null);
                var game = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var fighter in fighters)
                {
                    fighter.battleSfx = null;
                    fighter.battleVfx = null;
                    fighter.hitEffect = null;
                    fighter.Initialize();
                }
                action(scene, game, fighters);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                property.SetValue(null, previous);
            }
        }
    }
}
