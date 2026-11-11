# Snowdesk

Snowdesk draws falling snow over your Windows desktop. Flakes drift with
changing wind, and you can keep clicking and typing in the apps underneath.

## Install from this repository

Open PowerShell in the repository with the .NET 10 SDK installed. The project
can also build with the .NET 9 SDK if that is already installed.

```powershell
.\Install-Snowdesk.ps1
```

The script installs under `%LOCALAPPDATA%\Programs\Snowdesk` and adds that
directory to your user PATH. Run it again to install updates.

To build and try Snowdesk from the repository without changing PATH, run
`dotnet publish .\src\Snowdesk -c Release -o .\artifacts\snowdesk`, then
run `.\artifacts\snowdesk\snowdesk.exe start`.

## Commands

```powershell
snowdesk start
snowdesk status
snowdesk background on
snowdesk background off
snowdesk density 75
snowdesk accumulation on
snowdesk accumulation off
snowdesk stop
```

The effect runs in the background after `start` returns and covers all
connected monitors. Running `start` again keeps the existing overlay.
Use `stop` to remove the effect and end the background process.

`background on` shows snow only over exposed desktop areas. `background off`
restores snow over open windows and is the default. You can switch while
Snowdesk is running, and the setting is remembered across restarts. Run
`snowdesk background` to see the current setting.

`snowdesk density <1-100>` changes how many flakes fall. The default, `50`,
matches the original snowfall. Changes apply while Snowdesk is running and
are remembered across restarts. Run `snowdesk density` to see the current value.

`snowdesk accumulation on` grows snowbanks over time along the taskbar and
exposed window tops, including browser windows. Higher snowfall density grows
deeper banks faster. The taskbar keeps a narrow ridge of snow along its upper
edge in desktop-only mode, even with a maximized window. Snowbanks shift
along their ledges when windows accelerate or change direction, and snow
that reaches the edge falls off in clumps. You can also brush snow away
with the cursor. Individual browser tabs share the browser window's bank.

Accumulation is off by default. Run `snowdesk accumulation` to check the
setting or `snowdesk accumulation off` to clear the piles. You can switch
while Snowdesk is running. Your preference is saved across restarts, and
new piles form as snow falls.

## Implementation

The overlay targets about 30 frames per second and caps the number of flakes
per monitor. `SnowField` handles particle movement and wind, `SnowOverlay`
draws the particles, and `OverlayContext` manages displays and timing.
`SnowAccumulation` grows a snow profile per exposed ledge and simulates the
small bursts of snow shaken or brushed loose. Accumulation uses buffered frames
to prevent the clear and redraw from appearing separately.

