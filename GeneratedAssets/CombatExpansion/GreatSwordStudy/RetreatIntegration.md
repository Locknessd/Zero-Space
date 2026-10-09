# Ambush retreat integration status

The retreat animation has been baked, installed in saved BattleScene, and passed
native-driver equivalence on both fighter drivers. Both fighter action entries
and the shared presentation bank now reference the 3.6-second authored clip.
The ordinary gameplay suite and recovery lifecycle suite each passed all 48
cases. The remaining combat collections are tracked separately below.

The baker preserves the native Ambush phase, then appends the native backward
step start and stop. It preserves numeric curve tangents and weights, reconciles
omitted constant bindings against both drivers, and validates quaternion
hemispheres without rewriting the original phase. Offsets on unused helper
leaves require explicit dependency checks; body and weapon tolerances remain
unchanged.

Ordered native sources (all clip local ID 7400000):

- Ambush: `610fba6d346a64f4583237c3ef4f7e46`.
- Backward start: `12987da400bbe864aae42fdf303bc647`.
- Backward stop: `ed2751f54f474b74397a144408a8fe03`.

The output is `Assets/CombatExpansion/Animations/GreatSword_Ambush_Retreat.anim`,
stored through Git LFS. Its duration is 3.60000014 seconds. `RetreatBake.txt`
records 1096 native samples per driver, maximum position error 0.0000402296464
metres, zero reported native angular error, and maximum scale error
0.00000126299983. Native comparisons completed before saving the asset.
`RetreatJoinDiagnostics.txt` records the helper and join investigation.

The extended Ambush grounding was also baked locally and passed the independent
361 Hz validation: four pair cases, eight actor cases, and 10408 clearance
measurements; worst clearance was 0.00265280739 metres. The exported grounding
reports record that candidate. The extended grounding asset is now installed
together with the matching action, original-source provenance, presentation
profile and targeted BattleScene entries.

Completed checks after installation:

- `CombatExpansionGreatSwordStudy.ValidateRecoveryEntries()` passed all 16
  avatar, direction and action cases. Ambush minimum clearance stayed positive;
  no repeated-sample or stopped-clock drift was measured.
- `CombatExpansionGreatSwordCameraCheck.Validate()` passed all four actions on
  both avatars and both directions, including source endpoints and sword bounds.
- `CaptureGrounded()` and `CaptureRecoveryEntries()` generated fresh motion
  evidence. Both Ambush attacker assignments were visually reviewed: the victim
  remains down through the retreat and rises afterward with room to regain stance.
- `CombatExpansionPlaySession.BeginGreatSword()` passed all 48 saved-scene cases:
  16 range rejections, 24 ordinary queued old/new/old actions, four accepted
  lethal outcomes and four source interruptions. The queue cases exercise both
  avatars and both directions with audio, VFX, hit-stop, lighting, camera shake,
  weapon trails, character flash, damage identity and recovery checks enabled.
  See `../GreatSwordPlayMode.txt` for per-case evidence.

The prior case 17 equipment assertion failure is superseded by this complete
suite. Inactive query-root colliders and trails are checked using active hierarchy
state as well as their component flags. The isolated saved-scene launcher restored
its previous Play Mode scene and removed the temporary scene copy after completion.

The fresh `CombatExpansionPlaySession.BeginGreatSwordRecovery()` run also passed
all 48 cases: natural and cancelled recovery for all four actions on both avatars
and directions, plus Ambush pause, reset, actor disable and lethal scenarios.
The launcher restored the previous scene and removed its temporary copy. See
`../GreatSwordRecoveryPlayMode.txt`. Its historical header about missing full
contact profiles describes the original harness scope; the separately completed
gameplay suite above verifies the now-installed contact and presentation profiles.

These results cover GreatSword integration. They do not establish acceptance of
Samurai, double-axe, SlapFace, Rough Pack, or the required new punch/kick collection.
