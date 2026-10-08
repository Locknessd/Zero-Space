# Ambush retreat integration status

The authored retreat code is implemented and compiles, but the animation asset
has not yet been baked or installed. Existing saved gameplay still uses the
original Ambush clip. Do not treat this document as gameplay acceptance.

The baker preserves the native Ambush phase, then appends the native backward
step start and stop. It retains curve keys, tangents, source displacement,
quaternion continuity and animation events. Native-driver equivalence checks
must succeed before it saves the project-owned animation.

Ordered native sources (all clip local ID 7400000):

- Ambush: `610fba6d346a64f4583237c3ef4f7e46`.
- Backward start: `12987da400bbe864aae42fdf303bc647`.
- Backward stop: `ed2751f54f474b74397a144408a8fe03`.

The action definition now supports explicit original-source references for
adapted attacks. Inventory reports include those references as authored attacker
sources rather than losing native clip coverage when an adapted clip is used.
The installer replaces a previous profile by action label as well as source
identity, and rejects grounding with a stale duration.

The external-scene-change dialog has cleared. Run these Editor operations
serially to finish the retreat integration:

1. `CombatExpansionGreatSwordRetreatBake.Bake()` creates
   `Assets/CombatExpansion/Animations/GreatSword_Ambush_Retreat.anim` and validates
   native equivalence. Inspect `RetreatBake.txt` for actual results.
2. `CombatExpansionGreatSwordGrounding.BakeAmbush()` updates only Ambush's four
   avatar/role grounding tracks for the extended source duration.
3. `CombatExpansionGreatSwordGrounding.ValidateAmbush()` checks both directions
   on both fighter avatars at an independent sample rate.
4. `CombatExpansionGreatSwordSetup.Install()` persists the updated action,
   profile, definitions, source provenance and targeted scene entries.
5. Run affected contact/camera/recovery visual checks and
   `CombatExpansionPlaySession.BeginGreatSword()` through the ordinary queue.
   The victim must remain in its terminal native pose during the retreat, then
   get up with visibly adequate separation. Verify normal-speed playback.

All types above are in `FrankRetarget.Editor`. Compilation passed for both
runtime and Editor assemblies; native equivalence, rendered retreat quality,
updated grounding and gameplay checks remain pending.

The prior Play Mode failure at case 17 has a confirmed assertion defect:
`GetComponentsInChildren(false)` still returns components on an inactive query
root. The equipment fixtures put their colliders on that root. The assertion
now checks active hierarchy state for colliders and emitting trails, while still
requiring every base weapon root to be inactive. The existing report remains
historical evidence until a fresh suite completes; no gameplay pass is claimed.

The first live bake failed because the source clips have different binding
layouts. The baker now reconciles omitted constant curves against both driver
prefabs and native playback, and excludes only the unbound top-level Dummy003
helper. The revised baker passed private compilation but has not yet been run
in Unity. The previous failure report predates that revision and is not evidence
for the revised implementation.

This is an authoring checkpoint. The default study move now requires the baked
retreat asset, so Ambush study/installation operations deliberately fail until
step 1 succeeds; callers can request the original source with
`MakeMove(source, target, 0, originalAmbush: true)`. Saved gameplay continues to
reference the original clip. Do not claim a completed retreat or a fresh Play
Mode pass from this checkpoint.
