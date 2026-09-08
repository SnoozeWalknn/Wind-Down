# Refuse removal while an active schedule could reference this installation.
$ErrorActionPreference = 'Stop'
$service = New-Object -ComObject Schedule.Service
$service.Connect()
$folder = $service.GetFolder('\')
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$taskNames = @($folder.GetTasks(0) | Where-Object { $_.Name -like "WindDown-$sid*" -or $_.Name -like "Still-$sid*" } | Select-Object -ExpandProperty Name)
foreach ($name in $taskNames) {
    $task = $folder.GetTask($name)
    $schedule = $task.Definition.RegistrationInfo.Documentation | ConvertFrom-Json
    if (($name -eq "WindDown-$sid" -or $name -eq "Still-$sid") -and !$schedule.Result) { throw 'Cancel your active schedule in Wind Down before uninstalling.' }
}
if (Get-Process -Name 'Wind Down','Still' -ErrorAction SilentlyContinue) { throw 'Exit Wind Down and any earlier version before uninstalling.' }
foreach ($name in $taskNames) { $folder.DeleteTask($name, 0) }

$programs = [Environment]::GetFolderPath('Programs')
foreach ($shortcut in @((Join-Path $programs 'Wind Down.lnk'), (Join-Path $programs 'Still.lnk'))) {
    if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut }
}
foreach ($protocol in @('HKCU:\Software\Classes\wind-down-power-timer', 'HKCU:\Software\Classes\still-power-timer')) {
    if (Test-Path -LiteralPath $protocol) { Remove-Item -LiteralPath $protocol -Recurse }
}

$local = [Environment]::GetFolderPath('LocalApplicationData')
$expected = [IO.Path]::GetFullPath((Join-Path $local 'Programs\Wind Down'))
$legacy = [IO.Path]::GetFullPath((Join-Path $local 'Programs\Still'))
$actual = [IO.Path]::GetFullPath($PSScriptRoot)
if (Test-Path -LiteralPath $legacy) { Remove-Item -LiteralPath $legacy -Recurse -Force }
$legacyState = [IO.Path]::GetFullPath((Join-Path $local 'Still'))
if (Test-Path -LiteralPath $legacyState) { Remove-Item -LiteralPath $legacyState -Recurse -Force }
if ($actual -ieq $expected) { Remove-Item -LiteralPath $actual -Recurse -Force }
else { Write-Host 'Windows integration removed. You can now delete this portable Wind Down folder.' }
Write-Host 'Wind Down was removed. Your appearance preferences remain in LocalAppData\Wind Down.'
