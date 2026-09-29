# Extra Dim

A minimal screen dimmer and eye-care tool for Windows. One ~50 KB exe, nothing to install.
English by default; switch to 中文 in the panel.

## Download

Grab `ExtraDim.exe` from [Releases](https://github.com/ced2711/extra-dim/releases).

Recommended location (per-user, no admin needed):
`%LOCALAPPDATA%\Programs\ExtraDim\ExtraDim.exe`. Then turn on **Start with Windows** in the panel.

## Use

- A moon icon appears in the tray
- **Left-click**: panel · **Middle-click**: pause / resume · **Right-click**: menu
- `Ctrl+Alt+PgUp / PgDn`: brighter / darker on the display under the mouse
- `Ctrl+Alt+End`: pause / resume

## Features

- **Dim** up to 90% (never fully black), with smooth fades
- **Eye care**: warms the screen by colour temperature, 6500K (neutral) to 3400K.
  Default 4800K: studies point to 4000–5000K as the most comfortable range for long reading
  and screen work, while f.lux's 3400K night setting is meant for sleep, not all-day use.
- Eye care scales R/G/B directly (like Night Light), so blacks stay black with no yellow haze.
  It uses the Windows full-screen colour matrix, falls back to the gamma ramp, and only as a
  last resort uses a tinted overlay. Colours are restored on exit or crash.
- Multiple displays, optionally adjusted separately; adapts when displays change
- Screenshots and screen sharing show the normal screen
- Click-through, never steals focus, hidden from Alt+Tab; per-monitor DPI aware
- Settings are saved in `%APPDATA%\ExtraDim\settings.ini`

## Lightweight by design

- ~3 MB working set when idle, ~6 MB while dimming
- Zero CPU and zero wake-ups when idle: no polling timers; windows are created on first use
- Runs at below-normal priority in Windows 11 efficiency mode (EcoQoS)

## Build

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Uses the C# compiler that ships with Windows (.NET Framework 4); no SDK needed.
Output goes to `build\ExtraDim.exe`.

## License

[MIT](LICENSE)
