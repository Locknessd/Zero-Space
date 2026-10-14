using System.IO;
using System.Text;
using UnityEngine;
using ZeroSpace.Showcase;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void InspectBomCamera()
        {
            var report = new StringBuilder();
            using (var session = new PepeShowcaseSession(PepeShowcaseSetup.BomScenePath))
            {
                var showcase = session.showcase;
                report.AppendLine("Global fitted distance: " + showcase.director.SequenceDistance);
                for (int index = 0; index < showcase.library.takes.Length; index++)
                {
                    var take = showcase.library.takes[index];
                    showcase.PlayTake(index);
                    float required = 0;
                    for (int sample = 0; sample <= Mathf.CeilToInt(take.Duration * 60); sample++)
                    {
                        showcase.Evaluate(showcase.library.TakeStart(index) +
                            Mathf.Min(sample / 60f, take.Duration - .0001f), 1f / 60, false);
                        var camera = showcase.director.output;
                        var focus = camera.transform.position + camera.transform.forward * showcase.director.SafetyDistance;
                        var inverse = Quaternion.Inverse(camera.transform.rotation);
                        float vertical = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad / 2) * .9f;
                        float horizontal = vertical * camera.aspect;
                        foreach (var bone in new[] { HumanBodyBones.Head, HumanBodyBones.Hips,
                            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
                            HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                        {
                            var point = inverse * (showcase.character.GetBoneTransform(bone).position - focus);
                            required = Mathf.Max(required, Mathf.Abs(point.y) / vertical - point.z,
                                Mathf.Abs(point.x) / horizontal - point.z);
                        }
                    }
                    report.AppendLine(take.title + ": fitted weapons=" + showcase.director.TakeDistance.ToString("F3") +
                        "m; body/hands minimum=" + required.ToString("F3") + "m; locked=" + take.lockCamera);
                }
            }
            File.WriteAllText(PepeShowcaseSetup.Output + "/BomFramingDiagnostic.txt", report.ToString());
        }
    }
}
