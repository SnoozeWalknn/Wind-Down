$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$scripts = @(
    (Join-Path $repositoryRoot 'install.ps1'),
    (Join-Path $PSScriptRoot 'Install.ps1'),
    (Join-Path $PSScriptRoot 'Uninstall.ps1'),
    (Join-Path $PSScriptRoot 'WindDown.Cli.ps1')
)

foreach ($script in $scripts) {
    $tokens = $null
    $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($script, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw "$script has PowerShell parse errors: $($errors.Message -join '; ')" }
}

$integrationFiles = $scripts + (Join-Path $PSScriptRoot 'wind-down.cmd')
foreach ($file in $integrationFiles) {
    if ((Get-Content -LiteralPath $file -Raw) -match '(?i)\bStill\b') { throw "$file contains a legacy resource reference." }
}

$build = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Build.ps1') -Raw
foreach ($file in @('Install.ps1', 'Uninstall.ps1', 'WindDown.Cli.ps1', 'wind-down.cmd')) {
    if ($build -notmatch [regex]::Escape("packaging/$file")) { throw "Build.ps1 does not package $file." }
}

$installer = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Install.ps1') -Raw
$uninstaller = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Uninstall.ps1') -Raw
foreach ($requiredBoundary in @(
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WindDown',
    'PathEntryAdded',
    "Join-Path `$programsRoot `$productName"
)) {
    if (!$installer.Contains($requiredBoundary)) { throw "Install.ps1 is missing ownership boundary: $requiredBoundary" }
}

$bootstrap = Get-Content -LiteralPath (Join-Path $repositoryRoot 'install.ps1') -Raw
foreach ($requiredBoundary in @(
    'https://api.github.com/repos/$repository/releases/latest',
    'https://raw.githubusercontent.com/$repository/main/packaging',
    'The release download URL is not owned by the official Wind Down repository.',
    "`$segments -contains '..'"
)) {
    if (!$bootstrap.Contains($requiredBoundary)) { throw "install.ps1 is missing download boundary: $requiredBoundary" }
}
foreach ($requiredBoundary in @(
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WindDown',
    'HKCU:\Software\Classes\wind-down-power-timer',
    "StartsWith(`$baseName + '-Warning-'"
)) {
    if (!$uninstaller.Contains($requiredBoundary)) { throw "Uninstall.ps1 is missing ownership boundary: $requiredBoundary" }
}

$readme = Get-Content -LiteralPath (Join-Path $repositoryRoot 'README.md') -Raw
foreach ($command in @('irm https://raw.githubusercontent.com/SnoozeWalknn/Wind-Down/main/install.ps1 | iex', 'wind-down repair', 'wind-down uninstall')) {
    if (!$readme.Contains($command)) { throw "README.md is missing: $command" }
}

$help = & (Join-Path $PSScriptRoot 'wind-down.cmd') help 2>&1
if ($LASTEXITCODE -ne 0 -or ($help -join "`n") -notmatch 'wind-down uninstall') { throw 'The packaged command shim did not return its help successfully.' }

Write-Host 'Installer/CLI boundary checks passed.'
