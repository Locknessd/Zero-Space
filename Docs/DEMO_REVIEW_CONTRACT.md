# Five demo runtime review contract

Root owns the Unity Editor job queue. Do not invoke Unity or change scenes yourself.
Implement a modular Editor automation partial of BattlePresentationWorkbench in a new file
Assets/Editor/BattlePresentationWorkbench.Demos.cs, under about 300 lines.
Public static BeginDemoPlayReview() is called from Edit Mode. Hook EditorApplication update
and playModeStateChanged via InitializeOnLoadMethod as needed. Review directory is the
existing public const BattlePresentationWorkbench.Review. No edits to other code files.

Review each exact scene from the user brief, one at a time in Play Mode for roughly five
seconds. Inspect and trigger its existing demo controls or authored effect selector. Capture
its Game view at a visible effect age and log its actual triggered prefab and shader status.
Use project source files for setup reference. Current preview AuditDemos only opens preview
scenes and leaves Cartoon FX empty because Start and spawning never run.

Preserve all current dirty scenes and root active states. BattleScene is loaded additively;
the original old slash demo is also loaded dirty with disabled roots, user work is preserved
there. Prefer temporary scene copies following FrankBattleSfxPlayCheck conventions. Never
save imported source scenes. Isolate one camera and listener, restore the exact loaded scene
setup and root active states on completion or exception, and leave Edit Mode. Reset any
Time.timeScale changes made by demo scripts only in the test scene, preserving user pause
outside the test. Existing request endpoint allows invoking BattlePresentationWorkbench.

Persist a plain text report and Game view screenshots under Review plus DemosPlay. Classify
legacy shader incompatibility honestly under the assigned URP pipeline. Source demo scripts
must remain unchanged. Do not do audio audition or claim rendered success without root
running the operation and inspecting captured output. Root will execute and review results.
