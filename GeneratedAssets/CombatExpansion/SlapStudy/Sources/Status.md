# SlapFace source preparation status

The editor study tools compile, but source preparation is blocked by missing
native Avatar dependencies. No SlapFace drivers, paired captures, contacts,
damage profiles, or gameplay registrations have been accepted.

`CombatExpansionSlapStudy.PrepareDrivers()` failed validation of the first
source rig. The subsequent read-only `DiagnoseSources()` run in Unity 6000.5.9f1
completed and produced `SourceDiagnostics.json`. All four source FBXs use
`CopyFromOther` with unresolved Avatar GUID `54d24dcf9716c0c40821dd95a9e62844`.
Their imported models have no root Animator, no importer source Avatar and no
Avatar subassets. Exposed source transforms remain available for investigation.

The native FBXs and their import settings are unchanged. A valid native rig
preparation method must be established before `PrepareDrivers()` and
`CaptureSources()` can produce usable evidence. The diagnostic report records
source identities, importer state and hierarchy details for that follow-up.
