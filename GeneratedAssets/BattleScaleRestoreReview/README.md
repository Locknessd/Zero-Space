# Battle fighter scales and skill removal

Battle uses uniform model scales **Mankey 0.7** and **Meme/Pepe 0.1**. Source animation playback retains the visible scene scale and resizes its hidden calibrated skeleton to match. Pair spacing is adjusted for the smaller models.

Meteor Shot (`Heavy_Archer`) and Celestial Smite (`Heavy_WhiteMage`) are removed from both fighters' pools, the shared VFX variants and the SFX timeline. The F8 animation browser reads those live pools and now contains **18 pairs**: two light and seven heavy moves for each fighter.

Existing imported packs and previous skill assets remain available. Authored camera settings are preserved.

- `Installation.txt`: saved model scales and pool counts.
- `ScaleValidation.txt`: scale retained for all remaining pairs at start, middle, end and cancellation.
- `PlayValidation.txt`: native F8 browser, playback, recovery, KO and reset checks.
