# Setup-WSL.ps1  --  one-time prerequisite. RUN AS ADMINISTRATOR (right-click -> Run as administrator).
#   Installs WSL2 + Ubuntu-22.04 and creates the WSL user 'kmxros' as the default (no interactive prompt).
#   If the WSL feature was just enabled, Windows may require a REBOOT; then re-run this script.
#   After this succeeds, run KMX-Installer.ps1.
param(
  [string]$User   = "kmxros",
  [string]$Pass   = "kmxros",
  [string]$Distro = "Ubuntu-22.04"
)

function Test-Admin {
  $id = [Security.Principal.WindowsIdentity]::GetCurrent()
  return ([Security.Principal.WindowsPrincipal]$id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
if(-not (Test-Admin)){ Write-Host "Run this as Administrator (right-click -> Run as administrator)." -ForegroundColor Red; exit 1 }
$env:WSL_UTF8 = "1"   # make wsl.exe emit UTF-8 (not UTF-16) so distro detection works

Write-Host "[1/3] Ensuring WSL2 + $Distro ..." -ForegroundColor Cyan
wsl --set-default-version 2 2>$null | Out-Null
$have = $false
$list = (wsl -l -q) 2>$null
if($list){ foreach($n in $list){ if((($n -replace "`0","").Trim()) -eq $Distro){ $have = $true } } }
if(-not $have){
  wsl --install -d $Distro --no-launch
  if($LASTEXITCODE -ne 0){
    Write-Host "wsl --install failed. If the WSL feature was just enabled, REBOOT Windows and re-run this script." -ForegroundColor Yellow
    exit 1
  }
}

Write-Host "[2/3] Creating user '$User' (default) ..." -ForegroundColor Cyan
$mk = "id $User >/dev/null 2>&1 || (useradd -m -s /bin/bash $User && echo '${User}:${Pass}' | chpasswd && usermod -aG sudo $User); printf '[user]\ndefault=$User\n' > /etc/wsl.conf; echo SETUP_OK"
$out = (& wsl.exe -d $Distro --user root -- bash -lc $mk 2>&1 | Out-String)
if($out -notmatch "SETUP_OK"){
  Write-Host $out
  Write-Host "User setup failed. The distro may need first-run init: launch '$Distro' once (creating any user), or REBOOT and re-run." -ForegroundColor Yellow
  exit 1
}

Write-Host "[3/3] Restarting distro so default user applies ..." -ForegroundColor Cyan
wsl --terminate $Distro 2>$null | Out-Null
$who = (& wsl.exe -d $Distro -- whoami 2>&1 | Out-String).Trim()
Write-Host "Default WSL user is now: $who" -ForegroundColor Green
if($who -eq $User){
  $installer = Join-Path $PSScriptRoot "KMX-Installer.ps1"
  if(Test-Path $installer){
    Write-Host "WSL ready. Launching KMX-Installer wizard ..." -ForegroundColor Cyan
    & $installer -WslUser $User -WslDistro $Distro
  } else {
    Write-Host "OK. Next: run KMX-Installer.ps1" -ForegroundColor Green
  }
} else {
  Write-Host "Note: default user is '$who', expected '$User'. Pass -WslUser to KMX-Installer.ps1, or check /etc/wsl.conf." -ForegroundColor Yellow
}
