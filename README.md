# Snowdesk

Snowdesk is a Windows application that creates snow effects including snow falling from the sky, and snow accumulation on your windows and taskbar. This application works with minimal CPU and RAM usage, but performance may depend on your hardware.

## Install
1. Clone or download this repo
2. Open PowerShell in the repository with the .NET 10 SDK installed. The project
can also build with the .NET 9 SDK if that is already installed.
3. Run:
```powershell
.\Install-Snowdesk.ps1
```

The script installs under `%LOCALAPPDATA%\Programs\Snowdesk` and adds that
directory to your user PATH.

If you would like to update, first pull the changes to the repo then run the same command again.

To build and try Snowdesk from the repository without changing PATH, run
`dotnet publish .\src\Snowdesk -c Release -o .\artifacts\snowdesk`, then
run `.\artifacts\snowdesk\snowdesk.exe start`.

## Commands

```powershell
snowdesk start
snowdesk status
snowdesk stop
snowdesk background on/off
snowdesk density 1-100
snowdesk accumulation on/off
```
`SnowAccumulation` grows a snow profile per exposed ledge and simulates the
small bursts of snow shaken or brushed loose. Accumulation uses buffered frames
to prevent the clear and redraw from appearing separately.

