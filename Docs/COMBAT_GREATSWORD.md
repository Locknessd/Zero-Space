# GreatSword grounding and source weapon study

The four native GreatSword pairs now have isolated BattleScene study tools and baked grounding assets.
They are not registered gameplay actions. Contact timing, reactions/recovery, presentation cues and
ordinary gameplay validation remain unfinished.

Source weapon attachment restores the calibrated hand grip alignment when an injected prefab replaces
the native renderers. The exact driver socket is `root/ik_hand_root/ik_hand_gun/ik_hand_r`. Grounding
raises the source driver and attached prop by the same vertical correction applied to the target hips;
the next sample removes the previous correction to prevent accumulated movement. Cancellation resets
that correction before disposing the driver. Paired moves can also supply a camera key; the study uses
the existing `execution/0` through `execution/3` takes with the appropriate avatar suffix.

`CombatExpansionGreatSwordGrounding.Bake` samples rendered body clearance at 240 Hz minimum for
Ambush and Execution1/2/3, both avatars and both roles. Each saved correction includes the largest
of its neighboring samples to avoid interpolation undershoot. The four assets live under
`Assets/CombatExpansion/Actions/Frank_GreatSword_Attack_*_Grounding.asset`.

Validation evidence under `GeneratedAssets/CombatExpansion`:

- `GreatSwordGroundingValidation.txt`: PASS for 16 pair cases and 32 actor cases across both directions.
  Independent sampling at 361 Hz minimum produced 19,756 pair evaluations and 39,512 body clearance
  measurements. Worst clearance was -0.0125276763 m, within the check's -0.025 m tolerance. This checks
  sampled body grounding; it does not establish blade contact or visual acceptance.
- `GreatSwordGrounding.csv`: the bake records original clearance, base lift and stored lift, preserving
  the distinction between the measured correction and its conservative sample envelope.
- `SourceWeaponValidation.txt`: all 48 isolated sampling/cancellation cases passed across four pairs,
  both avatars, both directions and injected/null-prefab/invalid-socket modes. Checks cover clip and
  socket identity, tracked renderers, disabled colliders, receiver equipment, repeated/backward sampling,
  invalid-socket rollback and cancellation cleanup. Repeated/backward drift was zero, and post-entry
  grip errors remained below the 0.025 m threshold. The entry blend still has visible grip separation
  to review. Saved BattleScene fighters have no weapon-manager components, so manager-token assertions
  are explicitly not applicable in this suite.
- `GreatSwordStudy`: eight baseline sheets, frame times and 60 Hz bone trajectories from the actual
  BattleScene avatars. These predate grounding and the capture framing correction; some victim poses
  are cropped. They are source-motion evidence, not final camera or grounded-pose approval.

Runtime and Editor compilation completed with zero errors and the existing 39/13 warning lines.
These checks use isolated scene sampling; the new GreatSword path has not passed Play Mode validation.

Available Editor entry points are `CombatExpansionGreatSwordStudy.Capture`, `CaptureGrounded`,
`ScanContacts`, `CombatExpansionGreatSwordGrounding.Bake`, `Validate`, and
`CombatExpansionSourceWeaponCheck.Validate`. Grounded capture and contact scan are implemented but
have not yet produced reviewed results. Contact scanning measures the entire weapon mesh, including
the hilt, so nearby surfaces must be reviewed before assigning damage contacts.

Next work includes grounded visual review, exact strike and landing contacts, living-victim recovery,
per-contact feedback, action registration, and both-avatar/direction gameplay and lifecycle checks.
