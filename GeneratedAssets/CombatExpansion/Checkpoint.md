# Combat expansion checkpoint

This checkpoint preserves ongoing implementation and its current evidence.
It does not complete the combat expansion or approve the new Slap actions.

## Saved implementation

- SlapFace Sequence1 and Sequence2 are explicitly selectable in BattleScene,
  with four per-avatar action definitions, contact anchors and grounding assets.
- Actions can own presentation profiles, and source pairs can request a blend
  into standing idle recovery. Native attack tracks support chained clip studies.
- Editor tooling includes Slap installation and lifecycle checks, skinned-region
  contact measurements, KB combo captures and additional Samurai grounding work.

## Validation recorded at this checkpoint

- Runtime and Editor C# compilation passed with zero errors; 39 runtime warnings
  and 13 Editor warnings remain.
- Actual Editor presentation-profile lookup checks passed, including serialization
  and allocation checks. Selective light-registry preservation checks passed.
- Slap raw and grounded contact studies completed eight cases each. Independent
  grounding validation recorded 50,760 clearance measurements with no violations.
- The first KB combo candidate capture completed 12 cases, with native-pose
  equivalence and backward-seek checks. Contact timings and links are provisional.
- The first actual Slap Play Mode case failed: the impact missed its accepted
  receiver anchor. The suite did not reach standing-recovery validation. The
  failure report is preserved under `SlapStudy/PlayMode`.

## Remaining work

Slap contact delivery, lifecycle validation, gameplay presentation review and
lethal visual treatment remain unfinished. Samurai grounding tooling for
executions 2 through 10 has compiled but has not been run. The first KB study
is not an integrated combo; the required KB collection, axe combinations and
remaining action coverage still need implementation and gameplay validation.
