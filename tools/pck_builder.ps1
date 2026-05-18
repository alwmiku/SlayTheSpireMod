param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$OutputPath,
    [Parameter(Mandatory = $true, Position = 1, ValueFromRemainingArguments = $true)]
    [string[]]$Inputs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-FileMap {
    param([string[]]$Paths)

    $map = @{}
    foreach ($inp in $Paths) {
        if (Test-Path -LiteralPath $inp -PathType Container) {
            $root = (Resolve-Path -LiteralPath $inp).Path
            Get-ChildItem -LiteralPath $root -Recurse -File | ForEach-Object {
                $full = $_.FullName
                $rel = $full.Substring($root.Length).TrimStart('\', '/').Replace('\', '/')
                $map[$rel] = [System.IO.File]::ReadAllBytes($full)
            }
        } elseif (Test-Path -LiteralPath $inp -PathType Leaf) {
            $full = (Resolve-Path -LiteralPath $inp).Path
            $name = [System.IO.Path]::GetFileName($full)
            $map[$name] = [System.IO.File]::ReadAllBytes($full)
        } else {
            Write-Warning "Path not found, skipped: $inp"
        }
    }
    return $map
}

$files = Get-FileMap -Paths $Inputs
$sortedPaths = $files.Keys | Sort-Object

$entries = @()
foreach ($vpath in $sortedPaths) {
    $content = $files[$vpath]
    $pathBytes = [System.Text.Encoding]::UTF8.GetBytes($vpath)
    $md5 = [System.Security.Cryptography.MD5]::HashData($content)
    $entries += [PSCustomObject]@{
        PathBytes = $pathBytes
        Content   = $content
        Size      = [uint64]$content.Length
        Md5       = $md5
    }
}

$headerSize = 40
$indexSize = 16
foreach ($e in $entries) {
    $indexSize += 4 + $e.PathBytes.Length + 8 + 8 + 16
}

$dataStart = [uint64]($headerSize + $indexSize)
$currentOffset = [uint64]0
foreach ($e in $entries) {
    $e | Add-Member -NotePropertyName Offset -NotePropertyValue ($dataStart + $currentOffset)
    $currentOffset += $e.Size
}

$entryHashStream = New-Object System.IO.MemoryStream
$entryHashWriter = New-Object System.IO.BinaryWriter($entryHashStream)
foreach ($e in $entries) {
    $entryHashWriter.Write([uint32]$e.PathBytes.Length)
    $entryHashWriter.Write($e.PathBytes)
    $entryHashWriter.Write([uint64]$e.Offset)
    $entryHashWriter.Write([uint64]$e.Size)
    $entryHashWriter.Write($e.Md5)
}
$entryHashWriter.Flush()
$indexMd5 = if ($entries.Count -gt 0) {
    [System.Security.Cryptography.MD5]::HashData($entryHashStream.ToArray())
} else {
    [byte[]](0..15 | ForEach-Object { 0 })
}
$entryHashWriter.Dispose()
$entryHashStream.Dispose()

$outDir = Split-Path -Parent $OutputPath
if ($outDir -and -not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Path $outDir | Out-Null
}

$fs = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read)
try {
    $bw = New-Object System.IO.BinaryWriter($fs)
    try {
        $bw.Write([System.Text.Encoding]::ASCII.GetBytes("GDPC"))
        $bw.Write([uint32]2)
        $bw.Write([uint32]4)
        $bw.Write([uint32]5)
        $bw.Write([uint32]1)
        $bw.Write([byte[]](0..15 | ForEach-Object { 0 }))

        foreach ($e in $entries) {
            $bw.Write([uint32]$e.PathBytes.Length)
            $bw.Write($e.PathBytes)
            $bw.Write([uint64]$e.Offset)
            $bw.Write([uint64]$e.Size)
            $bw.Write($e.Md5)
        }

        $bw.Write($indexMd5)

        foreach ($e in $entries) {
            $bw.Write($e.Content)
        }
        $bw.Flush()
    } finally {
        $bw.Dispose()
    }
} finally {
    $fs.Dispose()
}

$size = (Get-Item -LiteralPath $OutputPath).Length
Write-Host "[OK] $OutputPath ($size bytes, $($entries.Count) files)"
