$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$output = Join-Path $projectRoot "bin\Release\net472"
$destination = Join-Path $env:USERPROFILE "AppData\LocalLow\Dionic Software\InfraSpace\mods\InfraSpaceStationTools"

dotnet build (Join-Path $projectRoot "InfraSpaceStationTools.csproj") -c Release

New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item (Join-Path $output "InfraSpaceStationTools.dll") $destination -Force
Copy-Item (Join-Path $output "0Harmony.dll") $destination -Force
Copy-Item (Join-Path $projectRoot "config.json") $destination -Force

Write-Host "Installed InfraSpaceStationTools to $destination"
