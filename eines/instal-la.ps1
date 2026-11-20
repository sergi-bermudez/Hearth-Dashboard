# Hearth - instal-lacio (l'executa "Instal-la Hearth.bat" amb permisos d'administrador)
$ErrorActionPreference = 'Continue'
$dir = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$eines = Join-Path $dir 'eines'
$port = 8080
$cfg = Join-Path $dir 'config.json'
if (Test-Path $cfg) { try { $c = Get-Content $cfg -Raw -ErrorAction Stop | ConvertFrom-Json; if ($c.port) { $port = [int]$c.port } } catch {} }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$Host.UI.RawUI.WindowTitle = 'Instal-lant Hearth'

function Pas($n, $t) { Write-Host ""; Write-Host " $n. $t" -ForegroundColor Cyan }
function Pregunta($t) { $r = Read-Host "    $t (S/N)"; return ($r -match '^[sSyY]') }

Write-Host ""
Write-Host "  ============================================" -ForegroundColor Yellow
Write-Host "    Hearth - el dashboard de casa per a l'iPad" -ForegroundColor Yellow
Write-Host "  ============================================" -ForegroundColor Yellow

Pas 1 "Desbloquejant els fitxers descarregats"
Get-ChildItem -LiteralPath $dir -Recurse | Unblock-File -ErrorAction SilentlyContinue

Pas 2 "Permis de xarxa i tallafoc (port $port)"
netsh http delete urlacl url=http://+:$port/ 2>&1 | Out-Null
netsh http add urlacl url=http://+:$port/ sddl="D:(A;;GX;;;WD)" | Out-Null
foreach ($n in @('Hearth', 'Hearth (nom a la xarxa)', 'Servidor Casa')) { netsh advfirewall firewall delete rule name="$n" 2>&1 | Out-Null }
netsh advfirewall firewall add rule name="Hearth" dir=in action=allow protocol=TCP localport=$port profile=private,domain | Out-Null
netsh advfirewall firewall add rule name="Hearth (nom a la xarxa)" dir=in action=allow protocol=UDP localport=5353 profile=private,domain | Out-Null
# Punt d'acces Wi-Fi del PC (Windows el considera xarxa publica): nomes els aparells connectats al PC
foreach ($n in @('Hearth (punt d''acces)', 'Hearth (punt d''acces, nom)')) { netsh advfirewall firewall delete rule name="$n" 2>&1 | Out-Null }
netsh advfirewall firewall add rule name="Hearth (punt d'acces)" dir=in action=allow protocol=TCP localport=$port profile=public remoteip=192.168.137.0/24 | Out-Null
netsh advfirewall firewall add rule name="Hearth (punt d'acces, nom)" dir=in action=allow protocol=UDP localport=5353 profile=public remoteip=192.168.137.0/24 | Out-Null
try {
    $publiques = Get-NetConnectionProfile | Where-Object { $_.NetworkCategory -eq 'Public' }
    foreach ($x in $publiques) {
        Write-Host "    La xarxa '$($x.Name)' es 'Publica': l'iPad no hi podra arribar." -ForegroundColor Yellow
        if (Pregunta "La passo a 'Privada' (recomanat si es la xarxa de casa)?") { Set-NetConnectionProfile -InterfaceIndex $x.InterfaceIndex -NetworkCategory Private }
    }
} catch {}

Pas 3 "Engegar Hearth sol en iniciar Windows"
$vbs = Join-Path $dir 'eines\Hearth.vbs'
schtasks /delete /f /tn "Servidor Casa" 2>&1 | Out-Null
try {
    $accio = New-ScheduledTaskAction -Execute 'wscript.exe' -Argument "`"$vbs`""
    $quan = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
    $ajust = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
    Register-ScheduledTask -TaskName 'Hearth' -Action $accio -Trigger $quan -Settings $ajust -Force | Out-Null
} catch { Write-Host "    No s'ha pogut crear l'inici automatic: $($_.Exception.Message)" -ForegroundColor Yellow }

Pas 4 "Icones a l'escriptori"
$escriptori = [Environment]::GetFolderPath('Desktop')
$ico = Join-Path $dir 'eines\hearth.ico'
"[InternetShortcut]`r`nURL=http://localhost:$port/`r`nIconFile=$ico`r`nIconIndex=0`r`n" | Set-Content -Encoding ASCII (Join-Path $escriptori 'Hearth.url')
try {
    $sh = New-Object -ComObject WScript.Shell
    $lnk = $sh.CreateShortcut((Join-Path $escriptori 'Hearth - configuracio.lnk'))
    $lnk.TargetPath = "http://localhost:$port/benvinguda.html"
    $lnk.IconLocation = $ico
    $lnk.Save()
} catch {}

Pas 5 "Programes de suport"
if (-not (Test-Path $eines)) { New-Item -ItemType Directory -Path $eines | Out-Null }
$yt = Join-Path $eines 'yt-dlp.exe'
if (-not (Test-Path $yt)) {
    Write-Host "    Baixant yt-dlp (per a YouTube)..."
    try { Invoke-WebRequest -UseBasicParsing 'https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe' -OutFile $yt; Write-Host "    Fet." -ForegroundColor Green } catch { Write-Host "    No s'ha pogut baixar. Ho pots fer despres des de Configuracio > YouTube." -ForegroundColor Yellow }
} else { Write-Host "    yt-dlp: ja hi es." }
$ff = Join-Path $eines 'ffmpeg.exe'
if (-not (Test-Path $ff) -and -not (Get-Command ffmpeg.exe -ErrorAction SilentlyContinue)) {
    Write-Host "    ffmpeg serveix per sentir el Spotify a l'iPad i per mirar YouTube en HD (uns 90 MB)."
    if (Pregunta "El baixo?") {
        try {
            $zip = Join-Path $env:TEMP 'ffmpeg-hearth.zip'
            Invoke-WebRequest -UseBasicParsing 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip' -OutFile $zip
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $z = [IO.Compression.ZipFile]::OpenRead($zip)
            $e = $z.Entries | Where-Object { $_.FullName -like '*/bin/ffmpeg.exe' } | Select-Object -First 1
            [IO.Compression.ZipFileExtensions]::ExtractToFile($e, $ff, $true)
            $z.Dispose(); Remove-Item $zip
            Write-Host "    Fet." -ForegroundColor Green
        } catch { Write-Host "    No s'ha pogut baixar: $($_.Exception.Message)" -ForegroundColor Yellow }
    }
} else { Write-Host "    ffmpeg: ja hi es." }
$cable = $false
try { $cable = [bool](Get-CimInstance Win32_SoundDevice | Where-Object { $_.Name -match 'VB-Audio|CABLE' }) } catch {}
if ($cable) { Write-Host "    Cable d'audio virtual: trobat." }
else {
    Write-Host "    Per sentir el Spotify a l'iPad cal el 'VB-Cable' (gratuit). Si no fas servir Spotify, no cal." -ForegroundColor Yellow
    if (Pregunta "Vols obrir la pagina per baixar-lo?") { Start-Process 'https://vb-audio.com/Cable/' }
}

Pas 6 "Engegant Hearth"
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | Where-Object { $_.CommandLine -match 'servidor\.ps1' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
try { Start-ScheduledTask -TaskName 'Hearth' } catch { Start-Process wscript.exe -ArgumentList "`"$vbs`"" }
$ok = $false
for ($i = 0; $i -lt 60 -and -not $ok; $i++) {
    Start-Sleep -Seconds 1
    try { $r = Invoke-WebRequest -UseBasicParsing "http://localhost:$port/api/config" -TimeoutSec 2; $ok = $r.StatusCode -eq 200 } catch {}
}
if ($ok) {
    Write-Host ""
    Write-Host "  Fet! Hearth ja funciona." -ForegroundColor Green
    Write-Host "  S'obre la pagina de benvinguda amb el codi QR per a l'iPad."
    Start-Process "http://localhost:$port/benvinguda.html"
} else {
    Write-Host ""
    Write-Host "  Hearth no ha arrencat. Aixo diu el registre:" -ForegroundColor Red
    $reg = Join-Path $dir 'dades\registre.txt'
    if (Test-Path $reg) { Get-Content $reg -Tail 25 }
    Write-Host ""
    Write-Host "  Envia aquest text a qui t'ajudi, o obre 'Inicia Hearth (amb finestra).bat' per veure l'error."
}
Write-Host ""
Read-Host "  Prem Retorn per tancar" | Out-Null
