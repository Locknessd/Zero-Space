# Ambush retreat integration status

The retreat animation has been baked and passed native-driver equivalence on
both fighter drivers. This is an authoring checkpoint; saved gameplay still
references the original Ambush clip. Gameplay installation and acceptance remain
pending.

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
reports record that candidate. The extended grounding asset is intentionally
not part of this checkpoint because the saved action still uses the original
duration. Commit the grounding asset together with its matching installed action.

To finish integration, run these Editor operations serially in
`FrankRetarget.Editor` with BattleScene loaded:

1. `CombatExpansionGreatSwordGrounding.BakeAmbush()` and `ValidateAmbush()`
   regenerate and check the grounding for the baked retreat.
2. `CombatExpansionGreatSwordSetup.Install()` persists the matching action,
   profile, source provenance and targeted scene entries.
3. Run the affected contact, camera and recovery visual checks, followed by
   `CombatExpansionPlaySession.BeginGreatSword()` through the ordinary queue.
   Verify normal-speed playback, terminal victim pose during retreat, and
   adequate separation before get-up.

The previous Play Mode case 17 failure came from an equipment assertion that
ignored inactive query roots. That assertion has been corrected, but the old
report remains historical evidence until a fresh suite completes. This bake
and the sampled grounding pass do not establish gameplay acceptance.
