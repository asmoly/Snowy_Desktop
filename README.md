# Snowdesk

https://github.com/user-attachments/assets/50f14457-0d84-469f-a980-a741e2f1934c

Snowdesk draws falling snow over your Windows desktop. Flakes drift with
changing wind, and you can keep clicking and typing in the apps underneath.

On my computer I had 0.04% CPU usage and it used 34MB of RAM.

Snowdesk is a Windows application that creates snow effects including snow falling from the sky, and snow accumulation on your windows and taskbar. This application works with minimal CPU and RAM usage, but performance may depend on your hardware.

## Install
1. Clone or download this repo
2. Open PowerShell in the repository with the .NET 10 SDK installed. The project
can also build with the .NET 9 SDK if that is already installed.
3. Run:
```powershell
To install .Net 10 SDK run:
winget install --id Microsoft.DotNet.SDK.10 --exact

To install snowdesk run:
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

