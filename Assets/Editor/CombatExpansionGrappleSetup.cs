using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    public static class CombatExpansionGrappleSetup
    {
        const string Folder = "Assets/CombatExpansion/Actions";
        const float ThrowContact = .85f;
        const float Landing = 1.1f;

        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before installing grapples.");
            var scene = SceneManager.GetSceneByPath(CombatExpansionInventory.Battle);
            if (!scene.IsValid() || !scene.isLoaded || scene.isDirty)
                throw new InvalidOperationException("Open the clean saved BattleScene first.");
            var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
            var library = AssetDatabase.LoadAssetAtPath<FrankUnarmedLibrary>("Assets/DemoSence/Vol10/UnarmedLibrary.asset");
            var definition = Asset<FrankGrappleDefinition>(Folder + "/Vol10_Grapple.asset");
            definition.escapeWindowSeconds = new Vector2(.15f, .55f);
            definition.decisionSeconds = .65f;
            definition.blendSeconds = .12f;
            definition.entryBlendSeconds = .12f;
            var grip = Cue(.1f, "grapple_grip");
            definition.releasePresentation = Profile("Vol10_HOLD", library.pairs[8].attacker,
                library.pairs[8].receiver, grip, Cue(.9f, "grapple_release"));
            var hit = Cue(definition.decisionSeconds + ThrowContact, "heavy_hit");
            var getUp = game.leftCombat.lightCombatMoves.First(m => m.sourcePair?.getUp).sourcePair.getUp;
            definition.throwing = new FrankGrappleDefinition.Continuation
            {
                attack = library.pairs[9].attacker,
                reaction = library.pairs[9].receiver,
                getUp = getUp,
                unarmedIndex = 9,
                presentation = Profile("Vol10_HOLD_LALI", library.pairs[9].attacker, library.pairs[9].receiver,
                    grip, Cue(definition.decisionSeconds + .65f, "heavy_swing"), hit,
                    new BattleSfxBank.Cue
                    {
                        seconds = definition.decisionSeconds + Landing, group = "body_fall",
                        finalLanding = true, damageOnLanding = true
                    })
            };
            definition.escaping = new FrankGrappleDefinition.Continuation
            {
                attack = library.pairs[12].attacker,
                reaction = library.pairs[12].receiver,
                unarmedIndex = 12,
                presentation = Profile("Vol10_NAGE_ESC", library.pairs[12].attacker, library.pairs[12].receiver,
                    grip, Cue(definition.decisionSeconds + .12f, "grapple_break"))
            };
            hit = MeasureContact(false, definition);
            hit.seconds += definition.decisionSeconds;
            definition.throwing.presentation.cues[2] = hit;
            var bank = game.battleSfx.bank;
            Undo.RecordObject(bank, "Configure grapple audio");
            var groups = bank.groups.ToList();
            foreach (var id in new[] { "grapple_grip", "grapple_release", "grapple_break" })
            {
                var original = bank.FindGroup(id == "grapple_grip" ? "getup" : "light_swing");
                if (original?.clips == null || original.clips.Length == 0 || !original.output)
                    throw new InvalidOperationException("Missing existing grapple audio source.");
                groups.RemoveAll(g => g.id == id);
                groups.Add(new BattleSfxBank.Group
                {
                    id = id, clips = original.clips, clipGains = original.clipGains,
                    startOffsets = original.startOffsets, output = original.output,
                    volume = original.volume * .6f, pitch = original.pitch, maxConcurrent = 2, priority = 100
                });
            }
            bank.groups = groups.ToArray();
            foreach (var source in new[] { game.leftCombat, game.rightCombat })
            {
                var target = source == game.leftCombat ? game.rightCombat : game.leftCombat;
                Undo.RecordObject(source, "Register grapple actions");
                var moves = source.lightCombatMoves.ToList();
                foreach (FrankGrappleOutcome outcome in Enum.GetValues(typeof(FrankGrappleOutcome)))
                {
                    var move = CombatExpansionVol10Study.MakeMove(source, target, 8);
                    move.moveName = definition.Presentation(outcome).label;
                    move.grapple = definition;
                    move.grappleOutcome = outcome;
                    move.requiresExplicitSelection = true;
                    move.actionDefinition = Action(move);
                    var errors = move.actionDefinition.Validate(move, bank).ToArray();
                    if (errors.Length != 0) throw new InvalidOperationException(string.Join("\n", errors));
                    moves.RemoveAll(m => m.moveName == move.moveName);
                    moves.Add(move);
                }
                source.lightCombatMoves = moves.ToArray();
                EditorUtility.SetDirty(source);
            }
            EditorUtility.SetDirty(definition);
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssetIfDirty(definition);
            AssetDatabase.SaveAssetIfDirty(bank);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static void ScanContacts() => MeasureContact(true);

        static BattleSfxBank.Cue MeasureContact(bool scan = false, FrankGrappleDefinition definition = null)
        {
            var scene = EditorSceneManager.OpenPreviewScene(CombatExpansionInventory.Battle);
            var result = Cue(ThrowContact, "heavy_hit");
            result.hasContactPoint = true;
            result.contactSource = "LeftLowerArm";
            var anchors = new List<BattleSfxBank.ContactAnchor>();
            var report = new StringBuilder("fighter,sourceSeconds,striker,anchor,gapMetres\n");
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                foreach (var source in new[] { game.leftCombat, game.rightCombat })
                {
                    var target = source == game.leftCombat ? game.rightCombat : game.leftCombat;
                    source.battleSfx = target.battleSfx = null;
                    source.battleVfx = target.battleVfx = null;
                    source.hitEffect = target.hitEffect = null;
                    source.ResetCombat();
                    target.ResetCombat();
                    var move = CombatExpansionVol10Study.MakeMove(source, target, definition ? 8 : 9);
                    if (definition)
                    {
                        move.grapple = definition;
                        move.grappleOutcome = FrankGrappleOutcome.Throw;
                    }
                    source.Animator.transform.position = Vector3.zero;
                    target.Animator.transform.position = Vector3.right * move.attackRange;
                    if (!source.ExecuteAttack(move, target)) throw new InvalidOperationException("Throw study failed.");
                    if (scan)
                    {
                        for (int frame = 36; frame <= 60; frame += 3)
                        foreach (var striker in new[] { "LeftLowerArm", "RightLowerArm" })
                        {
                            source.SourcePlayback.EvaluateAt(frame / 60f);
                            BattlePresentationContactSetup.TryMeasureContact(source.SourcePlayback, source, target,
                                striker, out var sampledBone, out _, out float sampledGap, out _);
                            report.AppendLine(FormattableString.Invariant(
                                $"{source.name},{frame / 60f:R},{striker},{sampledBone},{sampledGap:R}"));
                        }
                        source.SourcePlayback.Cancel();
                        continue;
                    }
                    source.SourcePlayback.EvaluateAt(ThrowContact + (definition ? definition.decisionSeconds : 0));
                    if (!BattlePresentationContactSetup.TryMeasureContact(source.SourcePlayback, source, target,
                        result.contactSource, out var bone, out var offset, out float gap, out _))
                        throw new InvalidOperationException(source.name + " throw contact gap " + gap);
                    anchors.Add(new BattleSfxBank.ContactAnchor { avatar = target.Animator.avatar, bone = bone, offset = offset });
                    result.contactBone = bone;
                    result.contactOffset = offset;
                    report.AppendLine(FormattableString.Invariant($"{source.name},{ThrowContact:R},{result.contactSource},{bone},{gap:R}"));
                    source.SourcePlayback.Cancel();
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            result.avatarContacts = anchors.ToArray();
            File.WriteAllText(CombatExpansionInventory.Output +
                (scan ? "/GrappleContactScan.csv" : "/GrappleContactCalibration.csv"), report.ToString());
            return result;
        }

        static CombatActionDefinition Action(CombatTripletData move)
        {
            var result = Asset<CombatActionDefinition>(Folder + "/" + move.moveName + ".asset");
            result.actionId = move.moveName;
            result.displayName = move.grappleOutcome == FrankGrappleOutcome.Throw ? "Hold to lariat" :
                move.grappleOutcome == FrankGrappleOutcome.Release ? "Hold and release" : "Resolved throw escape";
            result.gameplayTrigger = "Exact action registry ID; R/T starts a local left/right hold-to-lariat. " +
                "Defender Q/E or RequestThrowEscape(side, playbackId) escapes between 0.15 and 0.55 pair seconds. " +
                "Server-resolved exchanges select their outcome by ID and never accept a local override.";
            result.entryAndInterruption = "Living available grounded actors in range; existing approach; shared clock " +
                "and equipment leases. Reset, disable, unexpected death and cancellation release both participants.";
            result.recovery = "Release/escape separate into idle; the lariat's surviving victim uses existing GetUp. " +
                "No new armor, invulnerability or instant-kill rule.";
            result.presentation = "Restrained grip/break movement dust and routed cloth/whoosh audio; no damage, " +
                "hurt flash or hit-stop on a grip/break. Lariat uses heavy contact and damaging landing feedback. " +
                "Shared lighting, camera and presentation cleanup remain active.";
            var phase = move.grapple.For(move.grappleOutcome);
            result.contacts = move.grapple.Presentation(move.grappleOutcome).cues
                .Where(c => c.hasContactPoint || c.damageOnLanding || c.group.StartsWith("grapple_"))
                .Select(c => new CombatActionDefinition.Contact
                {
                    strikeId = move.moveName + "/" + c.group,
                    attack = c.seconds < move.grapple.decisionSeconds ? move.attackAnim : phase?.attack ?? move.attackAnim,
                    reaction = c.seconds < move.grapple.decisionSeconds ? move.hitAnim : phase?.reaction ?? move.hitAnim,
                    seconds = c.seconds, activeWindowSeconds = new Vector2(c.seconds - 1f / 60, c.seconds + 1f / 60),
                    strikingLimb = c.damageOnLanding ? HumanBodyBones.Hips : HumanBodyBones.LeftLowerArm,
                    targetRegion = c.damageOnLanding ? "Ground landing" : c.hasContactPoint ?
                        "Raised arm and shoulder during the lariat" : "Paired grip and grounded foot support",
                    directionInVictimSpace = c.damageOnLanding ? Vector3.down : Vector3.back,
                    reactionPolicy = CombatActionDefinition.ReactionPolicy.AuthoredPairContinuation,
                    result = c.group == "grapple_grip" ? CombatActionDefinition.Result.Hold :
                        c.group == "grapple_break" ? CombatActionDefinition.Result.Escape :
                        c.group == "grapple_release" ? CombatActionDefinition.Result.Release : CombatActionDefinition.Result.Knockdown,
                    nonDamagingInteraction = c.group.StartsWith("grapple_"), presentationGroup = c.group,
                    continuation = "Both roles retain one clock and playback identity. The selected source pair " +
                        "continues through release, escape or lariat landing without restarting the victim reaction."
                }).ToArray();
            EditorUtility.SetDirty(result);
            AssetDatabase.SaveAssetIfDirty(result);
            return result;
        }

        static BattleSfxBank.Cue Cue(float seconds, string group) => new BattleSfxBank.Cue { seconds = seconds, group = group };
        static BattleSfxBank.Move Profile(string id, AnimationClip attack, AnimationClip reaction,
            params BattleSfxBank.Cue[] cues) => new BattleSfxBank.Move { label = id, attack = attack, reaction = reaction, cues = cues };

        static T Asset<T>(string path) where T : ScriptableObject
        {
            var value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (value) return value;
            value = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(value, path);
            return value;
        }
    }
}
