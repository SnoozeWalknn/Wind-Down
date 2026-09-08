[CmdletBinding()]
param([switch]$Quiet)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$localData = [Environment]::GetFolderPath('LocalApplicationData')
$programsRoot = [IO.Path]::GetFullPath((Join-Path $localData 'Programs'))
$installRoot = [IO.Path]::GetFullPath((Join-Path $programsRoot 'Wind Down'))
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Wind Down.lnk'
$protocolKey = 'HKCU:\Software\Classes\wind-down-power-timer'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WindDown'

function Test-ExactPathEntry([string]$Entry, [string]$Expected) {
    if ([string]::IsNullOrWhiteSpace($Entry)) { return $false }
    return $Entry.Trim().Trim('"').TrimEnd('\') -ieq $Expected.TrimEnd('\')
}

function Publish-EnvironmentChange {
    if (!('WindDown.EnvironmentBroadcast' -as [type])) {
        Add-Type -Namespace WindDown -Name EnvironmentBroadcast -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true, CharSet=System.Runtime.InteropServices.CharSet.Auto)]
public static extern System.IntPtr SendMessageTimeout(System.IntPtr hWnd, uint Msg, System.IntPtr wParam, string lParam, uint flags, uint timeout, out System.IntPtr result);
'@
    }
    $result = [IntPtr]::Zero
    [void][WindDown.EnvironmentBroadcast]::SendMessageTimeout([IntPtr]0xffff, 0x001A, [IntPtr]::Zero, 'Environment', 2, 5000, [ref]$result)
}

function Remove-WindDownTasks {
    $service = New-Object -ComObject Schedule.Service
    $folder = $null
    $tasks = $null
    try {
        $service.Connect()
        $folder = $service.GetFolder('\')
        $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        $baseName = "WindDown-$sid"
        $names = @()
        $tasks = $folder.GetTasks(0)
        for ($index = 1; $index -le $tasks.Count; $index++) {
            $task = $tasks.Item($index)
            try {
                $name = [string]$task.Name
                if ($name -ceq $baseName -or $name.StartsWith($baseName + '-Warning-', [StringComparison]::OrdinalIgnoreCase)) { $names += $name }
            }
            finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($task) }
        }

        if ($names -contains $baseName) {
            $activeTask = $folder.GetTask($baseName)
            $definition = $null
            try {
                $definition = $activeTask.Definition
                try { $schedule = ([string]$definition.RegistrationInfo.Documentation) | ConvertFrom-Json }
                catch { throw 'The existing Wind Down schedule could not be read. Open Wind Down and cancel or finish it before uninstalling.' }
                if ($null -eq $schedule.Result) { throw 'Cancel the active Wind Down schedule before uninstalling.' }
            }
            finally {
                if ($null -ne $definition) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($definition) }
                [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($activeTask)
            }
        }

        foreach ($name in $names) { $folder.DeleteTask($name, 0) }
    }
    finally {
        if ($null -ne $tasks) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($tasks) }
        if ($null -ne $folder) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($folder) }
        if ($null -ne $service) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($service) }
    }
}

if ([IO.Directory]::GetParent($installRoot).FullName -ine $programsRoot) { throw 'Refusing to remove an unexpected installation path.' }
if (Get-Process -Name 'Wind Down' -ErrorAction SilentlyContinue) { throw 'Exit Wind Down before uninstalling it.' }

Remove-WindDownTasks

$ownsPathEntry = $false
if (Test-Path -LiteralPath $uninstallKey) {
    $ownsPathEntry = (Get-ItemPropertyValue -LiteralPath $uninstallKey -Name PathEntryAdded -ErrorAction SilentlyContinue) -eq 1
}
if ($ownsPathEntry) {
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $userEntries = @($userPath -split ';' | Where-Object { ![string]::IsNullOrWhiteSpace($_) -and !(Test-ExactPathEntry $_ $installRoot) })
    [Environment]::SetEnvironmentVariable('Path', ($userEntries -join ';'), 'User')
    $processEntries = @($env:Path -split ';' | Where-Object { ![string]::IsNullOrWhiteSpace($_) -and !(Test-ExactPathEntry $_ $installRoot) })
    $env:Path = $processEntries -join ';'
    try { Publish-EnvironmentChange } catch { }
}

if (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath -Force }
if (Test-Path -LiteralPath $protocolKey) { Remove-Item -LiteralPath $protocolKey -Recurse -Force }
if (Test-Path -LiteralPath $uninstallKey) { Remove-Item -LiteralPath $uninstallKey -Recurse -Force }

if (Test-Path -LiteralPath $installRoot) {
    if ([IO.Path]::GetFullPath($PSScriptRoot) -ieq $installRoot) {
        $cleanupScript = Join-Path ([IO.Path]::GetTempPath()) ("WindDown-Cleanup-" + [Guid]::NewGuid().ToString('N') + '.ps1')
        @'
param([string]$InstallRoot, [string]$ExpectedParent)
$ErrorActionPreference = 'SilentlyContinue'
Start-Sleep -Milliseconds 750
$resolved = [IO.Path]::GetFullPath($InstallRoot)
if ([IO.Directory]::GetParent($resolved).FullName -ieq [IO.Path]::GetFullPath($ExpectedParent)) {
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
Remove-Item -LiteralPath $PSCommandPath -Force
'@ | Set-Content -LiteralPath $cleanupScript -Encoding UTF8
        $powerShellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $env:WIND_DOWN_CLEANUP_SCRIPT = $cleanupScript
        $env:WIND_DOWN_CLEANUP_ROOT = $installRoot
        $env:WIND_DOWN_CLEANUP_PARENT = $programsRoot
        try {
            $arguments = '-NoLogo -NoProfile -ExecutionPolicy Bypass -Command "& $env:WIND_DOWN_CLEANUP_SCRIPT -InstallRoot $env:WIND_DOWN_CLEANUP_ROOT -ExpectedParent $env:WIND_DOWN_CLEANUP_PARENT"'
            Start-Process -FilePath $powerShellPath -ArgumentList $arguments -WindowStyle Hidden
        }
        finally {
            Remove-Item Env:\WIND_DOWN_CLEANUP_SCRIPT,Env:\WIND_DOWN_CLEANUP_ROOT,Env:\WIND_DOWN_CLEANUP_PARENT -ErrorAction SilentlyContinue
        }
    }
    else { Remove-Item -LiteralPath $installRoot -Recurse -Force }
}

if (!$Quiet) {
    Write-Host 'Wind Down was uninstalled.'
    Write-Host 'Your Wind Down appearance preferences remain in LocalAppData\Wind Down.'
}
