<#
.SYNOPSIS
    Builds BSAudioDecode for Windows x64 and Android arm64 and stages it into the package.

.DESCRIPTION
    win-x64 needs Visual Studio 2022 (Build Tools is enough) with the C++ x64 toolset and CMake
    (on PATH, or the one Visual Studio bundles). android-arm64 uses the NDK, CMake and Ninja that
    ship with the Unity Editor named in the project's ProjectSettings\ProjectVersion.txt (Android
    Build Support). Set BSAD_ANDROID_NDK to use another NDK.

    Outputs (committed, with their .meta files):
      Runtime\Plugins\AudioDecode\x86_64\BSAudioDecode.dll                Editor x64 + Standalone Win64
      Runtime\Plugins\AudioDecode\Android\arm64-v8a\libBSAudioDecode.so   Android ARM64 (stripped)

    The Unity Editor keeps BSAudioDecode.dll open from its first use until it exits. If staging
    reports the DLL as STALE, close Unity and run this again.

.PARAMETER Target
    win-x64, android-arm64 or all (default).

.PARAMETER Test
    Windows: runs bsadtool caps. Android: builds bsadtool and, if adb sees a device, runs caps on it.
    The decode comparisons against ffmpeg are in Tests~/AudioStream (dotnet run).

.PARAMETER Verify
    Dependency and export checks on the built binaries.
      DLL: no dynamic CRT, no static import of Media Foundation, exports == BSAD_* of the header.
      .so: NEEDED within libmediandk/liblog/libm/libdl/libc, LOAD segments 16 KB aligned,
           dynamic symbols == BSAD_* of the header.

.PARAMETER Clean
    Deletes the build directories of the selected targets first.

.PARAMETER NoStage
    Builds (and tests/verifies) only; leaves the package's Plugins folder alone.
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'android-arm64', 'all')] [string] $Target = 'all',
    [ValidateSet('Release', 'Debug')] [string] $Config = 'Release',
    [switch] $Test,
    [switch] $Verify,
    [switch] $Clean,
    [switch] $NoStage
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$package = Split-Path (Split-Path $root -Parent) -Parent
$plugins = Join-Path $package 'Runtime\Plugins\AudioDecode'
$doWindows = $Target -eq 'win-x64' -or $Target -eq 'all'
$doAndroid = $Target -eq 'android-arm64' -or $Target -eq 'all'

$summary = New-Object System.Collections.Generic.List[object]
$failures = New-Object System.Collections.Generic.List[string]

function Add-Row([string] $target, [string] $item, [string] $result, [string] $detail) {
    $summary.Add([pscustomobject]@{ Target = $target; Item = $item; Result = $result; Detail = $detail })
    if ($result -eq 'FAIL') { $failures.Add("$target $item - $detail") }
}

$header = Get-Content (Join-Path $root 'include\bsaudiodecode.h') -Raw
$expectedExports = @([regex]::Matches($header, 'BSAD_API\s+[^;(]*?BSAD_CALL\s+(BSAD_\w+)\s*\(') |
    ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)

function Compare-Exports([string] $target, [string[]] $actual) {
    $actual = @($actual | Sort-Object -Unique)
    $missing = @($expectedExports | Where-Object { $actual -notcontains $_ })
    $extra = @($actual | Where-Object { $expectedExports -notcontains $_ })
    if ($missing.Count -eq 0 -and $extra.Count -eq 0) {
        Add-Row $target 'exports' 'OK' "$($actual.Count) BSAD_* (all of the header, nothing else)"
    } else {
        Add-Row $target 'exports' 'FAIL' ("missing: " + ($missing -join ',') + " unexpected: " + ($extra -join ','))
    }
}

function Find-VisualStudio {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { throw 'vswhere.exe not found - install Visual Studio 2022 Build Tools with the C++ workload.' }
    $path = & $vswhere -products * -version '[17.0,18.0)' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -latest -property installationPath
    if (-not $path) { throw 'No Visual Studio 2022 install with the C++ x64 toolset was found.' }
    return $path
}

function Find-CMake([string] $vs) {
    $onPath = Get-Command cmake -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $bundled = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
    if (Test-Path $bundled) { return $bundled }
    throw 'CMake not found: put cmake on PATH or add the "C++ CMake tools for Windows" Visual Studio component.'
}

function Copy-Staged([string] $from, [string] $to) {
    New-Item -ItemType Directory -Force (Split-Path $to -Parent) | Out-Null
    try {
        Copy-Item $from $to -Force
        return 'staged'
    } catch {
        if ((Test-Path $to) -and ((Get-FileHash $to).Hash -eq (Get-FileHash $from).Hash)) { return 'unchanged (locked by Unity)' }
        Write-Warning "$to is locked by the Unity Editor and was NOT updated. Close Unity and run build.ps1 again."
        return 'STALE'
    }
}

function Stage-Binary([string] $target, [string] $built, [string] $staged) {
    $state = Copy-Staged $built $staged
    $hash = (Get-FileHash $staged -Algorithm SHA256).Hash.Substring(0, 12).ToLowerInvariant()
    $size = '{0:N0} bytes' -f (Get-Item $staged).Length
    Add-Row $target 'stage' ($(if ($state -eq 'STALE') { 'STALE' } else { 'OK' })) "$(Split-Path $staged -Leaf) $state, $size, sha256 $hash"
    if (-not (Test-Path "$staged.meta")) {
        Add-Row $target 'meta' 'FAIL' "$(Split-Path $staged -Leaf).meta missing: Unity would import it with the wrong platform settings"
    }
}

# Runs a native command and returns its output as strings; the exit code alone decides success.
function Invoke-Native([scriptblock] $command) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $command 2>&1 | ForEach-Object {
            if ($_ -is [System.Management.Automation.ErrorRecord]) { $_.Exception.Message } else { "$_" }
        }
    } finally {
        $ErrorActionPreference = $previous
    }
}

function Invoke-Checked([string] $what, [scriptblock] $command) {
    Invoke-Native $command | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" }
}

Write-Host "BSAudioDecode ($Config), target $Target" -ForegroundColor Cyan

if ($doWindows) {
    $vs = Find-VisualStudio
    $cmake = Find-CMake $vs
    $winBuild = Join-Path $root 'build\win-x64'
    if ($Clean -and (Test-Path $winBuild)) { Remove-Item -Recurse -Force $winBuild }
    Push-Location $root
    try {
        Invoke-Checked 'cmake configure (win-x64)' { & $cmake --preset win-x64 }
        # Quoted: PowerShell would otherwise split -v:m into "-v:" and "m".
        Invoke-Checked 'build (win-x64)' { & $cmake --build $winBuild --config $Config --target BSAudioDecode bsadtool -- '-v:m' '-nologo' }
    } finally {
        Pop-Location
    }
    $bin = Join-Path $winBuild $Config
    $dll = Join-Path $bin 'BSAudioDecode.dll'
    Add-Row 'win-x64' 'build' 'OK' 'BSAudioDecode, bsadtool'

    if ($Verify) {
        $dumpbin = Get-ChildItem (Join-Path $vs 'VC\Tools\MSVC') -Directory | Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'bin\Hostx64\x64\dumpbin.exe' } | Where-Object { Test-Path $_ } | Select-Object -First 1
        $imports = @((Invoke-Native { & $dumpbin -nologo -imports $dll }) | Where-Object { $_ -match '^    \S+\.dll\s*$' } |
            ForEach-Object { $_.Trim().ToLowerInvariant() } | Sort-Object -Unique)
        $crt = @($imports | Where-Object { $_ -match '^(vcruntime|msvcp|ucrtbase|api-ms-win-crt)' })
        Add-Row 'win-x64' 'no dynamic CRT' ($(if ($crt.Count -gt 0) { 'FAIL' } else { 'OK' })) ($crt -join ', ')
        $mf = @($imports | Where-Object { $_ -match '^(mf|mfplat|mfreadwrite|mfcore)\.dll$' })
        Add-Row 'win-x64' 'MF late-bound' ($(if ($mf.Count -gt 0) { 'FAIL' } else { 'OK' })) ($mf -join ', ')
        Add-Row 'win-x64' 'imports' 'INFO' ($imports -join ' ')
        $exports = @((Invoke-Native { & $dumpbin -nologo -exports $dll }) | ForEach-Object {
            if ($_ -match '^\s+\d+\s+[0-9A-Fa-f]+\s+[0-9A-Fa-f]+\s+(\S+)') { $Matches[1] } })
        Compare-Exports 'win-x64' $exports
    }

    if ($Test) {
        Invoke-Native { & (Join-Path $bin 'bsadtool.exe') caps } | Out-Host
        Add-Row 'win-x64' 'bsadtool caps' ($(if ($LASTEXITCODE -eq 0) { 'PASS' } else { 'FAIL' })) "exit $LASTEXITCODE"
    }

    if (-not $NoStage) {
        Stage-Binary 'win-x64' $dll (Join-Path $plugins 'x86_64\BSAudioDecode.dll')
    }
}

if ($doAndroid) {
    $project = $package
    while ($project -and -not (Test-Path (Join-Path $project 'ProjectSettings\ProjectVersion.txt'))) { $project = Split-Path $project -Parent }
    if (-not $project -and -not $env:BSAD_ANDROID_NDK) { throw 'No Unity project above this package: set BSAD_ANDROID_NDK.' }
    $unityVersion = (Select-String -Path (Join-Path $project 'ProjectSettings\ProjectVersion.txt') -Pattern '^m_EditorVersion:\s*(\S+)').Matches[0].Groups[1].Value
    $androidPlayer = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$unityVersion\Editor\Data\PlaybackEngines\AndroidPlayer"
    $ndk = if ($env:BSAD_ANDROID_NDK) { $env:BSAD_ANDROID_NDK } else { Join-Path $androidPlayer 'NDK' }
    if (-not (Test-Path (Join-Path $ndk 'build\cmake\android.toolchain.cmake'))) {
        throw "Android NDK not found at $ndk - install Android Build Support for Unity $unityVersion or set BSAD_ANDROID_NDK."
    }
    $cmakeBin = Join-Path $androidPlayer 'SDK\cmake\3.22.1\bin'
    $androidCmake = Join-Path $cmakeBin 'cmake.exe'
    $ninja = Join-Path $cmakeBin 'ninja.exe'
    if (-not (Test-Path $androidCmake) -or -not (Test-Path $ninja)) { throw "CMake/Ninja not found in $cmakeBin (Unity's Android SDK)." }
    $llvmBin = Join-Path $ndk 'toolchains\llvm\prebuilt\windows-x86_64\bin'

    $androidBuild = Join-Path $root 'build\android-arm64'
    if ($Clean -and (Test-Path $androidBuild)) { Remove-Item -Recurse -Force $androidBuild }
    $savedNdk = $env:BSAD_ANDROID_NDK
    $savedPath = $env:PATH
    $env:BSAD_ANDROID_NDK = $ndk
    $env:PATH = "$cmakeBin;$env:PATH"
    Push-Location $root
    try {
        # CMake writes a compiler path with spaces as its 8.3 short name (CLANG_~1.EXE); clang then
        # links as plain C and drops libc++. --driver-mode=g++ restores C++ linking.
        Invoke-Checked 'cmake configure (android-arm64)' {
            & $androidCmake --preset android-arm64 "-DCMAKE_MAKE_PROGRAM=$ninja" "-DCMAKE_BUILD_TYPE=$Config" `
                '-DCMAKE_SHARED_LINKER_FLAGS=--driver-mode=g++' '-DCMAKE_EXE_LINKER_FLAGS=--driver-mode=g++'
        }
        Invoke-Checked 'build (android-arm64)' { & $androidCmake --build $androidBuild --target BSAudioDecode bsadtool }
    } finally {
        Pop-Location
        $env:PATH = $savedPath
        $env:BSAD_ANDROID_NDK = $savedNdk
    }
    Add-Row 'android-arm64' 'build' 'OK' 'BSAudioDecode, bsadtool'
    $so = Join-Path $androidBuild 'libBSAudioDecode.so'
    $stripped = Join-Path $androidBuild 'stripped\libBSAudioDecode.so'
    New-Item -ItemType Directory -Force (Split-Path $stripped -Parent) | Out-Null
    Invoke-Checked 'llvm-strip' { & (Join-Path $llvmBin 'llvm-strip.exe') --strip-unneeded -o $stripped $so }

    if ($Verify) {
        $readelf = Join-Path $llvmBin 'llvm-readelf.exe'
        $allowed = @('libmediandk.so', 'liblog.so', 'libm.so', 'libdl.so', 'libc.so')
        $needed = @((Invoke-Native { & $readelf -d $stripped }) | ForEach-Object { if ($_ -match '\(NEEDED\).*\[(.+)\]') { $Matches[1] } })
        $unexpected = @($needed | Where-Object { $allowed -notcontains $_ })
        Add-Row 'android-arm64' 'NEEDED' ($(if ($unexpected.Count -gt 0) { 'FAIL' } else { 'OK' })) ($needed -join ' ')
        $aligns = @((Invoke-Native { & $readelf -l -W $stripped }) | ForEach-Object { if ($_ -match '^\s*LOAD\s.*\s(0x[0-9a-fA-F]+)\s*$') { $Matches[1] } })
        $bad = @($aligns | Where-Object { [Convert]::ToInt64($_, 16) -lt 0x4000 })
        Add-Row 'android-arm64' 'LOAD align' ($(if ($aligns.Count -eq 0 -or $bad.Count -gt 0) { 'FAIL' } else { 'OK' })) ($aligns -join ' ')
        $symbols = @((Invoke-Native { & $readelf --dyn-syms -W $stripped }) | ForEach-Object {
            $f = $_.Trim() -split '\s+'
            if ($f.Count -ge 8 -and $f[0] -match '^\d+:$' -and $f[6] -ne 'UND' -and ($f[4] -eq 'GLOBAL' -or $f[4] -eq 'WEAK') -and $f[5] -ne 'HIDDEN') {
                ($f[7] -split '@')[0]
            } })
        Compare-Exports 'android-arm64' $symbols
    }

    if ($Test) {
        $adb = Join-Path $androidPlayer 'SDK\platform-tools\adb.exe'
        $device = $null
        if (Test-Path $adb) { $device = (Invoke-Native { & $adb devices }) | Where-Object { $_ -match '\tdevice$' } | Select-Object -First 1 }
        if (-not $device) {
            Add-Row 'android-arm64' 'bsadtool caps (device)' 'SKIPPED' 'no adb device attached'
        } else {
            $tool = Join-Path $androidBuild 'bsadtool'
            Invoke-Native { & $adb push $tool /data/local/tmp/bsadtool } | Out-Host
            Invoke-Native { & $adb shell chmod 755 /data/local/tmp/bsadtool } | Out-Host
            Invoke-Native { & $adb shell /data/local/tmp/bsadtool caps } | Out-Host
            Add-Row 'android-arm64' 'bsadtool caps (device)' ($(if ($LASTEXITCODE -eq 0) { 'PASS' } else { 'FAIL' })) "exit $LASTEXITCODE"
        }
    }

    if (-not $NoStage) {
        Stage-Binary 'android-arm64' $stripped (Join-Path $plugins 'Android\arm64-v8a\libBSAudioDecode.so')
    }
}

Write-Host ''
$summary | Format-Table -AutoSize -Wrap | Out-String -Width 220 | Write-Host
if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAILED: $_" -ForegroundColor Red }
    throw "$($failures.Count) check(s) failed"
}
$stale = @($summary | Where-Object { $_.Result -eq 'STALE' })
if ($stale.Count -gt 0) {
    Write-Host 'Built OK, but a staged plugin is STALE (locked by Unity) - see above.' -ForegroundColor Yellow
} else {
    Write-Host 'OK' -ForegroundColor Green
}
