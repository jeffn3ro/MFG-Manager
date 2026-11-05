<#
    Draws the two pictures the setup wizard shows, using the app's logo (src\MfgManager.App\Assets\logo.png):
      dialog.bmp  - 493 x 312, the Welcome and Finish pages: dark panel with the logo on the left, white on the right
      banner.bmp  - 493 x 58, the top strip of the other pages: white with the logo on the right
    Windows Installer only shows .bmp files, so they're saved as 24-bit BMPs.

    Usage:  powershell -File tools\make-installer-art.ps1 -OutFolder installer\Assets
#>
param([Parameter(Mandatory)][string]$OutFolder)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

$Panel = [Windows.Media.Color]::FromRgb(0x1D, 0x20, 0x24)   # the app's dark panel: a shade lighter than the logo, so its rounded corners show
$Muted = [Windows.Media.Color]::FromRgb(0xBE, 0xC2, 0xBA)
$Gold  = [Windows.Media.Color]::FromRgb(0xF2, 0xC2, 0x30)   # the logo's gold-to-lime
$Lime  = [Windows.Media.Color]::FromRgb(0xA3, 0xE6, 0x35)

$logoPath = Join-Path (Split-Path $PSScriptRoot) 'src\MfgManager.App\Assets\logo.png'
$logo = New-Object Windows.Media.Imaging.BitmapImage
$logo.BeginInit(); $logo.UriSource = New-Object Uri $logoPath; $logo.CacheOption = 'OnLoad'; $logo.EndInit()

function Save-Bmp([int]$Width, [int]$Height, [scriptblock]$Draw, [string]$Path) {
    $visual = New-Object Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $dc.DrawRectangle([Windows.Media.Brushes]::White, $null, (New-Object Windows.Rect 0, 0, $Width, $Height))
    & $Draw $dc
    $dc.Close()
    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap $Width, $Height, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $rgb = New-Object Windows.Media.Imaging.FormatConvertedBitmap $bitmap, ([Windows.Media.PixelFormats]::Bgr24), $null, 0
    $encoder = New-Object Windows.Media.Imaging.BmpBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($rgb))
    $stream = [IO.File]::Create($Path)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
}

function Draw-Text($dc, [string]$Text, [double]$Size, $Color, [double]$CenterX, [double]$Top, $Weight) {
    $typeface = New-Object Windows.Media.Typeface (New-Object Windows.Media.FontFamily 'Segoe UI'),
        ([Windows.FontStyles]::Normal), $Weight, ([Windows.FontStretches]::Normal)
    $formatted = New-Object Windows.Media.FormattedText $Text, ([Globalization.CultureInfo]::InvariantCulture),
        ([Windows.FlowDirection]::LeftToRight), $typeface, $Size, (New-Object Windows.Media.SolidColorBrush $Color), 1.0
    $dc.DrawText($formatted, (New-Object Windows.Point ($CenterX - $formatted.Width / 2), $Top))
}

New-Item -ItemType Directory -Force $OutFolder | Out-Null
$OutFolder = (Resolve-Path $OutFolder).Path

# The wizard's text starts about 180 px from the left, so the panel stays left of that.
Save-Bmp 493 312 {
    param($dc)
    $panelWidth = 164
    $dc.DrawRectangle((New-Object Windows.Media.SolidColorBrush $Panel), $null, (New-Object Windows.Rect 0, 0, $panelWidth, 312))
    $stripe = New-Object Windows.Media.LinearGradientBrush $Gold, $Lime, 90
    $dc.DrawRectangle($stripe, $null, (New-Object Windows.Rect $panelWidth, 0, 3, 312))
    $size = 104
    $dc.DrawImage($logo, (New-Object Windows.Rect (($panelWidth - $size) / 2), 78, $size, $size))
    Draw-Text $dc 'MFG Manager' 15 ([Windows.Media.Colors]::White) ($panelWidth / 2) 196 ([Windows.FontWeights]::SemiBold)
    Draw-Text $dc 'Frame Generation, unlocked' 10 $Muted ($panelWidth / 2) 218 ([Windows.FontWeights]::Normal)
} (Join-Path $OutFolder 'dialog.bmp')

# The page title and description sit on the left of the banner, so the logo goes on the right.
Save-Bmp 493 58 {
    param($dc)
    $size = 44
    $dc.DrawImage($logo, (New-Object Windows.Rect (493 - $size - 8), 7, $size, $size))
} (Join-Path $OutFolder 'banner.bmp')

"Saved dialog.bmp and banner.bmp to $OutFolder"
