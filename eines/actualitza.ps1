# Hearth - actualitzacio automatica des de GitHub.
# El llanca el servidor. Baixa la versio nova, comprova que compila, atura el servidor,
# copia els fitxers (sense tocar dades ni config.json) i el torna a engegar.
# Si la versio nova no arrenca, torna a posar l'anterior.
param([int]$ServidorPid = 0, [string]$Dir = '', [string]$Repo = 'sergi-bermudez/Hearth-Dashboard')
$ErrorActionPreference = 'Stop'
if (-not $Dir) { $Dir = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path) }
$dades = Join-Path $Dir 'dades'
$log = Join-Path $dades 'actualitzacio.txt'
function Log($t) { $l = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + '  ' + $t; try { Add-Content -LiteralPath $log -Value $l -Encoding UTF8 } catch {} }

$tmp = Join-Path $env:TEMP 'hearth-actualitzacio'
# Torna a posar la copia anterior (i treu els .cs nous, que si no tambe es compilarien)
function Restaura {
    if (-not (Test-Path $copia)) { return }
    Get-ChildItem -LiteralPath $Dir -Filter *.cs | Where-Object { -not (Test-Path (Join-Path $copia $_.Name)) } | Remove-Item -Force
    robocopy $copia $Dir /E /R:3 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null
}
$copia = Join-Path $env:TEMP 'hearth-anterior'
try {
    Start-Sleep -Seconds 2
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    New-Item -ItemType Directory -Path $tmp | Out-Null
    $zip = Join-Path $tmp 'hearth.zip'
    Log "Baixant https://github.com/$Repo ..."
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -UseBasicParsing -Uri "https://codeload.github.com/$Repo/zip/refs/heads/main" -OutFile $zip
    Expand-Archive -LiteralPath $zip -DestinationPath $tmp -Force
    $src = Get-ChildItem -LiteralPath $tmp -Directory | Select-Object -First 1
    if (-not $src -or -not (Test-Path (Join-Path $src.FullName 'servidor.cs'))) { throw 'El paquet baixat no sembla Hearth' }
    $src = $src.FullName
    $nova = (Get-Content -LiteralPath (Join-Path $src 'VERSION') -Raw).Trim()

    # 1. Comprova que la versio nova compila (en un proces a part, sense aturar res)
    $prova = Join-Path $tmp 'prova.ps1'
    @'
param($d)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Web.Extensions, Microsoft.VisualBasic
$refs = @([System.Drawing.Bitmap].Assembly.Location, [System.Web.Script.Serialization.JavaScriptSerializer].Assembly.Location, [Microsoft.VisualBasic.FileIO.FileSystem].Assembly.Location)
try { Add-Type -Path (Get-ChildItem -LiteralPath $d -Filter *.cs | ForEach-Object { $_.FullName }) -ReferencedAssemblies $refs; exit 0 }
catch { Write-Output $_.Exception.Message; exit 1 }
'@ | Set-Content -LiteralPath $prova -Encoding ASCII
    $out = Join-Path $tmp 'prova.txt'
    $p = Start-Process powershell.exe -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $prova + '"'), ('"' + $src + '"')) -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $out
    if ($p.ExitCode -ne 0) { $e = ''; try { $e = (Get-Content -LiteralPath $out -Raw) -replace '\s+', ' ' } catch {}; throw ("La versio $nova no compila; no s'ha canviat res. " + $e) }
    Log "La versio $nova compila be. Aturant el servidor..."

    # 2. Copia de seguretat de l'actual
    if (Test-Path $copia) { Remove-Item $copia -Recurse -Force }
    robocopy $Dir $copia /E /XD dades .git /XF *.exe /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null

    # 3. Atura el servidor i copia
    if ($ServidorPid -gt 0) { try { Stop-Process -Id $ServidorPid -Force } catch {} ; try { Wait-Process -Id $ServidorPid -Timeout 20 } catch {} }
    Start-Sleep -Seconds 2
    robocopy $src $Dir /E /XD dades .git /XF config.json /R:3 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "No s'han pogut copiar els fitxers (robocopy $LASTEXITCODE)" }
} catch {
    Log ("ERROR: " + $_.Exception.Message)
    # Si ja s'havia aturat, el torna a engegar
    if ($ServidorPid -gt 0 -and -not (Get-Process -Id $ServidorPid -ErrorAction SilentlyContinue)) {
        Restaura
        try { Start-ScheduledTask -TaskName 'Hearth' } catch { Start-Process wscript.exe -ArgumentList ('"' + (Join-Path $Dir 'eines\Hearth.vbs') + '"') }
    }
    exit 1
}

# 4. Engega la versio nova i comprova que respon
function Engega { try { Start-ScheduledTask -TaskName 'Hearth' } catch { Start-Process wscript.exe -ArgumentList ('"' + (Join-Path $Dir 'eines\Hearth.vbs') + '"') } }
Engega
$port = 8080
try { $c = Get-Content -LiteralPath (Join-Path $Dir 'config.json') -Raw | ConvertFrom-Json; if ($c.port) { $port = [int]$c.port } } catch {}
$viu = $false
for ($i = 0; $i -lt 30 -and -not $viu; $i++) {
    Start-Sleep -Seconds 3
    try { $r = Invoke-WebRequest -UseBasicParsing -Uri "http://localhost:$port/" -TimeoutSec 3; if ($r.StatusCode -eq 200) { $viu = $true } } catch {}
}
if ($viu) { Log "Actualitzat a la versio $nova." }
else {
    Log "La versio $nova no arrenca; torno a posar l'anterior."
    Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | Where-Object { $_.CommandLine -like '*servidor.ps1*' } | ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force } catch {} }
    Start-Sleep -Seconds 2
    Restaura
    Engega
}
try { Remove-Item $tmp -Recurse -Force } catch {}
