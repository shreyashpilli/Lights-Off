param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist')
)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The .NET Framework C# compiler was not found. Build Quiet Run on Windows with .NET Framework 4.x installed.'
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$exePath = Join-Path $OutputDirectory 'QuietRun.exe'
& $compiler /nologo /target:winexe /optimize+ /platform:anycpu `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
    "/out:$exePath" (Join-Path $PSScriptRoot 'QuietRun.cs')
if ($LASTEXITCODE -ne 0) {
    throw "C# compilation failed with exit code $LASTEXITCODE."
}

$smoke = Start-Process -FilePath $exePath -ArgumentList '--self-test' -Wait -PassThru -NoNewWindow
if ($smoke.ExitCode -ne 0) {
    throw "Quiet Run smoke check failed with exit code $($smoke.ExitCode)."
}

$gui = Start-Process -FilePath $exePath -PassThru
try {
    $deadline = (Get-Date).AddSeconds(8)
    do {
        Start-Sleep -Milliseconds 200
        $gui.Refresh()
    } while ($gui.MainWindowHandle -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline)

    if ($gui.MainWindowHandle -eq [IntPtr]::Zero) {
        throw 'Quiet Run window did not appear during the GUI smoke check.'
    }
    if (-not $gui.CloseMainWindow()) {
        throw 'Quiet Run window could not be closed cleanly during the GUI smoke check.'
    }
    if (-not $gui.WaitForExit(5000)) {
        throw 'Quiet Run did not exit after its window was closed.'
    }
    if ($gui.ExitCode -ne 0) {
        throw "Quiet Run exited with code $($gui.ExitCode) during the GUI smoke check."
    }
}
finally {
    $gui.Dispose()
}

$zipPath = Join-Path $OutputDirectory 'QuietRun-Windows.zip'
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$archive = [System.IO.Compression.ZipFile]::Open(
    $zipPath,
    [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in @(
        $exePath,
        (Join-Path $PSScriptRoot 'README.md'),
        (Join-Path $PSScriptRoot 'Start Quiet Run.cmd')
    )) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive,
            $file,
            [System.IO.Path]::GetFileName($file),
            [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally {
    $archive.Dispose()
}

$hash = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Output "Built $exePath"
Write-Output "SHA256 $hash"
Write-Output "Packaged $zipPath"
Write-Output "ZIP SHA256 $zipHash"
