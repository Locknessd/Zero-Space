using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordStudy
    {
        const string RetreatOutput = Output + "/RetreatCandidates";
        const string RetreatMovement = "Assets/GreatSword_Animset/Animation/Root/Movement/";
        static readonly string[] RetreatNames =
        {
            "GreatSword_Strafe_Walk_B_Start", "GreatSword_Strafe_Walk_B", "GreatSword_Strafe_Walk_B_End"
        };

        public static void CaptureRetreatCandidates()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            Directory.CreateDirectory(RetreatOutput);
            var clips = RetreatNames.Select(RetreatClip).ToArray();
            var report = new StringBuilder("Native GreatSword retreat candidate study on actual BattleScene avatars.\n" +
                "Each sheet: grounded Ambush terminal reference, candidate beginning, middle, last native sample.\n" +
                "Candidate seconds advance at native speed; trajectories sample at 60 Hz plus exact middle/end.\n" +
                "Candidates retain the original Ambush actor origin and rotation, not terminal hips alignment.\n" +
                "No candidate offsets, entry blend, grounding correction, depth lock, or gameplay motion added.\n" +
                "Entry jumps therefore include authored origin differences; these are not proposed transitions.\n" +
                "Receiver stays frozen in grounded Ambush terminal pose. Camera reframes each image.\n" +
                "Last sample is length minus 0.00001 seconds, matching native driver endpoint clamp.\n");
            var csv = new StringBuilder("fighter,direction,candidate,phase,seconds,role,point,x,y,z\n");
            var entries = new StringBuilder("fighter,direction,candidate,point,entryJumpMetres\n");
            var summary = new StringBuilder("fighter,direction,candidate,endSeconds," +
                "hipsDx,hipsDy,hipsDz,hipsBackwardMetres,rootDx,rootDy,rootDz,rootBackwardMetres\n");
            foreach (var clip in clips)
                RetreatIdentity(report, clip);
            using (var render = new RetreatRender())
            {
                EachPair((source, target, move, pair, camera, framing, direction) =>
                {
                    if (move.moveName != "Frank_GreatSword_Attack_Ambush")
                        return;
                    report.AppendLine($"REFERENCE fighter={source.name}; direction={direction}; " +
                        $"grounding={AssetDatabase.GetAssetPath(move.grounding)}");
                    RetreatIdentity(report, move.attackAnim);
                    RetreatIdentity(report, move.hitAnim);
                    foreach (var clip in clips)
                        CaptureRetreat(source, target, move, pair, camera, framing, direction, clip,
                            render, report, csv, entries, summary);
                }, new[] { 1, -1 }, true);
            }
            File.WriteAllText(RetreatOutput + "/Report.txt", report.ToString());
            File.WriteAllText(RetreatOutput + "/Trajectories.csv", csv.ToString());
            File.WriteAllText(RetreatOutput + "/EntryJumps.csv", entries.ToString());
            File.WriteAllText(RetreatOutput + "/Displacement.csv", summary.ToString());
        }

        static AnimationClip RetreatClip(string name)
        {
            string path = RetreatMovement + name + ".FBX";
            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            if (clips.Length != 1 || clips[0].length <= .00001f)
                throw new InvalidOperationException("Expected one nonempty native movement clip: " + path);
            return clips[0];
        }

        static void RetreatIdentity(StringBuilder report, AnimationClip clip)
        {
            string path = AssetDatabase.GetAssetPath(clip);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long fileId);
            report.Append(FormattableString.Invariant(
                $"SOURCE path={path}; clip={clip.name}; guid={guid}; fileID={fileId}; "));
            report.Append(FormattableString.Invariant(
                $"length={clip.length:R}s; frameRate={clip.frameRate:R}; looping={clip.isLooping}; "));
            report.AppendLine($"rig={importer?.animationType}; motionNode={importer?.motionNodeName}");
        }

        static void CaptureRetreat(CharacterCombat source, CharacterCombat target, CombatTripletData move,
            FrankBattlePairPlayback pair, Camera camera, FrankCinematicCamera framing, int direction,
            AnimationClip clip, RetreatRender render, StringBuilder report, StringBuilder csv,
            StringBuilder entries, StringBuilder summary)
        {
            pair.EvaluateAt(pair.Duration);
            var terminal = Bones.Select(b => source.Animator.GetBoneTransform(b).position).ToArray();
            string candidate = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(clip));
            string key = source.name + "_" + direction + "_" + candidate;
            AppendRetreat(csv, source, target, pair.AttackerActor, direction, candidate,
                "AmbushTerminal", pair.Duration);
            render.Draw(source, target, pair.AttackerActor, camera, framing, 0);
            var weapons = pair.AttackerActor.Pose.weaponRenderers;
            var weaponVisibility = weapons.Select(r => r && r.enabled).ToArray();
            var root = new GameObject("Native retreat candidate " + candidate);
            SceneManager.MoveGameObjectToScene(root, source.gameObject.scene);
            root.transform.SetPositionAndRotation(pair.AttackerActor.transform.position,
                pair.AttackerActor.transform.rotation);
            var actor = root.AddComponent<FrankTestActor>();
            actor.characterName = source.name;
            actor.character = source.Animator;
            try
            {
                foreach (var weapon in weapons)
                    if (weapon)
                        weapon.enabled = false;
                actor.ConfigureSource(move.sourcePair.attackerDriver, clip, true, false, false,
                    preserveCharacterScale: true);
                actor.Pose.constrainTargetHipsDepth = false;
                actor.AttachSourceWeapon(move.sourcePair.attackerWeaponPrefab, move.sourcePair.attackerWeaponSocket);
                actor.Evaluate(0);
                Vector3 hipsStart = actor.Pose.targetHips.position;
                var nativeRoot = RetreatNativeRoot(actor);
                Vector3 rootStart = nativeRoot.position;
                for (int i = 0; i < Bones.Length; i++)
                {
                    float jump = Vector3.Distance(terminal[i], source.Animator.GetBoneTransform(Bones[i]).position);
                    entries.AppendLine(FormattableString.Invariant(
                        $"{source.name},{direction},{candidate},{Bones[i]},{jump:R}"));
                }
                float end = Mathf.Max(0, clip.length - .00001f);
                var samples = Enumerable.Range(0, Mathf.CeilToInt(end * 60))
                    .Select(i => Mathf.Min(i / 60f, end)).Concat(new[] { end * .5f, end })
                    .Distinct().OrderBy(t => t);
                foreach (float seconds in samples)
                {
                    actor.Evaluate(seconds);
                    AppendRetreat(csv, source, target, actor, direction, candidate, "Candidate", seconds);
                }
                Vector3 hipsDelta = actor.Pose.targetHips.position - hipsStart;
                Vector3 rootDelta = nativeRoot.position - rootStart;
                float hipsBackward = Vector3.Dot(hipsDelta, -actor.transform.forward);
                float rootBackward = Vector3.Dot(rootDelta, -actor.transform.forward);
                summary.Append(FormattableString.Invariant($"{source.name},{direction},{candidate},{end:R},"));
                summary.Append(FormattableString.Invariant(
                    $"{hipsDelta.x:R},{hipsDelta.y:R},{hipsDelta.z:R},{hipsBackward:R},"));
                summary.AppendLine(FormattableString.Invariant(
                    $"{rootDelta.x:R},{rootDelta.y:R},{rootDelta.z:R},{rootBackward:R}"));
                var times = new[] { 0, end * .5f, end };
                for (int i = 0; i < times.Length; i++)
                {
                    actor.Evaluate(times[i]);
                    render.Draw(source, target, actor, camera, framing, i + 1);
                }
                render.Save(RetreatOutput + "/" + key + ".png");
                report.Append(FormattableString.Invariant(
                    $"{key}: Ambush={pair.Duration:R}s; candidate=0,{times[1]:R},{end:R}s; "));
                report.Append(FormattableString.Invariant(
                    $"hipsBackward={hipsBackward:R}m; rootBackward={rootBackward:R}m; "));
                report.AppendLine("driver=" + AssetDatabase.GetAssetPath(move.sourcePair.attackerDriver));
            }
            finally
            {
                actor.Clear();
                Object.DestroyImmediate(root);
                for (int i = 0; i < weapons.Length; i++)
                    if (weapons[i])
                        weapons[i].enabled = weaponVisibility[i];
            }
        }

        static Transform RetreatNativeRoot(FrankTestActor actor)
        {
            var root = actor.Pose.driver.transform.Find("root");
            if (!root)
                throw new InvalidOperationException("Native GreatSword driver is missing authored root bone.");
            return root;
        }

        static void AppendRetreat(StringBuilder csv, CharacterCombat source, CharacterCombat target,
            FrankTestActor actor, int direction, string candidate, string phase, float seconds)
        {
            string prefix = FormattableString.Invariant(
                $"{source.name},{direction},{candidate},{phase},{seconds:R},");
            Append("attacker", "BodyRoot", source.Animator.transform.position);
            Append("receiver", "BodyRoot", target.Animator.transform.position);
            foreach (var bone in Bones)
            {
                Append("attacker", bone.ToString(), source.Animator.GetBoneTransform(bone).position);
                Append("receiver", bone.ToString(), target.Animator.GetBoneTransform(bone).position);
            }
            Append("native", "ActorOrigin", actor.transform.position);
            Append("native", "DriverRoot", actor.Pose.driver.transform.position);
            Append("native", "AuthoredRoot", RetreatNativeRoot(actor).position);
            Append("native", "Hips", actor.Pose.sourceHips.position);
            foreach (var limb in actor.Pose.limbs)
                Append("native", limb.sourceEnd.name, limb.sourceEnd.position);

            void Append(string role, string point, Vector3 position)
            {
                csv.AppendLine(prefix + FormattableString.Invariant(
                    $"{role},{point},{position.x:R},{position.y:R},{position.z:R}"));
            }
        }
    }
}
