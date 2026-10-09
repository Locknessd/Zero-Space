# GreatSword contact authoring review

These source-pose decisions use BladeContacts.csv (368 blade-only samples),
FirstLandings.csv (1660 surface-clearance samples), and the revised 16 fine-contact
sheets on both avatars and directions. They do not certify final gameplay quality.

| Action | Weapon contacts (source seconds) | Landing accent (source seconds) |
| --- | --- | --- |
| Ambush | 0.575, descending head contact | 0.616667 |
| Execution1 | 0.85, upper chest/neck thrust | 2.333333 |
| Execution2 | 0.816667, low hip/upper-leg thrust | 2.070833 |
| Execution3 | 0.366667 opening torso/arm cut; 0.683333 rising hip/leg cut; 1.520833 descending raised-arm contact | 1.5375 |

Every selected weapon sample has blade/body contact on both avatars in both
directions. The proximal fifth of the grip-to-tip span is excluded from the probe.
Stored avatar anchors are independently checked against body and blade surfaces
in the reverse-facing pose; coincident global nearest-point selection is not required.

Ambush at the previous 0.55-second draft timing was hilt/guard proximity rather than
blade contact. Execution3 uses the first descending arm contact, before the blade
continues across the falling body. Continuous impalement in Execution1/2 is one
weapon strike and does not award repeated damage during the held contact.

Landings are non-damaging presentation accents. Execution1 uses a common near-floor
phase across the two differently proportioned bodies: surface clearance at 2.333333
is 0.0283m and 0.0246m. The final Play Mode visual review must still assess that cue.
Ambush's initial foot/hand touch is separate from the body landing at 0.616667.

All 192 revised fine-capture ground values are finite; the earlier invalid report
has been superseded. The saved source-pose captures disable optional feedback only
for choreography inspection; gameplay must retain the real presentation systems.
Ambush recovery separation is still unresolved, and the ordinary gameplay suite
and final effect placement remain required before any completion claim.

Checkpoint validation: all four actions are installed for both fighters. The first
ordinary Play Mode suite passed its 16 range guards, then failed case 17 on the
existing Light_1 action: suppressed equipment retained a visible prop, collider,
or emitting trail. The suite stopped before the queued GreatSword cases, accepted
lethal cases, and interruption cases; those checks remain unverified. See
../GreatSwordPlayMode.txt for the recorded failure. Ambush retreat motion was
studied in RetreatCandidates but has not yet been authored into gameplay.

The measurements and Play Mode report describe the local checkpoint 31fcde0d.
The publication merge preserves the newer remote scene and sound-bank changes
and adds only eight GreatSword registry entries and four shared profiles. Its
registry has 19 moves per fighter; source inventory and coverage files remain
snapshots of the local capture state. The merged scene has not been replayed.
