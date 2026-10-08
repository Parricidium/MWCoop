# Compile MWCoop puis assemble dist\out\MWCoop-<version>.zip (sans aucune donnee du jeu).
# -Publier : cree la release v<Version> sur GitHub (Parricidium/MWCoop) avec le zip. -Notes : texte de la
# release (puces en francais, une ligne ---, les memes puces en anglais : le lanceur les affiche).
# -AFaire : seulement quand les joueurs ont une manipulation a faire apres le correctif (meme format : francais, ---,
# anglais ; une ligne par consigne). Mis en tete des notes ("A faire : ..." / "To do: ...") et en encadre
# "What you need to do" dans l'annonce Discord.
param([Parameter(Mandatory = $true)][string]$Version, [switch]$Publier, [string]$Notes = '', [string]$AFaire = '')
$root = Split-Path $PSScriptRoot
$code = Get-Content "$root\src\MWCoop\Version.cs" -Raw
if ($code -notmatch "Text = `"$([regex]::Escape($Version))`"") { throw "src\MWCoop\Version.cs ne dit pas $Version" }
$out = cmd /c "`"$root\build.cmd`"" 2>&1
if ($LASTEXITCODE -ne 0) { $out | Select-String 'error'; throw "echec de compilation" }
$ErrorActionPreference = 'Stop'   # apres la compilation : vcvars ecrit sur stderr

$stage = "$PSScriptRoot\out\MWCoop-$Version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force "$stage\MWCoop" | Out-Null
Copy-Item "$root\build\version.dll" $stage
Copy-Item "$root\build\MWCoop.exe" $stage   # lanceur : mises a jour automatiques
Copy-Item "$root\build\MWCoop.dll" "$stage\MWCoop"
Copy-Item "$PSScriptRoot\files\*" $stage -Recurse -Force
Set-Content "$stage\MWCoop\version.txt" $Version -NoNewline -Encoding ASCII

$zip = "$PSScriptRoot\out\MWCoop-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$z = [System.IO.Compression.ZipFile]::Open($zip, 'Create')
foreach ($f in Get-ChildItem $stage -File -Recurse) {
    $rel = $f.FullName.Substring($stage.Length + 1).Replace('\', '/')
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, $f.FullName, $rel) | Out-Null
}
$z.Dispose()
"Ecrit : $zip ($([math]::Round((Get-Item $zip).Length / 1KB)) Ko)"
# Empreintes SHA-256 publiees a cote du zip : chacun peut verifier ses fichiers (et les chercher sur VirusTotal)
# quand un antivirus a apprentissage automatique s'inquiete d'un exe non signe.
$sums = "$PSScriptRoot\out\SHA256SUMS-$Version.txt"
$lines = foreach ($f in @($zip, "$stage\MWCoop.exe", "$stage\version.dll", "$stage\MWCoop\MWCoop.dll")) {
    $h = (Get-FileHash $f -Algorithm SHA256).Hash.ToLower()
    $n = if ($f -eq $zip) { Split-Path $zip -Leaf } else { $f.Substring($stage.Length + 1).Replace('\', '/') }
    "$h  $n"
}
[System.IO.File]::WriteAllText($sums, ($lines -join "`r`n") + "`r`n", (New-Object System.Text.UTF8Encoding $false))
$lines
if ($Publier) {
    # Rien de non commite : le tag doit pointer sur le code du zip (0.26.3 : commit rate, release partie quand meme).
    if (git -C $root status --porcelain) { throw "modifications non commitees : committer avant de publier" }
    if (-not $Notes) { $Notes = "MWCoop $Version" }
    $todoEn = ''
    if ($AFaire.Trim()) {
        $tp = @($AFaire -split '(?m)^\s*---+\s*$')
        $np = @($Notes -split '(?m)^\s*---+\s*$')
        $lines = { param($t, $pre) (@($t -split "`r?`n" | ForEach-Object { $_.Trim() -replace '^- *', '' } | Where-Object { $_ }) | ForEach-Object { "- $pre$_" }) -join "`n" }
        $fr = & $lines $tp[0] ("$([char]0xC0) faire : ")
        $todoEn = (@($tp[-1] -split "`r?`n" | ForEach-Object { $_.Trim() -replace '^- *', '' } | Where-Object { $_ }) | ForEach-Object { "- $_" }) -join "`n"
        $en = & $lines $tp[-1] 'To do: '
        if ($np.Count -ge 2) { $Notes = "$fr`n$($np[0].Trim())`n---`n$en`n$($np[-1].Trim())" } else { $Notes = "$fr`n$en`n$($Notes.Trim())" }
    }
    $nf = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($nf, $Notes, (New-Object System.Text.UTF8Encoding $false))
    # Le tag est cree sur GitHub : le code doit y etre avant (sinon il pointe sur l'ancien main).
    $head = (git -C $root rev-parse HEAD).Trim()
    $po = cmd /c "git -C `"$root`" push origin HEAD:main 2>&1"   # par cmd : git ecrit sur stderr, ce qui arreterait le script
    if ($LASTEXITCODE -ne 0) { Remove-Item $nf; throw "echec du push de main" }
    gh release create "v$Version" $zip $sums --repo Parricidium/MWCoop --title "MWCoop $Version (pre-alpha)" --notes-file $nf --prerelease --target $head
    $rc = $LASTEXITCODE
    Remove-Item $nf
    if ($rc -ne 0) { throw "echec de la publication GitHub" }
    $url = "https://github.com/Parricidium/MWCoop/releases/tag/v$Version"
    "Publie : $url"
    # Annonce Discord (#announcements) si dist\discord-webhook.txt existe (hors depot, ecrit par
    # MWCoopDiscord\setup_server.py) : ligne 1 = webhook, ligne 2 = role Update Pings. Partie anglaise des notes.
    $wf = "$PSScriptRoot\discord-webhook.txt"
    if (Test-Path $wf) {
        try {
            $w = @(Get-Content $wf -Encoding UTF8 | Where-Object { $_.Trim() })
            $en = ($Notes -split '(?m)^\s*---+\s*$')[-1].Trim()
            if ($todoEn) { $en = (@($en -split "`n") | Where-Object { $_ -notmatch '^- To do: ' }) -join "`n" }   # (dans l'encadre)
            if ($en.Length -gt 3900) { $en = $en.Substring(0, 3900) + '...' }
            $embed = @{
                title = "MWCoop $Version (pre-alpha)"; url = $url; color = 7912959; description = $en
                footer = @{ text = 'Update from the launcher: NEW VERSION - UPDATE' }
            }
            $head = "<@&$($w[1].Trim())> **MWCoop $Version is out!**"
            if ($todoEn) {   # (consignes : encadre a part, et un mot dans le message)
                if ($todoEn.Length -gt 1000) { $todoEn = $todoEn.Substring(0, 1000) + '...' }
                $embed.fields = @(@{ name = "$([char]0x26A0)$([char]0xFE0F) What you need to do"; value = $todoEn })
                $head += ' **Action needed after updating: see below.**'
            }
            $msg = @{
                content          = $head
                allowed_mentions = @{ roles = @($w[1].Trim()) }
                embeds           = @($embed)
            }
            $body = [System.Text.Encoding]::UTF8.GetBytes(($msg | ConvertTo-Json -Depth 6))
            Invoke-RestMethod -Method Post -Uri $w[0].Trim() -Body $body -ContentType 'application/json; charset=utf-8' | Out-Null
            "Annonce Discord envoyee"
        } catch { Write-Warning "annonce Discord ratee : $_" }
    }
}
