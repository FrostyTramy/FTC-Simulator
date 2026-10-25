# Puts the generated developer tools into generated/tools (git-ignored): Gradle, Eclipse JDT LS and the native
# AprilTag library. Downloads are checked against pinned SHA-256 values. Safe to re-run: tools already there are kept.
# Usage: ./scripts/setup_tools.ps1      (needs the project .venv, docs/SETUP.md section 1)
. "$PSScriptRoot\lib\tools.ps1"
$ErrorActionPreference = 'Stop'

$tools = Join-Path $RepoRoot 'generated\tools'
New-Item -ItemType Directory -Force $tools | Out-Null

function Get-Verified([string]$url, [string]$file, [string]$sha256) {
    if (-not (Test-Path $file)) {
        Write-Host "downloading $url"
        Invoke-WebRequest -Uri $url -OutFile $file -UseBasicParsing
    }
    $actual = (Get-FileHash -Algorithm SHA256 $file).Hash.ToLowerInvariant()
    if ($actual -ne $sha256) {
        Remove-Item $file
        throw "SHA-256 mismatch for $file ($actual)"
    }
}

# Gradle 8.14.3: builds the shim, the runner and team code
$gradle = Join-Path $tools 'gradle-8.14.3'
if (-not (Test-Path (Join-Path $gradle 'bin\gradle.bat'))) {
    $zip = Join-Path $tools 'gradle-8.14.3-bin.zip'
    Get-Verified 'https://services.gradle.org/distributions/gradle-8.14.3-bin.zip' $zip 'bd71102213493060956ec229d946beee57158dbd89d0e62b91bca0fa2c5f3531'
    Expand-Archive -Path $zip -DestinationPath $tools -Force
}
Write-Host "Gradle:   $gradle"

# Eclipse JDT LS 1.62.0: the editor's language server (runs on Java 21+)
$jdtls = Join-Path $tools 'jdtls-1.62.0'
if (-not (Test-Path (Join-Path $jdtls 'plugins'))) {
    $download = Join-Path $tools 'jdtls-1.62.0-download'
    New-Item -ItemType Directory -Force $download | Out-Null
    $archive = Join-Path $download 'jdtls.tar.gz'
    Get-Verified 'https://download.eclipse.org/jdtls/milestones/1.62.0/jdt-language-server-1.62.0-202610131109.tar.gz' $archive 'f587dafd8410633c2c2bf005e853a2bbb03cddee88933d49bfe6d9cb1cf09342'
    New-Item -ItemType Directory -Force $jdtls | Out-Null
    & tar -xzf $archive -C $jdtls
    if ($LASTEXITCODE -ne 0) { throw 'could not unpack JDT LS' }
}
Write-Host "JDT LS:   $jdtls"

# AprilTag 3 (BSD-2) with its runtime DLLs (pthreads-win32, LGPL; msvcr100), from the pinned pupil-apriltags wheel
$site = Join-Path $RepoRoot '.venv\Lib\site-packages'
$dll = Join-Path $site 'pupil_apriltags\lib\apriltag.dll'
if (-not (Test-Path $dll)) { throw 'pupil-apriltags is missing from .venv: run docs/SETUP.md section 1 first' }
$apriltag = Join-Path $tools 'apriltag'
New-Item -ItemType Directory -Force $apriltag | Out-Null
Copy-Item $dll, (Join-Path $site 'pupil_apriltags.libs\*.dll') $apriltag -Force
Copy-Item (Join-Path $site 'pupil_apriltags-1.0.4.post11.dist-info\licenses\LICENSE') $apriltag -Force
Write-Host "AprilTag: $apriltag"
exit 0
