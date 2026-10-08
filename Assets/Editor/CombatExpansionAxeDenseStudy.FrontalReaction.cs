using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionAxeDenseStudy
    {
        const string FrontalOutput = "GeneratedAssets/CombatExpansion/AxeContactStudy/DenseCombo01FrontalReaction";
        const string FrontalGuid = "932279eb1c22db24385eb04c03856eb1";
        const string FrontalIdentity = FrontalGuid + ":7400044";
        const float FrontalOnset = .47166667f;

        public static void CaptureFrontalReaction()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            FrankReactionTrack track = null;
            try
            {
                var clip = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(FrontalGuid))
                    .OfType<AnimationClip>().Single(c => CombatExpansionInventory.Identity(c) == FrontalIdentity);
                track = ScriptableObject.CreateInstance<FrankReactionTrack>();
                track.hideFlags = HideFlags.HideAndDontSave;
                track.name = "Provisional Combo_01 first frontal reaction";
                track.segments = new[]
                {
                    new FrankReactionTrack.Segment
                    {
                        strikeId = "provisional-first-left-head-wing",
                        clip = clip,
                        seconds = FrontalOnset,
                        blendSeconds = .05f,
                        terminal = false
                    }
                };
                if (!track.TryValidate(out string error))
                    throw new InvalidOperationException(error);
                CaptureStudy(track);
            }
            finally
            {
                if (track)
                    Object.DestroyImmediate(track);
            }
        }

        static Surface MovingReceiver(CharacterCombat target, SkinnedMeshRenderer[] skins, float seconds,
            List<string> scope)
        {
            scope.Add("Current victim geometry at source seconds=" +
                seconds.ToString("R", CultureInfo.InvariantCulture));
            // Cache only the original visible renderer selection. Preview snapshots hide those renderers,
            // but their current bones, blend shapes, bindposes and world vertices remain authoritative.
            // Receiver rebuilds both skinned world vertices and triangle BVH for this evaluated pose.
            return Receiver(target, scope, skins);
        }

        static List<string> FrontalScope()
        {
            var scope = Scope();
            scope[0] = "PROVISIONAL first-reaction study; no action registration or gameplay acceptance.";
            scope[3] = "Before onset victim holds t=0 of base Hit:" + FrontalGuid + ":7400024, as in static study.";
            scope[4] = "Normal FrankReactionTrack playback and gameplay root motion; no target chasing or pose locking.";
            scope.Add("Prior static evidence: " + Output + "/Contacts.csv and Scope.txt; those outputs are unchanged.");
            scope.Add("Candidate reaction: KB_Hit_m_MidFront_Med " + FrontalIdentity +
                "; source onset=0.47166667s; blend=0.05s; terminal=false; exactly one temporary segment.");
            scope.Add("Onset comes from the first static left HeadWing surface intersection on BOTH avatars.");
            scope.Add("This onset is a provisional candidate, not accepted gameplay hit timing.");
            scope.Add("No second hit or reaction is authored automatically or from later measured contacts.");
            scope.Add("Second strike/reaction, final choreography and gameplay acceptance remain unassigned.");
            scope.Add("Victim world skin vertices and BVH rebuild from CURRENT bones and bindposes after every pose.");
            scope.Add("Only original visible renderer selection is cached; skinned world vertices are never cached.");
            scope.Add("Every rebuild retains finite-vertex, skin binding, Bake(false) and skeleton-bounds checks.");
            scope.Add("Both actors' displacements use the same authoritative pair clock as contacts and images.");
            scope.Add("Sheets add opening 0/0.2/0.4s, candidate onset, 1.3/1.8s and attack source-end poses.");
            scope.Add("Dense minima are selected from THIS moving capture, separately for each hand and window.");
            scope.Add("Temporary reaction track is destroyed even if pair start, sampling or rendering fails.");
            return scope;
        }
    }
}
