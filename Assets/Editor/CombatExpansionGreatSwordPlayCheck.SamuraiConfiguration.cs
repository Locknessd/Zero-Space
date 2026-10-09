using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordPlayCheck
    {
        sealed class SamuraiConfiguration
        {
            internal readonly string action, getUpIdentity, recoveryPose, directory;
            internal readonly string[] actions;
            internal readonly bool execution10;

            internal SamuraiConfiguration(string id, string getUp, string pose, string output, bool ten)
            {
                action = id;
                getUpIdentity = getUp;
                recoveryPose = pose;
                directory = "GeneratedAssets/CombatExpansion/SamuraiStudy/" + output;
                actions = new[] { "Light_1", id, "Heavy_6" };
                execution10 = ten;
            }
        }

        static readonly SamuraiConfiguration Samurai01Configuration = new SamuraiConfiguration(
            "Samurai_Execution01", "157da7dff6b3b3148a29246060747ce5:1827226128182048838",
            "prone", "PlayMode", false);
        static readonly SamuraiConfiguration Samurai10Configuration = new SamuraiConfiguration(
            "Samurai_Execution10", "f83ba830485ff2b46ba1d67ec650f1e0:1827226128182048838",
            "supine", "Execution10PlayMode", true);
        static bool Samurai10 => Samurai && samuraiConfiguration.execution10;
        static string SamuraiRecoveryPose => samuraiConfiguration.recoveryPose;
        static string SamuraiRecoveryTitle => SamuraiRecoveryPose == "prone" ? "Prone" : "Supine";

        static void ValidateSamurai10Profile(BattleSfxBank.Move profile)
        {
            var data = move.sourcePair;
            report.AppendLine("EXECUTION10_SOURCE attack=" + CombatExpansionInventory.Identity(data.attack) +
                " reaction=" + CombatExpansionInventory.Identity(data.reaction) + " attackSeconds=" +
                (data.attack ? data.attack.length.ToString("R") : "missing") + " reactionSeconds=" +
                (data.reaction ? data.reaction.length.ToString("R") : "missing") +
                " attackAlias=" + (move.attackAnim == data.attack) +
                " reactionAlias=" + (move.hitAnim == data.reaction) + " getUpAlias=" + (move.getUpAnim == data.getUp));
            Require(CombatExpansionInventory.Identity(data.attack) ==
                "3e685dd57e9c78b49b20bef3e8358ae3:7400020" &&
                CombatExpansionInventory.Identity(data.reaction) ==
                "b01b411d366a5a344bddf3428a7bc8f7:7400020" &&
                move.attackAnim == data.attack && move.hitAnim == data.reaction && move.getUpAnim == data.getUp &&
                Mathf.Abs(data.attack.length - 2.56666684f) < .00001f &&
                Mathf.Abs(data.reaction.length - 2.666666746f) < .00001f,
                "Execution10 needs its full original A/B native clips and exact supine recovery.");
            Require(CombatExpansionInventory.Identity(data.recoveryGrounding) ==
                "0e3315c06eebc2f62b838fffb249aca3:11400000" &&
                CombatExpansionInventory.Identity(move.grounding) ==
                "54888ce163d25b68b81efc494e6b1403:11400000" &&
                Mathf.Abs(data.recoveryBlendSeconds - .12f) < .000001f &&
                Mathf.Abs(data.entryBlendSeconds - .12f) < .000001f &&
                Mathf.Abs(data.maximumAlignmentError - .15f) < .000001f &&
                Mathf.Abs(move.attackRange - 1.7f) < .000001f &&
                Vector3.Distance(data.receiverOffset, Vector3.forward * 1.7f) < .000001f &&
                Quaternion.Angle(data.receiverRotation, Quaternion.Euler(0, 180, 0)) < .001f &&
                !data.spacing && !data.attacks && !data.reactions && !data.constrainDepthAfterSpacing &&
                data.standingRecoverySeconds == 0,
                "Execution10 native spacing, grounding, orientation or recovery blend changed.");
            Require((source.name == "Mankey" || source.name == "Pepe") &&
                (target.name == "Mankey" || target.name == "Pepe") && source.name != target.name,
                "Execution10 requires the two measured fighter identities.");
            foreach (var fighter in fighters)
            {
                string expectedAvatar = fighter.name == "Mankey"
                    ? "59dc3f26824c72872aa4cf6baeec671e:9000000"
                    : "757d9d3f1766533aa895e81a07aa5197:9000000";
                Require(CombatExpansionInventory.Identity(fighter.Animator.avatar) == expectedAvatar,
                    "Execution10 measured avatar identity changed.");
            }
            Require(AssetDatabase.GetAssetPath(move.actionDefinition) ==
                "Assets/CombatExpansion/Actions/Samurai_Execution10_" + source.name + ".asset" &&
                move.actionDefinition.lethalPairVariant == null,
                "Execution10 requires its per-attacker native-pair definition.");
            string attackerDriver = source.name == "Mankey"
                ? "224e94b27f72de65aa8403d8a294fde0:2627981156790044268"
                : "e843971f00482b8af9b3e546a40aa446:7468503520573832048";
            string receiverDriver = target.name == "Pepe"
                ? "22b682ea72ad141548c935b490a1b6ba:5840121589346898076"
                : "e9bda2f6da685faedac3c08af217f164:4668455661232588296";
            Require(CombatExpansionInventory.Identity(data.attackerDriver) == attackerDriver &&
                CombatExpansionInventory.Identity(data.receiverDriver) == receiverDriver,
                "Execution10 requires the calibrated armed A and unarmed B drivers.");
            float landing = source.name == "Mankey" ? 1.45f : 1.4625f;
            float stab = source.name == "Mankey" ? 1.879166722f : 1.862499952f;
            Require(profile.cues.Length == 4 && profile.cues[0].group == "grapple_release" &&
                Mathf.Abs(profile.cues[0].seconds - 1.2f) < .000001f &&
                profile.cues[2].group == "thrust_swing" &&
                Mathf.Abs(profile.cues[2].seconds - 1.8f) < .000001f,
                "Execution10 needs throw movement feedback before its separate blade thrust cue.");
            Require(expectedCues.Length == 2 && expectedCues[0].group == "body_fall" &&
                expectedCues[0].damageOnLanding && !expectedCues[0].finalLanding &&
                expectedCues[1].group == "stab_hit" && !expectedCues[1].damageOnLanding &&
                !expectedCues[1].finalLanding && expectedCues[1].contactSource == "Weapon" &&
                Mathf.Abs(expectedCues[0].seconds - landing) < .000001f &&
                Mathf.Abs(expectedCues[1].seconds - stab) < .000001f &&
                profile.cues.All(cue => !cue.finalLanding) &&
                BattleHitDamageSequence.ContactTimes(profile, 2.666666746f)
                    .SequenceEqual(expectedCues.Select(cue => cue.seconds)),
                "Execution10 requires exactly damaging throw landing then stab, with no early grab damage.");
            var definitions = move.actionDefinition.contacts;
            Require(definitions != null && definitions.Length == 2,
                "Execution10 requires exactly two native damage identities.");
            for (int i = 0; i < expectedCues.Length; i++)
            {
                var cue = expectedCues[i];
                Require(cue.hasContactPoint && cue.avatarContacts != null,
                    "Execution10 contact lacks its measured per-avatar anchor.");
                var anchors = cue.avatarContacts.Where(entry => entry != null &&
                    entry.avatar == target.Animator.avatar).ToArray();
                Require(anchors.Length == 1 && target.Animator.GetBoneTransform(anchors[0].bone) &&
                    Finite(anchors[0].offset), "Execution10 needs a unique finite measured victim anchor.");
                var contact = definitions[i];
                Require(contact != null && contact.strikeId == "Samurai_Execution10/" +
                    (i == 0 ? "throw_landing" : "downward_stab") &&
                    contact.attack == data.attack && contact.reaction == data.reaction &&
                    Mathf.Abs(contact.seconds - cue.seconds) < .000001f &&
                    contact.presentationGroup == cue.group && !contact.nonDamagingInteraction &&
                    contact.reactionPolicy == CombatActionDefinition.ReactionPolicy.AuthoredPairContinuation &&
                    contact.result == CombatActionDefinition.Result.Knockdown &&
                    Finite(contact.directionInVictimSpace) &&
                    Mathf.Abs(contact.directionInVictimSpace.magnitude - 1) < .001f,
                    "Execution10 contact identity or native knockdown continuation changed.");
            }
        }
    }
}
