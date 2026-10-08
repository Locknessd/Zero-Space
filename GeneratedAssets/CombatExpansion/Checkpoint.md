# Combat expansion checkpoint

This checkpoint preserves ongoing implementation and its current evidence.
It does not complete the combat expansion.

## Saved implementation

- SlapFace Sequence1 and Sequence2 are explicitly selectable in BattleScene,
  with four per-avatar definitions, contact anchors and grounding assets.
- Standing recovery blends native reach offsets back to controller bone positions,
  preventing the hand snap observed when those offsets were restored immediately.
- The Slap lifecycle assertion accounts for the existing camera-facing effect
  offset, and its launcher allows the complete interruption and cleanup suite.
- New punch-combo tooling measures all three candidate contacts across avatars,
  lane directions and spacings. Contact capture is still running locally;
  its partial outputs are excluded from this checkpoint.
- The combo catalogue proposes 30 sequences covering all 86 usable punch/kick
  clips. These are authoring plans, not accepted or registered gameplay combos.
- Optional exact-mesh exclusions support Samurai sheath filtering in trails and
  blade effects. Native blade calibration and both sheath exclusions are saved
  into BattleScene using a selective field merge that preserves other changes.

## Validation recorded at this checkpoint

- Runtime and Editor C# compilation passed with zero errors; existing warnings remain.
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
- Samurai blade configuration validation passed in Unity after correcting the
  fixture to use persistent mesh references and Unity null equality. Identity,
  calibration, idempotence, conflict rejection and trail/VFX predicates passed;
  10,000 warmed predicate passes allocated zero bytes.
- Selective scene saving passed preservation, encoding, malformed-input and
  actual BattleScene merge checks. Installation saved only `staticBlades` and
  `excludedMeshes` on the owned component. See the reports under
  `SamuraiStudy/BladeContacts/Presentation`. Gameplay trail appearance and effect
  directions remain unverified.

## Remaining work

Slap lethal treatment, final gameplay camera review and dedicated audio tuning
remain unfinished. Samurai actions still require gameplay registration and
presentation validation. Grounding tooling for executions 2 through 10 has compiled
but has not been run. No new punch-combo study is registered as a qualifying gameplay
combo; the required combo collection, axe combinations and remaining action
coverage still need implementation and gameplay validation.
