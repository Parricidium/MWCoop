# MWCoop network check & fix (21/10/2026)
# For: "Offline (GitHub unreachable, error 12029)" in the launcher, while the SAME MWCoop.exe works when run from
# another folder. That means something on the PC (firewall rule, antivirus) blocks MWCoop.exe in AppData\Local\MWCoop.
# Run MWCoop-network-fix.cmd (asks for administrator rights).  -Check : only look, change nothing.
# A report is written on the Desktop: MWCoop-network-report.txt (send it on Discord if it still fails).
param([switch]$Check)
$ErrorActionPreference = 'Continue'
$home_ = "$env:LOCALAPPDATA\MWCoop"
$exe = "$home_\MWCoop.exe"
$report = [Environment]::GetFolderPath('Desktop') + "\MWCoop-network-report.txt"
$lines = New-Object System.Collections.Generic.List[string]
function Say([string]$s) { Write-Host $s; $lines.Add($s) }
function Head([string]$s) { Write-Host ""; Write-Host $s -ForegroundColor Cyan; $lines.Add(""); $lines.Add($s) }
function Note([string]$s) { Write-Host $s -ForegroundColor DarkGray; $lines.Add($s) }
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

Write-Host "==================== MWCoop network check & fix ====================" -ForegroundColor Yellow
Say "MWCoop network check & fix - $(Get-Date -Format 'yyyy-MM-dd HH:mm') - administrator: $admin - check only: $Check"
Note "What this does, in order:"
Note "  1. Looks at the Windows Firewall rules about MWCoop. Removes BLOCK rules, adds ALLOW rules (in and out)"
Note "     for the launcher in AppData\Local\MWCoop."
Note "  2. Looks at the antivirus: lists them, shows if Windows Defender blocked MWCoop, adds the MWCoop folder"
Note "     to Defender's exclusions. Other antiviruses must be opened by you (hint at the end)."
Note "  3. Tests the connection to GitHub and to the mirrors from this script (not from the launcher)."
Note "  4. Runs the launcher silently (update check only, nothing installed) and shows what it could reach."
Note "  5. Saves everything to the Desktop as MWCoop-network-report.txt."
Note "Nothing is deleted on your PC, no game file is touched. Your mods and saves are not affected."
if (-not $admin -and -not $Check) { Note "NOT administrator: rules cannot be changed. Use MWCoop-network-fix.cmd (it asks for the rights)." }

Head "Launcher"
Say "  $exe"
if (-not (Test-Path $exe)) { Say "  NOT FOUND. Install MWCoop first (run MWCoop.exe from the unzipped folder and answer Yes), then run this again." }
else { $it = Get-Item $exe; Say ("  version " + $it.VersionInfo.FileVersion + ", " + $it.Length + " bytes, modified " + $it.LastWriteTime) }

# ---------------------------------------------------------------- 1. Windows Firewall
Head "1. Windows Firewall rules mentioning MWCoop"
Note "  (a BLOCK rule on the program path is exactly what gives 'error 12029: cannot connect')"
$rules = @()
try {
    $byName = @(Get-NetFirewallRule -DisplayName '*MWCoop*' -ErrorAction SilentlyContinue)
    $byApp = @(Get-NetFirewallApplicationFilter -ErrorAction Stop | Where-Object { $_.Program -like '*MWCoop*' } | Get-NetFirewallRule -ErrorAction SilentlyContinue)
    $seen = @{}
    foreach ($r in ($byName + $byApp)) { if ($r -and -not $seen[$r.Name]) { $seen[$r.Name] = $true; $rules += $r } }
} catch { Say "  (cannot read firewall rules: $_)" }
if ($rules.Count -eq 0) { Say "  none found" }
foreach ($r in $rules) {
    $app = ($r | Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue).Program
    Say ("  [" + $r.Action + " / " + $r.Direction + " / " + $(if ("$($r.Enabled)" -eq 'True') { 'enabled' } else { 'disabled' }) + "]  " + $r.DisplayName + "  ->  " + $app)
}
$blocks = @($rules | Where-Object { $_.Action -eq 'Block' -and "$($_.Enabled)" -eq 'True' })
if (-not $Check -and $admin) {
    foreach ($r in $blocks) {
        try { Remove-NetFirewallRule -Name $r.Name -ErrorAction Stop; Say ("  REMOVED block rule: " + $r.DisplayName) } catch { Say ("  could not remove " + $r.DisplayName + ": $_") }
    }
    if (Test-Path $exe) {
        foreach ($dir in 'Inbound', 'Outbound') {
            $name = "MWCoop launcher ($dir)"
            try {
                Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
                New-NetFirewallRule -DisplayName $name -Direction $dir -Action Allow -Program $exe -Profile Any -Enabled True -ErrorAction Stop | Out-Null
                Say "  ADDED allow rule: $name  ->  $exe"
            } catch { Say "  could not add $name : $_" }
        }
    }
} elseif ($blocks.Count -gt 0) { Say ("  " + $blocks.Count + " BLOCK rule(s) found. Run MWCoop-network-fix.cmd (as administrator, without -Check) to remove them.") }
else { Say "  no block rule: the Windows Firewall is not the cause (unless an antivirus manages it)" }

# ---------------------------------------------------------------- 2. Antivirus
Head "2. Antivirus"
try {
    $avs = Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct -ErrorAction Stop
    foreach ($a in $avs) { Say ("  installed: " + $a.displayName) }
} catch { Say "  (cannot list antivirus products)" }
try {
    $st = Get-MpComputerStatus -ErrorAction Stop
    Say ("  Windows Defender: real-time protection " + $st.RealTimeProtectionEnabled + ", mode " + $st.AMRunningMode)
    $det = Get-MpThreatDetection -ErrorAction SilentlyContinue | Where-Object { ($_.Resources -join ' ') -like '*MWCoop*' }
    if ($det) { foreach ($d in $det) { Say ("  Defender BLOCKED something of MWCoop: " + ($d.Resources -join ' ') + " at " + $d.InitialDetectionTime) } }
    else { Say "  Defender: never blocked anything of MWCoop" }
    if (-not $Check -and $admin) {
        $ex = (Get-MpPreference).ExclusionPath
        if ($ex -notcontains $home_) { Add-MpPreference -ExclusionPath $home_ -ErrorAction Stop; Say "  ADDED Defender exclusion for the folder $home_" } else { Say "  Defender exclusion already present for $home_" }
    }
} catch { Say "  (Windows Defender not available here: another antivirus is in charge)" }
Note "  Other antivirus (Avast, AVG, Kaspersky, Norton, Bitdefender, ESET, McAfee, Malwarebytes...): open it, find the"
Note "  'blocked' / 'quarantine' / 'firewall' list, allow MWCoop.exe, and add $home_ to the exclusions"
Note "  (web shield / network shield included). This script cannot do it for you."

# ---------------------------------------------------------------- 3. Connection test
Head "3. Connection test from this script (not from the launcher)"
Note "  (if these are OK but the launcher is still Offline, the block is specific to MWCoop.exe)"
foreach ($u in 'https://api.github.com/', 'https://github.com/', 'https://gh-proxy.com/', 'https://cdn.jsdelivr.net/') {
    try { $t = Measure-Command { Invoke-WebRequest -Uri $u -Method Head -UseBasicParsing -TimeoutSec 15 -ErrorAction Stop | Out-Null }; Say ("  OK    " + $u + " (" + [int]$t.TotalMilliseconds + " ms)") }
    catch { Say ("  FAIL  " + $u + " : " + $_.Exception.Message) }
}

# ---------------------------------------------------------------- 4. The launcher itself
Head "4. Launcher test: silent update check from AppData (nothing is installed)"
$ini = "$home_\mwcoop-lanceur.ini"
$game = ''
if (Test-Path $ini) { $m = Select-String -Path $ini -Pattern '^\s*Jeu\s*=\s*(.+)$' | Select-Object -First 1; if ($m) { $game = $m.Matches[0].Groups[1].Value.Trim() } }
if (-not $game) { foreach ($c in "${env:ProgramFiles(x86)}\Steam\steamapps\common\My Winter Car", "$env:ProgramFiles\Steam\steamapps\common\My Winter Car") { if (Test-Path "$c\mywintercar.exe") { $game = $c; break } } }
if ((Test-Path $exe) -and $game) {
    Say "  game folder: $game"
    $j = "$env:TEMP\mwcoop-maj.txt"
    Remove-Item $j, "$j.lanceur.log" -ErrorAction SilentlyContinue
    $p = Start-Process $exe -ArgumentList "/maj `"$game`" `"$j`"" -PassThru
    if (-not $p.WaitForExit(120000)) { Say "  the launcher did not finish in 2 minutes"; try { $p.Kill() } catch {} }
    if (Test-Path $j) { Get-Content $j | ForEach-Object { Say ("  launcher: " + $_) } }
    if (Test-Path "$j.lanceur.log") { Get-Content "$j.lanceur.log" | Select-String -Pattern 'miroir|versions|http ' | ForEach-Object { Say ("  log: " + $_.Line) } }
    $txt = if (Test-Path $j) { Get-Content $j -Raw } else { '' }
    if ($txt -match 'releases=1') { Say "  RESULT: GOOD. The launcher reaches GitHub (or a mirror) from AppData. Start MWCoop normally: it should be online now." }
    elseif ($txt) { Say "  RESULT: still no answer from AppData. Something on this PC still blocks MWCoop.exe (antivirus network shield, parental control, company proxy). Send the report on Discord." }
    else { Say "  RESULT: the launcher wrote nothing (older version than 1.8.6, or it could not start)." }
} else { Say "  skipped (launcher or game folder not found: '$game')" }

Head "Done"
Say "  Report saved: $report  ->  send it on Discord if MWCoop is still Offline."
$lines | Set-Content -Path $report -Encoding UTF8
if (-not $Check) { Write-Host ""; Write-Host "Press a key to close..."; $null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown') }
