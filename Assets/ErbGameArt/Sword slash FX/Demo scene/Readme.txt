Pack includes 50+ slash prefabs.

NOTE:
If you want to use posteffect like in the demo video:

	1) Download unity free posteffects 
	https://assetstore.unity.com/packages/essentials/post-processing-stack-83912
	2) Add "PostProcessingBehaviour.cs" on main Camera.
	3) Set the "PostEffects" profile. ("\Assets\ErbGameArt\Sword slash FX\Demo scene\CC.asset")
	4) You should turn on "HDR" on main camera for correct posteffects. (bloom posteffect works correctly only with HDR)
	If you have forward rendering path (by default in Unity), you need disable antialiasing "edit->project settings->quality->antialiasing"
	or turn of "MSAA" on main camera, because HDR does not works with msaa. If you want to use HDR and MSAA then use "MSAA of post effect". 
	It's faster then default MSAA and have the same quality.

Using old effects:
	You need to have your own mesh for sword slash and mowe it with the help of curves and particle system.
	Using shaders:
	All shaders work only with particle system using "Custom vertex stream" in render tab. 
	Use UV + Custom1.xy for Slash shader and UV + UV2 + Custom.xyzv for Fade slash shader.
	Then use custom data x and y (z for in FadeSlash shader to control cutout). One for slash path, another for slash length.
	Tutorial - https://youtu.be/R65AQ_B5v7k
	Tutorial how to use my asset with free "Melee Weapon Trail" asset (https://assetstore.unity.com/packages/tools/particles-effects/melee-weapon-trail-1728)
	https://youtu.be/8tW14NaIM-k
	Fill free to change opacity, size and another parameters in particle system.
	
Using new effect:
	Just Drag&drop prefab and change rotation and size for your skill/attack.
	Only new effects support LWRP and HDRP (Exept distortion particles, anyway you can delete it from the effect).






P.S. Please rate the asset in the store. Thanks! ^^

