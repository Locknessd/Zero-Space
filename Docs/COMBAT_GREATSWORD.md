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

During the entry blend, the injected prop follows the visible right hand using its calibrated palm
frame. Each sample starts from socket identity, then applies the temporary position and rotation
correction. After entry, the prop returns to its exact source socket pose with unit local scale.

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
  invalid-socket rollback and cancellation cleanup. Expanded sampling includes 0, 0.03, 0.06, 0.09 and
  0.12 seconds, plus repeated/backward evaluation. Repeated/backward drift was zero. The actual prop's
  calibrated grip error stayed below 0.000002 m, including entry; post-entry hand alignment also passed
  the 0.025 m threshold. Saved BattleScene fighters have no weapon-manager components, so manager-token
  assertions are explicitly not applicable in this suite.
- `GreatSwordStudy`: eight baseline sheets, frame times and 60 Hz bone trajectories from the actual
  BattleScene avatars. These predate grounding and the capture framing correction; some victim poses
  are cropped. They are source-motion evidence, not final camera or grounded-pose approval.
- `GreatSwordStudy/Grounded`: eight reviewed sheets, frame times and 60 Hz bone trajectories with
  grounding enabled. `ContactWindows` adds 32 sheets sampled at 0.05-second intervals. `Contacts.csv`
  contains 1,150 mesh-distance observations across eight avatar/pair combinations in one direction.
  The scan includes the entire weapon mesh, including the hilt; no damage times have been assigned.
- `GreatSwordCameraValidation.txt`: 16 cases, with four failing distance samples for Pepe Execution2
  at 2.0 and 2.016667 seconds in both directions. Sampled body containment passed, but camera distance
  exceeded the check's allowance. The cause remains unresolved; this is not camera acceptance.
- `GreatSwordRecoveryStudy`: six actual-avatar captures compare the current get-up with Frank prone
  and supine get-ups. These isolated motion samples do not establish valid controller transitions.
- `GreatSwordStudy/RecoveryEntries`: 16 captures exercise source completion into the selected get-up.
  They reveal discontinuities, including Execution1 depth shifts of approximately 2.7–2.9 m and
  transient floor penetration. Recovery remains unfinished; these artifacts record the failing baseline.

The commit's Runtime and Editor sources compiled with zero errors and 39/13 warning lines under
Unity 6000.5.9f1 when using the pre-existing local Cartoon FX compatibility edit. That unrelated edit
is excluded from this commit: compiling the exact Git index under this editor instead reports three
obsolete `GetInstanceID()` errors in the legacy `CFX_SpawnSystem.cs`. The project version is unchanged.
These checks use isolated scene sampling; the new GreatSword path has not passed Play Mode validation.

Available Editor entry points are `CombatExpansionGreatSwordStudy.Capture`, `CaptureGrounded`,
`CaptureWindows`, `ScanContacts`, `CaptureRecoveryEntries`, `CombatExpansionGreatSwordGrounding.Bake`,
`CombatExpansionGreatSwordGrounding.Validate`, `CombatExpansionGreatSwordCameraCheck.Validate`,
`CombatExpansionGreatSwordRecoveryStudy.Capture`, and `CombatExpansionSourceWeaponCheck.Validate`.

The grounded sheets under `GreatSwordStudy/Grounded` show prone Ambush endings, airborne separation
in Execution1, and supine Execution2/3 endings on both fighters. These require distinct recovery
assessment. The study selects the Frank prone get-up for Ambush/Execution1 and the Frank supine get-up
for Execution2/3 to make the transition defects reproducible. These selections are study configuration,
not registered gameplay behavior.

Next work includes exact strike and landing contacts, living-victim recovery, per-contact feedback,
action registration, and both-avatar/direction gameplay and lifecycle checks.
