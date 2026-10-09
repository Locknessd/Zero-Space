using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        static CAContact CAMeasureContact(FrankBattlePairPlayback pair, SwordRegion sword, CASkin[] skins,
            CASkin[] attackerSkins, CharacterCombat target, float seconds)
        {
            pair.EvaluateAt(seconds);
            foreach (var skin in skins.Concat(attackerSkins))
                skin.Update();
            var result = new CAContact
            {
                seconds = seconds,
                minimumGapM = float.PositiveInfinity,
                attackerMinimumY = attackerSkins.Min(s => s.world.Min(p => p.y)),
                victimMinimumY = skins.Min(s => s.world.Min(p => p.y))
            };
            var blade = sword.vertices.Select(sword.renderer.transform.TransformPoint).ToArray();
            foreach (var skin in skins)
            for (int face = 0; face < skin.triangles.Length; face += 3)
            {
                int ia = skin.triangles[face];
                int ib = skin.triangles[face + 1];
                int ic = skin.triangles[face + 2];
                var a = skin.world[ia];
                var b = skin.world[ib];
                var c = skin.world[ic];
                var bounds = new Bounds(a, Vector3.zero);
                bounds.Encapsulate(b);
                bounds.Encapsulate(c);
                for (int edge = 0; edge < sword.triangles.Length; edge += 3)
                {
                    var d = blade[sword.triangles[edge]];
                    var e = blade[sword.triangles[edge + 1]];
                    var f = blade[sword.triangles[edge + 2]];
                    var bladeBounds = new Bounds(d, Vector3.zero);
                    bladeBounds.Encapsulate(e);
                    bladeBounds.Encapsulate(f);
                    float limit = result.minimumGapM + CATie;
                    if (CAGeometry.BoundsGap(bounds, bladeBounds) > limit * limit)
                        continue;
                    var gap = new CAGeometry.Gap { squared = float.PositiveInfinity };
                    CAGeometry.CompareTriangles(d, e, f, a, b, c, ref gap);
                    float distance = Mathf.Sqrt(gap.squared);
                    if (distance > result.minimumGapM + CATie)
                        continue;
                    result.minimumGapM = Mathf.Min(result.minimumGapM, distance);
                    result.tiedTriangles.RemoveAll(t => t.gapM > result.minimumGapM + CATie);
                    var barycentric = CABarycentric(gap.body, a, b, c);
                    var pointWeights = new float[skin.humans[ia].Length];
                    var meanWeights = new float[pointWeights.Length];
                    for (int human = 0; human < pointWeights.Length; human++)
                    {
                        pointWeights[human] = skin.humans[ia][human] * barycentric.x +
                            skin.humans[ib][human] * barycentric.y + skin.humans[ic][human] * barycentric.z;
                        meanWeights[human] = (skin.humans[ia][human] + skin.humans[ib][human] +
                            skin.humans[ic][human]) / 3;
                    }
                    float[] groups = CAGrouped(pointWeights);
                    float maximum = groups.Max();
                    result.tiedTriangles.Add(new CATriangle
                    {
                        renderer = skin.path,
                        mesh = skin.identity,
                        targetTriangle = face / 3,
                        bladeRegionTriangle = edge / 3,
                        targetVertexIndices = new[] { ia, ib, ic },
                        bladeVertexIndices = sword.triangles.Skip(edge).Take(3).ToArray(),
                        gapM = distance,
                        bladeWorldPoint = gap.blade,
                        targetWorldPoint = gap.body,
                        barycentric = barycentric,
                        pointNormalizedHumanWeights = pointWeights,
                        triangleMeanNormalizedHumanWeights = meanWeights,
                        pointNormalizedGroupWeights = groups,
                        triangleMeanNormalizedGroupWeights = CAGrouped(meanWeights),
                        dominantGroups = CAGroups.Where((_, i) => maximum - groups[i] <= .01f).ToArray()
                    });
                }
            }
            if (!float.IsFinite(result.minimumGapM) || result.tiedTriangles.Count == 0)
                throw new InvalidOperationException("No exact native blade/body contact result.");
            result.tiedTriangles = result.tiedTriangles.OrderBy(t => t.gapM)
                .ThenBy(t => t.renderer, StringComparer.Ordinal).ThenBy(t => t.targetTriangle)
                .ThenBy(t => t.bladeRegionTriangle).ToList();
            var chosen = result.tiedTriangles[0];
            int bone = Enumerable.Range(0, (int)HumanBodyBones.LastBone)
                .Where(h => target.Animator.GetBoneTransform((HumanBodyBones)h))
                .OrderByDescending(h => chosen.pointNormalizedHumanWeights[h]).ThenBy(h => h).First();
            var anchor = target.Animator.GetBoneTransform((HumanBodyBones)bone);
            var hips = target.Animator.GetBoneTransform(HumanBodyBones.Hips);
            result.chosenBone = ((HumanBodyBones)bone).ToString();
            result.targetBoneLocalPoint = anchor.InverseTransformPoint(chosen.targetWorldPoint);
            result.targetHipsLocalPoint = hips.InverseTransformPoint(chosen.targetWorldPoint);
            result.sourceBladeLocalPoint = sword.renderer.transform.InverseTransformPoint(chosen.bladeWorldPoint);
            result.anchorRoundtripErrorM = Vector3.Distance(chosen.targetWorldPoint,
                anchor.TransformPoint(result.targetBoneLocalPoint));
            var victimRotation = target.Animator.transform.rotation;
            pair.EvaluateAt(seconds - CADelta);
            var before = sword.renderer.transform.TransformPoint(result.sourceBladeLocalPoint);
            var anchorBefore = anchor.TransformPoint(result.targetBoneLocalPoint);
            pair.EvaluateAt(seconds + CADelta);
            var after = sword.renderer.transform.TransformPoint(result.sourceBladeLocalPoint);
            var anchorAfter = anchor.TransformPoint(result.targetBoneLocalPoint);
            result.bladeVelocityWorldMps = (after - before) / (2 * CADelta);
            result.bladeVelocityVictimMps = Quaternion.Inverse(victimRotation) * result.bladeVelocityWorldMps;
            result.targetAnchorVelocityWorldMps = (anchorAfter - anchorBefore) / (2 * CADelta);
            result.relativeBladeVelocityVictimMps = Quaternion.Inverse(victimRotation) *
                (result.bladeVelocityWorldMps - result.targetAnchorVelocityWorldMps);
            pair.EvaluateAt(seconds);
            return result;
        }

        static float[] CAGrouped(float[] humans)
        {
            var groups = new float[CAGroups.Length];
            for (int human = 0; human < humans.Length; human++)
                groups[CAGroup(human)] += humans[human];
            return groups;
        }

        static Vector3 CABarycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            var ab = b - a;
            var ac = c - a;
            float aa = Vector3.Dot(ab, ab);
            float cc = Vector3.Dot(ac, ac);
            float cross = Vector3.Dot(ab, ac);
            float denominator = aa * cc - cross * cross;
            if (Mathf.Abs(denominator) < 1e-16f)
            {
                var points = new[] { a, b, c };
                int first = 0;
                int second = 1;
                float longest = (a - b).sqrMagnitude;
                for (int i = 0; i < 3; i++)
                for (int j = i + 1; j < 3; j++)
                    if ((points[i] - points[j]).sqrMagnitude > longest)
                    {
                        longest = (points[i] - points[j]).sqrMagnitude;
                        first = i;
                        second = j;
                    }
                if (longest <= 1e-16f)
                    return Vector3.one / 3;
                float t = Mathf.Clamp01(Vector3.Dot(p - points[first], points[second] - points[first]) / longest);
                var bary = Vector3.zero;
                bary[first] = 1 - t;
                bary[second] = t;
                return bary;
            }
            float ap = Vector3.Dot(p - a, ab);
            float cp = Vector3.Dot(p - a, ac);
            float v = Mathf.Clamp01((cc * ap - cross * cp) / denominator);
            float w = Mathf.Clamp01((aa * cp - cross * ap) / denominator);
            var result = new Vector3(Mathf.Max(0, 1 - v - w), v, w);
            return result / (result.x + result.y + result.z);
        }
    }
}
