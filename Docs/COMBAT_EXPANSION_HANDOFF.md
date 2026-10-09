# Combat expansion handoff — 9 October 2026

**Status: paused; the full request is not complete.** This file records the checkpoint and work left for the next run.
The user requested a commit/push and stop, then this handoff. Creating this document did not resume gameplay work.

Project: `/home/eragon/Project/unity/Zero-Space`.
Target scene: [Assets/Scenes/BattleScene.unity](../Assets/Scenes/BattleScene.unity).
Current project Editor: **Unity 6000.6.5f1**.

## Saved and pushed checkpoint

| Item | Checkpoint |
| --- | --- |
| Local branch | `NewDemo` |
| Local commit | `9f3970323b46fa4e353269849daf8943e3d20435` |
| Commit description | Fix Execution10 throw cue and checkpoint recovery validation |
| Pushed merge on `origin/NewDemo` | `2ec2ba81b0699158f97d5ad553199a2841b7457a` |
| Remote verification | 2026-10-09, 09:45 UTC |
| Merged-code compilation | Runtime and Editor: **0 errors**; 38 runtime / 14 Editor warnings |

The gameplay push used an isolated checkout to preserve the live Unity workspace. At that checkpoint, local HEAD
was an ancestor of the pushed merge, **0 ahead / 28 behind**. This did not mean the checkpoint was not pushed.
The isolated checkout was `/tmp/zero-space-push-733ycomy/checkout` and may not survive a future session.

At handoff creation, the main workspace had approximately 2,976 tracked modifications, mostly vendor imports, materials,
settings and logs. Local recordings and unsaved scene backups were excluded from the checkpoint.
Inspect and preserve these changes before reconciling the branch; do not blindly pull, reset, or `git add -A`.
This handoff is a subsequent documentation commit; the hashes above identify the gameplay checkpoint.
The user subsequently requested pushing this document and discarding unused leftovers.
Inspect current status for the resulting cleanup; preserve useful local drafts and project configuration.
The Unity 6000.6.5f1 version above describes the local workspace. Editor/package migration settings were excluded
from the gameplay checkpoint, whose tracked ProjectVersion still declares 6000.4.0f1.

## What is implemented

These are implementation milestones. Saved registration, numerical checks and passing lifecycle tests do not
replace final normal-speed visual review, listening, or the remaining per-action acceptance checks.

| Area | Implemented checkpoint | Still required |
| --- | --- | --- |
| Inventory | 461 source identities; 72 calibrated drivers; both-avatar source studies | Final role and duplicate classification |
| Atemi 1–3 | Saved actions, measured contacts, shared feedback and queue/server selection | Final transitions and abnormal-state review |
| Equipment | Owned unarmed suppression/restoration, stale-owner protection, terminal lethal poses | Verify for every new interaction |
| Hold / lariat / escape | Three saved actions, defender escape window, queue integration | Final choreography and broader per-action acceptance |
| Vol10 throws | Nine additional throws, grounding, victim and optional attacker GetUp | Fresh regression and final visual review |
| GreatSword | Ambush and Execution1–3 saved on both fighters | Recovery, presentation and final visual acceptance |
| SlapFace | Sequence1 and Sequence2 saved on both fighters | Lethal paths and final source/transition coverage |
| Rough Pack | Two looping source clips inventoried | Role/motion review and gameplay integration still pending |
| Samurai | Executions **01 and 10** saved on both fighters with unarmed victims | Finish 10; integrate **02–09** |
| Double axe | All 33 combos studied; 66 avatar adaptation checks | **0 gameplay combos integrated** |
| New KB punch/kick combos | Source studies and a 30-sequence coverage plan | **0 qualifying combos accepted; at least 20 required** |
| Shared tooling | Contact anchors, grounding, reaction tracks, absolute pose sampling, CPU-skin captures | Full final regression and performance acceptance |

The saved BattleScene has **32 unique registered move IDs, each on both fighters** (64 entries).
This includes existing actions and does not count as 32 completed new actions.
All 17 non-idle Vol10 pairs are represented through new or reused registrations.

Reachability uses `GameManager.EnqueueCombatAction(side, actionId)`, exact server `animationId` selection,
and the existing F8 animation browser. Some paired actions require explicit selection rather than random pools.
R/T queue local hold-to-lariat; the defender uses Q/E or `RequestThrowEscape(side, playbackId)`.
The escape window is 0.15–0.55 action seconds. Server-resolved outcomes retain health and lethality authority.
This remains a server-resolved exchange game; do not invent unsupported free-movement fighter rules.

## Tests already recorded

Paths in this table are relative to `GeneratedAssets/CombatExpansion/`.

| Saved evidence | Result | Scope / limit |
| --- | --- | --- |
| `AtemiQueuePlayMode.txt` | 20 queue cases passed | Normal local queue coverage |
| `AtemiAuthoritativeDamagePlayMode.txt` | 24 normal exchanges plus lethal passed | 77 damage contact frames |
| `EquipmentPlayMode.txt` | 20/20 passed | Actual-fighter lifecycle, using Atemi2 fixtures |
| `GrapplePlayMode.txt` | 40/40 passed | Queue, escape, authority and interruption |
| `ThrowPlayMode.txt` | 70/70 passed | Range, queues, lethal brainbuster and dual-recovery interruption |
| `ThrowAuthoritativeDamagePlayMode.txt` | 48 normal exchanges plus lethal passed | 101 damage contact frames |
| `GreatSwordPlayMode.txt` | 48/48 passed | Four saved pairs; visual acceptance separate |
| `SamuraiStudy/PlayMode/Report.txt` | Execution01: 40 cases / 44 activations passed | Not Execution10's suite |
| `SlapStudy/PlayMode/Report.txt` | 48 main cases / 56 plays, plus 8 range guards passed | Nonlethal only |
| `SlapStudy/PlayMode/RecoverySmoke/Report.txt` | 14/14 passed | Recovery smoke coverage |
| `SamuraiStudy/Execution10PlayMode/Report.txt` | 34/40 passed, failure at case 35 | Revised harness rerun pending |

These results belong to their recorded code revisions. After the absolute base-sampling change,
Samurai01, Grapple and GreatSword regressions passed; fresh Throw, Slap and GreatSword recovery regressions
remain pending. Grapple/GreatSword snapshots are in `BaseSamplingRegression/After/`.
No new Unity tests were run while writing this handoff.

## Latest Execution10 work

**Done and pushed:**

- Replaced the incorrect throw sword arc with `grapple_release` at 1.2 seconds.
- Retained damaging `body_fall` at 1.450000 seconds for Mankey / 1.462500 for Pepe.
- Retained `thrust_swing` at 1.8 seconds and damaging `stab_hit` at 1.879167 / 1.862500 seconds.
- Neither damage cue is a final landing; the throw landing must not suppress the later stab feedback.
- Refreshed guarded recovery evidence and remeasured contacts after the Unity upgrade.
  The stale importer/dependency-hash installation blocker is resolved.
- Inspected actual contact screenshots for both attackers: the incorrect early sword arc is removed.
- Saved the failed real gameplay report and recovery trajectories for diagnosis.
- Revised the pause harness to test live particles at the stab contact, then recovery entry separately.
  This revision compiled successfully but **has not run in Unity**.
- Installed `CombatExpansionRecoveryLocomotionStudy.CaptureCandidates`; compiled, **not run**.

**Latest actual gameplay result: 34/40 cases passed, then case 35 failed.**
Failure: `Supine pause must cover live particle simulation, not only presentation counters.`
The particles had expired by the first GetUp frame. This is a coverage-precondition failure;
it does not establish a broken particle pause clock. Cases 35–40 have no completed pass evidence in that run.
The revised harness still requires live-particle pause/resume evidence at contact and a separate recovery pause.
Do not describe the revision as passing until it has actually run.

**Real motion defect still open:** native victim hips end about 1.2–1.3 m away from the controller fight plane.
The 0.12-second recovery blend removes this depth offset too quickly. Exact first-frame continuity passes,
but that does not validate the subsequent movement. `CompleteSourceMotion()` preserves hips X only,
while `CombatPositioningController` and recovery depth constraints enforce lane Z.

Required solution: preserve the native endpoint through owned GetUp, then return naturally to the fight plane
using inspected locomotion. Keep one owner of root translation, bound it to the exact fighter/playback ID,
and release it on completion, cancel, reset and disable. Preserve lethal terminal poses and once-only callbacks.
A longer blend, hidden teleport, forced lane clamp, or root interpolation with visible foot sliding is not acceptance.

A recovery-depth lease is only a **local staged draft**, not installed runtime code:
`Library/CombatExpansionTools/recovery-depth-lease-module/`.
Its proposed API acquires, queries, updates and releases depth ownership for an owner/fighter/playback ID,
with a 3 m bound and stale-owner checks. Pair integration, locomotion, and lifecycle validation remain unfinished.

## Next run: do these first

1. Read this handoff, `Assets/AGENTS.md`, the original brief, and the current Git diff/status.
   Preserve the dirty workspace and any unsaved Unity scene before branch or Editor operations.
2. Inspect the current Editor and `Logs/Editor.log`. Last known state was Edit Mode with BattleScene dirty.
   The last isolated test restored its previous scene. A script refresh had not finished loading the latest code;
   no Editor restart was performed. Offline compilation does not prove the running Editor loaded that assembly.
3. Complete script refresh/domain reload and verify compilation before invoking the revised tests.
4. Run the revised Samurai10 suite; inspect its terminal report, recovery trajectories and actual motion.
   A returned `BeginSamurai10` invocation only starts the asynchronous test; wait for terminal completion.
5. Capture locomotion candidates on both actual fighter Avatars: `KB_Sidestep_L`, `KB_Sidestep_R`, and existing walk.
   Inspect chronological two-view sheets and root/hips/feet trajectories before choosing a return motion.
6. Finish recovery depth ownership and an authored-looking return, then rerun both actors/both directions,
   pause, interruption, reset, disable, lethal retention, repetition and stale-owner tests.
7. Run affected regressions, then continue the remaining collections below. Update this handoff with new evidence.

The local bridge helper can issue these commands **one at a time**, from the repository root:

```bash
python3 Library/CombatExpansionTools/command.py status
python3 Library/CombatExpansionTools/command.py refresh
```

After Editor compilation is verified:

```bash
python3 Library/CombatExpansionTools/command.py invoke FrankRetarget.Editor.CombatExpansionPlaySession.BeginSamurai10
```

After the suite finishes and restores its scene:

```bash
python3 Library/CombatExpansionTools/command.py invoke FrankRetarget.Editor.CombatExpansionRecoveryLocomotionStudy.CaptureCandidates
```

Do not overwrite a pending bridge request or run multiple Unity commands concurrently.
An observer timeout does not cancel the Unity operation. Inspect the current request/process before retrying.
The helper lives in ignored `Library`; if missing, use the tracked `Assets/Editor/BattlePresentationWorkbench.cs` protocol.
Candidate output will be under `GeneratedAssets/CombatExpansion/SamuraiStudy/RecoveryLocomotionCandidates`.

## Remaining full scope

- [ ] **Frank demo:** finish exact canonical/source-role tracing and integrate every remaining distinct action/support role.
  Remaining tester-only scope includes three Insane full combos and their 14 step choices,
  plus two alternate Warrior reactions. Full/step and source-rig equivalence remain unresolved.
- [ ] **Existing integrations:** finish normal-speed choreography, contact, recovery and abnormal-state acceptance
  for Atemi, grapples, throws and GreatSword. Do not infer full acceptance from registrations or grounding alone.
- [ ] **Double axe:** integrate all 33 distinct combos, both attachments, every intended strike and victim reaction,
  complete presentation and reachable gameplay. Contact/reaction candidates remain provisional.
  `AxeContactStudy/DenseCombo01FrontalReaction` already contains captures; inspect them before rerunning.
- [ ] **Samurai:** finish 01/10 acceptance and integrate 02–09 with owned unarmed victims.
  Execution02's orientation variant passed numerical grounding/contact capture but contact poses remain provisional;
  it is not registered. Execution03/05 captures exist; 05 still needs orientation adaptation.
  `CombatExpansionSamuraiStudy.CaptureExecution08KickContacts` has not run.
- [ ] **SlapFace / Rough:** finish source-equivalence coverage, all required recoveries and lethal outcomes.
  SlapFace's four imported identities are used across two nonlethal actions. Rough's two separate clips
  still have no verified BattleScene role or completed gameplay evidence; do not count them as SlapFace coverage.
  Slap lethal candidate grounding/capture tools exist but have not run:
  `CombatExpansionSlapLethalPreview.BakeCandidateGrounding`, `ValidateCandidateGrounding`, `CaptureGroundedCandidates`.
- [ ] **KB punch/kick:** implement at least 20 meaningfully distinct NEW combos and cover all 86 usable attack clips.
  Existing combos, Atemi, renamed sequences and catalogue entries do not count.
  Current P01 candidate is JabL1 → JabR1 → HookR; older catalogue naming is not accepted implementation.
  Pepe's first jab still needs region/range diagnosis; later contacts and zero unsigned gaps do not prove a valid hit.
  Next diagnostic: `CombatExpansionKbComboStudy.CapturePepeJabRegionDiagnostics`
  (0.78/0.86 m, 0/0.04-second entry, head versus torso, first 0.3 seconds at 240 Hz).
- [ ] **Combat rules:** verify confirmation, supported misses/blocks, reaction continuation, legal links and early KO
  against the actual exchange rules, without inventing guaranteed-combo claims.
- [ ] **Presentation:** every action/contact needs appropriate VFX, routed audio, lighting, camera, hit-stop and UI;
  verify weapon/equipment restoration and cleanup. Review normal speed and contact frames with reduced effects/shake.
- [ ] **Final validation:** actual listening, all lifecycle exits, repeated queues, bounded pools/performance,
  authoritative damage, fresh scene reload and Play Mode, and final Console/error accounting.
  Editor timings and heap deltas are diagnostic evidence, not a finished standalone performance benchmark.

## Evidence and notes to read

All report paths below are under `GeneratedAssets/CombatExpansion/` unless otherwise stated.

- Current Execution10: [gameplay checkpoint](../GeneratedAssets/CombatExpansion/SamuraiStudy/Installation/Execution10Gameplay.md),
  [failed lifecycle report](../GeneratedAssets/CombatExpansion/SamuraiStudy/Execution10PlayMode/Report.txt),
  `SamuraiStudy/Execution10PlayMode/RecoveryTrajectory/`, and `SamuraiStudy/Execution10SupineRecovery/`.
- [Overall implementation history](COMBAT_EXPANSION.md), [Frank coverage](COMBAT_FRANK_COVERAGE.md),
  [canonical tracing](COMBAT_FRANK_CANONICAL.md), [GreatSword](COMBAT_GREATSWORD.md),
  [reaction tracks](COMBAT_REACTION_TRACK.md), and [combat feel review](COMBAT_FEEL_VIDEO_ANALYSIS.md).
- [KB catalogue](../GeneratedAssets/CombatExpansion/KbComboStudy/Catalogue/kb-combo-catalogue.md)
  is a plan, not proof of gameplay implementation.
- Historical paragraphs in older documents can be superseded by later checkpoints.
  In particular, older Unity versions, “Samurai not integrated,” and the old Execution10 hash blocker are stale.
  Frank coverage's older GreatSword demo-only classification is also superseded by its saved 48-case suite.

Local-only notes under `Library/CombatExpansionTools/` may disappear if Unity's Library is rebuilt:
`recovery-depth-return-findings.md`, `recovery-depth-lease-contract.md`, `recovery-depth-lease-module/`,
`recovery-locomotion-module/`, `kb-next-link-findings.md`, `push-state.json`, and `push-compile/Results.json`.
Keep the distinction between local drafts, installed code, captured evidence and accepted gameplay.

Original full request for the next agent, if the attachment is still available:
`/home/eragon/.codex/attachments/47e2c55b-9590-432d-91a9-4dd1b46b8307/pasted-text-1.txt`.
The required outcome is all distinct Frank actions, all 33 axe combos, all 10 Samurai executions,
SlapFace/Rough interactions, at least 20 new KB combos with usable-source coverage, and all final quality gates.

Suggested next-run prompt:

> Read Docs/COMBAT_EXPANSION_HANDOFF.md and resume the remaining combat expansion from this checkpoint.
> Preserve the dirty workspace. First verify the current Unity compilation, rerun the revised Execution10 suite,
> then inspect recovery locomotion candidates and fix the depth return. Continue the full remaining scope;
> do not count studies, registrations or compilation alone as accepted gameplay.
