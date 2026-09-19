# UI consistency

The owner creates or reuses one PlayerHUD prefab. Rebinding caches the original
crosshair sizes instead of multiplying the already scaled sizes. All HUD text,
runtime labels and damage numbers use the same embedded Jura font, with Cyrillic
coverage; the font is a project-owned Resource rather than a demo dependency.
Killfeed separators use supported glyphs instead of platform font fallback.

The authored 1600 x 900 CanvasScaler remains the source of scaling settings.
Safe-area anchors and offsets are initialized immediately; scoreboard, reward
notifications, skills, respawn and killfeed share the safe-area root. Crosshair,
direction indicators and injury vignette remain relative to the full viewport.

Removed the unreferenced KillfeedManager/KillfeedEntry implementation. UI preview
and audio/killfeed validation tools now run only from their menu commands.

Unity Editor validation passed embedded font/scaler checks, repeated Bind and
single-scoreboard checks. The reported Play Mode/build visual discrepancy was
not reproduced in a player build; these changes remove concrete inconsistencies
found in the project, not proof of pixel-identical player output.
