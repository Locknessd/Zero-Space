# Clean Battle character shading

Removed the dark round marks on Mankey and Pepe. FlatKit's specular and rim layers replaced the textured base color with very dark colors, which made highlights appear as black patches.

The three shared Battle character materials now use soft toon shading with lighter colored shadows, 0.7 px outlines and no specular/rim replacement layers. Original body, face and eye textures are retained. Battle weapon materials, VFX and camera settings retain their existing configuration.

| Pose | Before | After |
| --- | --- | --- |
| Idle | [Before](Before.png) | [After](After.png) |
| Mankey attacking | [Before](Before_Mankey.png) | [After](After_Mankey.png) |
| Pepe attacking | [Before](Before_Pepe.png) | [After](After_Pepe.png) |

[Native rendering checks](AfterValidation.txt) cover both fighters at idle and opposing combat poses under Battle lighting, supported shader variants and retained textures. [Camera checks](CameraUnchanged.txt) compare serialized settings with the scene backup.

A [front-facing preview](After_Front.png) also confirms that both characters retain their facial details and textured eyes.

The Battle comic shading installer applies this corrected preset. **Tools → Battle → Clean character shading** reapplies the preset and generates these previews.
