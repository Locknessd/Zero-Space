using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionPreviewDamageCheck
    {
        static bool saved, priorInput, priorBackground;
        static float priorTimeScale;
        static string[] priorHealth;
        static Vector3[] positions;
        static Quaternion[] rotations;

        static void SaveState()
        {
            priorInput = game.enableLocalInputTesting;
            priorBackground = Application.runInBackground;
            priorTimeScale = Time.timeScale;
            Require(priorTimeScale > 0, "Validation requires an advancing clock.");
            priorHealth = new[] { ui.left.healthText.text, ui.right.healthText.text };
            positions = new[]
            {
                game.leftCombat.Animator.transform.position, game.rightCombat.Animator.transform.position
            };
            rotations = new[]
            {
                game.leftCombat.Animator.transform.rotation, game.rightCombat.Animator.transform.rotation
            };
            saved = true;
        }

        static string HealthSnapshot()
        {
            var field = typeof(GameManager).GetField("_characterHp", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, "Authoritative health observation field is unavailable.");
            var health = (Dictionary<PlayerUI.Side, long>)field.GetValue(game);
            return string.Join(";", health.OrderBy(item => item.Key).Select(item => item.Key + "=" + item.Value));
        }

        static void CheckStopped()
        {
            Require(!game.IsAnimationTestPlaying && !game.leftCombat.IsBusy && !game.rightCombat.IsBusy &&
                !game.leftCombat.IsDead && !game.rightCombat.IsDead && game.leftCombat.Animator.enabled &&
                game.rightCombat.Animator.enabled && !ui.knockout.Pending && !ui.knockout.HasShown &&
                !feedback.IsHolding && !feedback.IsSlowing && Time.timeScale == priorTimeScale &&
                game.battleVfx.ActiveEffectCount == 0 &&
                string.IsNullOrEmpty(ui.left.damageText.text) && string.IsNullOrEmpty(ui.right.damageText.text),
                "Stop/exit retained actor, damage UI, KO, effect or clock ownership.");
            var fighters = new[] { game.leftCombat, game.rightCombat };
            for (int i = 0; i < fighters.Length; i++)
                Require(Vector3.Distance(fighters[i].Animator.transform.position, positions[i]) < .001f,
                    "Stop/exit changed a saved fighter position.");
        }

        static void RestoreState()
        {
            if (game && game.battleVfx)
                game.battleVfx.SequenceBegan -= SequenceBegan;
            if (!saved)
                return;
            try
            {
                if (game)
                {
                    game.EndAnimationTestMode();
                    game.enableLocalInputTesting = priorInput;
                    var fighters = new[] { game.leftCombat, game.rightCombat };
                    for (int i = 0; i < fighters.Length; i++)
                        if (fighters[i] && fighters[i].Animator)
                            fighters[i].Animator.transform.SetPositionAndRotation(positions[i], rotations[i]);
                }
                if (ui)
                {
                    for (int i = 0; i < priorHealth.Length; i++)
                    {
                        var parts = priorHealth[i].Split('/');
                        ui.UpdateHealth(i == 0 ? MemeBattleUI.Side.Left : MemeBattleUI.Side.Right,
                            long.Parse(parts[0]), long.Parse(parts[1]));
                    }
                    ui.ResetTransientEffects();
                }
            }
            finally
            {
                Application.runInBackground = priorBackground;
                Time.timeScale = priorTimeScale;
                saved = false;
            }
        }
    }
}
