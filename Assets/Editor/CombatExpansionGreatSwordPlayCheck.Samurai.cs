using System.IO;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordPlayCheck
    {
        static bool Samurai => action == "Samurai_Execution01";
        static bool NativeSword => GreatSword || Samurai;

        static void ValidateSamuraiProfile(BattleSfxBank.Move profile)
        {
            Require(source.heavyCombatMoves.Contains(move) && move.requiresExplicitSelection &&
                move.weapon == TrumpWeaponManager.WeaponType.Katana,
                "Samurai must be explicitly registered with its required katana.");
            Require(move.actionDefinition && move.actionDefinition.actionId == action &&
                move.actionDefinition.presentationProfile == profile,
                "Samurai must resolve its explicit per-attacker presentation profile.");
            var errors = move.actionDefinition.Validate(move, game.battleSfx.bank).ToArray();
            Require(errors.Length == 0, "Invalid Samurai definition: " + string.Join("; ", errors));
            var data = move.sourcePair;
            Require(data.showWeapon && !data.attackerWeaponPrefab && data.attackerDriver && data.receiverDriver &&
                data.receiverDriver.GetComponentsInChildren<Renderer>(true).Length == 0 &&
                data.receiverDriver.GetComponentsInChildren<Collider>(true).Length == 0,
                "Samurai must use its native source blade and a renderer/collider-free receiver driver.");
            Require(data.getUp && !data.attackerGetUp && data.recoveryGrounding && move.grounding &&
                data.transferReceiverFingers && data.recoveryBlendSeconds > 0,
                "Samurai receiver grounding, fingers or prone recovery is missing.");
            Require(CombatExpansionInventory.Identity(data.getUp) ==
                "157da7dff6b3b3148a29246060747ce5:1827226128182048838",
                "Samurai must recover from its actual prone end pose.");
            var hits = expectedCues.Where(Primary).ToArray();
            var landings = expectedCues.Where(cue => cue.group == "body_fall").ToArray();
            Require(hits.Length == 2 && hits[0].group == "stab_hit" && hits[1].group == "heavy_hit" &&
                expectedCues.Length == 3 && landings.Length == 1 && landings[0].finalLanding &&
                !landings[0].damageOnLanding && landings[0].seconds > hits[1].seconds &&
                BattleHitDamageSequence.ContactTimes(profile,
                    Mathf.Max(data.attack.length, data.reaction.length)).Length == 2,
                "Samurai needs two damaging blade contacts and one nondamaging landing.");
            foreach (var cue in expectedCues)
            {
                Require(cue.hasContactPoint && !cue.damageOnLanding &&
                    (!Primary(cue) || cue.contactSource == "Weapon"),
                    "Samurai contact lacks an explicit anchor or uses unexpected damage routing.");
                var anchors = cue.avatarContacts.Where(entry => entry != null &&
                    entry.avatar == target.Animator.avatar).ToArray();
                Require(anchors.Length == 1 && target.Animator.GetBoneTransform(anchors[0].bone) &&
                    Finite(anchors[0].offset), "Samurai lacks the measured victim anchor.");
            }
        }

        static void CaptureSamuraiContact()
        {
            if (!Samurai || !QueuePlayback)
                return;
            string directory = "GeneratedAssets/CombatExpansion/SamuraiStudy/PlayMode";
            Directory.CreateDirectory(directory);
            ScreenCapture.CaptureScreenshot(directory + "/" + source.name + "_" + Direction +
                "_Case" + (step + 1) + "_Play" + (samuraiRepeat + 1) + "_Contact" + contacts + ".png");
        }
    }
}
