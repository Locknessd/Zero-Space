# Reaction track sampling and paired axe candidates

The existing pair player now accepts an optional `FrankReactionTrack`. Its ordered segments share the
pair clock, blend native reaction poses, preserve cumulative planar travel, and can hold an explicitly
authored terminal pose. Existing moves have no track assigned and retain their previous sampling path.
The track creates its graph once per interaction and releases its playables and cached data on cleanup.
This is playback infrastructure; it does not create a separate hit-confirmation or damage authority.

Paired playback also accepts an optional attacker weapon prefab and an exact source-driver socket path.
The prop uses the socket's local origin, rotation and scale, registers renderers with the retarget pose,
and disables imported colliders. Existing actor ownership removes it on cleanup; the receiver remains
unarmed. Runtime and Editor compilation passed. Live attachment, animation and cleanup verification
remain pending, and no new GreatSword action is registered by this infrastructure change.

`CombatExpansionReactionTrackCheck.Validate` passed eight isolated actual-avatar cases: both fighters,
both directions, and ordinary/lethal sampling. Forward/backward pose error was zero, pre-contact poses
remained held, blend boundaries were continuous, terminal holds remained fixed, and cancellation
released both actors. The fix primes native humanoid inputs at time zero before evaluating absolute
sample times: restoring transforms alone was insufficient because Unity applied history-dependent
root-motion deltas. In the same Editor check, 240 warmed samples per case measured 0.2026–0.2489 ms per
pair sample and zero managed bytes. These measurements exclude rendering and live presentation.
Evidence: `ReactionTrackValidation.txt`. The 16-case preview damage Play Mode suite also passed again
after this runtime change. The existing 40-case grapple lifecycle suite also passed on both avatars and
directions, including queue/authority guards, cancellation, actor disable and round reset; evidence is
`GrapplePlayMode.txt`. `CombatExpansionPlaySession.BeginGrapples` runs it from an isolated saved-scene
copy and restores the prior Editor setup. Full tracked-action Play Mode/presentation verification remains
outstanding.

`CombatExpansionAxeContactStudy` now captures temporary Combo_01/reaction pairs in the saved BattleScene
preview, with both avatars and directions, actual moving victim surfaces, native axe heads, gameplay
camera sheets, and hip paths. `AxeContactStudy/ReactionCandidate*` uses LowLeft_Med (7400050) followed by
MidFront_Stagger (7400064), both from KB_Hits GUID `932279eb1c22db24385eb04c03856eb1`; it is rejected for
heavy body overlap. `FrontalCandidate*` uses MidFront_Med (7400044) with larger entry spacing. Minimum
sampled lane separation improves from 0.051/0.157 m to 0.423/0.466 m for Mankey/Pepe, but crowding,
contact fit, and normal recovery still need work. Measurements are closest distal-head/body distances,
not approved hitboxes or successful gameplay hits. Neither candidate is registered as an action.

Direct sheet inspection confirms MidFront_Stagger stays on its feet, folds forward, retreats and returns
to guard. It is not a knockdown or valid standalone lethal hold. The temporary axe candidates therefore
configure no lethal hold for it. The pose validator's lethal case tests only the hold mechanism and
does not approve that diagnostic pose as a death animation.

The focused source-sheet review also corrected HighBack (7400060), HighFront (7400062), MidLeft
(7400066), and MidRight (7400068) Stagger on both avatars. All five retain foot support and return
upright; none showed floor contact. The reaction `MotionReview.md`/TSV retain all 37 source identities
and now classify these as standing recoil/stumble candidates. Paired suitability remains unapproved.
