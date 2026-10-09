# Grounded SlapFace contact review

All eight cases completed in Unity 6000.5.9f1. Full source playback is measured
at 30 Hz with candidate contact windows at 240 Hz, plus image timestamps.
The probe compares the moving right-hand and head skin regions using exact
triangle surface distance. Zero means touch or intersection, not measured
penetration depth. Both regions include descendant bones and require at least
50% corresponding skin influence on every vertex of a selected triangle.

| Sequence | Giver | Selected source/pair time | Right hand to head gap, both directions |
| --- | --- | --- | --- |
| 1 | Mankey | 1.4000000 s | 0 m |
| 1 | Pepe | 1.3916667 s | 0 m |
| 2 | Mankey | 0.9333333 s | 0 m |
| 2 | Pepe | 0.9050000 s | 0 m |

The four positive-direction dual-view sheets were visually reviewed. Sequence1
uses source A giving and B receiving; Sequence2 uses B giving and A receiving.
Both performances contain one right-palm slap across the face, followed by
head/trunk recoil and a hand-to-cheek response. Disjoint near-zero samples
during the same sweep are not additional attacks. The reaction direction and
relative phase remain those of the paired source performance.

The setup is 0.8 m, receiver source yaw 180 degrees, original native source
clips/drivers, both participants unarmed with finger transfer, 0.12 s entry
blend and the saved per-avatar grounding assets. No contact-frame translation,
limb stretch, speed change or enlarged hitbox was used. Both lane directions
were measured. All selected contact samples have zero surface gap and body
clearance above the floor. Independent grounding validation separately covers
50,760 clearances at 361 Hz with zero penetration violations.

The timestamped sheets retain the original BattleScene lighting with a neutral
preview floor. They establish source choreography and geometric contact; they
do not establish final gameplay-camera framing or presentation quality.

The source ends standing with lowered hands. The optional 0.3 s owned return
to the actual idle controller still needs Unity validation. The native victim
also ends upright on a lethal exchange, so a suitable KO finish remains to be
implemented and verified. Full source tails are retained for now; responsiveness
and any later authored trim need gameplay review. No action is marked complete
by this study. Installation, confirmed feedback, cleanup and repeated real
queue use are separate required checks.
