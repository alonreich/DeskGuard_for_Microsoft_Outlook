param([ValidateSet('', 'run', 'build', 'rebuild', 'clean')][string]$Mode = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DevSandbox.ps1')

function Invoke-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE). No app was launched." }
}

try {
    $repo = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
    Set-Location -LiteralPath $repo
    $output = Join-Path $repo '.dev-run'
    $app = Join-Path $output 'app'
    $devHome = Join-Path $env:LOCALAPPDATA 'DeskGuard_DEV'
    $project = Join-Path $repo 'src\DeskGuard.App\DeskGuard.App.csproj'
    $lockPath = Join-Path $repo '.dev-run.lock'

    if (-not (Test-Path $output)) { New-Item -ItemType Directory -Path $output -Force | Out-Null }
    $launchLock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)

    # Check for running repo instances
    $repoPrefix = $repo.TrimEnd('\') + '\'
    $running = @(Get-CimInstance Win32_Process | Where-Object {
        $_.ExecutablePath -and $_.ExecutablePath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase) -and
        $_.Name -eq 'DeskGuard.exe'
    })
    if ($running.Count) { throw 'Close the running DeskGuard instance, then run dev.cmd again.' }

    if ($Mode -in @('clean', 'rebuild')) { Remove-DevCache $output $repo }
    if ($Mode -eq 'clean') { exit 0 }
    Clear-DevManagedOutput $app $output

    New-Item -ItemType Directory -Path $devHome -Force | Out-Null

    Write-Host "[DEV] Compiling fast JIT development build..."
    Invoke-Dotnet @('publish', $project, '-c', 'Release', '-r', 'win-x64',
        '--artifacts-path', (Join-Path $output 'build'), '-o', $app,
        '-p:PublishAot=false', '-p:PublishTrimmed=false', '-p:SelfContained=true',
        '-p:UseSharedCompilation=false', '-nodeReuse:false')

    $exe = Join-Path $app 'DeskGuard.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw "Published executable is missing: $exe" }

    if ($Mode -eq 'build') {
        Write-Host "[DEV] Current source staged successfully: $exe"
        exit 0
    }

    Write-Host "[DEV] Launching DeskGuard in development sandbox..."
    $env:DESKGUARD_DEV_HOME = $devHome
    $process = Start-Process -FilePath $exe -WorkingDirectory $app -PassThru -Wait
    exit $process.ExitCode
}
catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
finally {
    if ($launchLock) { $launchLock.Dispose() }
}
