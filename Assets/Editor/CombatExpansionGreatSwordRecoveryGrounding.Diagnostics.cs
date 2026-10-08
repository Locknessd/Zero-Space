using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordRecoveryGrounding
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void DiagnoseTransitions()
        {
            var summary = new StringBuilder("source,target,action,direction,phase,requestedSeconds,normalizedTime," +
                "clipSeconds,blendWeight,clearance,cachedHeadingAngle,projectedHeadingAngle," +
                "sourceHorizontalRatio,currentHorizontalRatio,sourceBodyX,sourceBodyY,sourceBodyZ," +
                "currentBodyX,currentBodyY,currentBodyZ,pelvisDeltaAngle,pelvisDeltaAxisX," +
                "pelvisDeltaAxisY,pelvisDeltaAxisZ,pelvisLocalDeltaAngle,pelvisLocalAxisX," +
                "pelvisLocalAxisY,pelvisLocalAxisZ\n");
            var bones = new StringBuilder("source,target,action,direction,phase,seconds,bone," +
                "worldX,worldY,worldZ,localX,localY,localZ,worldQx,worldQy,worldQz,worldQw," +
                "localQx,localQy,localQz,localQw,upX,upY,upZ,forwardX,forwardY,forwardZ," +
                "scaleX,scaleY,scaleZ\n");
            string failure = null;
            int completed = 0;
            try
            {
                CombatExpansionGreatSwordGrounding.WithRecoveryFighters(fighters =>
                {
                    foreach (var source in fighters)
                    foreach (int index in new[] { 0, 1 })
                    foreach (int direction in new[] { 1, -1 })
                    {
                        foreach (var fighter in fighters) fighter.ResetCombat();
                        var target = fighters.Single(fighter => fighter != source);
                        var move = CombatExpansionGreatSwordStudy.MakeMove(source, target, index);
                        if (!move.grounding || !move.sourcePair.recoveryGrounding)
                            throw new InvalidOperationException("Diagnostics require both grounding assets.");
                        source.Animator.transform.position = Vector3.left * direction * move.attackRange * .5f;
                        target.Animator.transform.position = Vector3.right * direction * move.attackRange * .5f;
                        if (!source.ExecuteAttack(move, target))
                            throw new InvalidOperationException("Cannot start diagnostic pair: " + move.moveName);
                        var pair = source.SourcePlayback;
                        try
                        {
                            DiagnosePair(source, target, move, pair, direction, summary, bones);
                            completed++;
                        }
                        finally
                        {
                            pair.Cancel();
                        }
                    }
                });
            }
            catch (Exception exception)
            {
                failure = exception.ToString();
                throw;
            }
            finally
            {
                Write("GreatSwordRecoveryTransitionDiagnostics.csv", summary.ToString());
                Write("GreatSwordRecoveryTransitionBones.csv", bones.ToString());
                Write("GreatSwordRecoveryTransitionDiagnostics.txt",
                    (failure == null ? "CAPTURED\n" : "FAIL\n") +
                    "Ambush control and Execution1; both saved BattleScene avatars and directions.\n" +
                    "Completed cases: " + completed + "/8. Actual source completion and controller GetUp.\n" +
                    "Raw, grounded controller, and blended geometry at 0, .03, .06, .0635946, .09, .12 seconds.\n" +
                    "CSV rotations use quaternion xyzw; delta axes are normalized world/local axes respectively.\n" +
                    "Horizontal ratios are projected head-to-hips squared length divided by full squared length.\n" +
                    "Cached heading angle is zero when the obsolete yaw correction is absent.\n" +
                    "Diagnostic evidence only; no acceptance or runtime change.\n" + failure);
            }
        }

        static void DiagnosePair(CharacterCombat source, CharacterCombat target, CombatTripletData move,
            FrankBattlePairPlayback pair, int direction, StringBuilder summary, StringBuilder bones)
        {
            pair.EvaluateAt(pair.Duration);
            var hips = target.Animator.GetBoneTransform(HumanBodyBones.Hips);
            var head = target.Animator.GetBoneTransform(HumanBodyBones.Head);
            Vector3 sourceBody = head.position - hips.position;
            Quaternion sourceWorld = hips.rotation;
            Quaternion sourceLocal = hips.localRotation;
            CaptureDiagnostic(source, target, move, pair, direction, "source-endpoint", 0,
                sourceBody, sourceWorld, sourceLocal, summary, bones);
            var complete = typeof(FrankBattlePairPlayback).GetMethod("CompleteSourceMotion", PrivateInstance);
            if (complete == null) throw new MissingMethodException("CompleteSourceMotion");
            complete.Invoke(pair, null);
            if (!pair.IsRecovering) throw new InvalidOperationException("GetUp did not start.");
            CaptureDiagnostic(source, target, move, pair, direction, "initial-blended", 0,
                sourceBody, sourceWorld, sourceLocal, summary, bones);
            float previous = 0;
            foreach (float seconds in new[] { 0f, .03f, .06f, .0635946f, .09f, .12f })
            {
                pair.PrepareRecoveryPoseEvaluation();
                if (seconds > previous) target.Animator.Update(seconds - previous);
                previous = seconds;
                CaptureDiagnostic(source, target, move, pair, direction, "controller-raw", seconds,
                    sourceBody, sourceWorld, sourceLocal, summary, bones);
                Vector3 rawPosition = hips.localPosition;
                move.sourcePair.recoveryGrounding.Apply(seconds, null, target.Animator);
                CaptureDiagnostic(source, target, move, pair, direction, "controller-grounded", seconds,
                    sourceBody, sourceWorld, sourceLocal, summary, bones);
                hips.localPosition = rawPosition;
                pair.EvaluateRecoveryPose();
                CaptureDiagnostic(source, target, move, pair, direction, "blended", seconds,
                    sourceBody, sourceWorld, sourceLocal, summary, bones);
            }
        }

        static void CaptureDiagnostic(CharacterCombat source, CharacterCombat target, CombatTripletData move,
            FrankBattlePairPlayback pair, int direction, string phase, float seconds, Vector3 sourceBody,
            Quaternion sourceWorld, Quaternion sourceLocal, StringBuilder summary, StringBuilder bones)
        {
            var animator = target.Animator;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Vector3 currentBody = animator.GetBoneTransform(HumanBodyBones.Head).position - hips.position;
            var pose = typeof(FrankBattlePairPlayback).GetField("hitRecoveryPose", PrivateInstance)?.GetValue(pair);
            float heading = float.NaN;
            if (pose != null)
            {
                var field = pose.GetType().GetField("headingAngle", PrivateInstance);
                heading = field == null ? 0 : (float)field.GetValue(pose);
            }
            float normalized = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            float clipSeconds = normalized * move.getUpAnim.length;
            float blend = Mathf.SmoothStep(0, 1, clipSeconds / move.sourcePair.recoveryBlendSeconds);
            Vector3 fromGround = Vector3.ProjectOnPlane(sourceBody, Vector3.up);
            Vector3 toGround = Vector3.ProjectOnPlane(currentBody, Vector3.up);
            ShortestAxis(hips.rotation * Quaternion.Inverse(sourceWorld), out float worldAngle, out Vector3 worldAxis);
            ShortestAxis(hips.localRotation * Quaternion.Inverse(sourceLocal),
                out float localAngle, out Vector3 localAxis);
            AppendDiagnosticRow(summary, source.name, target.name, move.moveName, direction, phase, seconds,
                normalized, clipSeconds, blend, Clearance(target), heading,
                Vector3.SignedAngle(fromGround, toGround, Vector3.up),
                fromGround.sqrMagnitude / Mathf.Max(.000001f, sourceBody.sqrMagnitude),
                toGround.sqrMagnitude / Mathf.Max(.000001f, currentBody.sqrMagnitude),
                sourceBody.x, sourceBody.y, sourceBody.z, currentBody.x, currentBody.y, currentBody.z,
                worldAngle, worldAxis.x, worldAxis.y, worldAxis.z,
                localAngle, localAxis.x, localAxis.y, localAxis.z);
            CaptureBone(source, target, move, direction, phase, seconds, "AnimatorRoot", animator.transform, bones);
            if (hips.parent)
                CaptureBone(source, target, move, direction, phase, seconds, "HipsParent", hips.parent, bones);
            for (int index = 0; index < (int)HumanBodyBones.LastBone; index++)
            {
                var bone = animator.GetBoneTransform((HumanBodyBones)index);
                if (bone)
                    CaptureBone(source, target, move, direction, phase, seconds,
                        ((HumanBodyBones)index).ToString(), bone, bones);
            }
        }

        static void CaptureBone(CharacterCombat source, CharacterCombat target, CombatTripletData move,
            int direction, string phase, float seconds, string name, Transform bone, StringBuilder rows)
        {
            Vector3 world = bone.position;
            Vector3 local = bone.localPosition;
            Quaternion rotation = bone.rotation;
            Quaternion localRotation = bone.localRotation;
            Vector3 up = bone.up;
            Vector3 forward = bone.forward;
            Vector3 scale = bone.lossyScale;
            AppendDiagnosticRow(rows, source.name, target.name, move.moveName, direction, phase, seconds, name,
                world.x, world.y, world.z, local.x, local.y, local.z,
                rotation.x, rotation.y, rotation.z, rotation.w,
                localRotation.x, localRotation.y, localRotation.z, localRotation.w,
                up.x, up.y, up.z, forward.x, forward.y, forward.z, scale.x, scale.y, scale.z);
        }

        static void ShortestAxis(Quaternion rotation, out float angle, out Vector3 axis)
        {
            rotation.ToAngleAxis(out angle, out axis);
            if (angle <= 180) return;
            angle = 360 - angle;
            axis = -axis;
        }

        static void AppendDiagnosticRow(StringBuilder rows, params object[] values)
        {
            var fields = new List<string>(values.Length);
            foreach (object value in values)
            {
                string text = value is float number
                    ? number.ToString("R", CultureInfo.InvariantCulture)
                    : Convert.ToString(value, CultureInfo.InvariantCulture);
                fields.Add(Csv(text));
            }
            rows.AppendLine(string.Join(",", fields));
        }
    }
}
