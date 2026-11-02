# Porting the desktop GUI from Avalonia to SkiaSharp

The working plan for the `skia-gui` branch: the GUI drawn with SkiaSharp in the series' look (the Reckless
toolkit's, as Reckless Amphitheater wears it), in an SDL3 window, with the same panels where they are today.
Folded into README.md and CLAUDE.md, and deleted, once the port lands.

## What is kept

- The layout: the drawn menu bar across the top (File, Edit, Game, View, Engine built from the roster, Help, with
  the same shortcuts), the board with the capture bin under it on the left, a 290 px right column of the move list
  (SAN, monospaced) over "Move now!", and the status bar along the bottom. 860 x 660 by default, 680 x 540 at least.
- Every dialog, now drawn inside the window over a dimmed scrim: the message box, promotion, new game, custom
  time control, engine options, engine match, the board's edit menu. The Analysis window stays a window of its own.
- The logic tested without a window: `GameText`, `EngineRosterFile`, `GameController` (Play) untouched.
- `--version` (no display needed) and `--smoke` (the window opens, says so, and quits itself after ten seconds with
  exit code 0), `WindowsConsole` for both on Windows.
- The cburnett PNG pieces, drawn with mipmapped high-quality sampling; the app icon.

## What is replaced

| Avalonia | SkiaSharp + SDL3 |
|---|---|
| `Window`, the platform's compositor | an SDL3 window; Skia draws into a raster `SKSurface`, presented through an SDL streaming texture (no GL context) |
| Fluent theme | the series' tokens (`Look/Theme.cs`, from Amphitheater's `Theme.h`), Noto Serif and Monaspace Xenon embedded |
| `Menu`, `MenuItem`, `MenuFlyout` | `MenuBarView` and `MenuView` drawn in Skia on every OS (the toolkit's Linux `MenuBarView`), Primary = Cmd on macOS, Ctrl elsewhere |
| `Window.ShowDialog` | `DialogView` cards on the window's overlay layer, awaited as tasks |
| `TextBox`, `NumericUpDown`, `ComboBox`, `RadioButton`, `Button` | `TextListView`, `NumberField`, `ChoiceButton`, `ChoiceList`, `PlateButton` |
| `StorageProvider`, `Clipboard` | `IDesktopPlatform` (SDL's file dialogs, SDL's clipboard), faked in the tests |
| `Dispatcher`, `DispatcherTimer` | `UiSynchronizationContext`, pumped by the SDL loop and woken by `SDL_PushEvent`; the loop's clock tick |

## Packages

- `SkiaSharp` 3.119.4 (the version Avalonia already brought), its Windows and macOS natives by dependency, and
  `SkiaSharp.NativeAssets.Linux.NoDependencies` 3.119.4 on Linux (no fontconfig: every font is embedded).
- `ppy.SDL3-CS` 2026.1002.1 (published 2026-10-02): the binding with SDL3's natives for win-x64, osx-arm64/x64 and
  linux-x64 in the package.

## Architecture and test strategy

- **The view layer is the product code and is tested.** Views draw to an `SKCanvas` and take the app's own input
  events (pointer down, move and up, wheel, key, text). `MainView` holds everything `MainWindow.axaml.cs` did: the
  menus built from the roster, the dialogs, file and clipboard actions through `IDesktopPlatform`, the clock tick.
- **The SDL host is thin and excluded from coverage** (`Sdl/*.cs`, as `WindowsConsole.cs` is): window, event
  pump, present, file dialogs, clipboard, waking the UI context. No logic lives there.
- **Tests** render views to a raster surface and drive them with synthesized events: the board's drag and drop,
  hints and promotion; menus opened, chosen and reached by shortcut; each dialog's fields, OK and Cancel; the main
  view's wiring against a fake platform. The Desktop assembly stays at 90 % line coverage or more under
  `tools/coverage-gate.cs`.
