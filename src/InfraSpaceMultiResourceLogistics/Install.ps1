$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$output = Join-Path $projectRoot "bin\Release\net472"
$destination = Join-Path $env:USERPROFILE "AppData\LocalLow\Dionic Software\InfraSpace\mods\InfraSpaceMultiResourceLogistics"

dotnet build (Join-Path $projectRoot "InfraSpaceMultiResourceLogistics.csproj") -c Release

New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item (Join-Path $output "InfraSpaceMultiResourceLogistics.dll") $destination -Force
Copy-Item (Join-Path $output "0Harmony.dll") $destination -Force
Copy-Item (Join-Path $projectRoot "config.json") $destination -Force
Copy-Item (Join-Path $projectRoot "logo.jpg") $destination -Force

Write-Host "Installed InfraSpaceMultiResourceLogistics to $destination"
