# Battle skill replacements

These two skills were subsequently removed from Battle at the user's request. Their assets and captures below are retained as review history. The current Battle pools contain neither Meteor Shot nor Celestial Smite; see `GeneratedAssets/BattleScaleRestoreReview` for the active configuration.

| Pool entry | New skill | Animation and effects |
| --- | --- | --- |
| Heavy_Archer | Meteor Shot | Full bow draw, charged aim, release and follow-through. Archer fire arrow effects, a warm energy core and a compact Cartoon FX fire burst. |
| Heavy_WhiteMage | Celestial Smite | HandUpFast summoning gesture. A luminous White Mage comet descends onto the target and triggers a focused Energy Strike orb and shockwave. |

Each skill has one release and one damage contact. Charge follows the animated wrist; projectiles arrive at the animated chest at the damage cue. Ground dust, impact feedback and KO remain on the shared battle timeline.

The private attack clips are baked from evaluated source motion at 60 fps. Their drivers play transform curves without a second Mecanim Humanoid body offset; the existing separate Humanoid calibration still transfers the pose to each fighter. Source pack animations and prefabs remain available.

VFX preserve authored shapes, trails and particle motion with finite emission and bounded lifetimes. Demo spawn heights are removed because Battle supplies the wrist/chest anchors. The twelve-orb Energy Strike area effect is reduced to one centered orb and shockwave. Decorative oversized impact rings and black orb overlays are disabled.

Native Unity validation:

- [PlayValidation.txt](PlayValidation.txt): both fighters, both skills, normal damage and lethal KO; contact-aligned HP, one cast/shot/contact/landing and deferred KO after animation/slow motion.
- [VfxValidation.txt](VfxValidation.txt): 88 normal/lethal/mirrored cases across the Battle pools; supported shaders, finite particles, duplicate/cancel protection and frame skipping.
- [CameraUnchanged.txt](CameraUnchanged.txt): authored Camera, Transform and camera component blocks unchanged.
- [KoTimingValidation.txt](KoTimingValidation.txt): all seven deferred KO cases pass with the replacements, including animation/slow-motion ordering, external pause and cancellation.

Final Game view captures:

- Archer: [charge](Left_Archer_Charge.png), [flight](Left_Archer_Flight.png), [impact](Left_Archer_Impact.png).
- White Mage: [charge](Left_WhiteMage_Charge.png), [flight](Left_WhiteMage_Flight.png), [impact](Left_WhiteMage_Impact.png).
- Matching `Right_*` captures show Pepe using both skills.

The native Play check uses an isolated temporary Battle scene and local animation test damage. It restores Edit Mode and deletes the temporary scene on completion.
