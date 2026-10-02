# Capture la fenetre d'une instance de test (meme hors ecran) par PrintWindow, sans l'activer.
param([int]$Joueur = 1, [string]$Png = "$PSScriptRoot\capture$Joueur.png", [switch]$Client)
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public struct RECT { public int L, T, R, B; }
}
'@
[W]::SetProcessDPIAware() | Out-Null   # pixels reels (sinon image reduite par la mise a l'echelle de Windows)
$dir = "D:\Games\COOPTEST\My Winter Car\MWCoop-Joueur$Joueur"
$p = Get-Process mywintercar -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$dir\*" } | Select-Object -First 1
if (-not $p -or $p.MainWindowHandle -eq 0) { throw "instance $Joueur sans fenetre" }
$r = New-Object W+RECT
[W]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
$bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc()
[W]::PrintWindow($p.MainWindowHandle, $dc, 2) | Out-Null
$g.ReleaseHdc($dc); $g.Dispose()
$fmt = if ($Png -like '*.jpg') { [System.Drawing.Imaging.ImageFormat]::Jpeg } else { [System.Drawing.Imaging.ImageFormat]::Png }
if ($Client) {
    # Sans la barre de titre ni les bords : la zone de jeu seule.
    $c = New-Object W+RECT; [W]::GetClientRect($p.MainWindowHandle, [ref]$c) | Out-Null
    $bw = [int](($r.R - $r.L - $c.R) / 2); $th = $r.B - $r.T - $c.B - $bw
    $crop = $bmp.Clone((New-Object System.Drawing.Rectangle $bw, $th, $c.R, $c.B), $bmp.PixelFormat)
    $bmp.Dispose(); $bmp = $crop
}
$bmp.Save($Png, $fmt); $bmp.Dispose()
"$Png ($($r.R - $r.L)x$($r.B - $r.T) en $($r.L),$($r.T))"
