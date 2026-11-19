# Hearth - servidor per al dashboard de casa (iPad)
# No cal instal-lar res mes: fa servir el .NET que ja porta Windows.
param([switch]$Visible)
$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$dades = Join-Path $dir 'dades'
if (-not (Test-Path $dades)) { New-Item -ItemType Directory -Path $dades | Out-Null }
$registre = Join-Path $dades 'registre.txt'
try { if ((Test-Path $registre) -and (Get-Item $registre).Length -gt 2MB) { Remove-Item $registre } } catch {}
try { Start-Transcript -Path $registre -Append | Out-Null } catch {}
try { $Host.UI.RawUI.WindowTitle = 'Hearth' } catch {}

function Avisa($text) {
    Write-Host $text -ForegroundColor Red
    if ($Visible) { Read-Host 'Prem Retorn per sortir' | Out-Null }
}

Add-Type -AssemblyName System.Drawing, System.Web.Extensions, Microsoft.VisualBasic
$refs = @(
    [System.Drawing.Bitmap].Assembly.Location,
    [System.Web.Script.Serialization.JavaScriptSerializer].Assembly.Location,
    [Microsoft.VisualBasic.FileIO.FileSystem].Assembly.Location
)
try {
    Add-Type -Path (Get-ChildItem -LiteralPath $dir -Filter *.cs | ForEach-Object { $_.FullName }) -ReferencedAssemblies $refs
} catch {
    Avisa ("Error en compilar el servidor:`n" + $_.Exception.Message)
    exit 1
}

try {
    [CasaServidor]::Iniciar((Join-Path $dir 'config.json'), (Join-Path $dir 'www'), $dades)
} catch {
    Avisa ("El servidor s'ha aturat amb un error:`n" + $_.Exception.Message)
}
