# Remaining Samurai contact review

This is provisional source-pair evidence, not gameplay acceptance.

The list below records the original capture. Updated03/05 recapture evidence is at the end.

- Execution02 Mankey_Pepe_Positive: CAPTURED; 574 samples; minimum interval gap 0.668902 m; 0 intersection intervals (not approved strikes).
- Execution02 Pepe_Mankey_Positive: CAPTURED; 629 samples; minimum interval gap 0.631912 m; 0 intersection intervals (not approved strikes).
- Execution02 Mankey_Pepe_Negative: CAPTURED; 574 samples; minimum interval gap 0.668917 m; 0 intersection intervals (not approved strikes).
- Execution02 Pepe_Mankey_Negative: CAPTURED; 615 samples; minimum interval gap 0.631911 m; 0 intersection intervals (not approved strikes).
- Execution03 Mankey_Pepe_Positive: FAILED; 719 samples; minimum interval gap 0.000000 m; 3 intersection intervals (not approved strikes).
- Execution04 Mankey_Pepe_Positive: CAPTURED; 736 samples; minimum interval gap 0.000000 m; 1 intersection intervals (not approved strikes).
- Execution04 Pepe_Mankey_Positive: CAPTURED; 743 samples; minimum interval gap 0.000000 m; 1 intersection intervals (not approved strikes).
- Execution04 Mankey_Pepe_Negative: CAPTURED; 736 samples; minimum interval gap 0.000000 m; 1 intersection intervals (not approved strikes).
- Execution04 Pepe_Mankey_Negative: CAPTURED; 743 samples; minimum interval gap 0.000000 m; 1 intersection intervals (not approved strikes).
- Execution05 Mankey_Pepe_Positive: FAILED; 811 samples; minimum interval gap 0.339816 m; 0 intersection intervals (not approved strikes).
- Execution06 Mankey_Pepe_Positive: CAPTURED; 574 samples; minimum interval gap 0.000000 m; 1 intersection intervals (not approved strikes).
- Execution06 Pepe_Mankey_Positive: CAPTURED; 540 samples; minimum interval gap 0.000000 m; 2 intersection intervals (not approved strikes).
- Execution06 Mankey_Pepe_Negative: CAPTURED; 574 samples; minimum interval gap 0.000000 m; 1 intersection intervals (not approved strikes).
- Execution06 Pepe_Mankey_Negative: CAPTURED; 540 samples; minimum interval gap 0.000000 m; 2 intersection intervals (not approved strikes).

Execution02 Mankey-to-Pepe positive sheets 01–03 were visually inspected.
The victim kneels and collapses while the blade remains visibly separated.
The later downward blade motion also misses the grounded victim. These poses
do not support registering damage or reusing Execution01 contact timings.
Both assignment/direction cases have the same broad miss problem.

The original Execution03 capture failed contact-anchor comparisons at 0.475 and
2.3875 seconds because hips-local coordinates were compared to a world-metre
threshold. The installed repair compares stored selected-triangle world points
with the same 0.001 m gate. Unity validation passed 77 fixtures and 48 actual
rig queries. The complete four-case recapture now passes. Original failed evidence is
preserved in Remaining/History.

The source scene itself contains A at origin/yaw 0 and B at (0,0,1.7)/yaw 180;
its supplied controller selects only Execution01. Execution02 uses different
root-orientation import settings. Original-native comparison found a minimum
blade gap of about 0.684654 m on both lanes. Project-owned byte-identical FBX
variants changing only Execution02 keepOriginalOrientation to true produced
blade intersections on both lanes at 0.466667 s. Source preservation guards
passed. Positive native sheets were visually inspected; both the early blade
motion and downward stab align substantially better. Actual Mankey/Pepe
variant grounding and contact validation are still required.

Existing passing captures still use the original anchor comparison. They are
candidate evidence only; per-contact anatomy, complete choreography, support,
recovery, presentation and real gameplay integration remain unfinished.

Execution05 also failed backward replay, with actual world trajectory/support
errors reaching 0.001105 m against the unchanged 0.001 m gate. This differs
from Execution03's local-coordinate anchor error. Diagnosis traced the drift
to history-dependent native base-clip root sampling. Absolute base sampling
now restores the configured source hierarchy before evaluating the requested
clip time. A four-pair diagnostic measured zero world-pose replay error while
retaining authored native root travel; forward pose changes were below 0.15 mm.
The existing Samurai01 real Play Mode suite passed 40 cases / 44 activations
after the fix. Full Execution05 contact recapture now passes all four cases.

Execution06 Mankey-to-Pepe positive sheet01 was visually inspected. Its first
blade motion misses while the receiver reacts; the later thrust intersects.
A later low movement/fall needs separate body-contact review. The final victim
is face-up, so Execution01's prone recovery must not be copied into this action.

Execution07 and Execution08 finished all four assignment/direction captures.
Mankey-to-Pepe positive sheets 01 and 02 of each were visually inspected.
Execution07's blade sweeps stay visibly separated while the receiver recoils;
the minimum sampled blade gaps are 0.471894 m for Mankey and 0.301213 m for Pepe
in the positive direction. Its final receiver torso is prone.
Execution08 includes an early overhead sword motion followed by a front kick
against a kneeling receiver. The sword does not touch the victim in the sampled
poses (positive-direction minimum gaps 0.699379 m and 0.631912 m).
The kick is a distinct body-contact opportunity and is not measured by the
blade-only kernel. Its visible range/alignment also requires correction before
assigning an impact. Blade non-contact must not erase this kick from coverage.
Its receiver also ends prone. These findings do not approve either action.

Execution09 Mankey-to-Pepe positive sheets 01 and 02 were visually inspected.
They show a backward kick near 0.35–0.5 seconds and a jumping downward leg
motion around 1.4–1.75 seconds. Those body-contact opportunities must be
measured separately; a blade-only miss is not evidence that the whole action
has no strikes. Positive cases have 586/561 blade samples with no intersection.
No complete action contact map or recovery approval follows from those gaps.

Execution10 Mankey-positive sheets01/02 and Pepe-positive sheet01 were visually
inspected. The choreography is a grab, turn and throw onto the receiver back,
followed by a downward stab. The early Pepe blade intersection at 0.395833 s
occurs during the grab and is not automatically a damaging sword strike.
The receiver reaches ground around 1.45–1.55 s in the inspected sheets; precise
body-support timing and region still need authoring. The later blade candidates
are 1.879167 s for Mankey and 1.8625 s for Pepe. Both final victims are face-up.
An actual source-completion-to-supine-getup check is being prepared using the
existing controller recovery; no Execution10 action is registered yet.

## Completed03/05 recapture after anchor and base sampler repairs

Both requested recaptures completed. All nine remaining executions now have four completed cases; this is capture coverage, not gameplay integration. Existing passing reports retain their original measurement version. The original aggregate Status.txt remains a historical failure; RecaptureStatus.txt is the authoritative retry result.

| Execution | Case | Samples | Replay checks passed | Max replay position error m |
|---|---|---:|---:|---:|
| 03 | Mankey_Pepe_Negative | 719 | 40/40 | 0 |
| 03 | Mankey_Pepe_Positive | 719 | 40/40 | 0 |
| 03 | Pepe_Mankey_Negative | 763 | 36/36 | 0 |
| 03 | Pepe_Mankey_Positive | 763 | 36/36 | 0 |
| 05 | Mankey_Pepe_Negative | 811 | 68/68 | 0 |
| 05 | Mankey_Pepe_Positive | 811 | 68/68 | 0 |
| 05 | Pepe_Mankey_Negative | 898 | 68/68 | 0 |
| 05 | Pepe_Mankey_Positive | 898 | 68/68 | 0 |

Execution03 Mankey-positive sheets01–04 were viewed after recapture. The first blade motion enters the lower torso; the later downward motion meets the kneeling victim. The blade also crosses the falling victim leg near3.096s. That late overlap is not automatically a new intentional strike. The receiver ends prone (front normal dot up about-0.995). Complete contact authoring and recovery/presentation remain pending.
