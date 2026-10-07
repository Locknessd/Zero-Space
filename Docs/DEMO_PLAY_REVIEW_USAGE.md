# Five demo Play Mode review

From Edit Mode invoke `BattlePresentationWorkbench.BeginDemoPlayReview()`.
The existing request endpoint accepts:

```json
{"action":"invoke","value":"BattlePresentationWorkbench.BeginDemoPlayReview"}
```

The operation asynchronously runs the two ErbGameArt slash scenes, then legacy
Cartoon FX 1, 2, and 3. Each gets about 5.3 seconds of runtime, with two native
trigger attempts and Game view screenshots. Domain reloads and scene loading add
to the total duration. Endpoint acceptance does not mean the review has finished.
Read `GeneratedAssets/EditorPolishReview/DemosPlay/Report.txt` for completion.
Images are named with scene order, scene name, and attempt index.

Temporary scene copies and `playModeStartScene` isolate each run. The module
forces ordinary Play Mode reloads, preserves the previous start scene and Editor
options, and relies on Unity's Play Mode scene backup to restore loaded dirty
scenes without saving or closing the originals. It compares the loaded scene,
active scene, dirty flags, and root activation snapshot after each exit. Original
root activation is never modified. Prior time scale, Editor pause, background
execution, and listener pause are restored; audition is muted during the run.

The old mesh-path scene has no demo controller: its authored particle gallery is
restarted and reported explicitly. The new slash scene uses `Counter`; Cartoon FX
uses `OnNextEffect` and the native private `spawnParticle` method by reflection.
Capture age targets 180 ms after activation, subject to Editor frame scheduling.
Reports distinguish shader errors, URP tags, built-in incompatibility, and
unproven untagged custom shaders. Shader support flags and particle counts cannot
establish visual success: inspect the PNGs. Trigger failures are retained in the
report while subsequent attempts continue. Early Play Mode exit cancels the run.

The update loop reconciles persisted phases with actual Play/Edit Mode state.
Cleanup requires two Edit Mode updates with the original scene snapshot restored;
the next scene starts on a later update, without a one-shot delayed callback.
Reports include explicit exit requests, restored-scene checkpoints, and transition
phase details if a timeout occurs.

Static compilation checked this module against the project's existing Unity
6000.5.9f1 compiler references. No Unity review operation was executed during
implementation; runtime results require the root-controlled Editor invocation.
