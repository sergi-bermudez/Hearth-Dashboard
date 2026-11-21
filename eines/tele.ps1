# Hearth · projecta la pantalla del PC a la tele (Miracast) o la treu, sense tocar el PC.
# Obre el panell «Connecta» de Windows (Win + K) i hi prem la tele pel nom; per aturar, prem «Desconnecta»
# i tanca l'Edge del mode tele.
#   powershell -File tele.ps1 -Nom "LG"        projecta a la tele que conté "LG" al nom (buit = la primera)
#   powershell -File tele.ps1 -Atura           deixa de projectar i tanca Hearth del mode tele
#   powershell -File tele.ps1 -Llista          noms de les teles que accepten projecció (Miracast), en JSON
param([string]$Nom = '', [switch]$Atura, [switch]$Llista)
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

# Busca dins el panell que s'acaba d'obrir un element que compleixi $prova (fins a $segons)
function Busca([scriptblock]$prova, [int]$segons) {
    $fi = (Get-Date).AddSeconds($segons)
    while ((Get-Date) -lt $fi) {
        $fines = @()
        try { $fines += $AE::FromHandle([HearthTecles]::GetForegroundWindow()) } catch { }
        foreach ($el in $fines) {
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

$NO = '^(Desconnecta|Disconnect|Desligar|Desconectar|Projecta|Project|Cerca|Search|Busca|Find|Altres|Other|Mostra|Show|Tanca|Close)'
if ($Llista) {
    [HearthTecles]::WinK()
    Start-Sleep -Seconds 6
    $noms = @()
    try {
        $el = $AE::FromHandle([HearthTecles]::GetForegroundWindow())
        foreach ($x in $el.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
            $n = $x.Current.Name
            if ($n -and $x.Current.ControlType.ProgrammaticName -match 'ListItem' -and $n -notmatch $NO -and $noms -notcontains $n) { $noms += $n }
        }
    } catch { }
    [HearthTecles]::Esc()
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
if (-not $t) { [HearthTecles]::Esc(); 'no-trobada'; exit 2 }
$nomTele = $t.Current.Name
Prem $t
Start-Sleep -Seconds 2
[HearthTecles]::Esc()
"ok:$nomTele"
exit 0
