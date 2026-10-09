# Samurai Execution 10 gameplay integration

`Samurai_Execution10` is saved once in each fighter's BattleScene heavy registry.
Its per-attacker action assets are `Samurai_Execution10_Mankey.asset` and
`Samurai_Execution10_Pepe.asset` under `Assets/CombatExpansion/Actions`.
Gameplay acceptance remains incomplete. The latest real Play Mode run passed
24 of 40 cases, then failed the first post-contact reset presentation check.

Use `EnqueueCombatAction(side, "Samurai_Execution10")`, the exact server animation
ID, or the existing animation browser. Explicit selection keeps it out of random
action pools. Accepted server outcomes remain the damage and lethality authority.

The full 2.666666746 s pair uses PlayerA
`3e685dd57e9c78b49b20bef3e8358ae3:7400020` (2.56666684 s) and PlayerB
`b01b411d366a5a344bddf3428a7bc8f7:7400020` (2.666666746 s). Native spacing is 1.7 m, receiver yaw
180 degrees, entry blend 0.12 s, and maximum alignment error 0.15 m.
The attacker uses the native katana driver. The receiver uses the owned unarmed
driver with ordinary equipment suppressed for the interaction.

| Attacker | Damaging throw landing | Damaging downward stab |
|---|---|---|
| Mankey | 1.450000 s, Pepe RightUpperArm support | 1.879167 s, Pepe Head |
| Pepe | 1.462500 s, Mankey Hips support | 1.862500 s, Mankey Head |

Both contacts retain the native paired knockdown reaction without restarting it.
The landing uses `body_fall` with `damageOnLanding=true`; the stab uses `stab_hit`.
Neither is marked `finalLanding`, so landing does not end feedback before the
later stab. Early blade overlap during the grab is excluded from damage.
Both use measured victim anchors and the existing confirmed outcome pathway.

`heavy_swing` at 1.2 s and `thrust_swing` at 1.8 s use the established sound/VFX
timeline. Their timing and visual fit remain provisional until reviewed in play.
The newer installer and validation code expect `grapple_release` at 1.2 s to
remove the sword arc during the throw. That change is not yet persisted in the
two action assets: reinstall stopped on stale recovery evidence for
`Assets/BeatEmUp_GameTemplate3D/Textures/NightSkyTile.png.meta` after reimport.
Refresh the guarded evidence before reinstalling and rerunning the suite.
The stab resolves the explicit Katana impact assignment through the existing
`stab` override or Katana `light` route. Native blade trails retain the existing
calibrated endpoints and sheath exclusions. Hit-stop, impact light, shake, flash
and UI remain connected through the existing contact events.

Surviving victims use SupineRecovery
`f83ba830485ff2b46ba1d67ec650f1e0:1827226128182048838` with its existing grounded
recovery asset and a 0.12 s entry blend. Four authoring recovery cases passed
entry continuity, floor, repeated sampling and held-clock checks. This does not
establish the real player-loop result or normal-speed transition quality.
The subsequent player-loop trajectory capture found a visible recovery defect:
the native victim hips finish about 1.2–1.3 m away from the controller's fight
plane, and the 0.12 s recovery blend removes that depth offset too quickly.
The exact entry-frame continuity check passes but does not validate this motion.
An owned, natural return to the fight plane is still required.

`Execution10Installation.txt` records remeasured contact anchors in both lanes,
source identities, presentation references, and the selective scene save.
The saved YAML has exactly two move entries and one reference to each action.
`CombatExpansionPlaySession.BeginSamurai10` tests a fresh isolated scene copy;
its report belongs in `../Execution10PlayMode/Report.txt`.
The checkpoint includes that failed report, recovery trajectories, and archived
evidence. Local video/audio recordings are not part of the source commit;
normal-speed visual and listening acceptance remain outstanding.
