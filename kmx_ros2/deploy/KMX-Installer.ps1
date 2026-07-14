# KMX Deploy wizard - local web installer (Windows PowerShell 5.1)
# Right-click this file -> "Run with PowerShell". A browser opens with the wizard.
# Deps: only Windows PowerShell + a browser. WSL steps run via `wsl -u root`, so no sudo password.
#
# ASCII-only on purpose: step titles/descriptions live in steps.json (UTF-8),
# the actual shell commands live in steps.sh (bash). This file only orchestrates.

# ===== config (defaults; override via -WslUser / -WslDistro / -Port) =====
param(
  [string]$WslUser   = "kmxros",       # WSL user on the target PC (lowercase; created by Setup-WSL.ps1)
  [string]$WslDistro = "Ubuntu-22.04", # name shown by: wsl -l -v
  [int]$Port         = 8899
)
$KitDir = $PSScriptRoot          # this deploy folder
if([string]::IsNullOrEmpty($KitDir)){ $KitDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
if([string]::IsNullOrEmpty($KitDir)){ $KitDir = (Get-Location).Path }
$Artifacts = Join-Path $KitDir "artifacts"
$env:WSL_UTF8 = "1"              # make wsl.exe emit UTF-8 (not UTF-16) so string matching works

# ===== path conversion  C:\a\b -> /mnt/c/a/b =====
function To-WslPath([string]$p){
  if([string]::IsNullOrEmpty($p)){ return "" }
  $rp = Resolve-Path -LiteralPath $p -ErrorAction SilentlyContinue
  if($rp){ $p = $rp.Path }
  $drive = $p.Substring(0,1).ToLower()
  return "/mnt/$drive" + ($p.Substring(2) -replace '\\','/')
}
$KitWsl  = To-WslPath $Artifacts
$StepsSh = To-WslPath (Join-Path $KitDir "steps.sh")
$TgzOk = Test-Path (Join-Path $Artifacts "kmx_moveit.tgz")
Write-Host ("KitDir = {0}" -f $KitDir)
Write-Host ("KitWsl = {0}" -f $KitWsl)
Write-Host ("artifacts\kmx_moveit.tgz present: {0}" -f $TgzOk)
if(-not $TgzOk){ Write-Host "WARNING: artifacts not found. Run this installer from inside the KMX-Deploy folder (the one containing 'artifacts\')." -ForegroundColor Yellow }

# ===== load step metadata (UTF-8) =====
$Steps = Get-Content (Join-Path $KitDir "steps.json") -Raw -Encoding UTF8 | ConvertFrom-Json

$LogDir = Join-Path $KitDir "logs"
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
$Proc  = @{}
$State = @{}
foreach($s in $Steps){ $State[[int]$s.id] = "pending" }

function LogPath([int]$id){ return (Join-Path $LogDir ("step{0}.log" -f $id)) }

function WriteCmdFile([int]$id,[string]$fn,[string]$suffix){
  # Write the bash command to a .sh file and run it via `wsl bash <path>`.
  # This avoids Start-Process splitting a space-containing command string into separate args.
  $body = "export KIT_WSL='$KitWsl'`nexport WSL_USER='$WslUser'`nsource '$StepsSh'`n${fn}_${suffix}`n"
  $file = Join-Path $LogDir ("cmd_{0}_{1}.sh" -f $id,$suffix)
  [IO.File]::WriteAllText($file, ($body -replace "`r`n","`n"), (New-Object Text.UTF8Encoding($false)))
  return (To-WslPath $file)
}

function Start-Step([int]$id){
  $s = $Steps | Where-Object { [int]$_.id -eq $id }
  $log = LogPath $id
  Set-Content -Path $log -Value "" -Encoding UTF8
  if(Test-Path ($log + ".err")){ Remove-Item ($log + ".err") -Force -ErrorAction SilentlyContinue }
  $State[$id] = "running"
  if($s.ctx -eq "win"){
    $body = "[wsl2]`r`nnetworkingMode=mirrored`r`ndnsTunneling=true`r`nautoProxy=true`r`n"
    Set-Content -Path (Join-Path $env:USERPROFILE ".wslconfig") -Value $body -Encoding ascii
    Add-Content -Path $log -Value ("wrote " + (Join-Path $env:USERPROFILE ".wslconfig"))
    $State[$id] = "ok_run"
    return
  }
  $ctxuser = "root"
  if($s.ctx -eq "user"){ $ctxuser = $WslUser }
  if([string]::IsNullOrEmpty($KitWsl)){
    Add-Content -Path $log -Value "ERROR: artifacts path (KIT_WSL) is empty. KitDir=[$KitDir]. Run the installer from inside the KMX-Deploy folder (the one containing 'artifacts\')."
    $State[$id] = "ng"; return
  }
  $cmdWsl = WriteCmdFile $id $s.fn "run"
  $p = Start-Process "wsl.exe" `
        -ArgumentList @("-d",$WslDistro,"-u",$ctxuser,"--","bash",$cmdWsl) `
        -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $log -RedirectStandardError ($log + ".err")
  $Proc[$id] = $p
}

function Poll-State(){
  foreach($id in @($Proc.Keys)){
    $p = $Proc[$id]
    if($p -and $p.HasExited){
      if($State[$id] -eq "running"){
        if($p.ExitCode -eq 0){ $State[$id] = "ok_run" } else { $State[$id] = "ng" }
      }
      $Proc.Remove($id)
    }
  }
}

function Read-Log([int]$id){
  $log = LogPath $id
  $t = ""
  if(Test-Path $log){ $t = (Get-Content $log -Raw -Encoding UTF8 -ErrorAction SilentlyContinue) }
  $e = $log + ".err"
  if(Test-Path $e){
    $ev = (Get-Content $e -Raw -Encoding UTF8 -ErrorAction SilentlyContinue)
    if($ev){ $t = "$t`n[stderr]`n$ev" }
  }
  if($null -eq $t){ $t = "" }
  return $t
}

function Run-Verify([int]$id){
  $s = $Steps | Where-Object { [int]$_.id -eq $id }
  $log = LogPath $id
  $out = ""
  $ok = $false
  if($s.ctx -eq "win"){
    $out = ((& wsl.exe -l -v | Out-String) -replace "`0","")   # strip NULs in case wsl still emits UTF-16
    $ok = ($out -match [Regex]::Escape($WslDistro))
  } else {
    $ctxuser = "root"
    if($s.ctx -eq "user"){ $ctxuser = $WslUser }
    $cmdWsl = WriteCmdFile $id $s.fn "verify"
    try { $out = (& wsl.exe -d $WslDistro -u $ctxuser -- bash $cmdWsl 2>&1 | Out-String) } catch { $out = "$_" }
    $ok = ($out -match "VERIFY_OK")
  }
  Add-Content -Path $log -Value ("`n[verify] " + $out)
  if($ok){ $State[$id] = "ok" } else { $State[$id] = "ng" }
}

function State-Json(){
  Poll-State
  $arr = @()
  foreach($s in $Steps){
    $id = [int]$s.id
    $raw = $State[$id]
    $disp = "pending"
    switch($raw){ "ok" {$disp="ok"} "ng" {$disp="ng"} "running" {$disp="running"} "ok_run" {$disp="ran"} }
    $logtext = "$(Read-Log $id)"   # force to a single string (avoid object/array -> [object Object] in UI)
    $mode = $s.mode; if([string]::IsNullOrEmpty($mode)){ $mode = "install" }
    $arr += @{ id=$id; title=$s.title; desc=$s.desc; hint=$s.hint; cmd=$s.cmd; status=$disp; log=$logtext; mode=$mode }
  }
  return (@{ steps=$arr } | ConvertTo-Json -Depth 6)
}

# ===== HTTP server =====
$listener = New-Object System.Net.HttpListener
$prefix = "http://127.0.0.1:$Port/"
$listener.Prefixes.Add($prefix)
try { $listener.Start() } catch { Write-Host "Cannot open port $Port : $_" -ForegroundColor Red; exit 1 }
Write-Host "KMX Deploy wizard: $prefix   (Ctrl+C to quit)" -ForegroundColor Green
Start-Process $prefix

function Send-Text($ctx,[string]$body,[string]$ct){
  $bytes = [Text.Encoding]::UTF8.GetBytes($body)
  $ctx.Response.ContentType = "$ct; charset=utf-8"
  $ctx.Response.ContentLength64 = $bytes.Length
  $ctx.Response.OutputStream.Write($bytes,0,$bytes.Length)
  $ctx.Response.OutputStream.Close()
}
function Read-Body($ctx){
  $sr = New-Object IO.StreamReader($ctx.Request.InputStream, $ctx.Request.ContentEncoding)
  $txt = $sr.ReadToEnd(); $sr.Close()
  if($txt){ return ($txt | ConvertFrom-Json) }
  return $null
}

while($listener.IsListening){
  $ctx = $listener.GetContext()
  $path = $ctx.Request.Url.AbsolutePath
  try {
    if($path -eq "/"){
      Send-Text $ctx (Get-Content (Join-Path $KitDir "ui.html") -Raw -Encoding UTF8) "text/html"
    } elseif($path -eq "/api/state"){
      Send-Text $ctx (State-Json) "application/json"
    } elseif($path -eq "/api/run"){
      $b = Read-Body $ctx; Start-Step ([int]$b.step); Send-Text $ctx '{"ok":true}' "application/json"
    } elseif($path -eq "/api/verify"){
      $b = Read-Body $ctx; Run-Verify ([int]$b.step); Send-Text $ctx '{"ok":true}' "application/json"
    } else {
      $ctx.Response.StatusCode = 404; Send-Text $ctx '{"error":"not found"}' "application/json"
    }
  } catch {
    try { $ctx.Response.StatusCode = 500; Send-Text $ctx (@{ error = "$_" } | ConvertTo-Json) "application/json" } catch {}
  }
}
