$ErrorActionPreference = 'Stop'

$installPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\Snowdesk'
$project = Join-Path $PSScriptRoot 'src\Snowdesk\Snowdesk.csproj'

$currentExe = Join-Path $installPath 'snowdesk.exe'
if (Test-Path -LiteralPath $currentExe) {
    & $currentExe stop
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not stop the installed copy of Snowdesk.'
    }
}

dotnet publish $project -c Release --self-contained false -o $installPath
if ($LASTEXITCODE -ne 0) {
    throw 'Could not publish Snowdesk.'
}

$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if (($userPath -split ';') -notcontains $installPath) {
    $updatedPath = (@($userPath, $installPath) | Where-Object { $_ }) -join ';'
    [Environment]::SetEnvironmentVariable('Path', $updatedPath, 'User')
}
if (($env:Path -split ';') -notcontains $installPath) {
    $env:Path += ";$installPath"
}

Write-Host "Snowdesk is installed in $installPath."
Write-Host 'Run snowdesk start to begin.'
