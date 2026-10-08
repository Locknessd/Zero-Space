using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionKbComboStudy
    {
        const string Output = "GeneratedAssets/CombatExpansion/KbComboStudy/FirstCombo";
        const string Scope = "PROVISIONAL paired candidates only; no accepted impacts, links, contact or true-combo " +
            "claim; no registration, feedback, damage, knockdown or lethal hold. Source review: " +
            "Library/CombatExpansionTools/kb-first-combo.md. Contacts 0.1,0.525,1.51 are candidate reaction " +
            "onsets on the pair clock. Original visible BattleScene rigs and lighting; CPU preview skins, " +
            "neutral floor, source-native drivers. Preview scenes preserve live dirty scenes and singleton references. " +
            "120Hz plus exact endpoints and seam samples. Native equivalence compares local human rotations " +
            "outside blends against an independent single-clip graph; horizontal hips carry is independently " +
            "calculated at source links and subtracted from world positions. Terminal means last reaction only.";

        [Serializable]
        sealed class Report
        {
            public string status = "RUNNING", scope = Scope, utc = DateTime.UtcNow.ToString("O");
            public string unityVersion = Application.unityVersion;
            public SourceRecord[] sources;
            public List<CandidateRecord> candidates = new List<CandidateRecord>();
        }

        [Serializable]
        sealed class CandidateRecord
        {
            public string status = "PROVISIONAL", name, attacker, receiver, attackerDriver, receiverDriver;
            public string trajectory, seams, nativeEquivalence;
            public int laneSign, sampleCount, backwardsSeekSamples, nativeSamples;
            public float spacing, duration, entryBlend = .12f, maximumAlignmentError = .15f, receiverYaw = 180;
            public float worstBackwardsSeekMetres, worstBackwardsSeekDegrees;
            public float worstNativePositionMetres, worstNativeRotationDegrees;
            public Vector3[] carriedHorizontalHips;
            public float[] sheetSeconds;
            public string[] sheets;
            public TrackRecord[] attacks, reactions;
        }

        [Serializable]
        sealed class TrackRecord
        {
            public string id, guid;
            public long localId;
            public float seconds, sourceStartSeconds, sourceEndSeconds, blendSeconds, candidateContactSeconds;
            public bool terminal;
            public float lethalHoldSeconds = -1;
        }

        public static void CaptureFirstComboCandidates()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            Directory.CreateDirectory(Output);
            var report = new Report();
            File.WriteAllText(Output + "/Status.txt", "RUNNING 0/12 PROVISIONAL\n");
            File.WriteAllText(Output + "/Scope.txt", Scope + "\n");
            try
            {
                report.sources = ResolveSources();
                for (int assignment = 0; assignment < 2; assignment++)
                foreach (int lane in new[] { 1, -1 })
                foreach (float spacing in new[] { .65f, .8f, .95f })
                {
                    using var session = new SourceSession();
                    typeof(CombatPositioningController).GetProperty("Instance",
                        BindingFlags.Public | BindingFlags.Static).SetValue(null, null);
                    foreach (var fighter in session.Fighters)
                    {
                        fighter.battleSfx = null;
                        fighter.battleVfx = null;
                        fighter.hitEffect = null;
                        if (!fighter.Initialize())
                            throw new InvalidOperationException("Cannot initialize " + fighter.name);
                    }
                    report.candidates.Add(CaptureCandidate(session.Fighters, report.sources, assignment, lane, spacing));
                    RequireSourcesUnchanged(report.sources);
                    File.WriteAllText(Output + "/Study.json", JsonUtility.ToJson(report, true));
                    File.WriteAllText(Output + "/Status.txt", "RUNNING " + report.candidates.Count + "/12\n");
                }
                if (report.candidates.Count != 12)
                    throw new InvalidOperationException("Incomplete candidate grid.");
                report.status = "CAPTURED_PROVISIONAL";
                File.WriteAllText(Output + "/Study.json", JsonUtility.ToJson(report, true));
                File.WriteAllText(Output + "/Status.txt", "CAPTURED 12/12 PROVISIONAL; no accepted timings or links.\n");
            }
            catch (Exception error)
            {
                report.status = "FAILED";
                File.WriteAllText(Output + "/Study.json", JsonUtility.ToJson(report, true));
                File.WriteAllText(Output + "/Status.txt", "FAILED " + error + "\nPartial evidence is provisional.\n");
                throw;
            }
        }

        sealed class Tracks : IDisposable
        {
            public readonly FrankAttackTrack attacks = ScriptableObject.CreateInstance<FrankAttackTrack>();
            public readonly FrankReactionTrack reactions = ScriptableObject.CreateInstance<FrankReactionTrack>();
            public Tracks(SourceRecord[] sources)
            {
                attacks.hideFlags = HideFlags.HideAndDontSave;
                reactions.hideFlags = HideFlags.HideAndDontSave;
                attacks.steps = Enumerable.Range(0, 3).Select(i => new FrankAttackTrack.Step
                {
                    stepId = "strike" + (i + 1), clip = sources[i].clip, seconds = i * .4f,
                    sourceEndSeconds = i == 2 ? sources[i].length : .6f, blendSeconds = i == 0 ? 0 : .08f
                }).ToArray();
                reactions.segments = Enumerable.Range(0, 3).Select(i => new FrankReactionTrack.Segment
                {
                    strikeId = "strike" + (i + 1), clip = sources[i == 2 ? 4 : 3].clip,
                    seconds = new[] { .1f, .525f, 1.51f }[i], blendSeconds = .035f,
                    terminal = i == 2, lethalHoldSeconds = -1
                }).ToArray();
            }
            public void Dispose()
            {
                Object.DestroyImmediate(attacks);
                Object.DestroyImmediate(reactions);
            }
        }

        static CombatTripletData Move(CharacterCombat source, CharacterCombat target, SourceRecord[] sources,
            Tracks tracks, float spacing)
        {
            if (!tracks.attacks.TryValidate(out var attackError) ||
                !tracks.reactions.TryValidate(out var reactionError))
                throw new InvalidOperationException("Invalid provisional track: " + attackError);
            return new CombatTripletData
            {
                moveName = "KB_FirstCombo_PROVISIONAL", attackAnim = sources[0].clip, hitAnim = sources[3].clip,
                attackRange = spacing, requiresExplicitSelection = true,
                sourcePair = new FrankBattlePair
                {
                    attack = sources[0].clip, reaction = sources[3].clip,
                    attacks = tracks.attacks, reactions = tracks.reactions,
                    attackerDriver = Driver(source.name, sources.Take(3).ToArray()),
                    receiverDriver = Driver(target.name, sources.Skip(3).ToArray()),
                    receiverOffset = Vector3.forward * spacing, receiverRotation = Quaternion.Euler(0, 180, 0),
                    maximumAlignmentError = .15f, entryBlendSeconds = .12f, showWeapon = false
                }
            };
        }
    }
}
