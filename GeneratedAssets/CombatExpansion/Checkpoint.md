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
  their corresponding positive sequences were incomplete. The shorter-entry and
  chest-target follow-up recorded all 24 cases: four Mankey cases found all three
  provisional onsets, ten cases missed contacts, and ten reverse-lane cases were
  skipped after their forward case missed. No candidate is registered gameplay.
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
- Slap lethal authoring now saves two project-owned provisional receiver clips.
  Native-stream encoding and adaptive sample refinement passed dense prefix,
  retimed-fall, held-pose and backward-replay gates. Source assets are unchanged.
  Eight raw previews cover both sequences, avatar assignments and lane directions.
  Raw receiver floor penetration reaches 0.150 m on Pepe and 0.236 m on Mankey.
  Grounding, final visual acceptance and conditional gameplay assignment remain.
- Optional lethal-pair infrastructure selects a full receiver clip, grounding and
  presentation without changing primary damage identities or times. No gameplay
  asset assigns it yet. Its in-memory contract fixture passed in Unity.
- Execution02 through Execution10 contact-capture tooling is installed and compiled;
  all nine grounding assets now pass independent floor-clearance validation.
  Collection contact capture is in progress and excluded from this checkpoint.
- Global pause freezes pooled particles, effect ages, camera shake and surface
  highlights. Local contact holds retain the existing unscaled presentation clock.
- Punch diagnostics now offer a bounded torso probe that excludes head, neck and
  arm geometry. Earlier broad chest-probe measurements are historical candidate
  evidence; the corrected four-case jab diagnostic still needs to run in Unity.
- Slap lethal grounding bake, independent validation and grounded capture tools
  are installed and compiled. Their Unity runs and gameplay assignment remain
  pending; the two required authored receiver time maps are included.

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
  resolving the earlier 0.005514 m snap against a 0.002 m limit. Cleanup observes
  actual effect/voice expiry and disarms after the clean assertion. After the
  global-pause repair, the complete rerun passed all 40 cases / 44 activations,
  including lethal, reset, receiver-disable, native particle and audio freeze/
  resume checks. Visual/audio choreography acceptance remains separate.
- `SlapStudy/LethalAuthoring/BakeProvenance.txt` records the selected native-stream
  Root/Motion encoding and successful candidate validation. Maximum bone error
  is below 0.000490 m against the unchanged 0.002 m limit. The final source guard
  reports unchanged sources; this is export fidelity, not gameplay acceptance.
- Execution02 through Execution10 grounding passed independent 361 Hz checks
  on both avatar assignments and directions. Execution06 and Execution10 use
  a 0.005 m cushion only for the Mankey receiver; the 0.3 m correction cap and
  clearance threshold are unchanged. Original aggregate failure reports remain
  as history; all nine individual validation reports now pass. This establishes
  sampled floor clearance, not blade contact or gameplay acceptance.
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
remain unfinished. Samurai Execution01 has passed its complete extended lifecycle suite;
visual/audio choreography acceptance and the remaining nine registrations
are still required. All nine remaining executions have independently validated
grounding, as described in SamuraiStudy/BladeContacts/RemainingGroundingReview.md.
Their contacts, support and recovery still need review. Slap lethal grounding
and the corrected jab diagnostic remain unrun. No new punch-combo study is
registered as a qualifying gameplay combo; the required combo collection, axe combinations and remaining action
coverage still need implementation and gameplay validation.
