# SlapFace source preparation status

All four project-owned native imports and eight unarmed fighter drivers have
been prepared and validated in Unity 6000.5.9f1. Source capture completed for
all four clips on both Mankey and Pepe: 4232 sampled poses and 56 timestamped
sheets. See `NativeImports.json`, `Study.json` and `index.html`.

The original FBXs referenced missing Avatar GUID
`54d24dcf9716c0c40821dd95a9e62844`. `SourceDiagnostics.json` records that original
failure. The repair copies each source under
`Assets/CombatExpansion/SlapStudy/NativeSources`, preserving the original FBX
bytes and creating a valid Avatar from its complete native human mapping.
Original source files and import settings remain unchanged. Original GUID and
clip IDs remain the provenance keys; adapted identities and source hashes are
recorded explicitly. Reuse validates the prepared copies against that record.

Reproduction order: `CombatExpansionSlapStudy.PrepareNativeImports()`,
`PrepareDrivers()`, then `CaptureSources()` in Edit Mode.

The 24-case pair capture now passes on both giver avatars across two headings
and three spacings. The reviewed frontal setup uses receiver yaw 180 degrees
and 0.8 m spacing; 1.0 and 1.2 m place the striking hand too far away.
Sequence1 uses A as giver/B as receiver; Sequence2 reverses those roles.
See `../PairCandidates/Study.json` and its timestamped pose sheets.

`CaptureFaceContacts()` completed all eight avatar/direction cases using
moving skinned right-hand and head triangles. Each sequence contains one
slap; separated near-zero samples during a single hand sweep are not extra
strikes. This raw capture also exposed floor penetration, so its contact
positions are not the final authoring data.

`BakeFaceGrounding()` and `ValidateFaceGrounding()` now pass for both sequences.
The independent 361 Hz validation covers both assignments and directions:
50,760 visible-body clearance measurements, zero violations, and minimum
clearance 0.009902719 m. Saved maximum lifts are 0.12243624 m for Sequence1
and 0.14226146 m for Sequence2. See `../Grounding` for individual reports.
This verifies sampled clearance, not foot planting or complete choreography.

The grounded face-contact capture completed all eight cases. The reviewed
contact samples touch the moving face surface on both lane directions; see
`../FaceContactsGrounded/MotionReview.md` for selected per-avatar times.
Both actions are now registered for explicit selection in BattleScene, with
four per-avatar action definitions, authored feedback profiles and optional
standing recovery. The first actual Play Mode case failed its impact-position
assertion; see `../PlayMode/Status.txt`. Gameplay validation and standing-recovery
validation remain incomplete, and lethal visual treatment is not implemented.
These registrations are a work-in-progress checkpoint, not accepted integration.
