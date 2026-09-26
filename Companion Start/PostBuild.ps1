param(
    [Parameter(Mandatory)] [string]$SolutionDir,
    [Parameter(Mandatory)] [string]$TargetPath,
    [Parameter(Mandatory)] [string]$IconPath,
    [Parameter(Mandatory)] [string]$ModRoot
)

$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Force -Path $ModRoot | Out-Null
Copy-Item -Path $TargetPath -Destination $ModRoot -Force
Copy-Item -Path $IconPath -Destination $ModRoot -Force

# Mirrors the whole solution (.sln plus the project folder) into the mod's Source
# subfolder so it always matches what was just built, without bin/obj/.vs churn or
# stale files left over from earlier ad-hoc copies.
$sourceDest = Join-Path $ModRoot "Source"
$excludedDirectories = @("bin", "obj", ".vs", ".git")
if (Test-Path $sourceDest) {
    Remove-Item $sourceDest -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $sourceDest | Out-Null
Get-ChildItem -Path $SolutionDir -Force | Where-Object {
    $_.Name -notin $excludedDirectories
} | Copy-Item -Destination $sourceDest -Recurse -Force
if (-not (Test-Path $sourceDest)) {
    throw "Failed to mirror source to $sourceDest"
}
