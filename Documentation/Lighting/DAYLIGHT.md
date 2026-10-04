# Arena daylight

The arena uses a neutral 6200 K sun, cool sky fill and a warmer ground bounce. Raised diffuse illumination keeps characters and concrete readable beneath overhangs while directional shadows preserve depth. Sun intensity is 3.4; shadow strength is 0.8.

`ArenaDaylight` initializes the ambient spherical harmonics once on scene activation. This keeps unbaked geometry and moving characters illuminated without relying on an editor GI update or adding realtime lights. It has no per-frame update.

The player camera now uses the existing Neutral tone mapper, with +0.15 EV exposure and -2 contrast. Bloom and vignette are disabled to keep combat views clear. Shadow resolution, render scale and antialiasing are unchanged.

`Tools / CounterMine / Validate Daylight` checks the saved camera settings, illumination in six directions and alignment of the sky sun with the directional light. It captures three fixed views using display-only enemy models: `after-0.png` beneath cover, `after-1.png` in the central avenue, and `after-2.png` on the roof. Captures use an sRGB ARGB32 render target.

The menu-to-match Play Mode smoke check also verifies the actual player camera and runtime ambient illumination, captures `gameplay.png`, and checks death and respawn. These are Unity Editor checks; a published WebGL build and device performance have not been tested.
