# Stronger Battle VFX

Inventoried 2,342 particle prefabs in the project and rendered 32 relevant impact, slash and ground candidates at four ages in Unity. Selected effects were adapted into 25 Battle-only prefabs under `Assets/Vfx/Battle/Strong`.

| Weapon | Heavy contact | Slash |
| --- | --- | --- |
| Sword & Shield | Archer Light Arrow hit, bright shock ring | Larger Hovl blue shield sweep |
| Greatsword | Cartoon FX contrast hit with debris | Larger Hovl gold sweep |
| Spear | Compact Archer Light Arrow hit | Larger Hovl Prick thrust |
| Axe | Short Archer Fire Arrow impact | ERB New Slash 5, thick orange arc |
| Katana | White Mage Light hit | ERB New Slash 4, sharp orange crescent and sparks |
| Dual Daggers | Archer Discharge hit | Larger ERB blue multi-streak sweep |
| Assassin | White Mage Light hit | Larger ERB blue sweep |

All impacts retain their distinct weapon-colored comic cores. Punches and light contacts use short White Mage flashes. Ground hits add Cartoon FX debris to the floor shock, with a denser spreading smoke ring and the existing English SMASH text.

Global VFX scale increases from **1.00 to 1.28**. Contact cores additionally grow **20%**, making them **53.6% larger overall**. Smoke sprites additionally grow **15%** and the ring emits 20 puffs instead of 16. Retained slash prefabs increase another 21–30% before global scaling. Replaced axe/katana slashes use thicker, brighter shapes.

Pack scripts/audio, distortion, automatic playback, continuous emission, recurring bursts and sub-emitters are removed or disabled. The shared cue player continues to spawn one impact prefab per contact. Impacts are brief enough for the rapid dagger combos. Flipbook candidates use unsupported URP shaders in this Built-in project and were excluded.

The calibrated contact/damage bank is byte-identical. Only the BattleVfxPlayer scene object changed; camera settings, lights, actor scales and animation pools are unchanged. Removed Archer/White Mage skills remain absent.

- `Inventory.txt`, `Candidates0/1/2.png`, `Candidates.txt`: inventory and native rendered comparisons.
- `Selection.csv`, `Installation.txt`: exact selected pack sources and scales.
- `Before/` and `After/`: 42 native Battle camera renders each, showing slash, hit and ground for all seven heavy moves on both fighters; current poses use explicitly baked skinned meshes.
- `VfxValidation.txt`: 72 mirrored/normal/lethal cases; supported materials, one-shot emission, exact cue counts, cancellation, frame skipping and the 48-instance pool.
- `ContactValidation.txt`: 56 heavy/mirror/frame-skip cases and 240 verified mesh contacts; damage updates before impact on the same contact pose.
- `PlayValidation.txt`: all 18 actual Battle animation pairs, replay/recovery, KO/slow motion, stop/reset, queued events and pause restoration; 17,585 contact frames checked in Play Mode.
- `ScenePreservation.txt`: every serialized scene object except BattleVfxPlayer is unchanged.

Example: [axe slash before](Before/Mankey_Heavy_2_Slash.png), [axe slash after](After/Mankey_Heavy_2_Slash.png), [greatsword impact after](After/Mankey_Heavy_6_Hit.png).

The editor installer reads `Selection.csv` and always rebuilds from the original Battle templates, so rerunning it does not stack bursts or repeatedly enlarge effects.
