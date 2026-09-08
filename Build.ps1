param([switch]$Test, [switch]$Smoke)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if ($Test -and $Smoke) { throw 'Choose either -Test or -Smoke.' }

$dotnet = if (Test-Path '.tools/dotnet/dotnet.exe') { Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe' } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools/cli-home'
$packagePath = Join-Path $PSScriptRoot '.tools/packages'
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts'))
$output = Join-Path $artifactRoot 'Wind-Down-1.0-Windows-x64'
$projectOutput = Join-Path $artifactRoot 'Wind-Down-1.0-Project'
$archive = Join-Path $artifactRoot 'Wind-Down-1.0-Windows-x64.zip'

if (Get-Process -Name 'Wind Down' -ErrorAction SilentlyContinue) { throw 'Close Wind Down using Settings > Exit Wind Down before building.' }
foreach ($target in @($output, $projectOutput)) {
    if ([IO.Directory]::GetParent([IO.Path]::GetFullPath($target)).FullName -ine $artifactRoot) { throw 'Refusing to clean an unexpected output path.' }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
}
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive }
foreach ($obsolete in @('Wind Down', 'Still', 'Still-1.0-Windows-x64.zip', 'WindDown-icon-preview.png', 'WindDown-icon-sizes.png', 'current-icon.png', 'midnight-review.png', 'light-review.png', 'banner-review.png', 'taskbar-review.png', 'start-review.png')) {
    $obsoletePath = Join-Path $artifactRoot $obsolete
    if (Test-Path -LiteralPath $obsoletePath) { Remove-Item -LiteralPath $obsoletePath -Recurse -Force }
}

& $dotnet publish src/WindDown.Worker/WindDown.Worker.csproj -c Release --self-contained true -o $output "-p:RestorePackagesPath=$packagePath"
if ($LASTEXITCODE) { throw 'Worker build failed.' }
& $dotnet publish src/WindDown.App/WindDown.App.csproj -c Release -p:Platform=x64 -o $output "-p:RestorePackagesPath=$packagePath"
if ($LASTEXITCODE) { throw 'Application build failed.' }

Copy-Item packaging/Install.ps1,packaging/Uninstall.ps1 -Destination $output
Copy-Item packaging/README.md -Destination (Join-Path $output 'README.md')
Get-ChildItem -LiteralPath $output -Recurse -File -Filter '*.pdb' | Remove-Item -Force

if ($Test) {
    & $dotnet run --project tests/WindDown.Tests/WindDown.Tests.csproj -c Release "-p:RestorePackagesPath=$packagePath" -- --integration (Join-Path $output 'WindDown.Worker.exe')
    if ($LASTEXITCODE) { throw 'Validation failed.' }
}
elseif ($Smoke) {
    & $dotnet run --project tests/WindDown.Tests/WindDown.Tests.csproj -c Release "-p:RestorePackagesPath=$packagePath" -- --smoke (Join-Path $output 'WindDown.Worker.exe')
    if ($LASTEXITCODE) { throw 'Smoke validation failed.' }
}

$sourceRoots = @('src', 'tests', 'docs', 'packaging', 'tools')
$sourceFiles = @($sourceRoots | ForEach-Object {
    Get-ChildItem -LiteralPath $_ -Recurse -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
})
$sourceFiles += @('.gitignore', 'Build.ps1', 'Directory.Build.props', 'README.md', 'WindDown.sln') | ForEach-Object { Get-Item -LiteralPath $_ }
$sourceHashes = @($sourceFiles | Sort-Object FullName | ForEach-Object {
    [ordered]@{ Path = [IO.Path]::GetRelativePath($PSScriptRoot, $_.FullName); SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$binaryHashes = @('Wind Down.exe', 'WindDown.Worker.exe', 'Wind Down.dll', 'WindDown.Worker.dll', 'Assets/WindDown.ico') | ForEach-Object {
    $path = Join-Path $output $_
    [ordered]@{ Path = $_; SHA256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
}
$validation = if ($Test) { '33 checks passed; diagnostic worker did not call shutdown or sleep.' } elseif ($Smoke) { '7 targeted smoke checks passed; diagnostic scheduling did not call shutdown or sleep.' } else { 'Not run by this build invocation.' }
$manifest = [ordered]@{
    Product = 'Wind Down 1.0'
    Platform = 'Windows x64'
    Configuration = 'Release, self-contained'
    BuiltUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Validation = $validation
    Source = $sourceHashes
    Binaries = @($binaryHashes)
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'BUILD-MANIFEST.json') -Encoding UTF8

New-Item -ItemType Directory -Path $projectOutput | Out-Null
foreach ($sourceFile in $sourceFiles) {
    $relative = [IO.Path]::GetRelativePath($PSScriptRoot, $sourceFile.FullName)
    $destination = Join-Path $projectOutput $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
    Copy-Item -LiteralPath $sourceFile.FullName -Destination $destination
}
$projectManifest = [ordered]@{
    Product = 'Wind Down 1.0 Project'
    CreatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Files = @($sourceHashes)
}
$projectManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $projectOutput 'PROJECT-MANIFEST.json') -Encoding UTF8

Compress-Archive -Path $output -DestinationPath $archive -CompressionLevel Optimal
Write-Host "Project: $projectOutput"
Write-Host "Ready: $output\Wind Down.exe"
Write-Host "Package: $archive"
