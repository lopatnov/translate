#Requires -Version 7
<#
.SYNOPSIS
    Downloads the fastText LID-176 compressed language identification model.
.DESCRIPTION
    Downloads lid.176.ftz (~917 KB, MIT license) from the fastText releases page.
    This model identifies 176 languages and is used by FastTextLanguageDetector
    as a more accurate replacement for the Unicode-heuristic detector.

    Set Models__LangDetect__Path in your environment or appsettings.json to the
    path printed at the end of this script.

.EXAMPLE
    .\scripts\download-langdetect.ps1
    .\scripts\download-langdetect.ps1 -OutputDir ./models/langdetect
#>
param(
    [string]$OutputDir = "./models/langdetect"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ModelUrl  = "https://dl.fbaipublicfiles.com/fasttext/supervised-models/lid.176.ftz"
$ModelFile = "lid.176.ftz"
$Dest      = Join-Path $OutputDir $ModelFile

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

if (Test-Path $Dest) {
    Write-Host "[skip] $ModelFile already exists at $Dest"
} else {
    Write-Host "[download] $ModelFile from $ModelUrl ..."
    Invoke-WebRequest -Uri $ModelUrl -OutFile $Dest
    $size = (Get-Item $Dest).Length
    Write-Host "[done] $ModelFile saved ($([math]::Round($size/1KB, 1)) KB)"
}

Write-Host "`nSet in appsettings.json or environment:"
Write-Host "  Models__LangDetect__Path = $((Resolve-Path $Dest).Path)"
