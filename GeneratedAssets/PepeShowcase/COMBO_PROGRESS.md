# Assassin long combo

Status: COMPLETE on 2026-10-12. No pending Unity job remains.

User request on 2026-10-12: join Assassin attacks into one continuous long combo.
Keep the current Unity Editor running. Do not restart or discard the user's scene.

## Implementation

- Reuse the existing `FrankAttackTrack` and `FrankTestActor.SetAttackTrack` runtime mixer.
- Each showcase take can now reference an attack track and derives its duration from that track.
- A chain uses one actor, one native weapon rig and one camera throughout.
- Order: Combo04, Combo03, Combo04, Combo03, Skill05 (31 native slash cues).
- Trim recovery between attacks, overlap by 0.14 seconds and crossfade over 0.12 seconds.
- Next attack reaches full blend 0.04 seconds before its first slash cue.
- Skill05 retains its full landing/recovery at the end.
- Re-time original sound cues and retain native animated weapon VFX.
- New scene: `Assets/Scenes/PepeAssassinCombo.unity`.
- New library: `Assets/PepeShowcase/AssassinComboLibrary.asset`.
- Track: `Assets/PepeShowcase/AssassinLongCombo.asset`.
- Camera follows rapid movement more closely for tracks while keeping a fixed safe distance.
- Existing standalone showcase scene and library are preserved.

## Completed validation

Use `InvokeUnityJob.ps1` from this directory with PowerShell ExecutionPolicy Bypass.
Jobs are requested atomically through `Temp/FrankRetarget/request`; never duplicate a pending job.

1. Runtime compilation passed at 11:31:17; final Editor compilation passed at 11:37:21.
2. Created a 10.143-second sequence with five segments and 31 native slash cues.
3. Added 121 checker tiles only to the new scene to cover its longer forward travel.
4. Final checks passed on Pepe, Mankey and custom replacement: 610 poses and 31 slash cues each.
5. All four transitions passed continuity and deterministic seeking checks.
6. Final Play-mode video completed at 11:38:14, 1920x1080 at 60 fps with stereo sound.
7. Reviewed native VFX frames and all four transition montages from the actual video.
8. Full audio/video decode passed with exit code 0; see `ComboDecode.txt`.
9. All six protected file hashes match; see `ComboPreservation.After.json`.
10. Editor returned to the original scene in Edit mode at 11:38:15; no restart was performed.

The combo check covers Pepe, Mankey and custom replacement at 60 Hz, all 31 native slash cues,
actor continuity, deterministic seeking, transition boundary continuity and camera framing.
The final check also requires floor coverage under both feet across every sampled pose.

Reports: `ComboSetup.txt`, `ComboValidation.txt`, `ComboRecording.txt`.
Completed video: `GeneratedAssets/PepeShowcase/PepeAssassinLongCombo.mp4` (approximately 11.9 seconds including titles).
Protected hashes before work: `ComboPreservation.Before.json`.

## Use

Open `Assets/Scenes/PepeAssassinCombo.unity` and press Play.
Controls support Pepe, Mankey, a custom Humanoid, pause, replay, speed and looping the whole chain.
The loop restarts after the chain's final recovery and title fade.
The attack track exposes source trims, sequence start times and blend durations in the Inspector.
This continuation applies to the standalone showcase; it does not change BattleScene combat scheduling.
