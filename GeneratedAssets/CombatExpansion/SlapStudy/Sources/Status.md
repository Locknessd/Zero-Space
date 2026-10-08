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

The captures are independent source performances, not approved paired actions.
Giver/receiver roles, hand-to-face contact, relative setup, recovery, damage,
presentation and gameplay registration still require paired validation.

`CombatExpansionSlapStudy.CapturePairCandidates()` provides an unregistered
24-case runtime pair study across both giver avatars, two receiver headings
and three spacings. It compiles, but has not been run or visually accepted in
this checkpoint. Its hand/head measurements use joint pivots, not skin contact.
