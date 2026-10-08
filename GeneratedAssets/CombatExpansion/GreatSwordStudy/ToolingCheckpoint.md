# GreatSword tooling checkpoint

This checkpoint adds editor tools and source-pose review evidence. It does not
register or accept the four GreatSword actions as completed gameplay content.

## Included tools

- Guarded installer, draft contact specifications, measured profiles and action definitions.
- GreatSword ordinary-queue Play Mode checker and session launcher.
- Scene registry writer that targets selected action entries while preserving unrelated scene data.
- Dirty-scene snapshot helper, fine-contact captures and blade-only contact scan tools.

`ContactsReviewed` remains false. Contact timings, landing timings and reverse-facing
anchor validation still need review before the installer may run.

## Evidence and validation

- Current workspace runtime and editor C# compilation passed on Unity 6000.5.9f1
  with 39 and 14 warnings respectively. This check uses the local workspace,
  including its pre-existing legacy CFX compatibility change.
- All 16 fine-contact PNG sheets decoded successfully. Times.txt and
  Trajectories.csv cover both avatars, four pairs and both directions consistently.
- FineContacts/Ground.csv is excluded: 176 of 192 samples were non-finite because
  the capture helper measured disabled renderers. The helper now enables them
  before measurement, but its corrected capture has not been rerun.
- The new ordinary-queue Play Mode suite, blade scan and scene-save integration
  have not been run in Unity. Compilation is not gameplay or visual acceptance.

## Remaining review

Finalize actual blade contacts and first landing times, validate stored anchors
in both directions, then install and run the ordinary gameplay suite. Ambush
recovery still has a visible attacker/victim overlap that must be resolved.
Existing source-motion and recovery reports retain their original limited scope.
