# Menger

Interactive 3D Menger Sponge fractal explorer built with Unity and C#.

![Inner POV](https://github.com/user-attachments/assets/0fd0f4ec-6fd1-48da-b266-0fdd43df5335)

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
- `Alt + Enter` — Windowed/Full screen.
- `Escape` — Lock/Unlock mouse cursor.
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

## Showcase

![Starting POV](https://github.com/user-attachments/assets/5b560f78-29d3-40df-97ec-d4cab47a95cc)

| &nbsp; | &nbsp; |
| --- | --- |
|<img width=450 src="https://github.com/user-attachments/assets/5bebb7dc-798f-4b3f-898f-68af1e83f5eb">|<img width=450 src="https://github.com/user-attachments/assets/f13ef899-f33a-4f51-ad7a-58b57d3bfe64">|
|<img width=450 src="https://github.com/user-attachments/assets/90ff98d6-2e29-4fa3-b65a-b6b5a91d40cc">|<img width=450 src="https://github.com/user-attachments/assets/ab9ac8b1-15af-42a5-bc75-02f0b28495ba">|
|<img width=450 src="https://github.com/user-attachments/assets/a55e7f3b-0801-4ec2-8e99-571b6f8043bb">|<img width=450 src="https://github.com/user-attachments/assets/3bb8a0a9-fedc-4ef1-9800-9ed67cfc2f69">|
