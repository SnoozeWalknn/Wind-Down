[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$productName = 'Wind Down'
$localData = [Environment]::GetFolderPath('LocalApplicationData')
$programsRoot = [IO.Path]::GetFullPath((Join-Path $localData 'Programs'))
$destination = [IO.Path]::GetFullPath((Join-Path $programsRoot $productName))
$source = [IO.Path]::GetFullPath($PSScriptRoot)
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Wind Down.lnk'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WindDown'
$requiredFiles = @('Wind Down.exe', 'WindDown.Worker.exe', 'Install.ps1', 'Uninstall.ps1', 'WindDown.Cli.ps1', 'wind-down.cmd')

function Test-ExactPathEntry([string]$Entry, [string]$Expected) {
    if ([string]::IsNullOrWhiteSpace($Entry)) { return $false }
    return $Entry.Trim().Trim('"').TrimEnd('\') -ieq $Expected.TrimEnd('\')
}

function Add-CurrentPathEntry([string]$Entry) {
    $entries = @($env:Path -split ';')
    if (!($entries | Where-Object { Test-ExactPathEntry $_ $Entry })) {
        $env:Path = (($entries | Where-Object { ![string]::IsNullOrWhiteSpace($_) }) + $Entry) -join ';'
    }
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

function Assert-NoActiveSchedule {
    $service = New-Object -ComObject Schedule.Service
    $folder = $null
    $task = $null
    $definition = $null
    try {
        $service.Connect()
        $folder = $service.GetFolder('\')
        $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        try { $task = $folder.GetTask("WindDown-$sid") }
        catch { if ($_.Exception.HResult -ne -2147024894) { throw }; return }
        $definition = $task.Definition
        try { $schedule = ([string]$definition.RegistrationInfo.Documentation) | ConvertFrom-Json }
        catch { throw 'The existing Wind Down schedule could not be read. Open Wind Down and cancel or finish it before installing.' }
        if ($null -eq $schedule.Result) { throw 'Cancel the active Wind Down schedule before installing or repairing the app.' }
    }
    finally {
        if ($null -ne $definition) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($definition) }
        if ($null -ne $task) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($task) }
        if ($null -ne $folder) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($folder) }
        if ($null -ne $service) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($service) }
    }
}

if ([IO.Directory]::GetParent($destination).FullName -ine $programsRoot) { throw 'Refusing to use an unexpected installation path.' }
foreach ($required in $requiredFiles) {
    if (!(Test-Path -LiteralPath (Join-Path $source $required) -PathType Leaf)) { throw "The Wind Down package is missing $required." }
}

Assert-NoActiveSchedule
if (Get-Process -Name 'Wind Down' -ErrorAction SilentlyContinue) { throw 'Exit Wind Down before installing or repairing it.' }

$stage = $null
$backup = $null
$payloadChanged = $false
$shortcutExisted = Test-Path -LiteralPath $shortcutPath
$existingRegistration = Test-Path -LiteralPath $uninstallKey
$pathAddedThisRun = $false
try {
    if ($source -ine $destination) {
        $stage = Join-Path $programsRoot ("Wind Down.installing." + [Guid]::NewGuid().ToString('N'))
        $backup = Join-Path $programsRoot ("Wind Down.backup." + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $stage -Force | Out-Null
        Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $stage -Recurse -Force
        foreach ($required in $requiredFiles) {
            if (!(Test-Path -LiteralPath (Join-Path $stage $required) -PathType Leaf)) { throw "The staged Wind Down package is missing $required." }
        }
        if (Test-Path -LiteralPath $destination) { Move-Item -LiteralPath $destination -Destination $backup }
        Move-Item -LiteralPath $stage -Destination $destination
        $stage = $null
        $payloadChanged = $true
    }

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $null
    try {
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = Join-Path $destination 'Wind Down.exe'
        $shortcut.WorkingDirectory = $destination
        $shortcut.IconLocation = (Join-Path $destination 'Assets\WindDown.ico') + ',0'
        $shortcut.Description = 'Schedule your PC to shut down or sleep.'
        $shortcut.Save()
    }
    finally {
        if ($null -ne $shortcut) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) }
        if ($null -ne $shell) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
    }

    $previouslyOwnedPath = $false
    if ($existingRegistration) {
        $previouslyOwnedPath = (Get-ItemPropertyValue -LiteralPath $uninstallKey -Name PathEntryAdded -ErrorAction SilentlyContinue) -eq 1
    }
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $userEntries = @($userPath -split ';' | Where-Object { ![string]::IsNullOrWhiteSpace($_) })
    $entryPresent = @($userEntries | Where-Object { Test-ExactPathEntry $_ $destination }).Count -gt 0
    $ownsPathEntry = $previouslyOwnedPath
    if (!$entryPresent) {
        [Environment]::SetEnvironmentVariable('Path', (($userEntries + $destination) -join ';'), 'User')
        $ownsPathEntry = $true
        $pathAddedThisRun = $true
    }
    Add-CurrentPathEntry $destination
    try { Publish-EnvironmentChange } catch { }

    $appPath = Join-Path $destination 'Wind Down.exe'
    $commandPath = Join-Path $destination 'wind-down.cmd'
    $commandInterpreter = Join-Path $env:SystemRoot 'System32\cmd.exe'
    $uninstallCommand = '"{0}" /d /c ""{1}" uninstall"' -f $commandInterpreter, $commandPath
    $version = (Get-Item -LiteralPath $appPath).VersionInfo.ProductVersion
    if ([string]::IsNullOrWhiteSpace($version)) { $version = '1.0.0' }
    $estimatedSize = [int][Math]::Ceiling(((Get-ChildItem -LiteralPath $destination -Recurse -File | Measure-Object -Property Length -Sum).Sum) / 1KB)

    New-Item -Path $uninstallKey -Force | Out-Null
    New-ItemProperty -LiteralPath $uninstallKey -Name DisplayName -Value $productName -PropertyType String -Force | Out-Null
    New-ItemProperty -LiteralPath $uninstallKey -Name DisplayVersion -Value $version -PropertyType String -Force | Out-Null
    New-ItemProperty -LiteralPath $uninstallKey -Name Publisher -Value 'SnoozeWalknn' -PropertyType String -Force | Out-Null
    New-ItemProperty -LiteralPath $uninstallKey -Name InstallLocation -Value $destination -PropertyType String -Force | Out-Null
    New-ItemProperty -LiteralPath $uninstallKey -Name DisplayIcon -Value ($appPath + ',0') -PropertyType String -Force | Out-Null
    New-ItemProperty -LiteralPath $uninstallKey -Name UninstallString -Value $uninstallCommand -PropertyType String -Force | Out-Null
    Remove-ItemProperty -LiteralPath $uninstallKey -Name QuietUninstallString -ErrorAction SilentlyContinue
    New-ItemProperty -LiteralPath $uninstallKey -Name URLInfoAbout -Value 'https://github.com/SnoozeWalknn/Wind-Down' -PropertyType String -Force | Out-Null
    New-ItemProperty -LiteralPath $uninstallKey -Name HelpLink -Value 'https://github.com/SnoozeWalknn/Wind-Down/issues' -PropertyType String -Force | Out-Null
    New-ItemProperty -LiteralPath $uninstallKey -Name InstallDate -Value (Get-Date -Format 'yyyyMMdd') -PropertyType String -Force | Out-Null
    New-ItemProperty -LiteralPath $uninstallKey -Name EstimatedSize -Value $estimatedSize -PropertyType DWord -Force | Out-Null
    New-ItemProperty -LiteralPath $uninstallKey -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -LiteralPath $uninstallKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -LiteralPath $uninstallKey -Name PathEntryAdded -Value ([int]$ownsPathEntry) -PropertyType DWord -Force | Out-Null

    if ($null -ne $backup -and (Test-Path -LiteralPath $backup)) { Remove-Item -LiteralPath $backup -Recurse -Force }
    Write-Host 'Wind Down is installed.'
    Write-Host 'Run "wind-down" to open it, or find Wind Down in the Start menu.'
}
catch {
    if ($pathAddedThisRun) {
        $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
        $userEntries = @($userPath -split ';' | Where-Object { ![string]::IsNullOrWhiteSpace($_) -and !(Test-ExactPathEntry $_ $destination) })
        [Environment]::SetEnvironmentVariable('Path', ($userEntries -join ';'), 'User')
        $processEntries = @($env:Path -split ';' | Where-Object { ![string]::IsNullOrWhiteSpace($_) -and !(Test-ExactPathEntry $_ $destination) })
        $env:Path = $processEntries -join ';'
        try { Publish-EnvironmentChange } catch { }
    }
    if (!$shortcutExisted -and (Test-Path -LiteralPath $shortcutPath)) { Remove-Item -LiteralPath $shortcutPath -Force -ErrorAction SilentlyContinue }
    if (!$existingRegistration -and (Test-Path -LiteralPath $uninstallKey)) { Remove-Item -LiteralPath $uninstallKey -Recurse -Force -ErrorAction SilentlyContinue }
    if ($payloadChanged -and (Test-Path -LiteralPath $destination)) { Remove-Item -LiteralPath $destination -Recurse -Force -ErrorAction SilentlyContinue }
    if ($null -ne $backup -and (Test-Path -LiteralPath $backup)) { Move-Item -LiteralPath $backup -Destination $destination -ErrorAction SilentlyContinue }
    throw
}
finally {
    if ($null -ne $stage -and (Test-Path -LiteralPath $stage)) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
}
