# Furniture Veil

Hide and replace that gpose furniture that's too small for you.

Created by **Rude**. An experimental Dalamud companion for Brio. Select a placed furnishing during GPose, hide its original, then spawn and resize a replacement using Brio.

## Install

This build targets **Dalamud API 15 / .NET 10**.

1. Open `/xlsettings` in FFXIV.
2. Under **Experimental > Custom Plugin Repositories**, add this URL:

   ```text
   https://raw.githubusercontent.com/rude-jerk/dalamud-plugins/refs/heads/main/repo.json
   ```

3. Save, open `/xlplugins`, and search for **Furniture Veil** to install it. If you already use this repository, refresh the plugin list.
4. Enter GPose in housing and run `/fveil`.

For development, build from source or download `latest.zip` from [Releases](https://github.com/rude-jerk/furniture-veil/releases). Extract it to a permanent folder and add its `FurnitureVeil.dll` under **Experimental > Dev Plugin Locations**. Keep the DLL and JSON manifest together. Disable any developer copy before installing the repository version. The zip is a Dalamud plugin, not a Penumbra mod.

Dalamud's [developer-plugin instructions](https://dalamud.dev/faq/getting-started/) describe the same loading mechanism.

## Use

- Search the furniture list, which is sorted by distance from your player.
- Click an item name to mark its origin on screen. Instance numbers distinguish duplicate furnishings. This is a location marker, not a mesh outline.
- Click **Hide** for that particular placed instance.
- In Brio, spawn the replacement and adjust its scale/position. The selected row provides **Copy position** and **Copy item name** to help you match it manually.
- Click **Restore**, **Restore all**, or run `/fveil restore` to undo hiding.
- Exiting GPose restores tracked objects that are still loaded. Unloading the plugin also attempts restoration. Changing territory discards references to the old scene, which the game unloads.

Closing the window keeps hidden items hidden until restored or GPose ends. The marker is only drawn while the window is open. This plugin does not automatically spawn replacements or communicate with Brio.

## Validation status

- Release build: passes against the installed Dalamud libraries, with zero warnings/errors.
- Automated lifecycle checks: 14 passing tests. They cover individual-instance isolation, restoration, GPose transitions, stale selections, room changes, disappearing/replaced objects, and failed writes.
- **Live game rendering has not been tested.** Compilation and tests validate the plugin interfaces and session logic, not the game's response to hiding a furnishing.

First in-game acceptance checks:

1. In GPose, hide one simple chair/table. Confirm it disappears and a second copy stays visible.
2. Restore it, then hide it again and exit GPose. Confirm restoration.
3. Hide several pieces and use Restore all; repeat with plugin disable while still in GPose.
4. Spawn a Brio replacement, resize it, and confirm hiding/restoring the original does not affect the replacement.
5. Test a lamp and an animated furnishing separately. Confirm shadows, light emission, and effects behave acceptably; these may need additional handling.
6. Test an apartment, another player's house, and outdoor furnishings. Check the item list and names in each.

If an item remains visible after Hide, or leaves lighting/effects behind, record its name, indoor/outdoor location, and whether Restore works. Errors are logged in `/xllog`. Leaving and re-entering the housing instance reloads its original scene if restoration fails.

## Implementation

The backend reads the current housing furniture manager and resolves each furniture slot to its current object. It uses FFXIVClientStructs `GameObject.SetVisibility` (`0` shows, `1` hides). It does not call housing placement, inventory, or network functions. It changes the local view and has no saved layout editing feature.

All native reads and writes occur on the framework thread. Before each write, the backend rechecks the current territory/manager, furniture slot, object slot/address, furniture ID, base ID, layout ID, and entity ID. Saved addresses are never dereferenced directly. Preview objects are excluded. Original visibility is kept only for the current GPose session, and already hidden objects are not claimed.

The first version uses the object's visibility mechanism; it does not traverse furniture child models or separately suppress lights, VFX, or collision. The live checks above determine whether individual furniture types need that work. A location marker may sit away from the visible center of an unusually shaped furnishing.

Native game structures can change after a game/Dalamud update. Rebuild and revalidate against the corresponding installed libraries. Published updates are distributed through the [Dalamud plugin repository](https://github.com/rude-jerk/dalamud-plugins).

## Build and test

From this directory:

```powershell
dotnet build -c Release
dotnet run --project tests\FurnitureVeil.Tests.csproj -c Release
```

No NuGet package dependencies are required. The .NET 10 SDK and local Dalamud libraries are required. Override their location with `-p:DalamudDir="C:\path\to\Dalamud"` when needed.

Source references: [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs), [Dalamud sample plugin](https://github.com/goatcorp/SamplePlugin).
