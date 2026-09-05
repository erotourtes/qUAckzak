## qUAckzak

### Useful links

- [A tour of the C# language](https://learn.microsoft.com/en-us/dotnet/csharp/tour-of-csharp/overview)
- [Official modding tutorial](https://steamcommunity.com/sharedfiles/filedetails/?id=484818341)

## Development

### vscode/Dev container (recommended)

1. Link your Duck Game Rebuilt installation.

   ```sh
   mkdir -p .local
   ln -s /path/to/DuckGameRebuilt .local/duckgame-rebuilt
   ```

2. Install Docker, VS Code, and the **Dev Containers** extension.
3. Run **Dev Containers: Reopen in Container** from the command palette.
4. Press `Ctrl+Shift+B` to build the mod after editing it.

### Tasks

- **Clean** removes the `build` and `obj` directories.
- **Build (dev)** creates an unoptimized `.dll` with debug symbols in
  `build/Debug`. This is the default `Ctrl+Shift+B` task.
- **Build (prod)** creates an optimized `.dll` without debug symbols in
  `build/Release`.

Both tasks compile against Duck Game Rebuilt and FNA. The resulting DLL is
DGR-only and is loaded directly because `NoRecompilation` is enabled in
`mod.conf`.

qUAckzak is a client mod. DGR does not advertise it as a required lobby mod, so
you can keep it enabled when playing with people who do not have it installed.

For more info see [qUAckzak.Mod.csproj](./qUAckzak.Mod.csproj)

## Modes

### turboqUAck

- In **Options > Edit Controls**, bind **TURBO QUACK** for your keyboard or
  controller. Press that button to enable turboqUAck for the duck you currently
  control, and press it again to disable the mode. The current state is shown
  on screen. The toggle works in both offline and online play.
- Hold Left, Right, or Up while ragdolled to repeat that nudge at the fastest
  rate allowed by the game's normal ragdoll nudge cooldown. Release the
  direction to stop. Fancy Shoes are intentionally excluded.
- Hold Jump while trapped in a net or a Camping Gun sleeping bag to repeat the
  struggle until released or the duck escapes. Left and Right can still be held
  to steer a grounded net.
- Other local and remote ducks are not affected. The enabled state persists
  between rounds.
- Run `quackzak_turboquack_status` in the developer console to report whether
  turboqUAck is currently enabled or disabled.

All qUAckzak console commands use the lowercase `quackzak_` prefix.

### konUAmi

- In **Options > Edit Controls**, bind **KONUAMI** for your keyboard or
  controller.
- Press the bound button to pop the duck you currently control, using Duck
  Game's built-in Konami-code pop behavior.
- Other local and remote ducks are not affected.

## qUAckhat

qUAckhat packages live under `content/quackhat/<hat-id>/` and are configured by
their XSD-validated `hat.xml` manifests.

- Run `quackzak_quackhat_status` to list loaded packages and manifest errors.
- Run `quackzak_quackhat_reload` after editing a manifest to reload and
  validate every package without restarting Duck Game.
