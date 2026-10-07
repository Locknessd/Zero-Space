# Contact placement repair

The screenshot `GeneratedAssets/EditorPolishReview/After_Heavy_6_Heavy_2.png` shows the first
Mankey greatsword impact at Meme's foot while the blade crosses the waist. The first calibration
attempt incorrectly reused the camera's `BakeMesh(false)` plus position/rotation convention for
all geometry. Root's run reported a near-zero source distance at world Y=0.239 for that foot point;
that contradicted the visible blade and is not evidence that the contact is correct.

The revised calibration reconstructs every skinned vertex from the original mesh, active blend
shapes, bind poses and current bone world matrices, with the renderer's configured skin-weight limit.
It reads imported vertices using `MeshUtility.AcquireReadOnlyMeshData`, including non-readable
runtime meshes. Baked vertices are used only for independent diagnostics and triangle topology.
Static weapon meshes still use their complete local-to-world transform. This replaces the bake-space
assumption; root must compare the new output with the actual event frame before claiming a repair.

## Root invocation

Invoke these public static methods from the existing Editor request runner, adding
`BattlePresentationContactSetup` to its allowed types if necessary:

1. `BattlePresentationContactSetup.PreviewContactPlacement()` evaluates proposed anchors without saving.
2. `BattlePresentationContactSetup.RepairContactPlacement()` saves accepted bone/offset changes to the bank.
3. `BattlePresentationContactSetup.ValidateContactPlacement()` checks the saved anchors in both facings.

For a fast check of the motivating weapon, `PreviewGreatswordContact()` evaluates only both
fighters' `Heavy_6` cues and writes `Preview_Heavy_6.*` reports, without saving.

Equivalent menu items are under **Tools / Battle / Contact placement**. Run in Edit Mode.
No Unity jobs were launched during implementation; Unity execution and captures remain with the root agent.

## Scope and diagnostics

Both fighters' light and heavy move pools are inspected. Only existing authored melee anchors with
an explicit strike source are eligible. Skill/projectile moves, unanchored contacts and damaging
landings are preserved. Each candidate is evaluated at the exact existing `cue.seconds` value;
no cue source, time, order, count, damage setting, clip, rig asset or scene is saved or changed.
The tool opens a temporary preview scene and saves only the bank, after evaluation has completed.

Evaluated receiver triangles and the selected weapon/shield triangles are queried through triangle
BVHs. Limb sources use actual skinned triangles whose vertices are primarily weighted to the selected
hand/foot and its descendants. Two-sided vertex, edge-midpoint and face-center sampling followed by
exact nearest-surface projections refines the contact. This is a sampled closest-pair search, not
an exhaustive triangle/triangle intersection solver. It never uses stale imported bounds.

A candidate must improve at least one distance by 1 cm, worsen neither by more than 5 mm,
lie within 15 cm of the selected source and 2 cm of the receiver, and pass those checks in both
facing directions. Otherwise it is preserved with a reason. The 15 cm source limit is a guard,
not a claim that a 15 cm separation is a physical intersection; actual gaps are reported per cue.
The chosen point is normally directly on the receiver surface. The runtime's existing 1.5 cm
camera-facing effect offset is deliberately excluded from geometry diagnostics and remains intact.

Outputs are in `GeneratedAssets/ContactPlacementRepair/`:

- `Preview.csv`, `Repair.csv`, `Validation.csv`: fighter, move, cue index, exact source seconds,
  selected source, facing, old/new bone, proposed local offset, before/after source and receiver
  distances, world contact, acceptance and reason. Rejected rows describe a proposal that was not saved.
- Matching `.txt` summaries explain skipped/failed evaluations.
- `*Geometry.txt` records participating renderers, face counts, reconstructed world bounds, skin
  quality and maximum vertex disagreement with both old `BakeMesh(false)+TR` and `+TRS` transforms.
- Optional `AuditLiveContact(impact)` can be called from the root-owned contact capture callback.
  It does not resample the animation. `Live_*.txt` records exact event/display times, actual effect
  position, rendered source/receiver distances and projected screen bounds; paired OBJ files contain
  the evaluated world triangles for an independent overlay or inspection. The callback is Editor-only.

Validation is read-only and throws when any eligible saved anchor exceeds the reported limits or
cannot be evaluated. A repair may improve some cues while preserving unresolved cues; review both
accepted and rejected rows. Missing geometry, unsupported limb sources, invalid source times or
implausible body size are explicit failures, never replaced by a guessed chest point.

## Root validation and captures

After repair, verify that the bank diff contains only `contactBone` and `contactOffset` changes.
Run the saved-anchor validation, then the existing real Play Mode presentation capture at 1920x1080.
Revisit Mankey `Heavy_6` at the unchanged `1.43667` seconds, and review all other changed cue times
in `Repair.csv`, including Shield/limb selections and mirrored attacker direction. Confirm that the
actual effect center follows the selected striking surface at the receiver. Check existing hit-event
multiplicity and damage validation separately; this module does not advance the damage clock.

Self-check: all five Editor source files compile with Unity's bundled Roslyn compiler and the
references from `Assembly-CSharp-Editor.csproj`, plus the built runtime project assemblies.
Compilation is a code check; it is not evidence that the calibration or visual captures passed.

## Executed result and resolved visual issue

Root executed the repair and saved one right-foot anchor improvement. All 60 authored melee
anchors passed subsequent validation in both directions using explicit bone/bind-pose geometry.
The first Heavy_6 sword anchor was already on the low blade contact and was preserved.
The screenshot mismatch came from the source Animator refreshing native weapon bones during
hit-stop while the retargeted fighter remained frozen. Reapplying the same source sample in
FrankBattlePairPlayback.LateUpdate during hold/menu pause fixed the visible weapon drift.
This does not advance time or emit another contact. Final_Mankey_Heavy_6_Heavy_1.png shows
the rendered blade at the spark. The calibration never changes source cue timing or damage.

AuditLiveContact performs expensive geometry extraction and OBJ export. Use it only for
diagnostics; it is deliberately disconnected from normal captures and performance runs.
