# Individual cinematic shot directions

The direction library covers all 47 selectable animation entries: eight Frank pairs, eighteen Vol10 entries, seventeen combo entries and four Execution Sample entries. Each direction is applied to both character-role recordings by the scene camera bake, producing 94 fitted takes. The library contains 361 hand-authored timing beats.

## Direction method

These plans use the live Unity motion survey at `/tmp/frank-camera-survey.json`: measured pelvis, hand and foot trajectories, receiver height peaks and speed changes, and configured hit times where available. The prose describes intended framing and inferred movement phases. It does not claim that every phase has been visually approved. Live rendered framing and continuity validation belongs to the camera bake and review pass.

The approximately 31-degree reference lens provides the common style. Most variations stay close to that lens; aerial and spinning actions briefly reach 35-37 degrees. Camera travel remains on one half-space of the action axis. Short strikes get only small angular changes; longer weapon sequences receive longer reveals. Aerial throws widen before the measured apex, and ground finishes gain modest elevation to preserve separation. The idle camera returns exactly to its opening parameters.

Timing annotations are sampled estimates unless marked as a configured hit. The survey has roughly 0.10-second spacing; close event timings should not be interpreted as exact contact frames. Execution clip endpoint resets are deliberately excluded as choreography events. Renderer bounds in the initial survey were oversized, so these direction parameters require the final visible-geometry framing pass.

## Coverage and parameters

| Family | Entry | Duration | Beats | Yaw range | Elevation range | FOV range | Tracking / lead |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| frank 0 | 2-Handed | 5.833 s | 8 | 73-99 | 17.5-22 | 31-34 | 0.27 / 0.10 s |
| frank 1 | Assassin | 5.833 s | 8 | 92-111 | 20-25 | 31-36 | 0.29 / 0.14 s |
| frank 2 | Dual | 7.333 s | 8 | 69-101 | 19-43 | 31-36 | 0.32 / 0.16 s |
| frank 3 | Greatsword | 6.000 s | 8 | 100-145 | 17.5-24 | 31-35 | 0.29 / 0.12 s |
| frank 4 | Katana | 6.000 s | 8 | 62-86 | 17.25-23 | 30.5-34 | 0.24 / 0.09 s |
| frank 5 | Spear | 6.833 s | 8 | 84-101 | 18-23 | 31.5-34 | 0.30 / 0.12 s |
| frank 6 | Warrior | 5.833 s | 8 | 76-99 | 17.5-22 | 31-33 | 0.25 / 0.10 s |
| frank 7 | Warrior · alt hit | 5.833 s | 8 | 99-119 | 21-24 | 31.5-33 | 0.31 / 0.08 s |
| vol10 0 | Idle | 1.500 s | 5 | 81-81.5 | 19-19.2 | 30.9-31 | 0.42 / 0.03 s |
| vol10 1 | Shoulder throw | 1.417 s | 7 | 83-89.5 | 19-25 | 31-33 | 0.20 / 0.10 s |
| vol10 2 | Brainbuster | 1.733 s | 7 | 98.5-106 | 18-26 | 31-35 | 0.23 / 0.13 s |
| vol10 3 | Atemi 1 | 1.450 s | 7 | 72-79 | 18-23 | 30.5-32 | 0.18 / 0.08 s |
| vol10 4 | Atemi 2 | 1.050 s | 6 | 106-111 | 20-22 | 30.8-32 | 0.16 / 0.06 s |
| vol10 5 | Atemi 3 | 0.800 s | 7 | Mankey 65-67 / Pepe 115-117 | 40-42 | 30.8-31.5 | 0.15 / 0.04 s |
| vol10 6 | Fisherman suplex | 1.783 s | 7 | 105-117 | 17.5-25 | 31-34.5 | 0.24 / 0.12 s |
| vol10 7 | Flank throw | 1.833 s | 8 | 66-77.5 | 24-38 | 31.5-35 | 0.21 / 0.14 s |
| vol10 8 | Hold | 1.100 s | 6 | 98-101 | 21-22 | 30.5-31 | 0.19 / 0.06 s |
| vol10 9 | Hold + lariat | 2.000 s | 8 | 75-86 | 18-25 | 31-34 | 0.22 / 0.10 s |
| vol10 10 | Powerbomb | 2.033 s | 8 | 98-109 | 17.5-26 | 31-37 | 0.26 / 0.16 s |
| vol10 11 | Dragon screw | 1.200 s | 7 | 63-73 | 23-27 | 31.5-33 | 0.18 / 0.09 s |
| vol10 12 | Throw escape | 0.833 s | 7 | 108.5-113 | 21-23 | 31-32.5 | 0.16 / 0.07 s |
| vol10 13 | Neck breaker | 1.583 s | 8 | 88-94.5 | 25-38 | 31-35 | 0.21 / 0.12 s |
| vol10 14 | Four-way throw | 1.350 s | 7 | 110.5-119 | 20-26 | 31-34 | 0.20 / 0.11 s |
| vol10 15 | Windmill throw | 2.117 s | 8 | 60-75 | 25-38 | 31-34 | 0.25 / 0.13 s |
| vol10 16 | German suplex | 1.933 s | 8 | 94-103 | 25-38 | 30.5-34 | 0.23 / 0.12 s |
| vol10 17 | Giant swing | 2.083 s | 8 | 80-89 | 23-26 | 32-35 | 0.28 / 0.14 s |
| combo 0 | Combo 1 Full combo | 2.833 s | 8 | 68-87 | 18-24 | 31-33 | 0.22 / 0.10 s |
| combo 1 | Combo 1 Step 1 | 1.930 s | 8 | 74-84 | 18-24 | 30.8-32 | 0.19 / 0.07 s |
| combo 2 | Combo 1 Step 2 | 2.030 s | 8 | 103-112 | 20-25 | 31-33 | 0.20 / 0.08 s |
| combo 3 | Combo 1 Step 3 | 2.150 s | 8 | 82-91 | 17.5-24 | 30.8-33 | 0.20 / 0.09 s |
| combo 4 | Combo 2 Full combo | 4.967 s | 8 | 87-115 | 22-40 | 31-37 | 0.30 / 0.15 s |
| combo 5 | Combo 2 Step 1 | 1.813 s | 8 | 93-98.5 | 20-24 | 30.9-31.5 | 0.22 / 0.05 s |
| combo 6 | Combo 2 Step 2 | 2.063 s | 8 | 112-121 | 22-25 | 30.8-32 | 0.23 / 0.06 s |
| combo 7 | Combo 2 Step 3 | 1.997 s | 8 | 78-89 | 18-24 | 31-34 | 0.18 / 0.10 s |
| combo 8 | Combo 2 Step 4 | 1.997 s | 8 | 98.5-108 | 21-25 | 31-33 | 0.19 / 0.09 s |
| combo 9 | Combo 2 Step 5 | 1.897 s | 8 | 66-77 | 19-25 | 31.5-36 | 0.22 / 0.13 s |
| combo 10 | Combo 2 Step 6 | 1.797 s | 8 | 97-102 | 19.5-24 | 31-33.5 | 0.18 / 0.08 s |
| combo 11 | Combo 3 Full combo | 3.900 s | 8 | 61-88 | 18-26 | 31.5-37 | 0.27 / 0.15 s |
| combo 12 | Combo 3 Step 1 | 2.030 s | 8 | 105-117 | 19-25 | 31.5-34.5 | 0.20 / 0.11 s |
| combo 13 | Combo 3 Step 2 | 1.963 s | 8 | 70-79 | 20-25 | 31-33 | 0.20 / 0.08 s |
| combo 14 | Combo 3 Step 3 | 2.030 s | 8 | 87-95 | 19-25 | 31.5-35.5 | 0.22 / 0.12 s |
| combo 15 | Combo 3 Step 4 | 1.730 s | 8 | 109-120 | 20-26 | 32-36 | 0.21 / 0.13 s |
| combo 16 | Combo 3 Step 5 | 2.363 s | 8 | 73-86 | 18-26 | 31.5-36 | 0.23 / 0.14 s |
| execution 0 | Ambush | 2.500 s | 8 | 100-115 | 18-23.5 | 30.5-33 | 0.21 / 0.10 s |
| execution 1 | Execution 1 | 3.800 s | 8 | 115-146 | 17.75-26 | 31-36 | 0.26 / 0.13 s |
| execution 2 | Execution 2 | 3.933 s | 8 | 100-122 | 22-27 | 31-36 | 0.27 / 0.14 s |
| execution 3 | Execution 3 | 3.433 s | 8 | 80-96 | 18-25 | 31-33 | 0.23 / 0.11 s |

## Per-animation rationale

### frank / 0

2-Handed: restrained three-quarter opening; ease toward side-on for the first receiver acceleration at 1.58 s, open space through the 2.37 s knockback, and hold the full axe/body silhouette through the 3.46 s hand-speed burst. Descend gently into the low 4.15 s follow-through before settling on the pair.

Beats: 0.000, 1.050, 1.580, 2.370, 3.460, 4.150, 4.850, 5.833 seconds.

### frank / 1

Assassin: favor an elevated flank to separate the acrobatic silhouettes. Establish the fast 0.89 s approach, let the camera breathe before the 3.46 s launch, widen through the measured 3.76-3.86 s airborne apex, then finish above the 4.94 s receiver landing with a quiet recovery hold.

Beats: 0.000, 0.890, 1.580, 2.750, 3.460, 3.860, 4.940, 5.833 seconds.

### frank / 2

Dual: start broad enough for both blades and climb gradually ahead of the 1.98 s lift. The first rendered pass made the separated airborne bodies too small around 4.03 s, so the revised shot reaches 35 degrees at the 2.28 s receiver apex and 40-43 degrees during the attacker ascent to 4.36 s. This elevated view compresses the vertical separation while retaining both actors and blades; taper the angle through the 5.05 s descent and finish with a measured settling dolly.

Beats: 0.000, 1.350, 1.980, 2.280, 3.570, 4.360, 5.050, 7.333 seconds.

### frank / 3

Frank Greatsword: retain a full-blade opening and the 1.40 s preparation from a rear three-quarter angle. Begin the deeper reveal through the 2.20 s sweep, then turn gradually toward the action depth as the receiver rises at 3.70 s. The first rendered pass made both actors tiny after the 4.40 s knockback; the revised 135-145 degree finishing angle places that separation into depth while preserving the same side of the action axis. This is the standard Frank pair, not an execution take.

Beats: 0.000, 1.050, 1.400, 2.200, 3.200, 3.700, 4.400, 6.000 seconds.

### frank / 4

Katana: use a controlled blade-line reveal rather than a broad orbit. The first burst at 1.67 s gets a compact forward dolly, then the 2.26-2.36 s crossing stroke receives extra lateral breathing room. Settle rotation before the slow receiver collapse around 4.33 s so the delayed reaction stays legible.

Beats: 0.000, 1.150, 1.670, 2.160, 2.360, 3.150, 4.330, 6.000 seconds.

### frank / 5

Spear: hold a near-side view so the full thrust line remains visible. Drift backward in angle through the 1.19 s hand burst and the 1.68 s reaction; sustain a wider frame across the long 3.27-4.75 s exchange. The finishing 5.94 s foot burst gets a gentle pull-out before the final low reaction.

Beats: 0.000, 1.190, 1.680, 2.180, 3.270, 4.750, 5.940, 6.833 seconds.

### frank / 6

Warrior: a firm low three-quarter opening builds into the 1.48 s strike. Advance the orbit only between the separate 2.27 s, 3.16 s and 4.25 s hand bursts, keeping modest elevation for shield and weapon separation. Gently raise the finishing frame for the receiver settling near 5.24 s.

Beats: 0.000, 1.090, 1.480, 2.270, 3.160, 4.250, 5.240, 5.833 seconds.

### frank / 7

Warrior alternate hit: direct the same attack cadence from the opposite depth bias within the same camera half-space. Begin above the shield silhouette, draw toward the receiver on the 1.58 s and 2.87 s reaction changes, then progressively level and tighten through the alternate-hit recovery. The distinct reverse arc keeps this entry visually separate without crossing the action axis.

Beats: 0.000, 1.090, 1.580, 2.270, 2.870, 4.250, 5.240, 5.833 seconds.

### vol10 / 0

Idle: a quiet breathing portrait of the pair, using the measured 0.75 s breathing trough for a barely perceptible dolly. The start and end match exactly so the 1.50 s idle loop does not pump or orbit away.

Beats: 0.000, 0.380, 0.750, 1.130, 1.500 seconds.

### vol10 / 1

Shoulder throw: read the grip and hip turn from a medium-wide flank. Open before the 0.76 s receiver apex and gently lift the eye-line through the 0.94 s downward acceleration. Hold a clear two-body ground composition after the throw; the arc is deliberately small for this 1.42 s action.

Beats: 0.000, 0.300, 0.470, 0.760, 0.940, 1.180, 1.417 seconds.

### vol10 / 2

Brainbuster: let the vertical lift lead the frame. Widen before the 0.96 s receiver height maximum, pause angular travel over the inverted apex, and retain overhead clearance through the 1.25 s descent. Increase elevation for the 1.64 s low landing so the overlapping bodies remain distinct.

Beats: 0.000, 0.190, 0.580, 0.960, 1.250, 1.640, 1.733 seconds.

### vol10 / 3

Atemi 1: a compact side-forward strike portrait. Settle the frame before the 0.48 s hand burst, retain room for the 0.58 s receiver response, then shift gently upward as the receiver falls through the 1.16 s movement burst. Keep the actor hands and the falling body in one continuous composition.

Beats: 0.000, 0.250, 0.480, 0.580, 1.000, 1.160, 1.450 seconds.

### vol10 / 4

Atemi 2: emphasize the sharp 0.57 s punch with a very short controlled push-in. Use a rear-biased flank for readable arm extension, keep the lens steady at contact, then relax the distance as the receiver settles around 0.95 s. Angular change is limited to five degrees across the entire take.

Beats: 0.000, 0.190, 0.430, 0.570, 0.860, 1.050 seconds.

### vol10 / 5

Atemi 3: this 0.80 s exchange needs very little camera travel. The first rendered pass hid much of Pepe behind Mankey during the 0.44-0.75 s turn, so the revised 65-67 degree flank exposes depth separation while keeping the early 0.18 s attack and 0.27 s reaction readable. Allow a subtle dolly release through the 0.44 s second-hand motion and finish with both feet visible as the attacker straightens. Final framing uses a steady 40-42 degree elevated view; the Pepe-attacker take uses 115-117 degrees of yaw so the smaller performer is in front of the larger receiver.

Beats: 0.000, 0.120, 0.180, 0.270, 0.440, 0.620, 0.800 seconds.

### vol10 / 6

Fisherman suplex: begin low enough to read the grip, open upward before the receiver apex at 0.69 s, and let the 0.99 s rotation pass through a steady wide composition. Ease toward a slightly higher finishing view as the attacker drops near 1.39 s and the receiver reaches the floor.

Beats: 0.000, 0.200, 0.590, 0.690, 0.990, 1.390, 1.783 seconds.

### vol10 / 7

Flank throw: favor the near side to expose the changing footwork. The initial 1.01 s capture stacked the torsos, so the revised shot rises ahead of the 0.77 s attacker apex into a 37-38 degree view over the 1.06-1.25 s lifting and throwing interval. This opens the limb silhouette while preserving the staggered 1.16 s receiver apex. Gradually lower the angle into the 1.74 s floor contact rather than dropping the camera with the bodies.

Beats: 0.000, 0.480, 0.770, 1.060, 1.160, 1.250, 1.740, 1.833 seconds.

### vol10 / 8

Hold: give the grip exchange an intimate full-body frame rather than a throw-sized wide shot. A slow inward drift lasts through 0.80 s, followed by a small distance release for the concentrated 0.90 s hand and pelvis motion. Keep the camera level enough to show the arms separating at the finish.

Beats: 0.000, 0.250, 0.500, 0.800, 0.900, 1.100 seconds.

### vol10 / 9

Hold plus lariat: draw the eye into the 0.80 s hand acceleration without rotating across the contact. Open the composition for the joint 1.10 s body drop, then lift slightly above the ground struggle. Finish with a measured sideward reveal through the quieter 1.70-2.00 s recovery.

Beats: 0.000, 0.300, 0.700, 0.800, 1.100, 1.400, 1.700, 2.000 seconds.

### vol10 / 10

Powerbomb: prioritize scale and landing readability. Pull out decisively before the 0.48 s lift and hold a wide elevated frame through the 0.77 s nearly three-unit receiver apex. Keep the camera arc almost parked during the 1.07 s plunge, then make a slow settling dolly after 1.55 s.

Beats: 0.000, 0.250, 0.480, 0.770, 1.070, 1.300, 1.550, 2.033 seconds.

### vol10 / 11

Dragon screw: reveal the leg capture from a higher near-side angle. Keep the 0.20 s leg lift and 0.40 s rotational acceleration within one broad horizontal composition. A small orbit reveals the twisting movement; settle into an elevated ground view after the attacker drops around 0.50 s.

Beats: 0.000, 0.200, 0.300, 0.400, 0.500, 0.850, 1.200 seconds.

### vol10 / 12

Throw escape: keep both torsos legible as the grip breaks. Use a short reverse arc from rear flank, give the 0.19 s hand burst and 0.37 s receiver acceleration a modest frame expansion, then return to a comfortable neutral full-body frame as both recover by 0.83 s.

Beats: 0.000, 0.190, 0.280, 0.370, 0.560, 0.740, 0.833 seconds.

### vol10 / 13

Neck breaker: the first rendered 0.29 s turn partly hid Pepe behind the receiver. Establish a higher 25-degree opening and rise ahead of the fast 0.49 s foot exchange, reaching 35.5 degrees for the 0.59 s attacker apex and 38 degrees over the synchronized 0.79 s drop. This reveals the joined body rotation; taper toward 29 degrees only after the 1.29 s receiver settling.

Beats: 0.000, 0.200, 0.490, 0.590, 0.790, 1.050, 1.290, 1.583 seconds.

### vol10 / 14

Four-way throw: start just behind the action line to show the wrist-and-shoulder turn. Build a gentle inward dolly through the 0.58 s hand motion, release the frame before the 0.77 s receiver lift, and hold the 0.87 s throwing burst from a high enough angle to distinguish both bodies.

Beats: 0.000, 0.290, 0.580, 0.770, 0.870, 1.080, 1.350 seconds.

### vol10 / 15

Windmill throw: the initial 0.74 s lifting capture obscured the attacker behind the rotating receiver. Replace the low view with a gradual 25-38 degree crane that reveals the grip before the 1.06 s receiver apex and the 1.15 s rotational burst. Retain the full arm-and-leg reach through the reaction at 1.73 s, then ease down to an elevated forward-flank ground composition without spinning with the throw.

Beats: 0.000, 0.380, 0.770, 1.060, 1.150, 1.440, 1.730, 2.117 seconds.

### vol10 / 16

German suplex: the initial 0.68 s lift stacked the two torsos from the original low rear-flank view. Establish the rear grip at 25 degrees, open gradually through the 0.39 s crouch and the 0.68 s paired height peak, then reach 37-38 degrees over the 0.87 s rotation and back-bridge. This higher view reveals the linked bodies while keeping the feet and floor response in frame. Taper after the late 1.74 s settling.

Beats: 0.000, 0.390, 0.580, 0.680, 0.870, 1.260, 1.740, 1.933 seconds.

### vol10 / 17

Giant swing: retain the full circular reach rather than chasing the receiver around the axis. Begin wide, drift only nine degrees across the take, and increase elevation and margin for the 1.09 s swing speed and 1.39 s height maximum. Preserve the broad frame through the 1.59 s release, then ease toward the grounded finish.

Beats: 0.000, 0.400, 0.790, 1.090, 1.390, 1.590, 1.880, 2.083 seconds.

### combo / 0

Combo 1 full: a continuous shallow flank arc connects the measured 0.29 s, 0.88 s and 1.37 s attack bursts. Keep the camera quieter during each contact, expand for the 0.98 s receiver launch, and continue the frame through the delayed 1.86 s reaction before a composed recovery. Avoid treating the three steps as one generic impact.

Beats: 0.000, 0.290, 0.680, 0.880, 0.980, 1.370, 1.860, 2.833 seconds.

### combo / 1

Combo 1 step 1: establish the crouched entry and land the shortest inward dolly at the configured 0.28 s hit. Release distance as the receiver moves around 0.48 s, then watch the falling response from a gently rising angle through 1.35 s. The long generated-reaction tail receives a calm finish.

Beats: 0.000, 0.100, 0.280, 0.480, 0.680, 1.000, 1.350, 1.930 seconds.

### combo / 2

Combo 1 step 2: use the rear flank to separate the strong 0.39 s off-hand sweep and advancing leg. Hold angular motion nearly still at the configured 0.38 s hit, broaden the frame for the 0.48 s receiver burst, then reverse-dolly smoothly into the floor response near 1.45 s.

Beats: 0.000, 0.200, 0.380, 0.480, 0.770, 1.060, 1.450, 2.030 seconds.

### combo / 3

Combo 1 step 3: the low 0.39 s preparation and configured 0.413 s hit lead into a 0.49 s hand burst. Take a near-side view, rise slightly as the attacker recovers, and retain space for the delayed 0.78 s receiver motion. Use the final second as an unhurried settling shot rather than extending the orbit.

Beats: 0.000, 0.200, 0.390, 0.413, 0.550, 0.780, 1.370, 2.150 seconds.

### combo / 4

Combo 2 full: a broad evolving tracking shot follows the six-stage combination, with separate beats for the 0.99 s opening rush, 1.89 s launch, 2.28 s attacker burst and 2.58 s receiver apex. The initial rendered pass at 2.73-3.73 s showed a large vertical gap that reduced both subject sizes. Rise gradually through 34-40 degrees to compress that separation, hold the whole airborne response, then descend toward the 4.07 s landing and a quiet recovery frame.

Beats: 0.000, 0.650, 0.990, 1.890, 2.280, 2.580, 4.070, 4.967 seconds.

### combo / 5

Combo 2 step 1: a restrained opening shot with an early configured 0.163 s hit. Use only a small distance accent because the measured attack motion stays modest, then keep the slow 0.95 s receiver descent readable. Let the late 1.34 s low pose resolve from a slightly higher framing angle.

Beats: 0.000, 0.080, 0.163, 0.380, 0.570, 0.950, 1.340, 1.813 seconds.

### combo / 6

Combo 2 step 2: preserve the deliberate slower strike rhythm. Ease toward a closer rear-flank composition at the configured 0.413 s hit, then gradually widen into the 0.79 s attacker motion and 1.28 s receiver descent. Hold the last grounded beat without a dramatic camera move unsupported by the action.

Beats: 0.000, 0.290, 0.413, 0.590, 0.790, 1.280, 1.670, 2.063 seconds.

### combo / 7

Combo 2 step 3: anticipate the fast 0.30 s foot and pelvis rush. Establish a wide side angle immediately, hold it through the configured 0.347 s hit, then use a short forward arc around the 0.90 s attack recovery. The receiver tail around 1.20-1.60 s gets a steady elevated finish.

Beats: 0.000, 0.180, 0.300, 0.347, 0.600, 0.900, 1.600, 1.997 seconds.

### combo / 8

Combo 2 step 4: begin on the rear side to reveal the opening leg extension, then draw in just before the configured 0.347 s hit and 0.40 s hand peak. Open again for the 0.90 s returning footwork. Finish with a slow angle decrease that keeps the receiver collapse distinct from the previous step.

Beats: 0.000, 0.100, 0.250, 0.347, 0.500, 0.900, 1.400, 1.997 seconds.

### combo / 9

Combo 2 step 5: reveal the jump as a vertical event. Open ahead of the configured 0.247 s hit, hold the 0.50 s attacker apex in a high full-body frame, and keep that space through the 0.70 s descent burst. Ease inward only after the attacker reaches the low pose around 0.90 s.

Beats: 0.000, 0.247, 0.300, 0.500, 0.700, 0.900, 1.500, 1.897 seconds.

### combo / 10

Combo 2 step 6: the configured hit arrives at 0.147 s, so begin ready for impact instead of inventing a long wind-up. Make a small pull-out for the 0.20 s hand burst and receiver rise near 0.40 s, then settle from a stable near-profile view as the receiver descends through 1.00-1.40 s.

Beats: 0.000, 0.080, 0.147, 0.200, 0.400, 0.700, 1.400, 1.797 seconds.

### combo / 11

Combo 3 full: create a single measured arc across the 0.30 s opening rush, the receiver ascent to 1.50 s, the 1.70 s second burst, and the attacker apex at 2.90 s. Keep the wider frame through the 3.10 s simultaneous finishing descent, then let the shot exhale into recovery.

Beats: 0.000, 0.300, 0.500, 1.500, 1.700, 2.900, 3.100, 3.900 seconds.

### combo / 12

Combo 3 step 1: the 0.29 s advancing pelvis speed needs immediate lateral room. Open into the configured 0.38 s hit, then follow the receiver rise toward 0.68 s with a small crane. The camera reverses only its dolly distance, keeping the rear-flank angle progression smooth through the 1.64 s reaction finish.

Beats: 0.000, 0.190, 0.290, 0.380, 0.680, 0.870, 1.640, 2.030 seconds.

### combo / 13

Combo 3 step 2: favor the near flank for the immediate 0.10 s hand motion, then let the configured 0.313 s hit receive a restrained inward dolly. Open upward for the 0.59 s receiver crest and retain the low body in the frame through the late 1.18 s hand recovery and 1.57 s floor pose.

Beats: 0.000, 0.100, 0.313, 0.490, 0.590, 1.180, 1.570, 1.963 seconds.

### combo / 14

Combo 3 step 3: expand for the 0.29 s launch before the configured 0.38 s hit, then lift the composition into the 0.68 s attacker apex. Pause angular travel around the 0.87 s fast return and resume a small sideward drift as the generated receiver response reaches the floor at 1.64 s.

Beats: 0.000, 0.180, 0.290, 0.380, 0.680, 0.870, 1.640, 2.030 seconds.

### combo / 15

Combo 3 step 4: start wide because the configured hit is only 0.08 s into the clip. Keep a slowly reversing rear-flank view while the attacker continues upward to 0.86 s; reserve a separate wider beat for the 1.15 s descent rather than assuming the opening hit is the whole action. Finish higher over the 1.35 s receiver floor response.

Beats: 0.000, 0.080, 0.190, 0.580, 0.860, 1.150, 1.350, 1.730 seconds.

### combo / 16

Combo 3 step 5: show the complete airborne finish from a clean near-side angle. Build upward and outward before the 0.49 s attacker apex, retain the blade/body silhouette through the 0.69 s speed burst and configured 0.713 s hit, then settle after the 0.89 s receiver response and 1.67 s floor arrival.

Beats: 0.000, 0.200, 0.490, 0.690, 0.713, 0.890, 1.670, 2.363 seconds.

### execution / 0

Ambush: a compressed rear-flank stalking frame leads into the measured 0.58 s blade burst and 0.67 s receiver drop. Keep the camera steady through contact, open the low composition around 0.87 s, and then reveal the attacker recovery with a slow retreat. Ignore the clip-end playback reset as an action cue.

Beats: 0.000, 0.250, 0.380, 0.580, 0.670, 0.870, 1.630, 2.500 seconds.

### execution / 1

Execution 1: connect the 0.78-0.88 s opening burst and the later 1.75 s attack with a measured rear-flank reveal. The first rendered pass showed very small subjects from the 2.09 s flight through the 3.57 s finish. The revised 115-146 degree arc stages the long receiver travel into depth, preserving a stronger foreground attacker while retaining the distant reaction. Keep the camera steady through the 2.05 s launch and allow the 2.92 s landing to resolve without an abrupt contraction; clip-end resets are excluded from action timing.

Beats: 0.000, 0.450, 0.780, 0.880, 1.750, 2.050, 2.920, 3.800 seconds.

### execution / 2

Execution 2: start from a higher rear flank to distinguish the opening 0.79-0.89 s attack from Execution 1. Ease in during the mid-clip preparation, widen before the receiver height peak at 1.97 s, and hold the broad frame through the very fast 2.07 s launch. The 2.56 s low reaction resolves into a slow elevated retreat; the endpoint reset is not an impact.

Beats: 0.000, 0.500, 0.790, 0.890, 1.650, 1.970, 2.560, 3.933 seconds.

### execution / 3

Execution 3: favor a near-side tracking angle for the early 0.20 s advance and 0.69-0.78 s exchange. Allow a quiet mid-shot approach, then open for the separate 1.47 s hand burst and 1.57 s receiver response. Raise the finishing view as the body settles around 1.77 s, holding the final recovery without following the playback endpoint reset.

Beats: 0.000, 0.200, 0.390, 0.780, 1.200, 1.470, 1.770, 3.433 seconds.

## Specification checks

- Exactly 47 unique family/index pairs, matching every selectable survey entry.
- Five to eight strictly increasing beats per entry, beginning at zero and ending at that entry's measured duration.
- 361 authored beats; no two entries share an identical complete parameter signature.
- Yaw stays between 60 and 146 degrees; no within-shot action-axis crossing.
- Elevation stays between 17.5 and 43 degrees. FOV stays between 30.5 and 37 degrees. Higher angles are reserved for reviewed aerial separation and torso overlap.
- Tracking windows remain between 0.15 and 0.42 seconds; look-ahead remains between 0.03 and 0.16 seconds.
- Closely spaced contact/peak beats were checked for abrupt curve changes and refined individually.

## Rendered review and targeted refinements

Reviewed all 47 Pepe-role entries at six rendered phases each: 0%, 18%, 35%, 55%, 75% and 94%, totaling 282 images. Evidence files are `/tmp/frank-camera-review/{family}-{index}-pepe-{00..05}.png`. The root review independently covered all 47 Mankey-role entries. No frame crop was observed in the sampled poses. Static captures establish composition and overlap findings; they do not establish motion smoothness or exact-contact coverage between samples.

Nine plans were refined from the rendered evidence below. Their revised yaw/elevation curves were evaluated at 1,000 samples per second using the Unity builder interpolation formula; every revised angular channel remained below 30 degrees per second, with a maximum of 25.36 degrees per second for the standard Greatsword rear reveal. These direction edits were rebaked and reviewed. The final Pepe pass additionally raises Atemi 3 to 40-42 degrees because the larger receiver still masked the grip in its lower view. The integration owner separately refines radius release, transition pacing and stage size.

A readable floor-backed composition guard keeps each authored elevation beat at least FOV / 2 + 2 degrees. With the expanded stage, the upper camera ray then faces the floor instead of the distant stage edge. This preserves the lower dramatic angles where they remain useful while eliminating the visible stage/background seam. Runtime interpolation and continuous validation still check the resulting shots.

| Entry | Initial review result and refinement |
| --- | --- |
| frank/0 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| frank/1 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| frank/2 | At 4.03 s the separated airborne bodies were too small; added a gradual 35-43 degree apex view. |
| frank/3 | Both subjects became small after the long 4.50-5.64 s knockback; added a monotonic 100-145 degree rear depth reveal. |
| frank/4 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| frank/5 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| frank/6 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| frank/7 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| vol10/0 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| vol10/1 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| vol10/2 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| vol10/3 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| vol10/4 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| vol10/5 | Pepe was hidden behind Mankey during the 0.44-0.75 s turn; shifted the restrained flank curve to 65-67 degrees. |
| vol10/6 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| vol10/7 | Torso stacking around 1.01 s obscured the grip; added a 37-38 degree view through the lifting interval. |
| vol10/8 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| vol10/9 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| vol10/10 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| vol10/11 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| vol10/12 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| vol10/13 | The 0.29 s turning capture hid much of the rear actor; elevated the anticipation and drop to 28-38 degrees. |
| vol10/14 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| vol10/15 | The 0.74 s lift obscured the attacker behind the rotating receiver; added a gradual 25-38 degree revealing crane. |
| vol10/16 | The 0.68 s lift stacked the torsos; elevated the grip, lift and back-bridge to 32-38 degrees. |
| vol10/17 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| combo/0 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| combo/1 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| combo/2 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| combo/3 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| combo/4 | The 2.73-3.73 s aerial separation made the actors small; added a gradual 34-40 degree elevated view. |
| combo/5 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| combo/6 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| combo/7 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| combo/8 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| combo/9 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| combo/10 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| combo/11 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| combo/12 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| combo/13 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| combo/14 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| combo/15 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| combo/16 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| execution/0 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| execution/1 | The 2.09-3.57 s knockback sequence reduced both actors to small silhouettes; changed the arc to 115-146 degrees to stage the travel into depth. |
| execution/2 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |
| execution/3 | All six sampled phases reviewed; body and weapon action stay inside the frame with readable separation. No targeted angle revision required. |

## Final integration review

All 47 final Pepe-role entries were rechecked in the overview sheets by the integration reviewer, alongside all 47 Mankey-role entries in the authored-beat sheets. Atemi3 received the additional elevated view and a separate Pepe-attacker yaw curve; its seven final beat poses were reviewed in both roles. All nine targeted angle refinements were rebaked. The final saved setup passed 26,842 mathematical camera samples and 282 repeat seeks; six overview captures per role and all 361 direction beats per role were regenerated.

No body or weapon boundary cropping was observed in the reviewed final captures. Close grapples retain some physical silhouette overlap, and distant knockbacks and aerial separations retain wide coverage so both performers stay visible. These are deliberate composition tradeoffs. Mankey facial mesh separation is visible in several source poses and remains a character-rig limitation. Static frames establish the reviewed compositions; continuous motion was checked numerically, rather than claiming a real-time viewing of every frame.
