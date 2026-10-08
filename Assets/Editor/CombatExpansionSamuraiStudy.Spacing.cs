using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        public static void CaptureExecution01SpacingCandidates()
        {
            const string output = BladeOutput + "/SpacingCandidates";
            Directory.CreateDirectory(output);
            string status = output + "/Status.txt";
            string scopePath = output + "/Scope.txt";
            var scope = new List<string>
            {
                "Grounded Execution01 constant entry spacing adaptation candidates; Mankey attacks Pepe only.",
                "Eight cases: spacings 1.70, 1.66, 1.64 and 1.62 metres, each in both lane directions.",
                "The 1.70m case repeats native entry spacing as the grounded comparison baseline.",
                "Both assignments were measured separately in ../Execution01Grounded; " +
                    "Pepe-to-Mankey baseline is already measured there and is not repeated in this sweep.",
                "Motivation: the prior grounded Mankey-to-Pepe second slash missed by about 0.0506m at 1.70m.",
                "Only initial actor positions, source pair receiver offset and attack range use the candidate spacing.",
                "No mid-action translation, retiming or source clip modification is applied.",
                "Existing ExecuteAttack and FrankBattlePairPlayback evaluate the grounded source pair.",
                "PlayerA attacker retains the native sword; PlayerB victim has no weapon renderers.",
                "Measured BladeR mesh=3e685dd57e9c78b49b20bef3e8358ae3:4300002; " +
                    "111 faces with all three local Z coordinates <= -0.33; guard, grip and sheath excluded.",
                "Whole source pair sampled at 30Hz; grounded candidate windows [0.35,1.05] " +
                    "and [1.75,2.35] sampled at 240Hz.",
                "Current victim skin and triangle BVH are rebuilt at each source-clock evaluation.",
                "Unsigned triangle gaps report touch or intersection, not penetration depth or anatomical approval.",
                "Nearest bone labels describe pivot proximity only.",
                "Dual-view sheets show a cyan blade overlay; measurements use original unoffset vertices.",
                "Backwards-seek gaps and blade tips are checked against recorded samples.",
                "These are unaccepted candidates: no accepted contacts, damage, feedback or runtime registration.",
                "GroundingAsset=" + Execution01GroundingPath
            };
            File.WriteAllText(status, "RUNNING grounded Execution01 spacing candidates; completed pairs=0/8.\n");
            try
            {
                File.WriteAllLines(scopePath, scope);
                CombatExpansionHumanoidStudy.RequireEditor();
                var grounding = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(Execution01GroundingPath);
                if (!grounding)
                    throw new InvalidOperationException("Missing required Execution01 grounding: " +
                        Execution01GroundingPath);
                scope.Add("Grounding=" + CombatExpansionInventory.Identity(grounding));
                foreach (var clip in ResolveSources().Where(clip => clip.localId == 7400002))
                    scope.Add("BaselineSource " + clip.role + "=" + CombatExpansionInventory.Identity(clip.asset) +
                        "; name=" + clip.clip + "; path=" + clip.path);
                using var session = new SourceSession();
                InitializePairStudy(session.Fighters);
                var source = session.Fighters.Single(fighter => fighter.name == "Mankey");
                var target = session.Fighters.Single(fighter => fighter.name == "Pepe");
                scope.Add("AttackerAvatar=" + CombatExpansionInventory.Identity(source.Animator.avatar));
                scope.Add("ReceiverAvatar=" + CombatExpansionInventory.Identity(target.Animator.avatar));
                int completed = 0;
                foreach (float spacing in new[] { 1.70f, 1.66f, 1.64f, 1.62f })
                {
                    string candidate = output + "/Spacing_" + spacing.ToString("F2", CultureInfo.InvariantCulture);
                    Directory.CreateDirectory(candidate);
                    foreach (int direction in new[] { 1, -1 })
                    {
                        CaptureBladePair(session.Fighters, source, direction, candidate, scope, grounding, spacing);
                        File.WriteAllLines(scopePath, scope);
                        File.WriteAllText(status, "RUNNING completed pairs=" + ++completed + "/8.\n");
                    }
                }
                File.WriteAllText(status, "CAPTURED all eight grounded Execution01 spacing candidate pairs.\n" +
                    "No candidate spacing or contact is accepted; no damage or runtime registration is assigned.\n");
            }
            catch (Exception error)
            {
                scope.Add("Capture failure=" + error);
                File.WriteAllText(status, "FAILED: " + error + "\nPartial output is not complete evidence.\n");
                File.WriteAllLines(scopePath, scope);
                throw;
            }
        }
    }
}
