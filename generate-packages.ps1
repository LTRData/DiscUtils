$outputDir = "C:\XGitPrivate\DevePXEBootStuff\DevePXEBootClean\DiscUtils\nupkgs"

Write-Host "Cleaning output directory: $outputDir"
if (Test-Path -Path $outputDir) {
    Get-ChildItem -Path $outputDir -Filter *.nupkg | Remove-Item -Force
    Get-ChildItem -Path $outputDir -Filter *.snupkg | Remove-Item -Force
} else {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

Write-Host "Cleaning solution..."
dotnet clean --configuration Release

Write-Host "Restoring..."
dotnet restore

Write-Host "Building..."
dotnet build --configuration Release

Write-Host "Packing..."
dotnet pack --configuration Release --no-build --output $outputDir

Write-Host "Done. Packages are in $outputDir"
