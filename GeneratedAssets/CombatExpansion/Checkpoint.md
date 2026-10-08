# Combat expansion checkpoint

This checkpoint preserves ongoing implementation and its current evidence.
It does not complete the combat expansion.

## Saved implementation

- SlapFace Sequence1 and Sequence2 are explicitly selectable in BattleScene,
  with four per-avatar definitions, contact anchors and grounding assets.
- Standing and grounded recovery blend native reach offsets back to controller
  bone positions, preventing the hand/foot snap observed when those offsets were
  restored immediately.
- The Slap lifecycle assertion accounts for the existing camera-facing effect
  offset, and its launcher allows the complete interruption and cleanup suite.
- Punch-combo tooling measures all three candidate contacts across avatars,
  lane directions and spacings. The first contact capture completed 12 cases;
  a follow-up with high reactions and transient grounding completed 16 cases.
  The sequential contact study evaluated 12 positive-direction candidates, all
  with incomplete contacts; 12 negative-direction cases were skipped because
  their corresponding positive sequences were incomplete. Shorter entry blends
  and a chest-target variant are implemented in tooling but have not been run.
  These candidates still miss required contacts and are not registered moves.
- The combo catalogue proposes 30 sequences covering all 86 usable punch/kick
  clips. These are authoring plans, not accepted or registered gameplay combos.
- Optional exact-mesh exclusions support Samurai sheath filtering in trails and
  blade effects. Native blade calibration and both sheath exclusions are saved
  into BattleScene using a selective field merge that preserves other changes.
- Samurai contact authoring records blade/skin intersections, anatomical regions,
  bone-local anchors, relative velocities and non-leg landing support for both
  avatars and directions. Execution01 is now saved in both heavy move registries
  with per-attacker definitions, two damaging contacts, anchored landing feedback
  and prone recovery. Executions 02–10 remain pending.
- Landing VFX can use an explicitly assigned contact anchor, with the existing
  hip position as fallback. Live Samurai landing-anchor checks passed on both
  avatars and directions.
- Slap lethal authoring and raw-preview tools are preserved as provisional editor
  tooling. The empirical encoding diagnosis failed its native-pose tolerance;
  no lethal receiver candidate was baked or assigned. Adaptive sample refinement
  is implemented and compiles, but has not yet passed the empirical pose gate.

## Validation recorded at this checkpoint

- Runtime and Editor C# compilation passed with zero errors; existing warnings remain.
- After the grounded bone-offset blend fix, the existing GreatSword recovery
  regression passed all 48 lifecycle cases: natural completion, cancellation,
  pause, reset, disable and lethal outcomes. This validates recovery lifecycle,
  not complete contact or presentation acceptance. See `GreatSwordRecoveryPlayMode.txt`.
- Slap nonlethal Play Mode validation passed 48 main cases / 56 accepted plays and
  eight range guards across both actions, both avatars and both directions.
  Contact feedback, recovery, pause, interruption, reset, disable, repeated use,
  ownership and cleanup passed. Maximum recovery entry displacement was 0.000001 m.
  See `SlapStudy/PlayMode/Report.txt` and `Cases.csv`; lethal behavior was excluded.
- The earlier impact-anchor assertion and subsequent recovery-snap failure are
  resolved. `SlapStudy/PlayMode/RecoverySmoke` preserves a partial intermediate run;
  the enclosing PlayMode report contains the final full-suite result.
- Slap grounded contacts and independent grounding checks previously passed;
  the grounding validation recorded 50,760 clearances with no violations.
- The first punch-combo candidate capture previously passed 12 sampling cases,
  including native-pose equivalence and backward seeking. `KbComboStudy/FirstCombo/
  MotionReview.md` records visual limitations. Contact timings remain provisional.
- `KbComboStudy/FirstComboContacts` contains all 12 completed contact cases and
  a review of reach, reaction timing and raw floor penetration. Forward/backward
  contact, clearance and pose discrepancies were zero in this capture.
- `KbComboStudy/HighReactionLinks` contains 16 completed cases. Transient grounding
  kept sampled body clearance near or above 0.01 m, but the reaction/timing changes
  did not resolve reach for both avatars. These results do not qualify a combo.
- `SamuraiStudy/BladeContacts/ContactAuthoring` contains four completed cases with
  stable backward seeks. Both victims finish prone. Anatomy, support timing,
  and support anchors were remeasured during installation. Execution01's basic
  24-case Play Mode suite passed. The extended suite now passes the recovery
  boundary across both avatars/directions (0.000000 m at report precision),
  resolving the earlier 0.005514 m snap against a 0.002 m limit. Its first 16
  cases passed, then case 17 failed the lethal effects/audio cleanup assertion.
  The remaining extended cases and visual/audio acceptance are still open.
- `SlapStudy/LethalAuthoring/BakeProvenance.txt` records all four Root/Motion
  encoding trials and the failed pose-preservation gate. The final source guard
  reported unchanged sources. Raw candidate preview has not run.
- Samurai blade configuration validation passed in Unity after correcting the
  fixture to use persistent mesh references and Unity null equality. Identity,
  calibration, idempotence, conflict rejection and trail/VFX predicates passed;
  10,000 warmed predicate passes allocated zero bytes.
- `SamuraiStudy/PlayMode/Basic24/Report.txt` passed range rejection, queued
  existing/new/existing actions, accepted lethal/reset and precontact interruption
  across both avatars and directions. Native blade trails, three presentation
  contacts, audio, hit-stop, light, shake, flash and prone getup were observed.
- Selective scene saving passed preservation, encoding, malformed-input and
  actual BattleScene merge checks. Installation saved only `staticBlades` and
  `excludedMeshes` on the owned component. See the reports under
  `SamuraiStudy/BladeContacts/Presentation`. Gameplay trail appearance and effect
  directions remain unverified.

## Remaining work

Slap lethal treatment, final gameplay camera review and dedicated audio tuning
remain unfinished. Samurai Execution01 has basic gameplay validation; extended
lifecycle (including the recorded lethal cleanup assertion failure), complete
visual/audio acceptance and the remaining nine registrations
are still required. Grounding tooling for executions 2 through 10 has compiled
but has not been run. No new punch-combo study is registered as a qualifying gameplay
combo; the required combo collection, axe combinations and remaining action
coverage still need implementation and gameplay validation.
