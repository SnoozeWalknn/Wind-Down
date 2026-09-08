[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repository = 'SnoozeWalknn/Wind-Down'
$releaseApi = "https://api.github.com/repos/$repository/releases/latest"
$downloadPrefix = "https://github.com/$repository/releases/download/"
$integrationBase = "https://raw.githubusercontent.com/$repository/main/packaging"
$temporaryRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ("WindDown-Install-" + [Guid]::NewGuid().ToString('N'))))
$archivePath = Join-Path $temporaryRoot 'Wind-Down.zip'
$extractPath = Join-Path $temporaryRoot 'payload'

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    New-Item -ItemType Directory -Path $extractPath -Force | Out-Null

    Write-Host 'Finding the latest Wind Down release...'
    $headers = @{ Accept = 'application/vnd.github+json'; 'User-Agent' = 'Wind-Down-Installer' }
    $release = Invoke-RestMethod -Uri $releaseApi -Headers $headers -UseBasicParsing
    $assets = @($release.assets | Where-Object { $_.name -match '^Wind-Down-[0-9.]+-Windows-x64\.zip$' })
    if ($assets.Count -ne 1) { throw 'The latest release does not contain exactly one Wind Down Windows x64 package.' }

    $downloadUrl = [string]$assets[0].browser_download_url
    if (!$downloadUrl.StartsWith($downloadPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The release download URL is not owned by the official Wind Down repository.'
    }

    Write-Host "Downloading Wind Down $($release.tag_name)..."
    Invoke-WebRequest -Uri $downloadUrl -Headers $headers -UseBasicParsing -OutFile $archivePath

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        foreach ($entry in $zip.Entries) {
            $segments = @($entry.FullName.Replace('\', '/').Split('/') | Where-Object { $_ -ne '' })
            if ([IO.Path]::IsPathRooted($entry.FullName) -or $segments -contains '..') {
                throw 'The release archive contains an unsafe path and was not extracted.'
            }
        }
    }
    finally { $zip.Dispose() }

    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractPath -Force
    $executables = @(Get-ChildItem -LiteralPath $extractPath -Recurse -File | Where-Object { $_.Name -ceq 'Wind Down.exe' })
    if ($executables.Count -ne 1) { throw 'The release package does not contain exactly one Wind Down application.' }

    $payload = $executables[0].Directory.FullName
    $integrationFiles = @('Install.ps1', 'Uninstall.ps1', 'WindDown.Cli.ps1', 'wind-down.cmd')
    foreach ($file in $integrationFiles) {
        Invoke-WebRequest -Uri "$integrationBase/$file" -Headers $headers -UseBasicParsing -OutFile (Join-Path $payload $file)
    }

    foreach ($required in $integrationFiles + 'WindDown.Worker.exe') {
        if (!(Test-Path -LiteralPath (Join-Path $payload $required) -PathType Leaf)) {
            throw "The Wind Down download is missing $required."
        }
    }

    & (Join-Path $payload 'Install.ps1')
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath($temporaryRoot)
    $expectedParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    if ((Test-Path -LiteralPath $resolvedTemp) -and ([IO.Directory]::GetParent($resolvedTemp).FullName.TrimEnd('\') -ieq $expectedParent)) {
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
