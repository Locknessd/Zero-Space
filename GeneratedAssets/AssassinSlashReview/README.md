Assassin slash VFX review

The Heavy_Assassin combo keeps all eight blade contacts plus the original throw landing for Mankey and Meme (the right scene object is named Pepe).

Slash ribbons now cover 95% of the blade, persist for 160 ms, and follow the knife for up to 140 ms after contact, stopping at the next stroke. Straight thrusts use a narrow blade-aligned glint so a held stab cannot produce an invisible zero-area ribbon. A soft body-overlap pass keeps the purple stroke readable when the weapon is occluded. Only Assassin uses the extra material/pass. No new textures or per-frame skinning are added; cue sweeps are cached at up to 240 Hz and the visible leading point remains on the actual knife tip.

Before/After contain native Unity game-camera renders for all eight strokes, both fighters, before contact and during follow-through. Impact particles are hidden in these captures to isolate the slashes. The CSV files record each image time and changed pixel count. The first second-thrust images were entirely invisible; the final renders show a thin glint. Other cuts show larger purple crescents.

Validation.txt: 16 complete combos at 15/60 FPS, mirrored and lethal variants, 1,824 weapon endpoint checks, every one of 32 rendered views visible, no replay on pause/rewind, no pose-clock changes, and cleanup after recovery/cancel.
AllWeaponsValidation.txt: 36 general weapon cases, 148 sweeps, 728 endpoint checks, no detached blade tips or broken pooling.
ScopeValidation.txt: only Assassin trail settings changed in BattleScene; contact/audio/damage bank is byte-identical to the start of this change.
Managed WebGL compilation: 0 errors, 45 pre-existing warnings. This is a code compilation check; a full WebGL export was not produced.

The installed scene configuration is ready for the next WebGL build. Existing F8 test-panel restrictions are unchanged.
