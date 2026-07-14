# 配布キット作成 (Build the KMX deploy kit) - run on the BUILD machine (has ~/ws_moveit, ~/ros2_ws, ~/colcon_ws)
#   1) runs make_kit.sh in WSL  -> ~/KMX-Deploy/artifacts (BITstar rebuild[incremental] + tar fanuc/kmx/endpoint)
#   2) assembles the distributable folder (scripts + artifacts) at -Out
# Usage:  powershell -ExecutionPolicy Bypass -File Build-Kit.ps1 [-Out C:\KMX-Deploy]
# Note:   if kmx_planner/kmx_msgs changed in the repo, sync them into ~/ros2_ws first (sync.sh) so the kit is current.
param([string]$Out = "C:\KMX-Deploy")

$deploy = $PSScriptRoot
function To-WslPath([string]$p){
  $rp = (Resolve-Path -LiteralPath $p).Path
  $d = $rp.Substring(0,1).ToLower()
  return "/mnt/$d" + ($rp.Substring(2) -replace '\\','/')
}

# --- 1) make_kit in WSL (copy to a space-free temp path first to avoid quoting issues) ---
Write-Host "[1/2] make_kit (BITstar rebuild + tar) in WSL ..." -ForegroundColor Cyan
$tmp = Join-Path $env:TEMP "kmx_make_kit.sh"
Copy-Item (Join-Path $deploy "make_kit.sh") $tmp -Force
$tmpWsl = To-WslPath $tmp
& wsl.exe -e bash -lc "bash '$tmpWsl'"
if($LASTEXITCODE -ne 0){ Write-Host "make_kit FAILED (exit $LASTEXITCODE)" -ForegroundColor Red; exit 1 }

# --- 2) assemble distributable folder ---
Write-Host "[2/2] assembling $Out ..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force $Out | Out-Null
Get-ChildItem $deploy -Exclude logs,artifacts,Build-Kit.ps1 | Copy-Item -Destination $Out -Recurse -Force
$outWsl = To-WslPath $Out
& wsl.exe -e bash -lc "rm -rf '$outWsl/artifacts'; cp -r ~/KMX-Deploy/artifacts '$outWsl/'"
if($LASTEXITCODE -ne 0){ Write-Host "artifacts copy FAILED" -ForegroundColor Red; exit 1 }

Write-Host "DONE -> $Out" -ForegroundColor Green
Get-ChildItem "$Out\artifacts" | Select-Object Name, @{n='MB';e={'{0:N1}' -f ($_.Length/1MB)}} | Format-Table -AutoSize
Write-Host "Distribute: copy $Out to the new PC -> set `$WslUser in KMX-Installer.ps1 -> run it" -ForegroundColor Green
