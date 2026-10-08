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
  lane directions and spacings. Its actual contact capture has not been run.
- Optional exact-mesh exclusions support Samurai sheath filtering in trails and
  blade effects. The configuration helper has not been saved into BattleScene.

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
- Samurai blade configuration validation currently fails its excluded-mesh
  serialization round-trip fixture. Its failure is preserved under
  `SamuraiStudy/BladeContacts/Presentation/Status.txt`. Later checks in that
  validation method have not run; this module is not accepted gameplay integration.

## Remaining work

Slap lethal treatment, final gameplay camera review and dedicated audio tuning
remain unfinished. Samurai blade validation and scene configuration remain
unfinished. Grounding tooling for executions 2 through 10 has compiled but has
not been run. No new punch-combo study is registered as a qualifying gameplay
combo; the required combo collection, axe combinations and remaining action
coverage still need implementation and gameplay validation.
