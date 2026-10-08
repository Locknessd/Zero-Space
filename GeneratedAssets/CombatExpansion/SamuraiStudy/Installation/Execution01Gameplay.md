# Samurai Execution 01 gameplay integration

`Samurai_Execution01` is saved in both BattleScene heavy move registries with
explicit per-attacker action definitions. It is available through
`EnqueueCombatAction(side, "Samurai_Execution01")`, the exact server animation ID,
and the existing animation browser. It is excluded from random action pools.
The existing server outcome controls damage and lethality.

The source pair uses PlayerA `3e685dd57e9c78b49b20bef3e8358ae3:7400002`
(2.866667 s) and PlayerB `b01b411d366a5a344bddf3428a7bc8f7:7400002`
(3.166667 s). Playback preserves the full pair duration. The attacker keeps the
native katana; the victim driver contains no renderers or weapon colliders.
Both actors' ordinary equipment is suppressed under the existing owned state.

| Attacker | Spacing | Stab | Cut | Non-damaging landing |
|---|---:|---:|---:|---:|
| Mankey | 1.64 m | 0.395833 s, Pepe UpperChest | 2.033333 s, Pepe LeftShoulder | 2.666667 s, Pepe LeftUpperArm |
| Pepe | 1.70 m | 0.387500 s, Mankey UpperChest | 2.029167 s, Mankey UpperChest | 2.712500 s, Mankey Head |

Each strike has a measured bone-local anchor and a window of +/-1/240 s.
The source receiver continues its paired reaction without restarting a flinch.
The cut's anatomy includes arm/torso intersection ties preserved in the contact
report; the representative anchor does not establish a more precise region.
There are exactly two damaging contacts. The landing is feedback only.

The explicit profile routes `thrust_swing`, `stab_hit`, `blade_swing`,
`heavy_hit`, and `body_fall` through the existing sound/VFX timeline. Contact
events retain hit-stop, light, shake, character flash and combo UI feedback.
Landing placement uses the measured support anchor projected onto the floor.
Blade calibration and sheath exclusions remain assigned to BattleScene.

Surviving victims use the existing grounded ProneRecovery clip
`157da7dff6b3b3148a29246060747ce5:1827226128182048838`, with a 0.12 s entry blend.
Accepted lethal outcomes retain the native terminal pose and suppressed victim
equipment until reset. They do not enter getup.

## Actual Unity evidence

`../PlayMode/Basic24/Report.txt` passed all 24 cases in an isolated saved copy of
BattleScene with the normal lighting, camera, sound, VFX and feedback enabled:

- Four out-of-range rejections produced no playback, contacts or feedback.
- Twelve queued cases ran Light_1, Samurai_Execution01 and Heavy_6 in sequence
  across both avatars and directions. Each Samurai play produced exactly three
  presentation contacts, two damage opportunities, native blade trails, audio,
  hit-stop, light, shake, flash and successful prone getup.
- Four accepted lethal cases retained the victim terminal hold and passed reset
  cleanup for source actors, death state and equipment.
- Four precontact interruptions cancelled both participants without contacts,
  sound or VFX and without delayed callbacks during the observation period.

The installer remeasured both blade contacts and the landing support vertex in
both directions before selectively saving the heavy entries. Source identities,
grounding and unarmed victim drivers were checked. The test reloaded saved scene
data rather than relying on temporary Play Mode edits.

Three gameplay images have been visually reviewed: Mankey's opening stab and
landing, and Pepe's second cut. The measured contact flashes and landing effect
are visible at their intended regions; both actors remain readable in the scene.
This frame review is narrower than a complete normal-speed choreography review.

The extended 40-case harness includes reset during action, actor disable, pause,
repeated use, stale callbacks, recovery continuity and raw Editor measurements.
Its initial run caught a 0.005514 m recovery-entry snap caused by native local
bone offsets being restored immediately. The shared recovery correction now
blends those offsets for grounded getup as well as standing recovery.

The rerun passed its first 16 cases, including all four avatar/direction recovery
boundaries at 0.000000 m (report precision) against the unchanged 0.002 m limit.
Case 17 then failed the lethal effects/audio cleanup assertion. This is an open
validation issue; the extended suite has not passed. See `../PlayMode/Report.txt`
and the earlier failure archive in `../PlayMode/RecoveryBoundaryFailure`.

Frame timing, heap changes and object counts are raw Editor observations including
harness overhead, not a standalone performance verdict. The allocation counter
failed its capability probe and is reported unavailable. Dedicated listening,
whoosh timing, full normal-speed visual review and executions 02–10 remain open.
