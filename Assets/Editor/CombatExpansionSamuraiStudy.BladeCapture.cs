using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        public static void CaptureExecution01BladeContacts()
        {
            CaptureExecution01BladeContacts(false);
        }

        public static void CaptureGroundedExecution01BladeContacts()
        {
            CaptureExecution01BladeContacts(true);
        }

        static void CaptureExecution01BladeContacts(bool grounded)
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            var grounding = grounded ? AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(
                "Assets/CombatExpansion/SamuraiStudy/Grounding/Samurai_Execution01_Grounding.asset") : null;
            if (grounded && !grounding)
                throw new InvalidOperationException("Bake and validate Execution01 grounding before this capture.");
            string output = BladeOutput + (grounded ? "/Execution01Grounded" : "/Execution01");
            Directory.CreateDirectory(output);
            string status = output + "/Status.txt";
            File.WriteAllText(status, "RUNNING Samurai Execution01 blade study.\n");
            var scope = new List<string>
            {
                "Execution01 candidate pair, both actual fighter assignments and lane directions.",
                "Uses existing ExecuteAttack and FrankBattlePairPlayback; no gameplay registration or damage.",
                "Attacker PlayerA armed; victim PlayerB has no weapon renderers. Native source offset 1.7m.",
                "Only native BladeR mesh 3e685dd57e9c78b49b20bef3e8358ae3:4300002 is measured.",
                "Blade triangles have all three local Z coordinates <= -0.33: 111 of 7046 faces.",
                "Boundary is based on the exported native mesh projections; guard, grip and sheath are excluded.",
                "This is blade surface geometry, not a certified sharpened-edge collision model.",
                "Whole source pair sampled at 30Hz; candidate windows " +
                    (grounded ? "[0.35,1.05]" : "[0.4,1.05]") + " and [1.75,2.35] at 240Hz.",
                "Windows come from observed source motion; they are not approved contact times or hit counts.",
                "Current victim skin and triangle BVH are rebuilt after every source-clock evaluation.",
                "Unsigned gaps are exact triangle surface distance; zero may indicate touch or intersection.",
                "Distance does not measure penetration depth or prove a valid anatomical strike.",
                "Nearest bone is a pivot proximity label, not skin-weighted anatomy classification.",
                "Images show the measured blade in cyan with a tiny display-only normal offset.",
                "Measured geometry always uses original unoffset vertices. Sheath is never included.",
                "Preview scenery is hidden and replaced with a neutral floor; scene lighting is retained.",
                "Backwards-seek contact gaps and blade tips are compared with previously recorded samples."
            };
            scope.Add("Grounding=" + (grounding ? CombatExpansionInventory.Identity(grounding) : "none"));
            try
            {
                using var session = new SourceSession();
                InitializePairStudy(session.Fighters);
                int completed = 0;
                foreach (var source in session.Fighters)
                foreach (int direction in new[] { 1, -1 })
                {
                    CaptureBladePair(session.Fighters, source, direction, output, scope, grounding);
                    File.WriteAllText(status, "RUNNING completed pairs=" + ++completed + "/4.\n");
                }
                File.WriteAllLines(output + "/Scope.txt", scope);
                File.WriteAllText(status, "CAPTURED four Execution01 blade-contact studies.\n" +
                    "Contact selection, adaptation, grounding, recovery and gameplay integration remain required.\n");
            }
            catch (Exception error)
            {
                File.WriteAllText(status, "FAILED: " + error + "\nPartial capture is not complete evidence.\n");
                throw;
            }
        }

        static void CaptureBladePair(CharacterCombat[] fighters, CharacterCombat source, int direction,
            string output, List<string> scope, FrankPairGrounding grounding, float spacing = 1.7f)
        {
            var target = fighters.Single(fighter => fighter != source);
            var pair = BeginBladeStudy(fighters, source, target, 1, direction, grounding, spacing);
            try
            {
                pair.EvaluateAt(0);
                CheckPairWeapons(pair, pair.AttackerActor.Pose.weaponRenderers);
                using var sword = new SwordRegion(pair);
                var body = new CombatExpansionAxeDenseStudy.MovingBodyProbe(target);
                var diagnostics = new List<string>();
                var records = new Dictionary<float, BladeRecord>();
                var nodes = PairNodes(pair, source, target);
                var bounds = new Bounds(source.Animator.GetBoneTransform(HumanBodyBones.Hips).position, Vector3.zero);
                string stem = source.name + "_" + target.name + "_" + (direction > 0 ? "Positive" : "Negative");
                scope.Add(FormattableString.Invariant($"{stem} constant entry spacing={spacing:R}m"));
                var rows = new StringBuilder(BladeHeader);
                foreach (float seconds in BladeTimes(pair.Duration, grounding))
                {
                    pair.EvaluateAt(seconds);
                    diagnostics.Clear();
                    body.Update(diagnostics);
                    var contact = body.Measure(sword.renderer, sword.vertices, sword.triangles);
                    var record = new BladeRecord(seconds, contact, sword.Tip, body.MinimumY);
                    records.Add(seconds, record);
                    AppendBladeRow(rows, source, target, direction, record);
                    if (seconds == 0)
                    {
                        scope.Add(stem + " initial geometry diagnostics:");
                        scope.AddRange(diagnostics);
                    }
                    foreach (var node in nodes)
                        bounds.Encapsulate(node.node.position);
                    bounds.Encapsulate(sword.renderer.bounds);
                }
                File.WriteAllText(output + "/" + stem + ".csv", rows.ToString());
                foreach (var window in new[] { (grounding ? .35f : .4f, 1.05f), (1.75f, 2.35f) })
                {
                    var best = records.Values.Where(r => r.seconds >= window.Item1 && r.seconds <= window.Item2)
                        .OrderBy(r => r.contact.gap).ThenBy(r => r.seconds).First();
                    scope.Add(FormattableString.Invariant($"{stem} window={window} minimumGap={best.contact.gap:R}m ") +
                        FormattableString.Invariant($"seconds={best.seconds:R}; nearestPivot={best.contact.nearestBone}"));
                }
                var selected = BladeSheetTimes(pair.Duration, grounding).Select(time => records.Keys
                    .OrderBy(value => Mathf.Abs(value - time)).First()).Distinct().OrderBy(time => time).ToArray();
                bounds.Encapsulate(new Vector3(bounds.center.x, 0, bounds.center.z));
                bounds.Expand(.55f);
                sword.ShowOverlay();
                using var rendering = new GameplayPairRendering(source.gameObject.scene, fighters, pair);
                float worstGap = 0;
                float worstTip = 0;
                rendering.Write(output + "/" + stem + ".png", bounds, selected, seconds =>
                {
                    pair.EvaluateAt(seconds);
                    diagnostics.Clear();
                    body.Update(diagnostics);
                    var contact = body.Measure(sword.renderer, sword.vertices, sword.triangles);
                    var expected = records[seconds];
                    worstGap = Mathf.Max(worstGap, Mathf.Abs(contact.gap - expected.contact.gap));
                    worstTip = Mathf.Max(worstTip, Vector3.Distance(sword.Tip, expected.tip));
                    if (worstGap > .001f || worstTip > .001f)
                        throw new InvalidOperationException("Blade contact changed on backwards seek: " + stem);
                });
                scope.Add(FormattableString.Invariant($"{stem} samples={records.Count}; ") +
                    FormattableString.Invariant($"maxReseekGapError={worstGap:R}m; maxReseekTipError={worstTip:R}m"));
            }
            finally
            {
                pair.Cancel();
            }
        }
    }
}
