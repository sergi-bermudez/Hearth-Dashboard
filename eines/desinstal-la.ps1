# Hearth - desinstal-lacio (treu l'inici automatic, els permisos i les icones; no esborra les teves dades)
$ErrorActionPreference = 'Continue'
$dir = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$port = 8080
$cfg = Join-Path $dir 'config.json'
if (Test-Path $cfg) { try { $c = Get-Content $cfg -Raw -ErrorAction Stop | ConvertFrom-Json; if ($c.port) { $port = [int]$c.port } } catch {} }
Write-Host ""
Write-Host "  Desinstal-lant Hearth..." -ForegroundColor Yellow
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | Where-Object { $_.CommandLine -match 'servidor\.ps1' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
foreach ($t in @('Hearth', 'Servidor Casa')) { schtasks /delete /f /tn $t 2>&1 | Out-Null }
netsh http delete urlacl url=http://+:$port/ 2>&1 | Out-Null
foreach ($n in @('Hearth', 'Hearth (nom a la xarxa)', 'Servidor Casa')) { netsh advfirewall firewall delete rule name="$n" 2>&1 | Out-Null }
$escriptori = [Environment]::GetFolderPath('Desktop')
Remove-Item (Join-Path $escriptori 'Hearth.url'), (Join-Path $escriptori 'Hearth - configuracio.lnk') -ErrorAction SilentlyContinue
try { Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -match 'edge-perfil' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue } } catch {}
Write-Host ""
Write-Host "  Fet. Ja pots esborrar la carpeta de Hearth:" -ForegroundColor Green
Write-Host "  $dir"
Write-Host "  (les teves fotos, musica i pel-licules no es toquen: son a les seves carpetes)"
Write-Host ""
Read-Host "  Prem Retorn per tancar" | Out-Null
