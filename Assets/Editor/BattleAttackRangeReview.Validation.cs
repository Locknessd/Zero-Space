using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        [MenuItem("Tools/Battle/Validate Execution attack ranges")]
        public static void ValidateBattleAttackRanges()
        {
            Directory.CreateDirectory(RangeReview);
            var report = new StringBuilder();
            var csv = new StringBuilder("fighter,move,direction,lethal,range,seconds,surfaceGap,anchorGap,bodyGap\n");
            int cases = 0;
            int contacts = 0;
            BattleComboAuthoring.WithStudy((scene, game, fighters) =>
            {
                foreach (var source in fighters)
                foreach (var move in source.heavyCombatMoves.Where(IsExecutionRangeMove))
                foreach (int direction in new[] { 1, -1 })
                foreach (bool lethal in new[] { false, true })
                {
                    var target = fighters.Single(f => f != source);
                    PrepareRangePair(fighters, source, target, move.attackRange, direction);
                    int index = int.Parse(move.moveName.Substring(move.moveName.Length - 1));
                    var authored = CombatExpansionGreatSwordStudy.MakeMove(source, target, index);
                    if (Mathf.Abs(authored.attackRange - move.attackRange) > .0001f ||
                        Mathf.Abs(move.sourcePair.receiverOffset.z - move.attackRange) > .0001f)
                        throw new InvalidOperationException("Saved and authored range differ: " + move.moveName);
                    if (!source.ExecuteAttack(move, target, lethal))
                        throw new InvalidOperationException("Cannot validate range: " + move.moveName);
                    var pair = source.SourcePlayback;
                    var profile = pair.PresentationProfile(game.battleVfx.timeline);
                    float largest = 0;
                    try
                    {
                        foreach (var cue in profile.cues.Where(IsMeleeRangeContact))
                        {
                            pair.EvaluateAt(cue.seconds);
                            if (!cue.TryContactPosition(target.Animator, out var point))
                                throw new InvalidOperationException("Missing contact: " + move.moveName);
                            var probe = new BattlePresentationContactSetup.BladeContactProbe(pair, source, target);
                            float gap = probe.MeasureReceiverShift(Vector3.zero);
                            float anchor = probe.BladeDistance(point);
                            float body = probe.BodyDistance(point);
                            if (!float.IsFinite(gap) || gap > .025f || anchor > .12f || body > .025f)
                                throw new InvalidOperationException(FormattableString.Invariant(
                                    $"{source.name}/{move.moveName} contact mismatch: gap={gap:R}, anchor={anchor:R},") +
                                    FormattableString.Invariant($" body={body:R}; lethal={lethal}; lane={direction}."));
                            largest = Mathf.Max(largest, gap);
                            csv.AppendLine(FormattableString.Invariant($"{source.name},{move.moveName},") +
                                FormattableString.Invariant($"{direction},{lethal},{move.attackRange:R},") +
                                FormattableString.Invariant($"{cue.seconds:R},{gap:R},{anchor:R},{body:R}"));
                            if (direction == 1 && !lethal)
                                CaptureRangeContact(scene, source.name, move.moveName, cue.seconds);
                            contacts++;
                        }
                        report.AppendLine(FormattableString.Invariant($"PASS {source.name}/{move.moveName}; ") +
                            FormattableString.Invariant($"direction={direction}; lethal={lethal}; ") +
                            FormattableString.Invariant($"range={move.attackRange:R}m; largest surface gap={largest:R}m."));
                        cases++;
                        File.WriteAllText(RangeReview + "/Contacts.csv", csv.ToString());
                        File.WriteAllText(RangeReview + "/Validation.txt", report.ToString());
                    }
                    finally
                    {
                        pair.Cancel();
                    }
                }
            });
            report.AppendLine($"PASS {cases} actual pair playbacks and {contacts} melee contact samples.");
            File.WriteAllText(RangeReview + "/Validation.txt", report.ToString());
        }

        static void CaptureRangeContact(UnityEngine.SceneManagement.Scene scene,
            string fighter, string move, float seconds)
        {
            var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>())
                .Single(c => c.CompareTag("MainCamera"));
            var texture = new RenderTexture(1280, 720, 24);
            try
            {
                camera.scene = scene;
                camera.targetTexture = texture;
                using var skin = new CombatExpansionPreviewSkin(scene.GetRootGameObjects());
                var director = camera.GetComponent<FrankCinematicCamera>();
                director.ResetView();
                if (!director.Apply(0, true))
                    throw new InvalidOperationException("The range review camera did not initialize.");
                skin.Sample();
                CaptureBattleCamera(camera, RangeReview + "/" + fighter + "_" + move + "_" +
                    Mathf.RoundToInt(seconds * 1000) + "ms.png");
            }
            finally
            {
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
