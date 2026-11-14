[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PackageDirectory,
    [Parameter(Mandatory)][string] $SourceCommit,
    [Parameter(Mandatory)][string] $SourceRef,
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression

$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$PackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path

if ($SourceCommit -cnotmatch '^[a-fA-F0-9]{40}$') {
    throw 'SourceCommit must be a complete Git commit SHA.'
}

[xml] $rootProps = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'Directory.Build.props') -Raw
$packageVersion = $rootProps.Project.PropertyGroup.FileVersion
if ([string]::IsNullOrWhiteSpace($packageVersion)) {
    throw 'Missing FileVersion in root Directory.Build.props.'
}

# DiscUtils library and integration projects are the only expected release packages.
$expected = @{}
foreach ($tree in @('Library', 'Integrations')) {
    $projectFiles = @(Get-ChildItem -Path (Join-Path $RepositoryRoot "$tree/*/*.csproj") -File)
    foreach ($project in $projectFiles) {
        $id = 'LTRData.' + $project.BaseName
        if ($expected.ContainsKey($id)) { throw "Duplicate expected package ID: $id" }
        $expected[$id] = $true
    }
}
if ($expected.Count -eq 0) { throw 'No expected package projects found.' }

$packages = @(Get-ChildItem -LiteralPath $PackageDirectory -File -Filter '*.nupkg')
$symbols = @(Get-ChildItem -LiteralPath $PackageDirectory -File -Filter '*.snupkg')
if ($packages.Count -ne $expected.Count) {
    throw "Expected $($expected.Count) NuGet packages, found $($packages.Count)."
}
if ($symbols.Count -ne $packages.Count) {
    throw "Expected $($packages.Count) symbol packages, found $($symbols.Count)."
}

$seen = @{}
$entries = foreach ($package in ($packages | Sort-Object Name)) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $specs = @($zip.Entries | Where-Object { $_.FullName -like '*.nuspec' })
        if ($specs.Count -ne 1) { throw "$($package.Name): expected exactly one nuspec." }
        $reader = [System.IO.StreamReader]::new($specs[0].Open())
        try { [xml] $nuspec = $reader.ReadToEnd() }
        finally { $reader.Dispose() }
        $metadata = $nuspec.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]')
        $idNode = $metadata.SelectSingleNode('*[local-name()="id"]')
        $versionNode = $metadata.SelectSingleNode('*[local-name()="version"]')
        if (-not $idNode -or -not $versionNode) { throw "$($package.Name): missing ID/version." }
        $id = $idNode.InnerText
        $version = $versionNode.InnerText

        if (-not $expected.ContainsKey($id)) { throw "Unexpected release package: $id" }
        if ($seen.ContainsKey($id)) { throw "Duplicate release package: $id" }
        if ($version -cne $packageVersion) {
            throw "$($package.Name): package version $version does not match FileVersion $packageVersion."
        }
        $expectedFile = "$id.$version.nupkg"
        if ($package.Name -cne $expectedFile) {
            throw "Package filename mismatch: $($package.Name), expected $expectedFile."
        }
        $symbolName = "$id.$version.snupkg"
        $symbolPath = Join-Path $PackageDirectory $symbolName
        if (-not (Test-Path -LiteralPath $symbolPath -PathType Leaf)) {
            throw "Missing symbol package $symbolName."
        }
        $symbol = Get-Item -LiteralPath $symbolPath
        $seen[$id] = $true

        [pscustomobject]@{
            id = $id
            version = $version
            package = [pscustomobject]@{
                name = $package.Name
                size = $package.Length
                sha256 = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
            symbols = [pscustomobject]@{
                name = $symbol.Name
                size = $symbol.Length
                sha256 = (Get-FileHash -LiteralPath $symbol.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
    }
    finally { $zip.Dispose() }
}

$missing = @($expected.Keys | Where-Object { -not $seen.ContainsKey($_) })
if ($missing.Count) { throw "Missing packages: $($missing -join ', ')" }

$manifest = [pscustomobject]@{
    schemaVersion = 1
    repository = 'LTRData/DiscUtils'
    sourceCommit = $SourceCommit.ToLowerInvariant()
    sourceRef = $SourceRef
    packageVersion = $packageVersion
    packageCount = $expected.Count
    createdUtc = [DateTime]::UtcNow.ToString('o')
    signingStatus = 'unsigned'
    packages = @($entries | Sort-Object id)
}

$manifestPath = Join-Path $PackageDirectory 'nuget-release-manifest.json'
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Host "Validated $($expected.Count) package and symbol pairs at version $packageVersion."
Write-Host "Unsigned artifact manifest: $manifestPath"
