param(
    [Parameter(Mandatory)] [string]$SolutionDir,
    [Parameter(Mandatory)] [string]$TargetPath,
    [Parameter(Mandatory)] [string]$IconPath,
    [Parameter(Mandatory)] [string]$ModRoot
)

$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Force -Path $ModRoot | Out-Null

# Copy to a temp file and rename it over the old DLL rather than overwriting in place. A running
# game has the old DLL memory-mapped and reads method bodies from it lazily, so rewriting that same
# file makes any not-yet-JIT'd method (typically Harmony prefixes) fail with "BadImageFormatException:
# Method has zero rva". Renaming swaps in a new file and leaves the mapped one intact.
$dllDest = Join-Path $ModRoot (Split-Path $TargetPath -Leaf)
$dllTemp = "$dllDest.tmp"
Copy-Item -Path $TargetPath -Destination $dllTemp -Force
Move-Item -Path $dllTemp -Destination $dllDest -Force

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
