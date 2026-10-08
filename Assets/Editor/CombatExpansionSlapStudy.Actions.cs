using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapStudy
    {
        public static CombatTripletData MakeGroundedFaceMove(CharacterCombat source,
            CharacterCombat target, int sequence)
        {
            if (!source || !target || source == target || sequence < 1 || sequence > 2)
                throw new ArgumentException("SlapFace requires two distinct fighters and sequence 1 or 2.");
            var sources = ResolveSources();
            string label = "Sequence" + sequence;
            var attack = sources.Single(s => s.label == label + (sequence == 1 ? "_A" : "_B"));
            var reaction = sources.Single(s => s.label == label + (sequence == 1 ? "_B" : "_A"));
            var move = CandidateMove(source, target, attack, reaction, 180, .8f);
            move.moveName = "SlapFace_Sequence" + sequence;
            move.grounding = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(FaceGroundingPath(sequence));
            if (!move.grounding)
                throw new InvalidOperationException("Missing saved SlapFace grounding: " + sequence);
            move.sourcePair.transferReceiverFingers = true;
            move.sourcePair.standingRecoverySeconds = .3f;
            return move;
        }
    }
}
