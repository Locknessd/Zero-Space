# Heavy contact alignment

Battle's seven heavy moves for each fighter now use reviewed contact cues for impact VFX, hit audio and damage. Contact poses were sampled at 120 Hz on the rendered body and weapon meshes with the current Mankey 0.7 and Meme/Pepe 0.1 scales.

Each impact stores a point on the receiver's mesh relative to an animated bone. Runtime playback evaluates the exact contact pose, updates HP and the damage popup, then plays the impact. A frame crossing several contacts processes each one once and restores the displayed pose. The impact's camera offset is 1.5 cm instead of the former 35 cm.

| Heavy move | Damage contacts | Reviewed behavior |
| --- | ---: | --- |
| Sword & Shield / Heavy_5 | 5 | First contact uses the shield; the third cut moves from 2.90 s to about 3.12 s. |
| Greatsword / Heavy_6 | 4 | Contacts use the blade faces, including broad horizontal cuts. |
| Spear / Heavy_7 | 6 | Points follow the actual spear contacts throughout the combo. |
| Axe / Heavy_2 | 4 | The second strike uses the right foot/toes; the other strikes use the axe. |
| Dual Daggers / Heavy_8 | 7 | Separate points and cues for the ground and airborne contacts. |
| Katana | 2 | Removed the false third hit and slash during the sheathing motion. |
| Assassin | 3 | Two blade contacts plus damage on the final throw landing. |

The total damage is still split evenly across the actual contacts, with integer remainder preserved. Camera settings, model scales and the move pools remain as saved in Battle. Removed Archer and White Mage skills remain absent.

- `ContactPlan.csv` and `ContactSelection.csv`: reviewed windows and installed cue times for both fighters.
- `Installation.txt`: attached bones and measured blade/body gaps.
- `Validation.txt`: 56 heavy/mirrored/frame-skip cases, 240 verified mesh contacts; HP updates before VFX, exact totals, cancellation and duplicate guards. Maximum measured striker gap: 3.829 cm.
- `DamageValidation.txt`: integer totals, tiny and 64-bit values, overkill and cancellation.
- `VfxValidation.txt`: effect materials, one-shot bursts, both fighters, mirrored and lethal variants, pooling and frame skips.
- `PlayValidation.txt`: actual Battle HUD and all 18 animation pairs, replay, recovery, KO/slow motion, stop/reset, queued events and pause restoration; 17,034 contact frames verified in Play Mode.

The geometry sampler compensates for renderer scale when baking skinned meshes; see the [Unity BakeMesh API](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/skinnedmeshrenderer/bakemesh).
