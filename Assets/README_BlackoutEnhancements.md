# Blackout Protocol enhancements

## Runtime improvements

- Press **B** to trigger a scene-wide blackout and restore the original light state.
- Press **F** during a blackout to toggle the procedural flashlight.
- The flashlight has a two-minute battery and uses soft shadows for a stronger sense of depth.
- URP color grading shifts to a cool, low-exposure look during a blackout and restores the scene grade afterward.

The runtime system is created automatically after each scene loads, so existing scenes do not need a manager prefab.

## Texture quality

Use **Tools > Blackout Protocol > Improve Imported Texture Quality** in the Unity Editor to apply a consistent 2K cap, mipmaps, anisotropic filtering, and high-quality compression to imported non-sprite textures.
