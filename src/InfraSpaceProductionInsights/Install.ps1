$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$output = Join-Path $projectRoot "bin\Release\net472"
$destination = Join-Path $env:USERPROFILE "AppData\LocalLow\Dionic Software\InfraSpace\mods\InfraSpaceProductionInsights"

dotnet build (Join-Path $projectRoot "InfraSpaceProductionInsights.csproj") -c Release

New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item (Join-Path $output "InfraSpaceProductionInsights.dll") $destination -Force
Copy-Item (Join-Path $output "0Harmony.dll") $destination -Force
Copy-Item (Join-Path $projectRoot "config.json") $destination -Force

Write-Host "Installed InfraSpaceProductionInsights to $destination"
