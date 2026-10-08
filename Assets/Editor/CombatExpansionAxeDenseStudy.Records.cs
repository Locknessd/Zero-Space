using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionAxeDenseStudy
    {
        static IEnumerable<(string name, Transform transform)> Bones(FrankBattlePairPlayback pair,
            CharacterCombat source, CharacterCombat target)
        {
            foreach (bool attack in new[] { true, false })
            {
                string role = attack ? "Attacker" : "Victim";
                var actor = attack ? pair.AttackerActor : pair.ReceiverActor;
                var fighter = attack ? source : target;
                yield return (role + "/CombatRoot", fighter.transform);
                yield return (role + "/ModelRoot", fighter.Animator.transform);
                yield return (role + "/ActorRoot", actor.transform);
                yield return (role + "/DriverRoot", actor.activeDriver.transform);
                yield return (role + "/SourceHips", actor.Pose.sourceHips);
                foreach (var bone in Tracked)
                    yield return (role + "/" + bone, fighter.Animator.GetBoneTransform(bone));
            }
        }

        static void RecordPaths(List<string> rows, CharacterCombat source, CharacterCombat target,
            int direction, float spacing, float seconds, FrankBattlePairPlayback pair,
            Dictionary<string, Vector3> baseline)
        {
            foreach (var entry in Bones(pair, source, target))
            {
                var point = entry.transform.position;
                var delta = point - baseline[entry.name];
                if (entry.name.StartsWith("Victim/", StringComparison.Ordinal) && delta.sqrMagnitude > 1e-8f)
                    throw new InvalidOperationException("Held victim moved; cached body geometry would be stale: " +
                        entry.name);
                var name = entry.name.Split('/');
                rows.Add(Csv(source.name, target.name, direction, spacing, seconds, name[0], name[1],
                    point.x, point.y, point.z, delta.x, delta.y, delta.z));
            }
        }

        static void AddContact(List<string> rows, CharacterCombat source, CharacterCombat target,
            int direction, float spacing, (int window, float seconds) sample, string hand, string region,
            Gap gap, Dictionary<string, Vector3> baseline)
        {
            if (!float.IsFinite(gap.squared))
                throw new InvalidOperationException("Nonfinite " + region + " gap");
            var anchor = NearestAnchor(target, gap.body, out var bone);
            Vector3 local = anchor.InverseTransformPoint(gap.body);
            Vector3 a = source.Animator.GetBoneTransform(HumanBodyBones.Hips).position - baseline["Attacker/Hips"];
            Vector3 b = target.Animator.GetBoneTransform(HumanBodyBones.Hips).position - baseline["Victim/Hips"];
            rows.Add(Csv(Identity, source.name, target.name, direction, spacing, sample.window, sample.seconds,
                hand, region, Mathf.Sqrt(gap.squared), gap.blade.x, gap.blade.y, gap.blade.z,
                gap.body.x, gap.body.y, gap.body.z, bone, local.x, local.y, local.z, a.x, a.y, a.z, b.x, b.y, b.z));
        }

        static string Csv(params object[] cells) => string.Join(",", cells.Select(cell =>
        {
            string value = cell is float number ? number.ToString("R", CultureInfo.InvariantCulture) :
                Convert.ToString(cell, CultureInfo.InvariantCulture);
            return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0 ? value :
                "\"" + value.Replace("\"", "\"\"") + "\"";
        }));
    }
}
