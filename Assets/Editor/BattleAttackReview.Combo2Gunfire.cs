using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class BattleAttackReview
    {
        public static void ValidateCombo2Gunfire()
        {
            var report = new StringBuilder();
            int cases = 0;
            BattleComboAuthoring.WithStudy((scene, game, fighters) =>
            {
                var vfx = game.battleVfx;
                foreach (var fighter in fighters)
                    fighter.battleVfx = vfx;
                foreach (var source in fighters)
                foreach (int direction in new[] { 1, -1 })
                foreach (bool lethal in new[] { false, true })
                foreach (bool skip in new[] { false, true })
                {
                    foreach (var fighter in fighters)
                        fighter.ResetCombat();
                    vfx.ResetForMatch();
                    var target = fighters.Single(f => f != source);
                    var move = source.heavyCombatMoves.Single(m => m.moveName == "combo_02");
                    source.transform.position = Vector3.zero;
                    target.transform.position = Vector3.right * direction * move.attackRange;
                    var events = new List<(string id, float time, Vector3 position)>();
                    Action<string, GameObject> observe = (id, root) =>
                    {
                        float time = source.SourcePlayback.SampleTime;
                        events.Add((id, time, root.transform.position));
                        if (id != "gun_shot")
                            return;
                        var gun = source.SourcePlayback.AttackerActor.Pose.weaponRenderers
                            .Single(r => r.name == "WP_gun");
                        var bounds = gun.GetComponent<MeshFilter>().sharedMesh.bounds;
                        bounds.Expand(.002f);
                        if (!bounds.Contains(gun.transform.InverseTransformPoint(root.transform.position)))
                            throw new InvalidOperationException("Gun muzzle detached from the native weapon.");
                        var particles = root.GetComponentsInChildren<ParticleSystem>(true);
                        foreach (var system in particles)
                            system.Simulate(.03f, false, true, true);
                        if (!particles.Any(p => p.particleCount > 0))
                            throw new InvalidOperationException("Gun muzzle spawned without visible particles.");
                    };
                    vfx.EffectPlayed += observe;
                    try
                    {
                        if (!source.ExecuteAttack(move, target, lethal))
                            throw new InvalidOperationException("Combo_02 gunfire attack rejected.");
                        var playback = source.SourcePlayback;
                        var profile = playback.PresentationProfile(vfx.timeline);
                        // Expected shots come from native animation frames, independently of the cue count.
                        var shotTimes = new[] { 12f / 30f, 17f / 30f, 91f / 30f };
                        if (profile.cues.Count(c => c.group == "gun_shot") != 3 ||
                            profile.cues.Count(IsContact) != 7)
                            throw new InvalidOperationException("Combo_02 must have three gunshots and seven hits.");
                        foreach (float time in shotTimes)
                        {
                            var shot = profile.cues.Single(c => c.group == "gun_shot" &&
                                Mathf.Abs(c.seconds - time) < .0001f);
                            var hit = profile.cues.Single(c => c.group == "heavy_hit" &&
                                c.contactSource == "Gun" && Mathf.Abs(c.seconds - time) < .0001f);
                            playback.EvaluateAt(time);
                            if (!shot.TryContactPosition(target.Animator, out _) ||
                                !hit.TryContactPosition(target.Animator, out var point))
                                throw new InvalidOperationException("Gunshot receiver anchor is missing.");
                            var probe = new BattlePresentationContactSetup.ContactProbe(playback,
                                source, target, "Gun");
                            if (probe.BodyDistance(point) > .025f)
                                throw new InvalidOperationException("Gun hit detached from the receiver skin.");
                        }
                        var times = skip ? new[] { playback.Duration } :
                            profile.cues.Select(c => c.seconds).Distinct().ToArray();
                        foreach (float time in times)
                        {
                            playback.EvaluateAt(time);
                            vfx.AdvanceSequence(playback, time);
                            if (Mathf.Abs(playback.SampleTime - time) > .0001f)
                                throw new InvalidOperationException("Gun VFX changed the animation clock.");
                        }
                        foreach (float time in shotTimes)
                        {
                            int flashes = events.Count(e => e.id == "gun_shot" && Mathf.Abs(e.time - time) < .0001f);
                            var hits = events.Where(e => e.id == "heavy_hit" &&
                                Mathf.Abs(e.time - time) < .0001f).ToArray();
                            if (flashes != 1 || hits.Length != 1)
                                throw new InvalidOperationException(
                                    "A native gunshot lost or duplicated muzzle/hit VFX.");
                            playback.EvaluateAt(time);
                            var cue = profile.cues.Single(c => c.group == "heavy_hit" &&
                                Mathf.Abs(c.seconds - time) < .0001f);
                            cue.TryContactPosition(target.Animator, out var point);
                            // Authored impacts move 15 mm toward the camera to avoid intersecting the skin.
                            if (Vector3.Distance(hits[0].position, point) > .016f)
                                throw new InvalidOperationException(
                                    "Gun hit spawned away from its sampled receiver anchor.");
                        }
                        playback.EvaluateAt(playback.Duration);
                        int count = events.Count;
                        vfx.AdvanceSequence(playback, playback.Duration);
                        vfx.AdvanceSequence(playback, .2f);
                        if (events.Count != 17 || count != events.Count)
                            throw new InvalidOperationException("Unexpected or repeated combo_02 VFX.");
                        if (source.name == "Mankey" && direction == 1 && !lethal && !skip)
                            CaptureEffects(scene, playback, vfx, profile, "combo_02");
                        playback.Cancel();
                        if (vfx.ActiveEffectCount != 0)
                            throw new InvalidOperationException("Cancelled combo_02 retained gunfire VFX.");
                        report.AppendLine($"PASS {source.name} direction={direction} lethal={lethal} skip={skip}: " +
                            "three visible muzzle flashes, three matched gun hits, seven hits total.");
                        cases++;
                    }
                    finally
                    {
                        source.SourcePlayback?.Cancel();
                        vfx.EffectPlayed -= observe;
                    }
                }
            });
            report.AppendLine($"PASS {cases} combo_02 cases: native frames 12, 17, 91; no duplicate or lost effects.");
            File.WriteAllText(Output + "/Combo2Gunfire.txt", report.ToString());
        }
    }
}
