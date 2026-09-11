[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string] $PackageDirectory
)

$ErrorActionPreference = 'Stop'
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
if (-not $PackageDirectory) {
    $PackageDirectory = $RepositoryRoot
}

Add-Type -AssemblyName System.IO.Compression.FileSystem

# Utilities and SourceGenerators are not packable. These two trees share
# the LTRData.$(MSBuildProjectName) package identity convention.
$expected = @{}
foreach ($tree in @('Library', 'Integrations')) {
    $projects = Get-ChildItem -Path (Join-Path $RepositoryRoot "$tree/*/*.csproj") -File
    foreach ($project in $projects) {
        [xml] $metadata = Get-Content -LiteralPath $project.FullName -Raw
        $id = 'LTRData.' + $project.BaseName
        $readmePath = Join-Path $project.DirectoryName 'README.md'
        if (-not (Test-Path -LiteralPath $readmePath -PathType Leaf)) {
            throw "Missing local README: $readmePath"
        }
        $description = $metadata.SelectSingleNode('/Project/PropertyGroup/Description')
        if (-not $description -or -not $description.InnerText.Trim()) {
            throw "Missing package description: $($project.FullName)"
        }
        $expected[$id] = @{
            Readme = [Convert]::ToBase64String([IO.File]::ReadAllBytes($readmePath))
            Description = $description.InnerText.Trim()
        }
    }
}
if ($expected.Count -eq 0) {
    throw "No package projects found under $RepositoryRoot"
}

function Read-ZipText($Entry) {
    $reader = [IO.StreamReader]::new($Entry.Open())
    try { return $reader.ReadToEnd() }
    finally { $reader.Dispose() }
}

$seen = @{}
$packages = Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.nupkg' -Recurse -File
foreach ($package in $packages) {
    $archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $specs = @($archive.Entries | Where-Object { $_.FullName -like '*.nuspec' })
        if ($specs.Count -ne 1) {
            throw "Expected one nuspec in $($package.FullName)"
        }
        [xml] $spec = Read-ZipText $specs[0]
        $metadata = $spec.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]')
        $id = $metadata.SelectSingleNode('*[local-name()="id"]').InnerText
        if (-not $expected.ContainsKey($id)) { continue }

        $readmeMetadata = $metadata.SelectSingleNode('*[local-name()="readme"]')
        if (-not $readmeMetadata -or $readmeMetadata.InnerText -cne 'README.md') {
            throw "${id}: nuspec must declare README.md"
        }
        $entries = @($archive.Entries | Where-Object { $_.FullName -ceq 'README.md' })
        if ($entries.Count -ne 1) {
            throw "${id}: expected exactly one root README.md"
        }
        $stream = $entries[0].Open()
        $copy = [IO.MemoryStream]::new()
        try {
            $stream.CopyTo($copy)
            $actual = [Convert]::ToBase64String($copy.ToArray())
        }
        finally {
            $stream.Dispose()
            $copy.Dispose()
        }
        if ($actual -cne $expected[$id].Readme) {
            throw "${id}: packaged README differs from the project's local README"
        }
        $description = $metadata.SelectSingleNode('*[local-name()="description"]')
        if (-not $description -or $description.InnerText.Trim() -cne $expected[$id].Description) {
            throw "${id}: packaged description differs from the project description"
        }
        $seen[$id] = $true
    }
    finally { $archive.Dispose() }
}

$missing = @($expected.Keys | Where-Object { -not $seen.ContainsKey($_) } | Sort-Object)
if ($missing.Count) {
    throw "Packages missing from ${PackageDirectory}: $($missing -join ', '). Build or pack all Library and Integrations projects first."
}
Write-Host "Verified local README contents and descriptions for $($seen.Count) NuGet packages."
