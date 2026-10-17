# Menger

Interactive 3D Menger Sponge fractal explorer built with Unity and C#.

SCREENSHOT_PLACEHOLDER_HERE

Walking inside a recursive (modified) Menger sponge. Descend through second-order tunnels with a position-driven ×3 scale of the whole sponge, come back out the same way.

## Requirements

- Windows 10/11 x64
- Unity 6.6,
    - URP 17.6,
    - Input System 1.20 (already in `Packages/manifest.json`).

No other dependencies, no paid assets.

## Run

1. Open the project in Unity.
2. Open `Assets/Scenes/MengerPrototype.unity` and press Play.
3. The cursor is captured; `Esc` releases it, a left click captures it again.

## Controls

Input System actions created in `WalkerController`, rebindable through the Input System API. No rebinding UI.

- `Mouse` — mouse-look (yaw / pitch, FPS like)
- `WASD` — walk and strafe.
- `Space` — jump (hold to keep jumping).
- `Left click` — go to the point the view centre is aimed at (marked by a ring on the surface): that face becomes "up", the line of sight is made parallel to it.
- `F1` / `F2` — diagnostics overlay / help text.
- **F10** — quit the game.
- **R** — back to the start.

Going through a tunnel mouth on foot, in a jump or in a fall changes the region and scales the world (x3 inward, x1/3 out through the exit) at once, like a click through it. The smooth zoom only runs when you fly in near the mouth axis.

There is no edge guard: walk off the face and you fall (gravity stays that face's "down"). A fall has no limit of its own: you are put back at the start only when you get farther than `escapeDistance` (900 root-cube units, measured from the cube's faces whatever region you are in) from the sponge.

Turquoise square outlines mark every accessible second-order tunnel (the transitions). Amber outlines mark the way back to the
parent region (only inside a child region, only where the child's cube opens onto empty space).

Small amber triangles at the screen edge point to the 6 nearest exits (the mouths back to the parent region) that are not currently visible.

## Tests

```shell
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml
```
