# diagnostico-crash.ps1
# Script de diagnostico para crashes de kernel ao executar Excalibur5
# Execute como Administrador para acesso completo aos logs do sistema

$ErrorActionPreference = "SilentlyContinue"
$output = @()
$separator = "=" * 70

function Write-Section($title) {
    $script:output += ""
    $script:output += $separator
    $script:output += "  $title"
    $script:output += $separator
    $script:output += ""
}

# --- Cabecalho ---
$output += $separator
$output += "  DIAGNOSTICO DE CRASH DE KERNEL - Excalibur5"
$output += "  Gerado em: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
$output += "  Computador: $env:COMPUTERNAME"
$output += $separator

# --- Info do Sistema ---
Write-Section "INFORMACOES DO SISTEMA"

$os = Get-CimInstance Win32_OperatingSystem
$output += "SO: $($os.Caption) $($os.Version) (Build $($os.BuildNumber))"
$output += "Arquitetura: $($os.OSArchitecture)"
$output += "RAM Total: $([math]::Round($os.TotalVisibleMemorySize / 1MB, 1)) GB"
$output += "RAM Livre: $([math]::Round($os.FreePhysicalMemory / 1MB, 1)) GB"

$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$output += "CPU: $($cpu.Name)"

# --- Driver de Video ---
Write-Section "DRIVERS DE VIDEO (GPU)"

$gpus = Get-CimInstance Win32_VideoController
foreach ($gpu in $gpus) {
    $output += "GPU: $($gpu.Name)"
    $output += "  Driver: $($gpu.DriverVersion)"
    $output += "  Data do Driver: $($gpu.DriverDate)"
    $output += "  Status: $($gpu.Status)"
    $output += "  RAM Video: $([math]::Round($gpu.AdapterRAM / 1GB, 1)) GB"
    $output += ""
}

# Drivers de kernel relacionados a video
$output += "Drivers de kernel de video carregados:"
$videoDrivers = Get-WmiObject Win32_SystemDriver | Where-Object {
    $_.Name -match "nvlddmkm|atikmpag|igdkmd|dxgkrnl|BasicDisplay"
}
foreach ($drv in $videoDrivers) {
    $output += "  $($drv.Name) | Estado: $($drv.State) | Path: $($drv.PathName)"
}

# --- .NET Runtime ---
Write-Section "RUNTIME .NET INSTALADO"

$dotnetInfo = & dotnet --list-runtimes 2>&1
if ($dotnetInfo) {
    foreach ($line in $dotnetInfo) {
        $output += "  $line"
    }
} else {
    $output += "  (nao foi possivel obter info do .NET)"
}

# --- Eventos Kernel-Power (Event ID 41 = reinicio inesperado) ---
Write-Section "EVENTOS KERNEL-POWER (reinicio inesperado) - Ultimos 30 dias"

$startDate = (Get-Date).AddDays(-30)
$kernelPower = Get-WinEvent -FilterHashtable @{
    LogName = 'System'
    ProviderName = 'Microsoft-Windows-Kernel-Power'
    Id = 41
    StartTime = $startDate
} -MaxEvents 20 2>$null

if ($kernelPower) {
    $output += "Encontrados $($kernelPower.Count) evento(s):"
    $output += ""
    foreach ($evt in $kernelPower) {
        $output += "  [$($evt.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss'))] Event ID $($evt.Id)"
        $output += "  Mensagem: $($evt.Message.Substring(0, [Math]::Min(200, $evt.Message.Length)))"
        $output += ""
    }
} else {
    $output += "  Nenhum evento Kernel-Power (ID 41) encontrado nos ultimos 30 dias."
}

# --- BugCheck (BSOD) ---
Write-Section "EVENTOS BUGCHECK (tela azul/preta) - Ultimos 30 dias"

$bugCheck = Get-WinEvent -FilterHashtable @{
    LogName = 'System'
    ProviderName = 'Microsoft-Windows-WER-SystemErrorReporting'
    StartTime = $startDate
} -MaxEvents 20 2>$null

if ($bugCheck) {
    $output += "Encontrados $($bugCheck.Count) evento(s):"
    $output += ""
    foreach ($evt in $bugCheck) {
        $output += "  [$($evt.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss'))] Event ID $($evt.Id)"
        $output += "  $($evt.Message.Substring(0, [Math]::Min(300, $evt.Message.Length)))"
        $output += ""
    }
} else {
    $output += "  Nenhum evento BugCheck encontrado nos ultimos 30 dias."
}

# --- Erros criticos do sistema ---
Write-Section "ERROS CRITICOS DO SISTEMA - Ultimos 7 dias"

$critical = Get-WinEvent -FilterHashtable @{
    LogName = 'System'
    Level = 1  # Critical
    StartTime = (Get-Date).AddDays(-7)
} -MaxEvents 15 2>$null

if ($critical) {
    $output += "Encontrados $($critical.Count) evento(s) criticos:"
    $output += ""
    foreach ($evt in $critical) {
        $output += "  [$($evt.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss'))] Source: $($evt.ProviderName)"
        $output += "  Event ID: $($evt.Id)"
        $msg = if ($evt.Message.Length -gt 250) { $evt.Message.Substring(0, 250) + "..." } else { $evt.Message }
        $output += "  $msg"
        $output += ""
    }
} else {
    $output += "  Nenhum evento critico encontrado nos ultimos 7 dias."
}

# --- Erros de aplicacao .NET ---
Write-Section "ERROS DE APLICACAO (Excalibur5) - Ultimos 7 dias"

$appErrors = Get-WinEvent -FilterHashtable @{
    LogName = 'Application'
    Level = 2  # Error
    StartTime = (Get-Date).AddDays(-7)
} -MaxEvents 50 2>$null | Where-Object {
    $_.Message -match "Excalibur|\.NET|CLR|wpf|render"
}

if ($appErrors) {
    $output += "Encontrados $($appErrors.Count) evento(s):"
    $output += ""
    foreach ($evt in $appErrors | Select-Object -First 10) {
        $output += "  [$($evt.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss'))] Source: $($evt.ProviderName)"
        $msg = if ($evt.Message.Length -gt 300) { $evt.Message.Substring(0, 300) + "..." } else { $evt.Message }
        $output += "  $msg"
        $output += ""
    }
} else {
    $output += "  Nenhum erro de aplicacao relacionado encontrado."
}

# --- Minidumps ---
Write-Section "MINIDUMPS (arquivos de crash)"

$dumpPath = "$env:SystemRoot\Minidump"
if (Test-Path $dumpPath) {
    $dumps = Get-ChildItem $dumpPath -Filter "*.dmp" | Sort-Object LastWriteTime -Descending | Select-Object -First 5
    if ($dumps) {
        $output += "Ultimos minidumps encontrados em $dumpPath :"
        foreach ($d in $dumps) {
            $output += "  $($d.Name) | $($d.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')) | $([math]::Round($d.Length / 1KB)) KB"
        }
        $output += ""
        $output += "  Dica: Use WinDbg para analisar esses dumps e identificar o driver culpado."
    } else {
        $output += "  Pasta Minidump existe mas esta vazia."
    }
} else {
    $output += "  Pasta $dumpPath nao encontrada."
    $output += "  Verifique se minidumps estao habilitados em:"
    $output += "  Sistema > Configuracoes avancadas > Inicializacao e recuperacao"
}

$memoryDump = "$env:SystemRoot\MEMORY.DMP"
if (Test-Path $memoryDump) {
    $dmpInfo = Get-Item $memoryDump
    $output += ""
    $output += "  MEMORY.DMP encontrado: $($dmpInfo.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')) | $([math]::Round($dmpInfo.Length / 1MB)) MB"
}

# --- Antivirus ---
Write-Section "SOFTWARE DE SEGURANCA"

$av = Get-CimInstance -Namespace "root\SecurityCenter2" -ClassName AntiVirusProduct 2>$null
if ($av) {
    foreach ($product in $av) {
        $output += "  $($product.displayName)"
    }
} else {
    $output += "  Nao foi possivel detectar antivirus (pode requerer admin)."
}

# --- Recomendacoes ---
Write-Section "RECOMENDACOES"

$output += "1. Se houver eventos Kernel-Power (ID 41), confirma reinicio inesperado."
$output += "2. Se houver BugCheck, o codigo do erro indica o driver culpado:"
$output += "   - nvlddmkm.sys = driver NVIDIA"
$output += "   - atikmpag.sys = driver AMD"
$output += "   - igdkmd64.sys = driver Intel"
$output += "   - dxgkrnl.sys  = DirectX kernel (geralmente ligado ao driver de GPU)"
$output += "3. Se o driver de video estiver desatualizado, atualize-o."
$output += "4. Para testar se e a GPU, adicione no App.xaml.cs:"
$output += '   RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;'
$output += "5. Se houver minidumps, analise com WinDbg (comando: !analyze -v)"
$output += ""

# --- Salvar resultado ---
$reportPath = Join-Path $PSScriptRoot "diagnostico-crash-report.txt"
$output | Out-File -FilePath $reportPath -Encoding UTF8

Write-Host ""
Write-Host "Diagnostico concluido!" -ForegroundColor Green
Write-Host "Relatorio salvo em: $reportPath" -ForegroundColor Cyan
Write-Host ""
Write-Host "Envie o arquivo 'diagnostico-crash-report.txt' para analise." -ForegroundColor Yellow
Write-Host ""

# Abrir o relatorio
Start-Process notepad.exe $reportPath
