# Compile MWCoop puis assemble dist\out\MWCoop-<version>.zip (sans aucune donnee du jeu).
# -Publier : cree la release v<Version> sur GitHub (Parricidium/MWCoop) avec le zip. -Notes : texte de la
# release (puces en francais, une ligne ---, les memes puces en anglais, une ligne ---, les memes en russe, une ligne ---,
# les memes en arabe : le lanceur les affiche dans sa langue ; l'annonce Discord prend l'anglais, le russe et l'arabe --
# demandes de JD, 13/10).
# -AFaire : seulement quand les joueurs ont une manipulation a faire apres le correctif (meme format : francais, ---,
# anglais ; une ligne par consigne). Mis en tete des notes ("A faire : ..." / "To do: ...") et en encadre
# "What you need to do" dans l'annonce Discord.
# -Merci : remerciement ajoute a l'annonce Discord (anglais), mentions <@id> comprises (la personne est notifiee).
# -Ping : force la mention du role Update Pings dans l'annonce (sinon seulement a la premiere version d'une famille X.Y).
# -Nightly (canal temporaire, demande de JD, 14/10) : version en developpement (ex. 0.96.0-nightly.1, branche refonte) ;
# pre-release GitHub, paquet MWCoop-<version>.nightly (le meme zip sous un nom que les lanceurs d'avant 0.95.2 ne prennent
# pas), branche courante poussee (pas main), PAS d'annonce Discord. Le lanceur ne la propose qu'en canal Nightly.
param([Parameter(Mandatory = $true)][string]$Version, [switch]$Publier, [string]$Notes = '', [string]$AFaire = '', [string]$Merci = '', [switch]$Nightly, [switch]$Ping)
if ($Nightly -ne ($Version -match 'nightly')) { throw "-Nightly va avec une version 'x.y.z-nightly.N' (et seulement elle)" }
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
Copy-Item "$root\build\MWCoopRT.dll" "$stage\MWCoop"   # ray tracing (DXR), charge par le mod
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
if ($Nightly) { $nz = "$PSScriptRoot\out\MWCoop-$Version.nightly"; Copy-Item $zip $nz -Force; Remove-Item $zip; $zip = $nz }
# Editeur de carte (editor\, demande de JD du 20/10) : paquet a part, EditeurCarte-<version>.editeur (un zip sous un autre
# nom : les lanceurs prennent le premier .zip d'une release pour le mod). Le lanceur (page Outils) le telecharge a la
# demande dans %LOCALAPPDATA%\MWCoop\editeur.
$edStage = "$PSScriptRoot\out\EditeurCarte-$Version"
if (Test-Path $edStage) { Remove-Item $edStage -Recurse -Force }
New-Item -ItemType Directory -Force $edStage | Out-Null
foreach ($f in 'EditeurCarte.exe', 'EditeurCarte.exe.config', 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll') { Copy-Item "$root\build\editeur\$f" $edStage }
Copy-Item "$root\build\editeur\ui" $edStage -Recurse
Set-Content "$edStage\version.txt" $Version -NoNewline -Encoding ASCII
$edPack = "$PSScriptRoot\out\EditeurCarte-$Version.editeur"
if (Test-Path $edPack) { Remove-Item $edPack -Force }
$z = [System.IO.Compression.ZipFile]::Open($edPack, 'Create')
foreach ($f in Get-ChildItem $edStage -File -Recurse) {
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, $f.FullName, $f.FullName.Substring($edStage.Length + 1).Replace('\', '/')) | Out-Null
}
$z.Dispose()
"Ecrit : $edPack ($([math]::Round((Get-Item $edPack).Length / 1KB)) Ko)"
# Empreintes SHA-256 publiees a cote du zip : chacun peut verifier ses fichiers (et les chercher sur VirusTotal)
# quand un antivirus a apprentissage automatique s'inquiete d'un exe non signe.
$sums = "$PSScriptRoot\out\SHA256SUMS-$Version.txt"
$lines = foreach ($f in @($zip, $edPack, "$stage\MWCoop.exe", "$stage\version.dll", "$stage\MWCoop\MWCoop.dll", "$stage\MWCoop\MWCoopRT.dll")) {
    $h = (Get-FileHash $f -Algorithm SHA256).Hash.ToLower()
    $n = if ($f -eq $zip -or $f -eq $edPack) { Split-Path $f -Leaf } else { $f.Substring($stage.Length + 1).Replace('\', '/') }
    "$h  $n"
}
[System.IO.File]::WriteAllText($sums, ($lines -join "`r`n") + "`r`n", (New-Object System.Text.UTF8Encoding $false))
$lines
if ($Publier) {
    # Rien de non commite : le tag doit pointer sur le code du zip (0.26.3 : commit rate, release partie quand meme).
    if (git -C $root status --porcelain) { throw "modifications non commitees : committer avant de publier" }
    if (-not $Notes) { $Notes = "MWCoop $Version" }
    # " --- " au milieu d'une ligne vaut une ligne --- (0.63.0 : tout etait reste colle, francais et anglais, dans les
    # notes du lanceur comme dans l'annonce Discord, qui ne doit avoir que l'anglais)
    $Notes = $Notes -replace '[ \t]+---+[ \t]+', "`n---`n"
    $AFaire = $AFaire -replace '[ \t]+---+[ \t]+', "`n---`n"
    $todoEn = ''
    if ($AFaire.Trim()) {
        # (parties : francais, anglais, russe, arabe ; une partie absente prend l'anglais)
        $tp = @($AFaire -split '(?m)^\s*---+\s*$')
        $np = @($Notes -split '(?m)^\s*---+\s*$')
        $tEn = if ($tp.Count -ge 2) { $tp[1] } else { $tp[0] }; $tRu = if ($tp.Count -ge 3) { $tp[2] } else { $tEn }; $tAr = if ($tp.Count -ge 4) { $tp[3] } else { $tEn }
        $lines = { param($t, $pre) (@($t -split "`r?`n" | ForEach-Object { $_.Trim() -replace '^- *', '' } | Where-Object { $_ }) | ForEach-Object { "- $pre$_" }) -join "`n" }
        $fr = & $lines $tp[0] ("$([char]0xC0) faire : ")
        $todoEn = (@($tEn -split "`r?`n" | ForEach-Object { $_.Trim() -replace '^- *', '' } | Where-Object { $_ }) | ForEach-Object { "- $_" }) -join "`n"
        $todoRu = (@($tRu -split "`r?`n" | ForEach-Object { $_.Trim() -replace '^- *', '' } | Where-Object { $_ }) | ForEach-Object { "- $_" }) -join "`n"
        $en = & $lines $tEn 'To do: '
        $ru = & $lines $tRu ("$([char]0x041D)$([char]0x0443)$([char]0x0436)$([char]0x043D)$([char]0x043E): ")
        $todoAr = (@($tAr -split "`r?`n" | ForEach-Object { $_.Trim() -replace '^- *', '' } | Where-Object { $_ }) | ForEach-Object { "- $_" }) -join "`n"
        $ar = & $lines $tAr ("$([char]0x0645)$([char]0x0637)$([char]0x0644)$([char]0x0648)$([char]0x0628): ")
        if ($np.Count -ge 4) { $Notes = "$fr`n$($np[0].Trim())`n---`n$en`n$($np[1].Trim())`n---`n$ru`n$($np[2].Trim())`n---`n$ar`n$($np[3].Trim())" }
        elseif ($np.Count -ge 3) { $Notes = "$fr`n$($np[0].Trim())`n---`n$en`n$($np[1].Trim())`n---`n$ru`n$($np[2].Trim())" }
        elseif ($np.Count -ge 2) { $Notes = "$fr`n$($np[0].Trim())`n---`n$en`n$($np[1].Trim())" } else { $Notes = "$fr`n$en`n$($Notes.Trim())" }
    }
    $nf = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($nf, $Notes, (New-Object System.Text.UTF8Encoding $false))
    # Le tag est cree sur GitHub : le code doit y etre avant (sinon il pointe sur l'ancien main).
    $head = (git -C $root rev-parse HEAD).Trim()
    $branch = if ($Nightly) { (git -C $root rev-parse --abbrev-ref HEAD).Trim() } else { 'main' }
    if ($Nightly -and $branch -eq 'main') { Remove-Item $nf; throw "une Nightly se publie depuis sa branche (refonte), pas main" }
    $po = cmd /c "git -C `"$root`" push origin HEAD:$branch 2>&1"   # par cmd : git ecrit sur stderr, ce qui arreterait le script
    if ($LASTEXITCODE -ne 0) { Remove-Item $nf; throw "echec du push de $branch" }
    if ($Nightly) { gh release create "v$Version" $zip $edPack $sums --repo Parricidium/MWCoop --title "MWCoop $Version (Nightly)" --notes-file $nf --target $head --prerelease }
    else { gh release create "v$Version" $zip $edPack $sums --repo Parricidium/MWCoop --title "MWCoop $Version" --notes-file $nf --target $head }
    $rc = $LASTEXITCODE
    Remove-Item $nf
    if ($rc -ne 0) { throw "echec de la publication GitHub" }
    $url = "https://github.com/Parricidium/MWCoop/releases/tag/v$Version"
    "Publie : $url"
    # Liste des versions dans le depot (distersions.json, meme forme que l'API) : le lanceur la lit par les miroirs des fichiers
    # quand l'API de GitHub est injoignable ou saturee (joueurs russes, 21/10). Commit a part, a pousser avec main.
    if (-not $Nightly) {
        try {
            gh api "repos/Parricidium/MWCoop/releases?per_page=40" | Out-File -FilePath "$PSScriptRoot\versions.json" -Encoding utf8
            git add "$PSScriptRoot\versions.json"
            git commit -q -m "versions.json $Version" -- "$PSScriptRoot\versions.json"
            "versions.json : $Version en tete"
        } catch { Write-Warning "versions.json : $_" }
    }
    # Annonce Discord (#announcements) si dist\discord-webhook.txt existe (hors depot, ecrit par
    # MWCoopDiscord\setup_server.py) : ligne 1 = webhook, ligne 2 = role Update Pings. Parties anglaise, russe et arabe des notes.
    $wf = "$PSScriptRoot\discord-webhook.txt"
    if ($Nightly) { "Nightly : pas d'annonce Discord" }
    elseif (Test-Path $wf) {
        try {
            $w = @(Get-Content $wf -Encoding UTF8 | Where-Object { $_.Trim() })
            $parts = @($Notes -split '(?m)^\s*---+\s*$')
            $en = $(if ($parts.Count -ge 2) { $parts[1] } else { $parts[0] }).Trim()
            $ruN = $(if ($parts.Count -ge 3) { $parts[2] } else { '' }).Trim()
            $arN = $(if ($parts.Count -ge 4) { $parts[3] } else { '' }).Trim()
            if ($todoEn) {   # (dans l'encadre)
                $en = (@($en -split "`n") | Where-Object { $_ -notmatch '^- To do: ' }) -join "`n"
                $ruN = (@($ruN -split "`n") | Where-Object { $_ -notmatch "^- $([char]0x041D)$([char]0x0443)$([char]0x0436)$([char]0x043D)$([char]0x043E): " }) -join "`n"
                $arN = (@($arN -split "`n") | Where-Object { $_ -notmatch "^- $([char]0x0645)$([char]0x0637)$([char]0x0644)$([char]0x0648)$([char]0x0628): " }) -join "`n"
            }
            # Notes longues (0.96.0 : liste detaillee Reworked / Legacy, demande de JD) : au lieu de couper a 3900 caracteres,
            # chaque langue va dans son propre message a la suite (russe, arabe), elle-meme decoupee par lignes si besoin.
            $chunks = { param($t) $acc = @(); $cur = ''; foreach ($l in @($t -split "`n")) { if (($cur.Length + $l.Length + 1) -gt 3900 -and $cur) { $acc += $cur; $cur = '' }; $cur = if ($cur) { "$cur`n$l" } else { $l } }; if ($cur) { $acc += $cur }; $acc }
            $ruTitle = "**$([char]0x0420)$([char]0x0443)$([char]0x0441)$([char]0x0441)$([char]0x043A)$([char]0x0438)$([char]0x0439)**"   # (Russkiy)
            $arTitle = "**$([char]0x0627)$([char]0x0644)$([char]0x0639)$([char]0x0631)$([char]0x0628)$([char]0x064A)$([char]0x0629)**"   # (al-arabiya)
            $desc = $en
            $follow = @()   # messages a la suite : @(titre, texte)
            if ($ruN) { if (($desc.Length + $ruN.Length + 20) -le 3900) { $desc = "$en`n`n$ruTitle`n$ruN" } else { $follow += ,@($ruTitle, $ruN) } }
            if ($arN) { if (-not $follow -and ($desc.Length + $arN.Length + 20) -le 3900) { $desc += "`n`n$arTitle`n$arN" } else { $follow += ,@($arTitle, $arN) } }
            $enParts = @(& $chunks $desc)
            $desc = $enParts[0]
            for ($k = 1; $k -lt $enParts.Count; $k++) { $follow = @(,@('', $enParts[$k])) + $follow }
            $embed = @{
                title = "MWCoop $Version"; url = $url; color = 7912959; description = $desc
                footer = @{ text = 'Update from the launcher: NEW VERSION - UPDATE' }
            }
            # Role Update Pings : seulement a la premiere version d'une famille X.Y (1.2, 1.3, 1.4...), pas aux X.Y.1, X.Y.2...
            # (demande de JD, 21/10) ; -Ping force la mention. La famille est connue par les tags deja poses (v$Version exclu).
            $xy = (($Version -split '\.')[0..1]) -join '.'
            # (les pre-versions -- v1.10.0-nightly.1... -- ne sont jamais annoncees : elles ne comptent pas comme famille deja annoncee ;
            # la 1.10.0 est partie sans mention a cause de sa nightly, 22/10)
            $family = @(git tag -l "v$xy.*" | Where-Object { $_.Trim() -and $_.Trim() -ne "v$Version" -and $_.Trim() -notmatch '-' })
            $big = $Ping -or ($family.Count -eq 0)
            "Annonce Discord : " + $(if ($big) { "avec @Update Pings (premiere version $xy)" } else { "sans mention (famille $xy deja annoncee : $($family -join ', '))" })
            $head = $(if ($big) { "<@&$($w[1].Trim())> " } else { '' }) + "**MWCoop $Version is out!**"
            if ($todoEn) {   # (consignes : encadre a part, et un mot dans le message)
                if ($todoEn.Length -gt 1000) { $todoEn = $todoEn.Substring(0, 1000) + '...' }
                $embed.fields = @(@{ name = "$([char]0x26A0)$([char]0xFE0F) What you need to do"; value = $todoEn })
                if ($ruN -and $todoRu -and $todoRu -ne $todoEn) {
                    if ($todoRu.Length -gt 1000) { $todoRu = $todoRu.Substring(0, 1000) + '...' }
                    $embed.fields += @{ name = "$([char]0x26A0)$([char]0xFE0F) $([char]0x0427)$([char]0x0442)$([char]0x043E) $([char]0x043D)$([char]0x0443)$([char]0x0436)$([char]0x043D)$([char]0x043E) $([char]0x0441)$([char]0x0434)$([char]0x0435)$([char]0x043B)$([char]0x0430)$([char]0x0442)$([char]0x044C)"; value = $todoRu }
                }
                if ($arN -and $todoAr -and $todoAr -ne $todoEn) {
                    if ($todoAr.Length -gt 1000) { $todoAr = $todoAr.Substring(0, 1000) + '...' }
                    $embed.fields += @{ name = "$([char]0x26A0)$([char]0xFE0F) $([char]0x0645)$([char]0x0627) $([char]0x064A)$([char]0x062C)$([char]0x0628) $([char]0x0641)$([char]0x0639)$([char]0x0644)$([char]0x0647)"; value = $todoAr }
                }
                $head += ' **Action needed after updating: see below.**'
            }
            $users = @()
            if ($Merci.Trim()) {
                $head += "`n$($Merci.Trim())"
                $users = @([regex]::Matches($Merci, '<@!?(\d+)>') | ForEach-Object { $_.Groups[1].Value })
            }
            $msg = @{
                content          = $head
                allowed_mentions = @{ roles = @($w[1].Trim()); users = $users }
                embeds           = @($embed)
            }
            $body = [System.Text.Encoding]::UTF8.GetBytes(($msg | ConvertTo-Json -Depth 6))
            Invoke-RestMethod -Method Post -Uri $w[0].Trim() -Body $body -ContentType 'application/json; charset=utf-8' | Out-Null
            foreach ($f in $follow) {   # (suite : un message par langue, sans mention)
                foreach ($part in @(& $chunks $f[1])) {
                    $d2 = if ($f[0]) { "$($f[0])`n$part" } else { $part }
                    $m2 = @{ allowed_mentions = @{ parse = @() }; embeds = @(@{ title = "MWCoop $Version"; url = $url; color = 7912959; description = $d2 }) }
                    Start-Sleep -Milliseconds 800
                    Invoke-RestMethod -Method Post -Uri $w[0].Trim() -Body ([System.Text.Encoding]::UTF8.GetBytes(($m2 | ConvertTo-Json -Depth 6))) -ContentType 'application/json; charset=utf-8' | Out-Null
                }
            }
            "Annonce Discord envoyee" + $(if ($follow) { " (+ $($follow.Count) suite(s))" })
        } catch { Write-Warning "annonce Discord ratee : $_" }
    }
}
