using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class BattleAttackReview
    {
        const string Output = BattleComboAuthoring.Report;
        static readonly HashSet<Material> CheckedMaterials = new HashSet<Material>();

        public static void Run(bool repair)
        {
            Directory.CreateDirectory(Output);
            CheckedMaterials.Clear();
            var report = new StringBuilder();
            var contactRows = new StringBuilder("attacker,move,lethal,mirrored,seconds,source,sourceGap,receiverGap\n");
            int cases = 0;
            int failures = 0;
            int materialSlots = 0;
            var changed = new HashSet<UnityEngine.Object>();
            var previous = CombatPositioningController.Instance;
            var property = typeof(CombatPositioningController).GetProperty("Instance");
            var scene = EditorSceneManager.OpenPreviewScene(BattleComboAuthoring.Battle);
            try
            {
                property.SetValue(null, null);
                var game = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                var vfx = game.battleVfx;
                foreach (var fighter in fighters)
                    fighter.Initialize();
                foreach (var source in fighters)
                foreach (var move in source.lightCombatMoves.Concat(source.heavyCombatMoves))
                foreach (bool lethal in new[] { false, true })
                foreach (bool mirrored in new[] { false, true })
                {
                    var target = fighters.Single(f => f != source);
                    string label = source.name + "/" + move.moveName + " lethal=" + lethal + " mirrored=" + mirrored;
                    var heard = new List<string>();
                    Action<string, GameObject> observe = (id, root) =>
                    {
                        heard.Add(id);
                        CheckMaterials(root.GetComponentsInChildren<Renderer>(true), ref materialSlots);
                        if (!float.IsFinite(root.transform.position.sqrMagnitude))
                            throw new InvalidOperationException("Non-finite VFX position.");
                    };
                    vfx.EffectPlayed += observe;
                    try
                    {
                        foreach (var fighter in fighters)
                            fighter.ResetCombat();
                        vfx.ResetForMatch();
                        source.transform.position = Vector3.zero;
                        target.transform.position = Vector3.right * move.attackRange * (mirrored ? -1 : 1);
                        if (!move.IsValid || !source.ExecuteAttack(move, target, lethal))
                            throw new InvalidOperationException("Attack was rejected.");
                        var playback = source.SourcePlayback;
                        var profile = playback.PresentationProfile(vfx.timeline);
                        if (profile?.cues == null || profile.cues.Length == 0)
                            throw new InvalidOperationException("Missing presentation timeline.");
                        CheckMaterials(playback.AttackerActor.Pose.weaponRenderers, ref materialSlots);
                        foreach (var cue in profile.cues)
                        {
                            if (cue == null || !float.IsFinite(cue.seconds) || cue.seconds < 0 ||
                                cue.seconds > playback.Duration)
                                throw new InvalidOperationException("Cue outside the source animation.");
                            playback.EvaluateAt(cue.seconds);
                            if (move.moveName.StartsWith("combo_", StringComparison.Ordinal) &&
                                (cue.group == "body_fall" || IsContact(cue)))
                            {
                                float floor = BattlePresentationContactSetup.EvaluatedFloor(target);
                                if (floor < -.025f)
                                    throw new InvalidOperationException("Receiver penetrates the floor: " + floor);
                            }
                            if (IsContact(cue) && cue.hasContactPoint)
                            {
                                var probe = new BattlePresentationContactSetup.ContactProbe(playback,
                                    source, target, cue.contactSource);
                                if (repair && !lethal && !mirrored)
                                {
                                    RepairAnchor(cue, probe, target);
                                    changed.Add(move.actionDefinition ? move.actionDefinition : vfx.timeline);
                                }
                                if (!cue.TryContactPosition(target.Animator, out var point))
                                    throw new InvalidOperationException("Contact anchor missing.");
                                float sourceGap = probe.SourceDistance(point);
                                float bodyGap = probe.BodyDistance(point);
                                contactRows.AppendLine($"{source.name},{move.moveName},{lethal},{mirrored}," +
                                    $"{cue.seconds:R},{cue.contactSource},{sourceGap:R},{bodyGap:R}");
                                if (sourceGap > .15f || bodyGap > .025f)
                                {
                                    report.AppendLine($"CONTACT_FAIL {label} {cue.seconds:R}s {cue.contactSource} " +
                                        $"source={sourceGap:R}m receiver={bodyGap:R}m");
                                    failures++;
                                }
                            }
                            vfx.AdvanceSequence(playback, cue.seconds);
                            int count = heard.Count;
                            vfx.AdvanceSequence(playback, cue.seconds);
                            vfx.AdvanceSequence(playback, Mathf.Max(0, cue.seconds - .1f));
                            if (heard.Count != count || Mathf.Abs(playback.SampleTime - cue.seconds) > .0001f)
                                throw new InvalidOperationException("Duplicate cue or changed animation clock.");
                        }
                        int hits = profile.cues.Count(IsContact);
                        int landings = profile.cues.Count(c => c.group == "body_fall" || c.group == "knockout_fall");
                        if (vfx.ContactCount != hits || heard.Count(id => id == "ground_impact") != landings ||
                            heard.Count(id => id == "gun_shot") != profile.cues.Count(c => c.group == "gun_shot"))
                            throw new InvalidOperationException("Missing hit, landing or gunshot VFX.");
                        if (source.name == "Mankey" && !lethal && !mirrored)
                            CaptureEffects(scene, playback, vfx, profile, move.moveName);
                        playback.Cancel();
                        if (vfx.ActiveEffectCount != 0)
                            throw new InvalidOperationException("VFX survived cancellation.");
                        report.AppendLine("PASS " + label + "; contacts=" + hits + "; effects=" + heard.Count);
                        cases++;
                    }
                    catch (Exception error)
                    {
                        report.AppendLine("FAIL " + label + ": " + error.Message);
                        failures++;
                    }
                    finally
                    {
                        source.SourcePlayback?.Cancel();
                        vfx.EffectPlayed -= observe;
                    }
                }
                foreach (var asset in changed)
                {
                    EditorUtility.SetDirty(asset);
                    AssetDatabase.SaveAssetIfDirty(asset);
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                property.SetValue(null, previous);
                report.AppendLine($"Cases={cases}; failures={failures}; supported material slots={materialSlots}.");
                File.WriteAllText(Output + (repair ? "/Repair.txt" : "/Validation.txt"), report.ToString());
                File.WriteAllText(Output + "/Contacts.csv", contactRows.ToString());
            }
            if (failures != 0)
                throw new InvalidOperationException("Attack presentation review found " + failures + " failures.");
        }

        static bool IsContact(BattleSfxBank.Cue cue) => cue != null &&
            (cue.group == "light_hit" || cue.group == "heavy_hit" || cue.group == "stab_hit");

        static void RepairAnchor(BattleSfxBank.Cue cue, BattlePresentationContactSetup.ContactProbe probe,
            CharacterCombat target)
        {
            if (cue.contactSource == "Gun")
                return;
            float gap = probe.Measure(out _, out var bone, out var offset);
            if (gap > .15f)
                return;
            var previous = cue.avatarContacts ?? Array.Empty<BattleSfxBank.ContactAnchor>();
            cue.avatarContacts = previous.Where(c => c.avatar != target.Animator.avatar)
                .Append(new BattleSfxBank.ContactAnchor
                    { avatar = target.Animator.avatar, bone = bone, offset = offset })
                .ToArray();
        }

        internal static void CheckMaterials(IEnumerable<Renderer> renderers, ref int slots)
        {
            foreach (var renderer in renderers)
            {
                if (!renderer)
                    throw new InvalidOperationException("Missing weapon/VFX renderer.");
                foreach (var material in renderer.sharedMaterials)
                {
                    if (!material || !material.shader || !material.shader.isSupported ||
                        !CheckedMaterials.Contains(material) && ShaderUtil.ShaderHasError(material.shader))
                        throw new InvalidOperationException("Missing or unsupported material on " + renderer.name);
                    CheckedMaterials.Add(material);
                    slots++;
                }
            }
        }
    }
}
