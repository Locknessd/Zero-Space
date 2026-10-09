Heavy Spear and Assassin now have eight reviewed weapon contacts per fighter.
Spear gained two reverse strikes at approximately 2.14 and 2.41 seconds.
Assassin gained five fast cuts/stabs between 1.71 and 2.52 seconds and one
weapon contact on entering the throw at approximately 3.00 seconds.
Assassin also retains the damaging final landing at 4.10 seconds, making nine
damage presentation contacts. Accepted backend damage and final HP are unchanged.

The complete source motion was sampled at 120 Hz for both fighters. Missing
strikes were reviewed in pose sheets and their first blade/body surface contact
refined at 480 Hz. Sustained contact while gripping/throwing the receiver does
not repeatedly create hits. Each new stroke has a shared impact cue for VFX,
audio and HP, plus a matching weapon swing. The two expanding contact rings and
immediate contact flash remain active on each new impact.

One existing Meme/Pepe Spear hit was refined from 2.738330 to 2.734163 seconds:
its measured blade/body gap fell from 3.8289 cm to 0.0223 cm. The preceding
swing moved by the same amount, and the body-surface anchor was recalibrated.

- `FullMotion.csv`, timeline sheets and `ContactIntervals.txt`: complete motion survey.
- `CandidatePlan.csv`, candidate sheets and `Candidates.csv`: reviewed strokes.
- `MissingSelection.csv`: the sixteen added impact times, groups and measured gaps.
- `ExistingContactRefinement.csv`: the refined original Spear hit and swing.
- `Validation.txt`: 32 fighter/mirror/lethal/frame-skip cases, 256 surface contacts;
  all eight strokes covered; deliberately missing strokes rejected; exact damage
  totals, immediate rings, duplicate and cancellation checks passed.
- `Before/`, `After/`, `ContactPreviews.txt`: each added contact rendered at the
  same animation pose before and after completing the cue bank.
- `ScopeValidation.txt`: only the four Spear/Assassin profiles changed;
  other original cues and the Battle scene preserved.

Older six-hit Spear and two-blade-hit Assassin records in
`GeneratedAssets/BattleHeavyContactReview` describe the earlier calibration;
use this review for the completed Spear/Assassin timelines.
