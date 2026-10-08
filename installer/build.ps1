# Builds installer\bin\BingLan-Setup-<version>.exe: a self-contained win-x64 publish of the
# app wrapped in a per-user Inno Setup installer (no administrator rights needed).
param([string]$Version = "0.2.7")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $PSScriptRoot "obj\publish"
$iscc = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue)?.Source,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw "未找到 Inno Setup 6：winget install JRSoftware.InnoSetup --scope user" }

if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
dotnet publish (Join-Path $root "src\BingLan.App\BingLan.App.csproj") -c Release -r win-x64 `
    --self-contained true -p:SatelliteResourceLanguages="zh-Hans%3Ben" -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $iscc "/DAppVersion=$Version" (Join-Path $PSScriptRoot "BingLan.iss")
exit $LASTEXITCODE
