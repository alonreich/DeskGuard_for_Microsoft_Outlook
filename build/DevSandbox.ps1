function Assert-DevOwnedPath([string]$Path, [string]$Root) {
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $boundary = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing path outside development boundary: $full"
    }
    return $full
}

function Remove-DevCache([string]$Path, [string]$Root) {
    $checked = Assert-DevOwnedPath $Path $Root
    if (Test-Path -LiteralPath $checked) {
        Remove-Item -LiteralPath $checked -Recurse -Force -ErrorAction Stop
        if (Test-Path -LiteralPath $checked) { throw "Cleanup incomplete: $checked" }
    }
}

function Clear-DevManagedOutput([string]$App, [string]$Output) {
    $null = Assert-DevOwnedPath $App $Output
    if (Test-Path -LiteralPath $App) {
        Get-ChildItem -LiteralPath $App -Force | ForEach-Object { Remove-DevCache $_.FullName $Output }
    }
}
