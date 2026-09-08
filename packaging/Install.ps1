# Per-user installation. No administrator rights or background startup entry.
$ErrorActionPreference = 'Stop'
$local = [Environment]::GetFolderPath('LocalApplicationData')
$destination = [IO.Path]::GetFullPath((Join-Path $local 'Programs\Wind Down'))
$legacyDestination = [IO.Path]::GetFullPath((Join-Path $local 'Programs\Still'))
$source = [IO.Path]::GetFullPath($PSScriptRoot)
if (!(Test-Path (Join-Path $source 'Wind Down.exe'))) { throw 'Run this installer from the published Wind Down folder.' }

$service = New-Object -ComObject Schedule.Service
$service.Connect()
$folder = $service.GetFolder('\')
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$taskNames = @($folder.GetTasks(0) | Where-Object { $_.Name -like "WindDown-$sid*" -or $_.Name -like "Still-$sid*" } | Select-Object -ExpandProperty Name)
foreach ($name in $taskNames) {
    $task = $folder.GetTask($name)
    $schedule = $task.Definition.RegistrationInfo.Documentation | ConvertFrom-Json
    if (($name -eq "WindDown-$sid" -or $name -eq "Still-$sid") -and !$schedule.Result) {
        throw 'Cancel the active schedule before installing or moving Wind Down. If it was created by an earlier version, open Wind Down once to migrate it first.'
    }
}
if (Get-Process -Name 'Wind Down','Still' -ErrorAction SilentlyContinue) { throw 'Exit Wind Down and any earlier version before installing.' }

foreach ($name in @($taskNames | Where-Object { $_ -like "Still-$sid*" })) { $folder.DeleteTask($name, 0) }
if ($source -ine $destination) {
    if (Test-Path -LiteralPath $destination) {
        $expectedParent = [IO.Path]::GetFullPath((Join-Path $local 'Programs'))
        if ([IO.Directory]::GetParent($destination).FullName -ine $expectedParent) { throw 'Refusing to replace an unexpected installation path.' }
        Remove-Item -LiteralPath $destination -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Get-ChildItem -LiteralPath $source | Copy-Item -Destination $destination -Recurse -Force
}

$programs = [Environment]::GetFolderPath('Programs')
$legacyShortcut = Join-Path $programs 'Still.lnk'
if (Test-Path -LiteralPath $legacyShortcut) { Remove-Item -LiteralPath $legacyShortcut }
$shell = New-Object -ComObject WScript.Shell
$shortcutPath = Join-Path $programs 'Wind Down.lnk'
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $destination 'Wind Down.exe'
$shortcut.WorkingDirectory = $destination
$shortcut.IconLocation = Join-Path $destination 'Assets\WindDown.ico'
$shortcut.Description = 'Schedule your PC to shut down or sleep.'
$shortcut.Save()

$legacyProtocol = 'HKCU:\Software\Classes\still-power-timer'
if (Test-Path -LiteralPath $legacyProtocol) { Remove-Item -LiteralPath $legacyProtocol -Recurse }
if (Test-Path -LiteralPath $legacyDestination) {
    $expectedLegacyParent = [IO.Path]::GetFullPath((Join-Path $local 'Programs'))
    if ([IO.Directory]::GetParent($legacyDestination).FullName -ine $expectedLegacyParent) { throw 'Refusing to remove an unexpected legacy path.' }
    Remove-Item -LiteralPath $legacyDestination -Recurse -Force
}
Write-Host 'Wind Down is installed. Open Wind Down from the Start menu.'
