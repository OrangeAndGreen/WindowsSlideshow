<#
.SYNOPSIS
    Builds Viggi Slideshow and installs it as the Windows screensaver.

.DESCRIPTION
    Publishes a self-contained release build to %LOCALAPPDATA%\Programs\ViggiSlideshow, copies
    the program as ViggiSlideshow.scr, makes it the current screensaver (what right-click >
    Install does for a .scr), and opens Screen Saver Settings to set the wait time.

.PARAMETER NoActivate
    Install the files but leave the current screensaver as it is.
#>
param(
    [switch]$NoActivate
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\ViggiSlideshow\ViggiSlideshow.csproj'
$target = Join-Path $env:LOCALAPPDATA 'Programs\ViggiSlideshow'

# A running copy holds its files open
Get-Process ViggiSlideshow -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "Publishing to $target"
dotnet publish $project -c Release -o $target --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# The .exe keeps working alongside; the .scr is the same program under the name Windows looks for
$scr = Join-Path $target 'ViggiSlideshow.scr'
Copy-Item (Join-Path $target 'ViggiSlideshow.exe') $scr -Force

if (-not $NoActivate) {
    Set-ItemProperty 'HKCU:\Control Panel\Desktop' -Name 'SCRNSAVE.EXE' -Value $scr
    Set-ItemProperty 'HKCU:\Control Panel\Desktop' -Name 'ScreenSaveActive' -Value '1'
    Write-Host "Viggi Slideshow is now the screensaver."
    Start-Process 'control.exe' 'desk.cpl,,@screensaver'
}

Write-Host "Installed $scr"
