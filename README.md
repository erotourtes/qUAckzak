## qUAckzak

### Useful links

- [A tour of the C# language](https://learn.microsoft.com/en-us/dotnet/csharp/tour-of-csharp/overview)
- [Official modding tutorial](https://steamcommunity.com/sharedfiles/filedetails/?id=484818341)

## Development

### vscode/Dev container (recommended)

1. Link the vanilla game, Duck Game Rebuilt, and the XNA 4 reference directory.

   ```sh
   mkdir -p .local
   ln -s "/path/to/vanilla/Duck Game" .local/duckgame-vanilla
   ln -s /path/to/DuckGameRebuilt .local/duckgame-rebuilt
   ln -s /path/to/XNA/GAC_32 .local/xna
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

Both tasks compile against vanilla Duck Game and XNA. The resulting DLL is the
portable artifact: vanilla loads it directly, while Duck Game Rebuilt performs
its normal XNA-to-FNA remapping. Keep `NoRecompilation` out of `mod.conf`, as
enabling it would make the DLL DGR-only.

For more info see [qUAckzak.Mod.csproj](./qUAckzak.Mod.csproj)
