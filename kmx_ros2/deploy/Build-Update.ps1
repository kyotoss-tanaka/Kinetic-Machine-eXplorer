# 差分アップデート キット作成 (Build the KMX UPDATE kit) - run on the BUILD machine.
#   1) runs make_update.sh in WSL  -> ~/KMX-Deploy/artifacts (slim ros2_src.tgz + scripts.tgz + apply_update.sh, NO MoveIt rebuild)
#   2) assembles a small distributable folder (deploy scripts + slim artifacts) at -Out
# Usage:  powershell -ExecutionPolicy Bypass -File Build-Update.ps1 [-Out C:\KMX-Update]
# Note:   if kmx_planner/kmx_msgs changed in the repo, sync them into ~/ros2_ws first (sync.sh) and colcon build,
#         so the kit reflects the latest code. This does NOT rebuild BITstar MoveIt (use Build-Kit.ps1 for that).
# On the target PC: replace the existing KMX-Deploy folder with this, run KMX-Installer.ps1, pick the "アップデート" tab.
param([string]$Out = "C:\KMX-Update")

$deploy = $PSScriptRoot
function To-WslPath([string]$p){
  $rp = (Resolve-Path -LiteralPath $p).Path
  $d = $rp.Substring(0,1).ToLower()
  return "/mnt/$d" + ($rp.Substring(2) -replace '\\','/')
}

# --- 1) make_update in WSL (copy to a space-free temp path first to avoid quoting issues) ---
Write-Host "[1/2] make_update (slim tar, no MoveIt rebuild) in WSL ..." -ForegroundColor Cyan
$tmp = Join-Path $env:TEMP "kmx_make_update.sh"
Copy-Item (Join-Path $deploy "make_update.sh") $tmp -Force
# apply_update.sh must sit next to make_update.sh (make_update copies it into artifacts)
Copy-Item (Join-Path $deploy "apply_update.sh") (Join-Path $env:TEMP "apply_update.sh") -Force
$tmpWsl = To-WslPath $tmp
& wsl.exe -e bash -lc "bash '$tmpWsl'"
if($LASTEXITCODE -ne 0){ Write-Host "make_update FAILED (exit $LASTEXITCODE)" -ForegroundColor Red; exit 1 }

# --- 2) assemble distributable folder (deploy scripts + slim artifacts) ---
Write-Host "[2/2] assembling $Out ..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force $Out | Out-Null
Get-ChildItem $deploy -Exclude logs,artifacts,Build-Kit.ps1,Build-Update.ps1 | Copy-Item -Destination $Out -Recurse -Force
$outWsl = To-WslPath $Out
& wsl.exe -e bash -lc "rm -rf '$outWsl/artifacts'; cp -r ~/KMX-Deploy/artifacts '$outWsl/'"
if($LASTEXITCODE -ne 0){ Write-Host "artifacts copy FAILED" -ForegroundColor Red; exit 1 }

Write-Host "DONE -> $Out" -ForegroundColor Green
Get-ChildItem "$Out\artifacts" | Select-Object Name, @{n='MB';e={'{0:N1}' -f ($_.Length/1MB)}} | Format-Table -AutoSize
Write-Host "Distribute: copy $Out to the target PC (replace its KMX-Deploy) -> run KMX-Installer.ps1 -> 'アップデート' tab -> 実行/確認" -ForegroundColor Green
