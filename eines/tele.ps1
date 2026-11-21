# Hearth · projecta la pantalla del PC a la tele (Miracast) o la treu, sense tocar el PC.
# Obre el panell «Connecta» de Windows (Win + K) i hi prem la tele pel nom; per aturar, prem «Desconnecta»
# i tanca l'Edge del mode tele.
#   powershell -File tele.ps1 -Nom "LG"        projecta a la tele que conté "LG" al nom (buit = la primera)
#   powershell -File tele.ps1 -Atura           deixa de projectar i tanca Hearth del mode tele
#   powershell -File tele.ps1 -Llista          noms de les teles que accepten projecció (Miracast), en JSON
#   powershell -File tele.ps1 -Manual          només obre el panell Projecta perquè la persona triï la tele al PC
# Deixa a dades\tele-diag.txt què ha vist al panell, per poder entendre-ho si una tele no surt.
param([string]$Nom = '', [switch]$Atura, [switch]$Llista, [switch]$Manual)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class HearthTecles {
    [DllImport("user32.dll")] static extern void keybd_event(byte b, byte s, uint f, UIntPtr e);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
    public static void WinK() { keybd_event(0x5B, 0, 0, UIntPtr.Zero); keybd_event(0x4B, 0, 0, UIntPtr.Zero); keybd_event(0x4B, 0, 2, UIntPtr.Zero); keybd_event(0x5B, 0, 2, UIntPtr.Zero); }
    public static void Esc() { keybd_event(0x1B, 0, 0, UIntPtr.Zero); keybd_event(0x1B, 0, 2, UIntPtr.Zero); }
    public static void Clic(int x, int y) { SetCursorPos(x, y); mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
}
"@
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

function Prem($el) {
    $p = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$p)) { $p.Invoke(); return }
    if ($el.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$p)) { $p.Select(); return }
    $r = $el.Current.BoundingRectangle
    [HearthTecles]::Clic([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
}

$DIAG = Join-Path (Split-Path $PSScriptRoot -Parent) 'dades\tele-diag.txt'
function Diag([string]$t) { try { Add-Content -Path $DIAG -Value ((Get-Date -Format 's') + ' ' + $t) -Encoding UTF8 } catch { } }

# El panell de projecció (Win+K) no sempre és la finestra en primer pla: a Windows 11 és el desplegable
# de Configuració ràpida (ShellExperienceHost / ShellHost / explorer). Mira totes les finestres de l'entorn.
function Finestres() {
    $l = @()
    try { $l += $AE::FromHandle([HearthTecles]::GetForegroundWindow()) } catch { }
    $shell = @{}
    foreach ($n in 'ShellExperienceHost', 'ShellHost', 'explorer', 'StartMenuExperienceHost') {
        foreach ($pr in @(Get-Process -Name $n -ErrorAction SilentlyContinue)) { $shell[$pr.Id] = 1 }
    }
    try {
        foreach ($w in $AE::RootElement.FindAll($TS::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
            try { if ($shell.ContainsKey($w.Current.ProcessId)) { $l += $w } } catch { }
        }
    } catch { }
    return $l
}

# Noms de dispositius que Windows veu per Wi-Fi Direct (on són les teles Miracast), sense tocar la pantalla
function NomsWinRT() {
    $noms = @()
    try {
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        $asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
        $null = [Windows.Devices.Enumeration.DeviceInformation, Windows.Devices.Enumeration, ContentType = WindowsRuntime]
        $sel = 'System.Devices.Aep.ProtocolId:="{0407d24e-53de-4c9a-9ba1-9ced54641188}"'
        $op = [Windows.Devices.Enumeration.DeviceInformation]::FindAllAsync($sel, $null, [Windows.Devices.Enumeration.DeviceInformationKind]::AssociationEndpoint)
        $task = $asTask.MakeGenericMethod([Windows.Devices.Enumeration.DeviceInformationCollection]).Invoke($null, @($op))
        if ($task.Wait(12000)) {
            foreach ($d in $task.Result) { $n = $d.Name; if ($n -and $n -notmatch '^DIRECT-' -and $noms -notcontains $n) { $noms += $n } }
        }
        Diag ('WinRT: ' + ($noms -join ' | '))
    } catch { Diag ('WinRT error: ' + $_.Exception.Message) }
    return $noms
}

# Busca dins el panell que s'acaba d'obrir un element que compleixi $prova (fins a $segons)
function Busca([scriptblock]$prova, [int]$segons) {
    $fi = (Get-Date).AddSeconds($segons)
    while ((Get-Date) -lt $fi) {
        foreach ($el in (Finestres)) {
            try {
                $tots = $el.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
                foreach ($x in $tots) { if (& $prova $x) { return $x } }
            } catch { }
        }
        Start-Sleep -Milliseconds 700
    }
    return $null
}

if ($Atura) {
    # tanca l'Edge del mode tele
    Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -like '*edge-tele*' } | ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force } catch { } }
    [HearthTecles]::WinK()
    Start-Sleep -Milliseconds 1500
    $b = Busca { param($x) $x.Current.Name -match '^(Desconnecta|Disconnect|Desligar|Desconectar|Déconnecter|Trennen)' } 8
    if ($b) { Prem $b; Start-Sleep -Milliseconds 500; [HearthTecles]::Esc(); 'ok'; exit 0 }
    [HearthTecles]::Esc(); 'no-projectava'; exit 0
}

$NO = '^(Desconnecta|Disconnect|Desligar|Desconectar|Projecta|Project|Cerca|Search|Busca|Find|Altres|Other|Mostra|Show|Tanca|Close|Enrere|Back|Configura|Settings|Defini|Config|Wi-Fi|Bluetooth|Cast|Transmet|Volum|Volume|Brillantor|Brightness|Mode|Estalvi|Energy|Battery|Bateria|Accessib|Hotspot|Nearby|Avió|Airplane|Ubicació|Location|Night|Nit|Focus)'
if ($Manual) { [HearthTecles]::WinK(); 'ok:manual'; exit 0 }
if ($Llista) {
    $noms = @(NomsWinRT)
    # també el que surt al panell Projecta (hi ha teles que només hi apareixen mentre està obert)
    [HearthTecles]::WinK()
    $fi = (Get-Date).AddSeconds(14); $vist = @()
    while ((Get-Date) -lt $fi) {
        Start-Sleep -Milliseconds 1500
        foreach ($w in (Finestres)) {
            try {
                foreach ($x in $w.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
                    $n = $x.Current.Name; $ct = $x.Current.ControlType.ProgrammaticName
                    if (-not $n) { continue }
                    $k = "$ct=$n"; if ($vist -notcontains $k) { $vist += $k }
                    if ($ct -match 'ListItem' -and $n -notmatch $NO -and $n.Length -lt 60 -and $noms -notcontains $n) { $noms += $n }
                }
            } catch { }
        }
    }
    [HearthTecles]::Esc()
    Diag ('Panell: ' + (($vist | Select-Object -First 120) -join ' | '))
    ConvertTo-Json -Compress -InputObject @($noms)
    exit 0
}

[HearthTecles]::WinK()
Start-Sleep -Milliseconds 1500
# les teles surten com a elements de llista (o botons) amb el seu nom; cal esperar que les trobi
$t = Busca {
    param($x)
    $n = $x.Current.Name
    $ct = $x.Current.ControlType.ProgrammaticName
    if (-not $n -or $ct -notmatch 'ListItem|Button') { return $false }
    if ($n -match $NO) { return $false }
    if ($Nom) { return $n -like "*$Nom*" }
    return $ct -match 'ListItem'
} 20
if (-not $t) {
    # no l'ha sabut prémer sola: deixa el panell obert perquè la persona la triï al PC
    Diag "No he trobat '$Nom' al panell"
    'manual'; exit 0
}
$nomTele = $t.Current.Name
Prem $t
Start-Sleep -Seconds 2
[HearthTecles]::Esc()
"ok:$nomTele"
exit 0
