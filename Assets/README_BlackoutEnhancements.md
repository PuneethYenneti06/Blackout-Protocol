# Blackout Protocol enhancements

## Runtime improvements

- Press **B** to trigger a scene-wide blackout and restore the original light state.
- Press **F** during a blackout to toggle the procedural flashlight.
- The flashlight has a two-minute battery and uses soft shadows for a stronger sense of depth.
- Lighting, ambient colour, and URP colour grading transition smoothly to a cool, low-exposure look during a blackout.
- A low electrical hum and switch click reinforce the blackout state without requiring a scene-specific audio setup.

The runtime system is created automatically after each scene loads, so existing scenes do not need a manager prefab.

## Texture quality

Use **Tools > Blackout Protocol > Improve Imported Texture Quality** in the Unity Editor to apply a consistent 2K cap, mipmaps, anisotropic filtering, and high-quality compression to imported non-sprite textures.
