using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordRecoveryGrounding
    {
        const int BakeRate = 240;
        const int ValidationRate = 361;
        const float MaximumLift = .75f;
        const long ClipId = 1827226128182048838;
        const string ActionRoot = "Assets/CombatExpansion/Actions/Frank_GreatSword_";
        static readonly string[] Names = { "ProneRecovery", "SupineRecovery" };
        static readonly string[] Guids =
        {
            "157da7dff6b3b3148a29246060747ce5",
            "f83ba830485ff2b46ba1d67ec650f1e0"
        };

        public static void Bake()
        {
            var rows = new StringBuilder("fighter,clipGuid,clipId,seconds,clearance,baseLift,storedLift\n");
            int completed = 0;
            string failure = null;
            try
            {
                CombatExpansionGreatSwordGrounding.WithRecoveryFighters(fighters =>
                {
                    for (int index = 0; index < Names.Length; index++)
                    {
                        var clip = Clip(index);
                        var tracks = new List<FrankPairGrounding.Track>();
                        foreach (var fighter in fighters)
                        {
                            int intervals = Intervals(clip, BakeRate);
                            var raw = new float[intervals + 1];
                            var clearances = new float[intervals + 1];
                            Sample(fighter, clip, intervals, (frame, seconds) =>
                            {
                                float clearance = Clearance(fighter);
                                clearances[frame] = clearance;
                                raw[frame] = Mathf.Max(0, .01f - clearance);
                                CheckLift(raw[frame]);
                            });
                            var stored = new float[raw.Length];
                            for (int frame = 0; frame <= intervals; frame++)
                            {
                                stored[frame] = Mathf.Max(raw[frame], Mathf.Max(raw[Mathf.Max(0, frame - 1)],
                                    raw[Mathf.Min(intervals, frame + 1)]));
                                float seconds = clip.length * frame / intervals;
                                rows.AppendLine(FormattableString.Invariant(
                                    $"{Csv(fighter.name)},{Guids[index]},{ClipId},{seconds:R},") +
                                    FormattableString.Invariant(
                                        $"{clearances[frame]:R},{raw[frame]:R},{stored[frame]:R}"));
                            }
                            tracks.Add(new FrankPairGrounding.Track
                            {
                                avatar = fighter.Animator.avatar,
                                receiver = true,
                                duration = clip.length,
                                lift = stored
                            });
                            completed++;
                        }
                        string path = AssetPath(index);
                        var asset = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(path);
                        if (!asset)
                        {
                            if (AssetDatabase.LoadMainAssetAtPath(path))
                                throw new InvalidOperationException("Unexpected asset at " + path);
                            asset = ScriptableObject.CreateInstance<FrankPairGrounding>();
                            AssetDatabase.CreateAsset(asset, path);
                        }
                        asset.tracks = tracks.ToArray();
                        EditorUtility.SetDirty(asset);
                        AssetDatabase.SaveAssetIfDirty(asset);
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
                Write("GreatSwordRecoveryGrounding.csv", rows.ToString());
                Write("GreatSwordRecoveryGroundingBake.txt",
                    (failure == null ? "BAKED; validation still required.\n" : "FAIL\n") +
                    Scope(BakeRate) + "Completed avatar/clip tracks: " + completed + ".\n" + failure);
            }
        }

        public static void Validate()
        {
            var rows = new StringBuilder("fighter,clipGuid,clipId,minimumClearance,seconds,samples\n");
            int completed = 0;
            float worst = float.PositiveInfinity;
            string failure = null;
            try
            {
                CombatExpansionGreatSwordGrounding.WithRecoveryFighters(fighters =>
                {
                    for (int index = 0; index < Names.Length; index++)
                    {
                        var clip = Clip(index);
                        var asset = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(AssetPath(index));
                        if (!asset || asset.tracks == null || asset.tracks.Length != fighters.Length)
                            throw new InvalidOperationException("Missing recovery tracks: " + AssetPath(index));
                        foreach (var fighter in fighters)
                        {
                            var matches = asset.tracks.Where(track => track != null && track.receiver &&
                                track.avatar == fighter.Animator.avatar).ToArray();
                            if (matches.Length != 1)
                                throw new InvalidOperationException("Missing or duplicate recovery avatar track.");
                            var track = matches[0];
                            if (Mathf.Abs(track.duration - clip.length) > .0001f || track.lift == null ||
                                track.lift.Length != Intervals(clip, BakeRate) + 1)
                                throw new InvalidOperationException("Stale recovery track: " + AssetPath(index));
                            foreach (float lift in track.lift) CheckLift(lift);
                            float minimum = float.PositiveInfinity;
                            float minimumTime = 0;
                            int intervals = Intervals(clip, ValidationRate);
                            Sample(fighter, clip, intervals, (frame, seconds) =>
                            {
                                asset.Apply(seconds, null, fighter.Animator);
                                float clearance = Clearance(fighter);
                                if (clearance < minimum)
                                {
                                    minimum = clearance;
                                    minimumTime = seconds;
                                }
                            });
                            rows.AppendLine(FormattableString.Invariant(
                                $"{Csv(fighter.name)},{Guids[index]},{ClipId},{minimum:R},") +
                                FormattableString.Invariant($"{minimumTime:R},{intervals + 1}"));
                            worst = Mathf.Min(worst, minimum);
                            completed++;
                        }
                    }
                });
                if (completed != 4 || !float.IsFinite(worst) || worst < -.025f)
                    throw new InvalidOperationException("Recovery clearance coverage/tolerance failed: " + worst);
            }
            catch (Exception exception)
            {
                failure = exception.ToString();
                throw;
            }
            finally
            {
                Write("GreatSwordRecoveryGroundingValidation.txt", (failure == null ? "PASS\n" : "FAIL\n") +
                    Scope(ValidationRate) + FormattableString.Invariant(
                        $"Completed tracks: {completed}; worst clearance: {worst:R} m.\n") + failure + rows);
            }
        }

        static void Sample(CharacterCombat fighter, AnimationClip clip, int intervals, Action<int, float> sample)
        {
            fighter.ResetCombat();
            var animator = fighter.Animator;
            animator.transform.position = Vector3.zero;
            if (!fighter.BeginSourceGetUp(clip))
                throw new InvalidOperationException("Cannot enter controller GetUp: " + fighter.name);
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            int state = Animator.StringToHash("Base Layer.GetUp");
            for (int frame = 0; frame <= intervals; frame++)
            {
                float normalized = (float)frame / intervals;
                // The terminal GetUp pose is its left limit; sampling exactly 1 can
                // execute the controller's zero-duration transition into Idle.
                animator.Play(state, 0, Mathf.Min(normalized, .999999f));
                animator.Update(0);
                if (!animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.GetUp"))
                    throw new InvalidOperationException("Controller left GetUp during grounding sample.");
                Vector3 rawHips = hips.localPosition;
                try
                {
                    sample(frame, clip.length * normalized);
                }
                finally
                {
                    hips.localPosition = rawHips;
                }
            }
        }

        static AnimationClip Clip(int index)
        {
            string path = AssetDatabase.GUIDToAssetPath(Guids[index]);
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().SingleOrDefault(candidate =>
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out string guid, out long id) &&
                guid == Guids[index] && id == ClipId);
            if (!clip || !clip.isHumanMotion || !float.IsFinite(clip.length) || clip.length <= 0)
                throw new InvalidOperationException("Missing expected humanoid recovery: " + Guids[index]);
            return clip;
        }

        static int Intervals(AnimationClip clip, int rate) => Mathf.Max(1, Mathf.CeilToInt(clip.length * rate));
        static string AssetPath(int index) => ActionRoot + Names[index] + "_Grounding.asset";
        static string Csv(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
        static float Clearance(CharacterCombat fighter) =>
            CombatExpansionGreatSwordGrounding.RecoveryClearance(fighter);

        static void CheckLift(float lift)
        {
            if (!float.IsFinite(lift) || lift < 0 || lift > MaximumLift)
                throw new InvalidOperationException("Recovery needs manual floor adaptation: " + lift);
        }

        static string Scope(int rate) =>
            "Saved isolated BattleScene avatars; actual CharacterCombat controller GetUp; " + rate +
            " Hz minimum; receiver tracks; target clearance 0.01 m; tolerance -0.025 m.\n" +
            "Clip GUIDs: " + string.Join(", ", Guids) + "; local ID: " + ClipId + ".\n" +
            "Neighbor envelope pads one adjacent bake sample. Terminal sample is GetUp left limit.\n" +
            "Controller grounding only; paired source-entry blend and lifecycle require separate validation.\n";

        static void Write(string name, string contents)
        {
            Directory.CreateDirectory(CombatExpansionInventory.Output);
            File.WriteAllText(Path.Combine(CombatExpansionInventory.Output, name), contents);
        }
    }

    // Share the existing isolated-scene and rendered-geometry scanner without copying it.
    public static partial class CombatExpansionGreatSwordGrounding
    {
        internal static void WithRecoveryFighters(Action<CharacterCombat[]> action) => WithFighters(action);
        internal static float RecoveryClearance(CharacterCombat fighter) => Clearance(fighter);
    }
}
