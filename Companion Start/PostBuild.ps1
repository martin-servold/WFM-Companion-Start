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
robocopy $SolutionDir $sourceDest /MIR /XD bin obj .vs .git /NFL /NDL /NJH /NJS /NC /NS
if ($LASTEXITCODE -ge 8) {
    throw "robocopy failed with exit code $LASTEXITCODE while mirroring source to $sourceDest"
}
