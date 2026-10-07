using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static class CombatExpansionGrappleStudy
    {
        const int Width = 480;
        const int Height = 320;
        static readonly string[] Actions = { "Vol10_HOLD", "Vol10_HOLD_LALI", "Vol10_NAGE_ESC" };
        static readonly HumanBodyBones[] Bones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.LeftHand,
            HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot
        };

        [MenuItem("Tools/Battle/Combat Expansion/Capture grapple study")]
        public static void Capture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before capturing the isolated preview.");
            string output = CombatExpansionInventory.Output + "/GrappleStudy";
            Directory.CreateDirectory(output);
            var previousTarget = RenderTexture.active;
            var scene = default(Scene);
            RenderTexture targetTexture = null;
            Texture2D stamp = null;
            Texture2D sheet = null;
            // EvaluateAt consults this global. Never let preview samples constrain a loaded scene.
            var positioningProperty = typeof(CombatPositioningController).GetProperty("Instance");
            var positioning = CombatPositioningController.Instance;
            try
            {
                positioningProperty.SetValue(null, null);
                scene = EditorSceneManager.OpenPreviewScene(CombatExpansionInventory.Battle);
                positioningProperty.SetValue(null, null);
                var roots = scene.GetRootGameObjects();
                var game = roots.SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var camera = roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true))
                    .First(c => c.CompareTag("MainCamera"));
                var framing = camera.GetComponent<FrankCinematicCamera>();
                if (!framing)
                    throw new InvalidOperationException("The saved battle camera requires FrankCinematicCamera.");
                framing.battle = game;
                framing.keepBattleCameraInFront = true;
                camera.scene = scene;
                targetTexture = RenderTexture.GetTemporary(Width, Height, 24);
                camera.targetTexture = targetTexture;
                camera.aspect = (float)Width / Height;
                stamp = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                sheet = new Texture2D(Width * 4, Height * 3, TextureFormat.RGB24, false);
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var fighter in fighters)
                {
                    fighter.battleSfx = null;
                    fighter.battleVfx = null;
                    fighter.hitEffect = null;
                    if (!fighter.Initialize())
                        throw new InvalidOperationException("Could not initialize preview fighter " + fighter.name);
                }
                var csv = new StringBuilder("attacker,action,screenDirection,actor,role,seconds,bone,x,y,z\n");
                var times = new StringBuilder("Chronological: left to right, top to bottom; seconds.\n");
                var report = new StringBuilder("# Grapple transition measurements\n\n");
                report.AppendLine("Saved BattleScene registry actions; preview copies only. No loaded scene is saved.");
                report.AppendLine("World displacement in metres between 60 Hz samples, including endpoint intervals.");
                report.AppendLine("Intervals touching entry [0, entry blend] or branch [decision, decision + blend] are included.");
                report.AppendLine("Measured maxima are diagnostics, not motion approval; visual review is required.");
                report.AppendLine("Resolved escape captures do not verify defender input acceptance.\n");
                report.AppendLine("| Scenario | Actor / role | Window | Hip max | Hand max | Hand | Hand interval (s) |");
                report.AppendLine("| --- | --- | --- | ---: | ---: | --- | --- |");
                using var skins = new CombatExpansionPreviewSkin(fighters.Select(f => f.gameObject).ToArray());
                for (int identity = 0; identity < fighters.Length; identity++)
                foreach (string action in Actions)
                foreach (int direction in new[] { 1, -1 })
                {
                    var source = fighters[identity];
                    var receiver = fighters[1 - identity];
                    var side = identity == 0 ? PlayerUI.Side.Left : PlayerUI.Side.Right;
                    var move = game.FindCombatAction(side, action);
                    if (move == null || !move.grapple)
                        throw new InvalidOperationException("Saved registry lacks grapple " + source.name + "/" + action);
                    foreach (var fighter in fighters)
                        fighter.ResetCombat();
                    source.Animator.transform.position = Vector3.left * direction * move.attackRange * .5f;
                    receiver.Animator.transform.position = Vector3.right * direction * move.attackRange * .5f;
                    // The front camera looks along negative Z, so world positive X reads right to left.
                    string facing = direction > 0 ? "rightToLeft" : "leftToRight";
                    string key = source.name + "_" + action + "_" + facing;
                    if (!source.ExecuteAttack(move, receiver))
                        throw new InvalidOperationException("Could not begin " + key);
                    var pair = source.SourcePlayback;
                    try
                    {
                        Measure(pair, source, receiver, facing, csv, report, key);
                        float[] sampleTimes = PoseTimes(pair.Duration);
                        times.Append(key + ": ");
                        for (int index = 0; index < sampleTimes.Length; index++)
                        {
                            float seconds = sampleTimes[index];
                            pair.EvaluateAt(seconds);
                            skins.Sample();
                            if (!framing.Apply(0, true))
                                throw new InvalidOperationException("Camera could not frame " + key);
                            camera.Render();
                            RenderTexture.active = targetTexture;
                            stamp.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                            stamp.Apply();
                            sheet.SetPixels(index % 4 * Width, (2 - index / 4) * Height,
                                Width, Height, stamp.GetPixels());
                            times.Append(FormattableString.Invariant($"{seconds:R} "));
                        }
                        sheet.Apply();
                        File.WriteAllBytes(output + "/" + key + ".png", sheet.EncodeToPNG());
                        times.AppendLine();
                    }
                    finally
                    {
                        pair.Cancel();
                    }
                }
                File.WriteAllText(output + "/Times.txt", times.ToString());
                File.WriteAllText(output + "/Trajectories.csv", csv.ToString());
                File.WriteAllText(output + "/Report.md", report.ToString());
                Debug.Log("Grapple study captured: " + output + ". Visual review remains required.");
            }
            finally
            {
                RenderTexture.active = previousTarget;
                if (stamp)
                    Object.DestroyImmediate(stamp);
                if (sheet)
                    Object.DestroyImmediate(sheet);
                if (targetTexture)
                    RenderTexture.ReleaseTemporary(targetTexture);
                if (scene.IsValid())
                    EditorSceneManager.ClosePreviewScene(scene);
                positioningProperty.SetValue(null, positioning);
            }
        }

        static float[] PoseTimes(float duration)
        {
            if (duration <= 0 || float.IsNaN(duration) || float.IsInfinity(duration))
                throw new InvalidOperationException("Invalid grapple duration.");
            var times = new List<float> { 0, .06f, .12f, .3f, .55f, .65f, .70f, .77f, .95f };
            times.RemoveAll(t => t >= duration);
            if (duration > .95f)
            {
                times.Add(Mathf.Lerp(.95f, duration, 1f / 3));
                times.Add(Mathf.Lerp(.95f, duration, 2f / 3));
            }
            times.Add(duration);
            // Short future outcomes keep valid landmarks and subdivide gaps, never repeat the end pose.
            while (times.Count < 12)
            {
                int widest = 0;
                for (int i = 1; i < times.Count - 1; i++)
                    if (times[i + 1] - times[i] > times[widest + 1] - times[widest])
                        widest = i;
                times.Insert(widest + 1, (times[widest] + times[widest + 1]) * .5f);
            }
            return times.ToArray();
        }

        sealed class Peak
        {
            public float hips;
            public float hands;
            public HumanBodyBones hand;
            public float from;
            public float to;
        }

        static void Measure(FrankBattlePairPlayback pair, CharacterCombat source, CharacterCombat receiver,
            string direction, StringBuilder csv, StringBuilder report, string key)
        {
            var actors = new[] { source, receiver };
            var previous = new Vector3[2, Bones.Length];
            var peaks = new Peak[2, 2];
            for (int actor = 0; actor < 2; actor++)
            for (int window = 0; window < 2; window++)
                peaks[actor, window] = new Peak();
            float lastTime = 0;
            int frames = Mathf.CeilToInt(pair.Duration * 60);
            for (int frame = 0; frame <= frames; frame++)
            {
                float seconds = Mathf.Min(frame / 60f, pair.Duration);
                pair.EvaluateAt(seconds);
                for (int actor = 0; actor < actors.Length; actor++)
                for (int bone = 0; bone < Bones.Length; bone++)
                {
                    var transform = actors[actor].Animator.GetBoneTransform(Bones[bone]);
                    if (!transform)
                        throw new InvalidOperationException("Missing " + actors[actor].name + "/" + Bones[bone]);
                    Vector3 position = transform.position;
                    string role = actor == 0 ? "attacker" : "receiver";
                    csv.Append(FormattableString.Invariant(
                        $"{source.name},{pair.Move.moveName},{direction},{actors[actor].name},{role},{seconds:R},"));
                    csv.AppendLine(FormattableString.Invariant(
                        $"{Bones[bone]},{position.x:R},{position.y:R},{position.z:R}"));
                    if (frame > 0)
                    {
                        float delta = Vector3.Distance(previous[actor, bone], position);
                        var grapple = pair.Move.grapple;
                        if (lastTime <= grapple.entryBlendSeconds)
                            Accumulate(peaks[actor, 0], bone, delta, lastTime, seconds);
                        if (seconds >= grapple.decisionSeconds &&
                            lastTime <= grapple.decisionSeconds + grapple.blendSeconds)
                            Accumulate(peaks[actor, 1], bone, delta, lastTime, seconds);
                    }
                    previous[actor, bone] = position;
                }
                lastTime = seconds;
            }
            for (int actor = 0; actor < actors.Length; actor++)
            for (int window = 0; window < 2; window++)
            {
                Peak peak = peaks[actor, window];
                string role = actor == 0 ? "attacker" : "receiver";
                string label = window == 0 ? "entry" : "branch";
                report.Append(FormattableString.Invariant($"| {key} | {actors[actor].name} / {role} | {label} | "));
                report.AppendLine(FormattableString.Invariant(
                    $"{peak.hips:F6} | {peak.hands:F6} | {peak.hand} | {peak.from:F6}–{peak.to:F6} |"));
            }
        }

        static void Accumulate(Peak peak, int bone, float delta, float from, float to)
        {
            if (Bones[bone] == HumanBodyBones.Hips)
                peak.hips = Mathf.Max(peak.hips, delta);
            if ((Bones[bone] == HumanBodyBones.LeftHand || Bones[bone] == HumanBodyBones.RightHand) &&
                delta > peak.hands)
            {
                peak.hands = delta;
                peak.hand = Bones[bone];
                peak.from = from;
                peak.to = to;
            }
        }
    }
}
