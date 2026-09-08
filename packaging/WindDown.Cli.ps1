[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Command = 'launch',
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArguments = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Show-Help {
    Write-Output @'
Wind Down command line

Usage:
  wind-down             Open Wind Down
  wind-down install     Install or restore Wind Down integration
  wind-down repair      Restore Wind Down integration
  wind-down uninstall   Uninstall Wind Down
  wind-down help        Show this help
'@
}

$normalized = $Command.Trim().ToLowerInvariant()
if ($RemainingArguments.Count -gt 0) {
    Write-Error "Unexpected argument: $($RemainingArguments[0])"
    exit 2
}

switch ($normalized) {
    { $_ -in @('', 'launch', 'open') } {
        $appPath = Join-Path $PSScriptRoot 'Wind Down.exe'
        if (!(Test-Path -LiteralPath $appPath -PathType Leaf)) { throw 'Wind Down.exe is missing. Run "wind-down repair" from a complete Wind Down package.' }
        Start-Process -FilePath $appPath -WorkingDirectory $PSScriptRoot
        exit 0
    }
    { $_ -in @('install', 'repair') } {
        & (Join-Path $PSScriptRoot 'Install.ps1')
        exit 0
    }
    'uninstall' {
        & (Join-Path $PSScriptRoot 'Uninstall.ps1')
        exit 0
    }
    { $_ -in @('help', '-h', '--help', '/?') } {
        Show-Help
        exit 0
    }
    default {
        Write-Error "Unknown Wind Down command: $Command"
        Show-Help
        exit 2
    }
}
