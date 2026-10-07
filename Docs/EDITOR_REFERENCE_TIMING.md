# Editor reference timing notes

## Sources and limits

The reference is *Classic Iron Man vs Red Hulk (Max Difficulty) - Marvel Tokon*,
[the linked video](https://www.youtube.com/watch?v=mBc5gjSkOmM). The local file
`GeneratedAssets/EditorPolishReview/Reference/Reference.f398.mp4` is now present and
fully decodes: 1280×720, AV1, 60 FPS, 20,177 frames (5:36.28). This supersedes the earlier
partial-download limit. Timestamps below use the decoded stream's nominal 60 FPS timeline
(1 frame ≈ 16.7 ms). Exact source frame indices are shown where checked; rounded seconds are
for readability. Frame-step strips were inspected at every source frame across four short
windows; wider samples cover 90–110 s. The video is an edited recording, so timings describe
its output and do not establish game-engine frame data. Audio was not listened to.

The supplied `Assets/Movie_001(1).mp4` is 1920×1080 at 30 FPS, 145.3 s. Its existing
`GeneratedAssets/EditorPolishReview/Recording/Overview.png` is an untimed combat montage;
it supports visual comparison only and cannot be synchronized to the reference. Rows below
separate observation, inference, and proposal. Proposals are qualitative and are not measured
implementation settings.

## Combat observations

| Time and inspected frames | Observation | Inference | Proposal |
| --- | --- | --- | --- |
| 90.0–92.0 s (f5400–5520) | The HUD and “ROUND 1” then “FIGHT!!” overlays appear while Hulk rises into the air and Iron Man advances. [90.0 s](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/t_090.0.png), [92.0 s](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/t_092.0.png) | The opening overlay overlaps the initial character movement; the first active exchange is visible around 93.5 s. | Keep the round callout readable while ensuring it clears before the first close contact. |
| 93.500–93.667 s (f5610–5620) | A bright orange contact effect overlays the fighters; the on-screen “Counter” label becomes visible during this short sampled window. [f5610](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/frame_005610.png), [f5613](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/frame_005613.png), [f5620](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/frame_005620.png) | A hit/counter event is presented with a concentrated impact flash. The exact input or move cannot be identified from these frames alone. | Favor a brief, localized impact read and a clear counter label; do not derive hit-stop duration from this edited capture. |
| 96.000–96.167 s (f5760–5770) | Iron Man is airborne beside Red Hulk; a bright star-shaped flash appears at their overlap, followed by a broad green sweep effect. [f5760](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/frame_005760.png), [f5765](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/frame_005765.png), [f5770](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/frame_005770.png) | At least one contact effect and a larger follow-up visual occur in rapid succession. The green effect may be from a teammate/assist; attribution is uncertain. | Preserve silhouette readability by limiting how long the larger effect obscures both fighters. |
| 99.000–99.167 s (f5940–5950) | Red Hulk turns into Iron Man with a sweeping motion and orange flame/impact imagery; the “Counter” label is visible. [99.0 s](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/t_099.0.png), [99.1 s](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/t_099.1.png) | The exchange reads as a counter-hit against Iron Man, though the small HUD label does not establish game logic. | Make the attacker, target, and resulting direction of travel easy to read in a counter exchange. |
| 100.000–100.167 s (f6000–6010) | Iron Man is airborne to Hulk’s right and drifts farther right across these adjacent frames; by 100.133 s he is near the edge of the view. [f6000](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/frame_006000.png), [f6004](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/frame_006004.png), [f6010](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/frame_006010.png) | This follows the preceding close-range exchange and reads as a launch/knockback, but the exact initiating hit is between the sampled events. | Keep launched motion visually continuous and leave enough room for the target’s full travel arc. |
| 100.400–101.500 s (f6024–6090; 0.1 s samples) | Iron Man is on the ground after the airborne movement, remains down in the half-second overview, then is upright by roughly 101.5 s. [100.5 s](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/t_100.5.png), [101.0 s](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/t_101.0.png), [101.5 s](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/t_101.5.png) | The sequence includes a grounded knockdown and a visible return to standing; the exact first/last recovery frames were not exhaustively isolated here. | Tune knockdown and recovery readability against continuous game capture; treat this interval only as an approximate presentation reference. |
| 102.600–104.000 s (f6156–6240; 0.1 s samples) | Iron Man rises/attacks while Hulk remains close; blue/orange shield-like effects and repeated cyan projectile imagery appear in front of Iron Man. [102.5 s](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/t_102.5.png), [103.5 s](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/t_103.5.png), [104.0 s](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/t_104.0.png) | The frames show a sustained projectile/guard-looking exchange with overlapping teammate activity; attribution of every effect is uncertain. | Keep defensive and offensive effects distinct in color and shape when they overlap. |
| 106.700–107.167 s (f6402–6430) | Iron Man is struck while airborne; the HUD displays “2 Hits,” then he falls toward the ground as Hulk’s follow-through ends. [f6420](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/frame_006420.png), [f6424](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/frame_006424.png), [f6430](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/frame_006430.png) | This is a short two-hit sequence followed by visible descent. Exact contact onset is earlier than the selected 107.0 s step window. | Let the hit count and falling target remain legible, and keep the follow-through from hiding the landing. |
| 107.400–108.500 s (f6444–6510; 0.1 s samples) | Iron Man is prone on the ground through the early part of the interval; by about 108.4–108.5 s he is upright again. [107.5 s](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/t_107.5.png), [108.0 s](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/t_108.0.png), [108.5 s](../GeneratedAssets/EditorPolishReview/ReferenceStills/frame10/t_108.5.png) | A second knockdown and recovery is visible. These sparse steps bracket presentation only; they do not identify the exact recovery frame. | Compare ground pose, recovery transition, and neutral pose in an uncut capture before choosing specific timings. |

## Timing use

The full reference provides visible combat examples for contact flashes, counter presentation,
airborne travel, grounded knockdown, and return to standing. Its frame-step evidence is direct
only for the inspected windows above; timestamps between cited frames remain approximate where
not explicitly frame-stepped. The reference’s capture/editing and overlapping assists make it
unsuitable for claiming exact move ownership, hit-stop, recovery data, or hidden engine behavior.
