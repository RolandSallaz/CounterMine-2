# CounterMine 2

Тактический мультиплеерный шутер на Unity + Photon: две команды, боты, киллфид, экономика.

**Играть:** https://rolandsallaz.github.io/CounterMine-2/

## Управление

- **WASD** — движение, **мышь** — обзор, **ЛКМ** — огонь, **ПКМ** — прицеливание
- **Shift** — спринт, **Space** — прыжок
- **C / Ctrl** — присесть (на бегу — подкат)
- **R** — перезарядка, **Esc** — освободить курсор

## Сборка

- Unity 6000.3.11f1 (+ WebGL Build Support для веба)
- Headless-сборка: `Unity -batchmode -quit -executeMethod WebGLBuildGhPages.Build` с `CM_WEBGL_OUTPUT` и `CM_GHPAGES_BUILD=1`
