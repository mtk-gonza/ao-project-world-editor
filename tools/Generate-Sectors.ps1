[CmdletBinding()]
param(
    [string]$ReferenceRoot = "",
    [string]$GeneratedRoot = "",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ReferenceRoot)) {
    $ReferenceRoot = Join-Path $PSScriptRoot "..\..\reference\Recursos"
}
if ([string]::IsNullOrWhiteSpace($GeneratedRoot)) {
    $GeneratedRoot = Join-Path $PSScriptRoot "..\..\ao-project-resources"
}

$referenceFullPath = [IO.Path]::GetFullPath($ReferenceRoot)
$generatedFullPath = [IO.Path]::GetFullPath($GeneratedRoot)
$legacyDirectory = Join-Path $referenceFullPath "Mapas"
$sectorDirectory = Join-Path $generatedFullPath "sectors"
$manifestPath = Join-Path $sectorDirectory "sectors.index.json"

if (-not (Test-Path $legacyDirectory -PathType Container)) {
    throw "No existe la carpeta legacy de mapas: $legacyDirectory"
}

New-Item -ItemType Directory -Force -Path $sectorDirectory | Out-Null

function Find-Sibling([string]$directory, [string]$baseName, [string]$extension) {
    Get-ChildItem -LiteralPath $directory -File |
        Where-Object { $_.BaseName -ieq $baseName -and $_.Extension -ieq $extension } |
        Select-Object -First 1
}

$entries = @()
$sources = Get-ChildItem -LiteralPath $legacyDirectory -File -Filter "*.csm" |
    ForEach-Object {
        if ($_.BaseName -match '(?i)^mapa(?<id>[0-9]+)$') {
            [pscustomobject]@{ File = $_; Id = [int]$Matches.id }
        }
    } | Sort-Object Id, @{ Expression = { $_.File.Name } }

foreach ($source in $sources) {
    $name = "sector-{0:D3}" -f $source.Id
    $destination = Join-Path $sectorDirectory $name
    if ((Test-Path $destination) -and -not $Force) {
        throw "Ya existe $destination. Use -Force para regenerar los sectores."
    }

    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    $files = @()
    foreach ($extension in @('.csm', '.inf', '.dat')) {
        $sourceFile = Find-Sibling $source.File.DirectoryName $source.File.BaseName $extension
        $targetPath = Join-Path $destination ("sector" + $extension)
        if ($null -ne $sourceFile) {
            Copy-Item -LiteralPath $sourceFile.FullName -Destination $targetPath -Force
            $files += [pscustomobject]@{
                Kind = $extension.TrimStart('.')
                SourceName = $sourceFile.Name
                GeneratedName = Split-Path $targetPath -Leaf
                Length = (Get-Item $targetPath).Length
                Sha256 = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash
            }
        }
    }

    $entries += [pscustomobject]@{
        SectorId = $source.Id
        Directory = $name
        SourceName = $source.File.Name
        Valid = $source.File.Length -gt 0
        InvalidReason = if ($source.File.Length -eq 0) { 'empty-csm' } else { $null }
        Files = $files
        MissingFormats = @('.csm', '.inf', '.dat' | Where-Object { $_ -notin $files.Kind })
    }
}

$manifest = [pscustomobject]@{
    Format = 'AOSECT01'
    Version = 1
    GeneratedUtc = [DateTime]::UtcNow.ToString('o')
    SourceDirectory = (Resolve-Path $legacyDirectory).Path
    Sectors = $entries
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host ("Generados {0} sectores en {1}" -f $entries.Count, $sectorDirectory)
