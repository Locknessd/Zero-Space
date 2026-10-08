using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionAxeDenseStudy
    {
        const string Output = "GeneratedAssets/CombatExpansion/AxeContactStudy/DenseCombo01";
        const string Identity = "5b18d7ef2de2c7e45acf6b7c15d8a3ce:7400000";
        static readonly HumanBodyBones[] Tracked =
        {
            HumanBodyBones.Hips, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot
        };

        public static void Capture()
        {
            CaptureStudy(null);
        }

        static void CaptureStudy(FrankReactionTrack reactions)
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            string output = reactions ? FrontalOutput : Output;
            Directory.CreateDirectory(output);
            var singletonTypes = new[]
            {
                typeof(CombatPositioningController), typeof(GameManager), typeof(MemeBattleUI),
                typeof(MortalKombatCamera), typeof(WebSocketManager)
            };
            var singletons = singletonTypes.Select(t => t.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static)).ToArray();
            var values = singletons.Select(p => p.GetValue(null)).ToArray();
            var active = SceneManager.GetActiveScene();
            Scene scene = default;
            var scope = reactions ? FrontalScope() : Scope();
            var contacts = new List<string>
            {
                "sourceIdentity,fighter,victim,direction,spacingM,window,seconds,hand,region,bladeBodyGapM," +
                "bladeX,bladeY,bladeZ,bodyX,bodyY,bodyZ,nearestBone,boneLocalX,boneLocalY,boneLocalZ," +
                "attackerHipsDeltaX,attackerHipsDeltaY,attackerHipsDeltaZ," +
                "victimHipsDeltaX,victimHipsDeltaY,victimHipsDeltaZ"
            };
            var paths = new List<string>
            {
                "fighter,victim,direction,spacingM,seconds,role,transform,x,y,z,deltaX,deltaY,deltaZ"
            };
            try
            {
                foreach (var singleton in singletons)
                    singleton.SetValue(null, null);
                scene = EditorSceneManager.OpenPreviewScene(CombatExpansionInventory.Battle);
                // Preview Awake callbacks must never route pair constraints through another scene.
                foreach (var singleton in singletons)
                    singleton.SetValue(null, null);
                var roots = scene.GetRootGameObjects();
                var game = roots.SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var fighter in fighters)
                {
                    fighter.battleSfx = null;
                    fighter.battleVfx = null;
                    fighter.hitEffect = null;
                    if (!fighter.Initialize())
                        throw new InvalidOperationException("Cannot initialize " + fighter.name);
                }
                foreach (var source in fighters)
                foreach (int direction in new[] { 1, -1 })
                    CapturePair(scene, fighters, source, direction, contacts, paths, scope, reactions, output);
                File.WriteAllLines(output + "/Contacts.csv", contacts);
                File.WriteAllLines(output + "/Displacements.csv", paths);
                scope.Add("Completed contact rows: " + (contacts.Count - 1));
                File.WriteAllLines(output + "/Scope.txt", scope);
                Debug.Log("Dense Combo_01 study complete: " + output);
            }
            finally
            {
                try
                {
                    if (scene.IsValid())
                        EditorSceneManager.ClosePreviewScene(scene);
                }
                finally
                {
                    for (int i = 0; i < singletons.Length; i++)
                        singletons[i].SetValue(null, values[i]);
                    if (active.IsValid() && active.isLoaded)
                        SceneManager.SetActiveScene(active);
                }
            }
        }

        static void CapturePair(Scene scene, CharacterCombat[] fighters, CharacterCombat source, int direction,
            List<string> contacts, List<string> paths, List<string> scope, FrankReactionTrack reactions, string output)
        {
            foreach (var fighter in fighters)
                fighter.ResetCombat();
            var target = fighters.Single(f => f != source);
            float spacing = source.name == "Mankey" ? 1f : 1.35f;
            source.transform.position = Vector3.left * direction * spacing * .5f;
            target.transform.position = Vector3.right * direction * spacing * .5f;
            source.Animator.transform.position = source.transform.position;
            target.Animator.transform.position = target.transform.position;
            var move = CombatExpansionAxeContactStudy.MakeMove(source, target, spacing);
            if (CombatExpansionInventory.Identity(move.attackAnim) != Identity)
                throw new InvalidOperationException("Unexpected Combo_01 identity");
            if (reactions)
            {
                move.sourcePair.reactions = reactions;
                move.sourcePair.reactionDelay = 0;
                scope.Add(Csv("Moving pair", source.name, target.name, direction, spacing,
                    "attack source end", move.attackAnim.length));
            }
            if (!source.ExecuteAttack(move, target))
                throw new InvalidOperationException("Dense axe pair could not begin");
            var pair = source.SourcePlayback;
            try
            {
                pair.ReceiverActor.Pose.transferFingers = true;
                pair.EvaluateAt(0);
                if (Vector3.Dot(pair.AttackerActor.transform.forward, Vector3.right * direction) < .99f)
                    throw new InvalidOperationException("Pair direction does not match requested lane direction");
                var axes = new[]
                {
                    new AxeRegion(pair.AttackerActor, true, scope),
                    new AxeRegion(pair.AttackerActor, false, scope)
                };
                var body = Receiver(target, scope);
                var movingSkins = reactions ? BodySkins(target) : null;
                var baseline = Bones(pair, source, target).ToDictionary(p => p.name, p => p.transform.position);
                RecordPaths(paths, source, target, direction, spacing, 0, pair, baseline);
                var recorded = new HashSet<float> { 0 };
                var minimumTimes = new Dictionary<string, (float gap, float seconds)>();
                foreach (var sample in Times())
                {
                    pair.EvaluateAt(sample.seconds);
                    if (reactions)
                        body = MovingReceiver(target, movingSkins, sample.seconds, scope);
                    RecordPaths(paths, source, target, direction, spacing, sample.seconds, pair, baseline, !reactions);
                    recorded.Add(sample.seconds);
                    foreach (var axe in axes)
                    {
                        axe.Update();
                        var head = body.Closest(axe.head);
                        AddContact(contacts, source, target, direction, spacing, sample, axe.hand,
                            "HeadWing", head, baseline);
                        AddContact(contacts, source, target, direction, spacing, sample, axe.hand,
                            "HaftOrUnclassified", body.Closest(axe.remainder), baseline);
                        Vector3 hand = source.Animator.GetBoneTransform(axe.hand == "Left" ?
                            HumanBodyBones.LeftHand : HumanBodyBones.RightHand).position;
                        Vector3 nearest = body.Nearest(hand);
                        AddContact(contacts, source, target, direction, spacing, sample, axe.hand,
                            "HandBonePoint", new Gap
                            {
                                squared = (nearest - hand).sqrMagnitude, blade = hand, body = nearest
                            }, baseline);
                        string key = sample.window + axe.hand;
                        if (!minimumTimes.TryGetValue(key, out var best) || head.squared < best.gap)
                            minimumTimes[key] = (head.squared, sample.seconds);
                    }
                }
                var selected = new[] { .43f, .48f, .52f, .56f, .9f, .96f, 1.02f, 1.07f, 1.12f }
                    .Concat(minimumTimes.Values.Select(v => v.seconds)).Distinct().OrderBy(t => t).ToArray();
                Action<float> movingSample = null;
                if (reactions)
                {
                    selected = selected.Concat(new[] { 0, .2f, .4f, FrontalOnset, 1.3f, 1.8f, move.attackAnim.length })
                        .Distinct().OrderBy(t => t).ToArray();
                    movingSample = seconds =>
                    {
                        MovingReceiver(target, movingSkins, seconds, scope);
                        if (recorded.Add(seconds))
                            RecordPaths(paths, source, target, direction, spacing, seconds, pair, baseline, false);
                    };
                }
                WriteSheets(scene, fighters, pair, axes, source, direction, selected, scope, output, movingSample);
            }
            finally
            {
                pair.Cancel();
            }
        }

        static IEnumerable<(int window, float seconds)> Times()
        {
            foreach (var window in new[] { (id: 1, start: .43, end: .56), (id: 2, start: .90, end: 1.12) })
            {
                int last = (int)Math.Floor((window.end - window.start) * 240 + 1e-8);
                for (int step = 0; step <= last; step++)
                    yield return (window.id, (float)(window.start + step / 240.0));
                if (window.start + last / 240.0 < window.end - 1e-8)
                    yield return (window.id, (float)window.end);
            }
        }

        static List<string> Scope() => new List<string>
        {
            "Dense Combo_01 study. No action entries, hit times, reactions or gameplay acceptance are authored.",
            "Source: " + Identity + "; actual BattleScene Mankey and Pepe, each as attacker, both lane directions.",
            "Proposed spacing: Mankey attack 1.0m; Pepe attack 1.35m, from prior frontal candidate, unapproved.",
            "Victim holds t=0 of prior study Hit:932279eb1c22db24385eb04c03856eb1:7400024 throughout.",
            "No reaction is advanced; body surface therefore stays static. Pair root policy is current gameplay playback.",
            "Windows: [0.43,0.56], [0.90,1.12] seconds. Each starts at boundary and steps 1/240s; end is included.",
            "Last interval of each window can be shorter than 1/240s. Samples are poses, not confirmed strikes.",
            "World positions and unsigned triangle-surface gaps are metres; zero means surface touch/intersection.",
            "Gap is minimum over vertex-face, edge-edge and segment-face intersection candidates via triangle BVH.",
            "A fully enclosed surface can have nonzero surface gap; this is not penetration depth or collision approval.",
            "FBX inspected offline: both native axes have 210 control points and a long shaft with broad distal wings.",
            "Head selection uses actual imported mesh: longest bounds axis, wider terminal third identifies head end.",
            "Shaft center/width measured at 20-50 percent length from butt; exclude 1.35 times shaft half-width.",
            "Select triangles wholly beyond 55 percent length and outside shaft corridor; preserve original topology.",
            "HeadWing is a conservative blade-wing proxy, not certified sharpened edge; collar/haft is excluded.",
            "HaftOrUnclassified is remaining mesh, including ambiguous collar/head transitions; never called blade.",
            "HandBonePoint is hand pivot proximity only, not hand skin surface distance.",
            "Victim includes visible skinned meshes with >=1000 vertices; named geometry and counts logged below.",
            "Body uses explicit current bone/bindpose world skinning, with Bake(false) space diagnostics.",
            "Nearest bone is Euclidean proximity to major bone pivot, not skin-weight anatomy classification.",
            "Displacements.csv stores actor, driver, model roots, source hips and actual hips/feet against t=0.",
            "Images show plain anatomy plus colored head overlays: left cyan, right orange. Two opposing oblique views.",
            "Images include fixed context poses and each hand/window sampled minimum; image timestamps are stamped.",
            "Preview-only state; render visibility, material/mesh ownership, RenderTexture and singletons restored."
        };
    }
}
