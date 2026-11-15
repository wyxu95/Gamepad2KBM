# Gamepad2KBM

English | [简体中文](README.zh-CN.md)

Use a gamepad as a keyboard and mouse on Windows, with time-based pointer acceleration, split QWERTY typing, IME support, and vibration feedback.

Supports Windows 10/11 x64 and XInput-compatible controllers. Written in C# and built with the .NET Framework compiler included with Windows.

![Split keyboard and mouse mode](docs/preview.png)

## Getting started

1. Download or clone this repository.
2. On Windows, double-click `start.cmd`.
3. Center both sticks and release all buttons, then press **LB + RB together, release both, and press them together again within 0.6 seconds** to enter mouse mode.

The launcher builds the program into `%LOCALAPPDATA%\Gamepad2KBM\Gamepad2KBM.exe`. The main program starts in standby in the system tray. Open the input monitor separately with `monitor.cmd` when you want to identify controller buttons.

You can also run `dist\Gamepad2KBM.exe` with `settings.json` beside it. Source files can reside in WSL.

## Start with Windows

Right-click the tray icon and enable **开机自启** (Start with Windows). The main program will launch in standby when the current user signs in to Windows. Click the same item again to disable it.

The startup entry points directly to the executable, so startup works independently of the source checkout and WSL. The input monitor remains available through its separate launcher.

You can also manage startup from PowerShell:

```powershell
$exe = "$env:LOCALAPPDATA\Gamepad2KBM\Gamepad2KBM.exe"
Start-Process $exe -ArgumentList '--enable-startup' -Wait
Start-Process $exe -ArgumentList '--disable-startup' -Wait
```

## Controls

| Input | Mouse mode | Keyboard mode |
|---|---|---|
| LB + RB, tapped together twice | Return to standby | Return to standby |
| Left stick | Move the pointer | Select a key on the left wheel |
| Right stick | Scroll vertically and horizontally | Select a key on the right wheel |
| LT / RT | — | Confirm the left / right key |
| A | Left click; hold to drag | Space / commit IME candidate |
| B | Right click | Escape |
| X | — | Backspace; hold to repeat |
| Y | Click at the pointer and enter keyboard mode | Return to mouse mode |
| LB / RB, individually | — | PageUp / PageDown on release |
| D-pad | — | Arrow keys |
| L3 / R3 | — | Enter / Tab |
| View / Back | — | Toggle left letters / digits |
| Menu / Start | — | Toggle right letters / symbols |
| Keyboard F8 | Stop controller input | Stop controller input |

LB + RB tapped together twice also enables control from standby. Release the sticks and buttons once after entering a mode.

### Split keyboard

The two wheels follow QWERTY row order:

- Left, 13 keys: `QWER / ASDF / ZXCVB`
- Right, 14 keys: `TYUIOP / GHJKL; / NM`

Push a stick beyond about 45% travel to highlight a key, then press its trigger to type once. Press the trigger again to repeat. Returning below about 29% travel clears the selection. When both triggers are pressed together, the left key is sent first.

Xiaohe shuangpin annotations appear beside the corresponding letter keys. Letter keys pass through the foreground application's system IME; select your preferred shuangpin scheme in that IME. The program tags its generated keyboard input to preserve Escape and Enter when AutoHotkey remappings are active.

The left digit layer contains `1234567890`. The right symbol layer contains `.,;:!?-_/@()[]`. Digits are sent as keyboard keys, and symbols are inserted as literal text. Both sides switch independently and reset to letters on standby or disconnection.

The dark overlay uses 80% opacity, passes mouse input through, and preserves the target application's input focus.

### Rear buttons on GameSir T4N-Pro

L3 and R3 mean pressing the left and right sticks down vertically.

Map the left rear button L4 to **View / Back** and the right rear button R4 to **Menu / Start** to use them for layer switching. The input monitor will show the mapped View or Menu input. The front buttons retain the same layer actions.

Following the [official manual](https://gamesir.com/support/manuals/gamesir-super-nova):

1. Hold **M + the rear button** until the light strip slowly flashes white, then release.
2. Press the button to assign: View for L4, Menu for R4.
3. Press the same rear button again to save.

To clear one rear button, enter its mapping mode and press that rear button again to save an empty mapping. Configure or clear each rear button separately. Use individual short presses for layer switching; this model reserves holding View + Menu for hardware mode changes in wired/receiver mode.

### Vibration feedback

| Action | Duration |
|---|---:|
| Enter or return to mouse mode | 240 ms |
| Enter keyboard mode | 360 ms |
| Return to standby | 120 ms |
| Switch either keyboard layer | 165 ms |

Feedback stops automatically. F8, disconnection, and application exit stop the motors immediately. The tray's **测试震动** item tests a 360 ms pulse on the selected controller. XInput vibration results are recorded in the log.

## Pointer acceleration

Holding the same stick direction and deflection increases speed over time. Defaults in `settings.json`:

| Setting | Default | Meaning |
|---|---:|---|
| `MouseDeadzone` | 0.12 | Radial pointer deadzone |
| `InitialSpeed` | 100 | Initial full-deflection speed, px/s |
| `MaximumSpeed` | 1600 | Maximum full-deflection speed, px/s |
| `AccelerationDelay` | 0.08 | Delay before acceleration, seconds |
| `FullSpeedAfter` | 0.90 | Time from initial deflection to full ramp, seconds |
| `LetterDeadzone` | 0.45 | Deflection needed to select a key |
| `LetterHysteresisDegrees` | 2.0 | Angular margin for keeping the selected key |

After 0.08 seconds, speed follows a Smoothstep ramp until 0.9 seconds. The result is multiplied by the fourth power of deflection normalized beyond the deadzone: 50% normalized deflection caps speed at 100 px/s, while full deflection reaches 1600 px/s. Reducing deflection slows the pointer immediately. Centering stops movement and resets the timer; changing direction preserves elapsed time.

Edit the configuration through the tray and restart to apply it. Disconnecting returns to standby and releases held mouse buttons; use the activation gesture after reconnecting.

## Build and validation

Exit the application from the tray before rebuilding. In Windows PowerShell:

```powershell
.\build.ps1 -CoreOnly  # Logic checks
.\build.ps1 -Test      # Logic checks plus interactive Windows input/focus checks
.\build.ps1 -Run       # Build and launch
```

From WSL, with the checkout as the working directory:

```bash
/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe \
  -NoProfile -ExecutionPolicy Bypass \
  -File "$(wslpath -w ./build.ps1)" -CoreOnly
```

Checks cover acceleration, fractional motion, deadzones, every wheel key, selection hysteresis, trigger confirmation, double-tap activation, keyboard layers, scrolling, button repeat, disconnection cleanup, and vibration timing. Interactive checks use a temporary window to verify real keyboard events, mouse clicks, pointer movement, and focus preservation. They require that temporary window to receive foreground focus.

Query all four XInput slots:

```powershell
$dir = "$env:LOCALAPPDATA\Gamepad2KBM"
Start-Process "$dir\Gamepad2KBM.exe" -ArgumentList @('--probe', "$dir\probe.json") -Wait
Get-Content "$dir\probe.json"
```

The application selects the controller receiving the activation gesture. Logs are written to `gamepad2kbm.log` beside the executable. Use the same privilege level as the target application, and configure other controller mappers to let Gamepad2KBM handle desktop keyboard/mouse input independently.

## Source

- `src/Core.cs`: acceleration, selection, layers, activation gestures, and feedback timing.
- `src/Native.cs`: XInput and Windows input APIs.
- `src/App.cs`: tray, overlay, startup registration, and polling.
- `src/Monitor.cs`: the separately launched input monitor.
- `tests/Tests.cs`: logic checks, rendering previews, and interactive Windows checks.

## Acknowledgements and license

Interaction inspired by [joytyping](https://github.com/HHHLZX/joytyping). Xiaohe labels reference [Xiaohe](https://flypy.cc/) and [Rime double-pinyin](https://github.com/rime/rime-double-pinyin). The AHK compatibility marker references [AutoHotkey](https://github.com/AutoHotkey/AutoHotkey/blob/v2.0/source/keyboard_mouse.h).

Licensed under the [MIT License](LICENSE).
