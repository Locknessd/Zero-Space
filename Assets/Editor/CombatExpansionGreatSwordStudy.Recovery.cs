using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordStudy
    {
        static AnimationClip Recovery(int index)
        {
            string name = index < 2 ? "Frank_Damage@Damage_Getup01_P_iP.FBX" :
                "Frank_Damage@Damage_Getup02_L_iP.FBX";
            string path = "Assets/Selected/Frank_Damages/Asset/Animations/Damages_InPlace/" + name;
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Single(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
        }

        public static void CaptureRecoveryEntries()
        {
            string output = Output + "/RecoveryEntries";
            Directory.CreateDirectory(output);
            var report = new StringBuilder("Actual pair completion into selected controller GetUp.\n" +
                "Editor sampling only; no gameplay or transition acceptance claim.\n" +
                "Frame 0 is the source endpoint; frame 1 is GetUp entry, then increasing recovery time.\n");
            var csv = new StringBuilder("fighter,move,direction,bone,beforeX,beforeY,beforeZ," +
                "afterX,afterY,afterZ,jump\n");
            var texture = RenderTexture.GetTemporary(480, 320, 24);
            var stamp = new Texture2D(480, 320, TextureFormat.RGB24, false);
            var sheet = new Texture2D(1920, 960, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var complete = typeof(FrankBattlePairPlayback).GetMethod("CompleteSourceMotion",
                BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                EachPair((source, target, move, pair, camera, framing, direction) =>
                {
                    pair.EvaluateAt(pair.Duration);
                    var before = Bones.Select(b => target.Animator.GetBoneTransform(b).position).ToArray();
                    var visible = new[] { source, target }
                        .SelectMany(f => f.Animator.GetComponentsInChildren<SkinnedMeshRenderer>())
                        .Where(r => r.enabled).ToArray();
                    camera.targetTexture = texture;
                    camera.aspect = 1.5f;
                    using var skin = new CombatExpansionPreviewSkin(new[] { source.gameObject, target.gameObject });
                    Draw(0);
                    foreach (var renderer in visible)
                        renderer.enabled = true;
                    complete.Invoke(pair, null);
                    if (!pair.IsRecovering)
                        throw new InvalidOperationException("Controller recovery did not start: " + move.moveName);
                    float worst = 0;
                    for (int i = 0; i < Bones.Length; i++)
                    {
                        var a = before[i];
                        var b = target.Animator.GetBoneTransform(Bones[i]).position;
                        float jump = Vector3.Distance(a, b);
                        worst = Mathf.Max(worst, jump);
                        csv.AppendLine(FormattableString.Invariant(
                            $"{source.name},{move.moveName},{direction},{Bones[i]},{a.x:R},{a.y:R},{a.z:R},{b.x:R},{b.y:R},{b.z:R},{jump:R}"));
                    }
                    float[] times = { 0, .05f, .1f, .2f, .35f, .5f, .65f, .8f, .95f,
                        move.getUpAnim.length - .05f, move.getUpAnim.length - .001f };
                    float current = 0;
                    for (int frame = 1; frame < 12; frame++)
                    {
                        float seconds = Mathf.Clamp(times[frame - 1], current, move.getUpAnim.length - .001f);
                        while (current < seconds - .000001f)
                        {
                            float step = Mathf.Min(1f / 60, seconds - current);
                            pair.PrepareRecoveryPoseEvaluation();
                            target.Animator.Update(step);
                            pair.EvaluateRecoveryPose();
                            current += step;
                        }
                        Draw(frame);
                    }
                    sheet.Apply();
                    string key = source.name + "_" + move.moveName + "_" + direction;
                    File.WriteAllBytes(output + "/" + key + ".png", sheet.EncodeToPNG());
                    report.AppendLine(FormattableString.Invariant($"{key}: maximum entry bone jump={worst:R}m; ") +
                        "getup=" + move.getUpAnim.name + "; samples=" + string.Join(", ", times.Select(t =>
                            t.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
                    camera.targetTexture = null;

                    void Draw(int frame)
                    {
                        foreach (var renderer in visible)
                            renderer.enabled = true;
                        framing.Apply(0, true);
                        skin.Sample();
                        camera.Render();
                        RenderTexture.active = texture;
                        stamp.ReadPixels(new Rect(0, 0, 480, 320), 0, 0);
                        stamp.Apply();
                        sheet.SetPixels(frame % 4 * 480, (2 - frame / 4) * 320, 480, 320, stamp.GetPixels());
                    }
                }, new[] { 1, -1 }, true);
                File.WriteAllText(output + "/Report.txt", report.ToString());
                File.WriteAllText(output + "/Entries.csv", csv.ToString());
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(stamp);
                Object.DestroyImmediate(sheet);
                RenderTexture.ReleaseTemporary(texture);
            }
        }
    }
}
