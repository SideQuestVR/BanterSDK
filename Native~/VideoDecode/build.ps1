<#
.SYNOPSIS
    Builds BSVideoDecode for Windows x64 and Android arm64 and stages it into the package.

.DESCRIPTION
    Both targets compile against the Unity plugin headers (Data\PluginAPI) of the Unity Editor named in the project's
    ProjectSettings\ProjectVersion.txt; set BSVD_UNITY_PLUGIN_API to use another folder.

    win-x64 needs Visual Studio 2022 (Build Tools is enough) with the C++ x64 toolset and a Windows SDK (fxc compiles the
    flip shader), and CMake (on PATH, or the one Visual Studio bundles). android-arm64 uses the NDK, CMake and Ninja that
    ship with that Unity Editor (Android Build Support); set BSVD_ANDROID_NDK to use another NDK.

    Outputs (committed, with their .meta files):
      Runtime\Plugins\VideoDecode\x86_64\BSVideoDecode.dll                Editor x64 + Standalone Win64
      Runtime\Plugins\VideoDecode\Android\arm64-v8a\libBSVideoDecode.so   Android ARM64 (stripped)

    The Unity Editor keeps BSVideoDecode.dll open from its first use until it exits. If staging reports the DLL as
    STALE, close Unity and run this again.

.PARAMETER Target
    win-x64, android-arm64 or all (default).

.PARAMETER Test
    Windows: bsvdtool caps, then, with fixtures, bsvdtool all (every check, on the linked sources and on the built DLL,
    with a stand-in for Unity's D3D11 device). Android: tools\run-android.sh when it exists.

.PARAMETER Fixtures
    The folder of test clips for -Test on both targets (default: $env:BSVD_FIXTURES). Make them with ffmpeg on PATH:
    python tools\bsvdtool\win_fixtures.py <folder> (counter, crop, SD, 10-bit) and python spike\fixtures.py <folder>
    (the bars clips).

.PARAMETER Adapter
    -Test on Windows: the graphics adapter standing in for Unity's (part of its name); default: the default adapter.

.PARAMETER Verify
    Dependency and export checks on the built binaries.
      DLL: no dynamic CRT, no static import of Media Foundation, exports == BSVD_* of the header + UnityPluginLoad/Unload.
      .so: NEEDED within what src\android\platform.cmake links (+ libc, libm, libdl), LOAD segments 16 KB aligned,
           dynamic symbols == BSVD_* of the header + UnityPluginLoad/Unload.

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
    [switch] $NoStage,
    [string] $Fixtures = $env:BSVD_FIXTURES,
    [string] $Adapter = ''
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$package = Split-Path (Split-Path $root -Parent) -Parent
$plugins = Join-Path $package 'Runtime\Plugins\VideoDecode'
$doWindows = $Target -eq 'win-x64' -or $Target -eq 'all'
$doAndroid = $Target -eq 'android-arm64' -or $Target -eq 'all'

$summary = New-Object System.Collections.Generic.List[object]
$failures = New-Object System.Collections.Generic.List[string]

function Add-Row([string] $target, [string] $item, [string] $result, [string] $detail) {
    $summary.Add([pscustomobject]@{ Target = $target; Item = $item; Result = $result; Detail = $detail })
    if ($result -eq 'FAIL') { $failures.Add("$target $item - $detail") }
}

# The exported names: every BSVD_* function of the header, and Unity's plugin entry points.
$header = Get-Content (Join-Path $root 'include\bsvideodecode.h') -Raw
$expectedExports = @(@([regex]::Matches($header, 'BSVD_API\s+[^;(]*?BSVD_CALL\s+(BSVD_\w+)\s*\(') |
    ForEach-Object { $_.Groups[1].Value }) + @('UnityPluginLoad', 'UnityPluginUnload') | Sort-Object -Unique)

function Compare-Exports([string] $target, [string[]] $actual) {
    $actual = @($actual | Sort-Object -Unique)
    $missing = @($expectedExports | Where-Object { $actual -notcontains $_ })
    $extra = @($actual | Where-Object { $expectedExports -notcontains $_ })
    if ($missing.Count -eq 0 -and $extra.Count -eq 0) {
        Add-Row $target 'exports' 'OK' "$($actual.Count): the header's BSVD_* + UnityPluginLoad/Unload, nothing else"
    } else {
        Add-Row $target 'exports' 'FAIL' ("missing: " + ($missing -join ',') + " unexpected: " + ($extra -join ','))
    }
}

# The Unity project above this package, and its editor's version.
$project = $package
while ($project -and -not (Test-Path (Join-Path $project 'ProjectSettings\ProjectVersion.txt'))) { $project = Split-Path $project -Parent }
$unityVersion = $null
if ($project) {
    $unityVersion = (Select-String -Path (Join-Path $project 'ProjectSettings\ProjectVersion.txt') -Pattern '^m_EditorVersion:\s*(\S+)').Matches[0].Groups[1].Value
}
$editorData = if ($unityVersion) { Join-Path $env:ProgramFiles "Unity\Hub\Editor\$unityVersion\Editor\Data" } else { $null }
$pluginApi = if ($env:BSVD_UNITY_PLUGIN_API) { $env:BSVD_UNITY_PLUGIN_API } elseif ($editorData) { Join-Path $editorData 'PluginAPI' } else { $null }
if (-not $pluginApi -or -not (Test-Path (Join-Path $pluginApi 'IUnityGraphics.h'))) {
    throw "Unity's plugin headers not found ($pluginApi): install Unity $unityVersion or set BSVD_UNITY_PLUGIN_API."
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

# The DLL's importer settings (Editor x86_64 Windows and Standalone Win64 only): BSAudioDecode.dll.meta's, with a guid of
# its own. Written once, so the guid stays.
function Write-DllMeta([string] $path) {
    if (Test-Path $path) { return }
    $model = Join-Path $package 'Runtime\Plugins\AudioDecode\x86_64\BSAudioDecode.dll.meta'
    $guid = [guid]::NewGuid().ToString('N')
    $text = [System.IO.File]::ReadAllText($model) -replace '(?m)^guid: [0-9a-f]{32}', "guid: $guid"
    [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($false)))
    Add-Row 'win-x64' 'meta' 'OK' "wrote $(Split-Path $path -Leaf) (guid $guid)"
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

Write-Host "BSVideoDecode ($Config), target $Target, Unity plugin API $pluginApi" -ForegroundColor Cyan
$savedApi = $env:BSVD_UNITY_PLUGIN_API
$env:BSVD_UNITY_PLUGIN_API = $pluginApi

try {
if ($doWindows) {
    $vs = Find-VisualStudio
    $cmake = Find-CMake $vs
    $winBuild = Join-Path $root 'build\win-x64'
    if ($Clean -and (Test-Path $winBuild)) { Remove-Item -Recurse -Force $winBuild }
    Push-Location $root
    try {
        Invoke-Checked 'cmake configure (win-x64)' { & $cmake --preset win-x64 }
        # Quoted: PowerShell would otherwise split -v:m into "-v:" and "m".
        Invoke-Checked 'build (win-x64)' { & $cmake --build $winBuild --config $Config --target BSVideoDecode bsvdtool -- '-v:m' '-nologo' }
    } finally {
        Pop-Location
    }
    $bin = Join-Path $winBuild $Config
    $dll = Join-Path $bin 'BSVideoDecode.dll'
    $tool = Join-Path $bin 'bsvdtool.exe'
    Add-Row 'win-x64' 'build' 'OK' 'BSVideoDecode, bsvdtool'

    if ($Verify) {
        $dumpbin = Get-ChildItem (Join-Path $vs 'VC\Tools\MSVC') -Directory | Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'bin\Hostx64\x64\dumpbin.exe' } | Where-Object { Test-Path $_ } | Select-Object -First 1
        $imports = @((Invoke-Native { & $dumpbin -nologo -imports $dll }) | Where-Object { $_ -match '^    \S+\.dll\s*$' } |
            ForEach-Object { $_.Trim().ToLowerInvariant() } | Sort-Object -Unique)
        $crt = @($imports | Where-Object { $_ -match '^(vcruntime|msvcp|ucrtbase|api-ms-win-crt)' })
        Add-Row 'win-x64' 'no dynamic CRT' ($(if ($crt.Count -gt 0) { 'FAIL' } else { 'OK' })) ($crt -join ', ')
        $mf = @($imports | Where-Object { $_ -match '^(mf|mfplat|mfreadwrite|mfcore|evr)\.dll$' })
        Add-Row 'win-x64' 'MF late-bound' ($(if ($mf.Count -gt 0) { 'FAIL' } else { 'OK' })) ($mf -join ', ')
        Add-Row 'win-x64' 'imports' 'INFO' ($imports -join ' ')
        $exports = @((Invoke-Native { & $dumpbin -nologo -exports $dll }) | ForEach-Object {
            if ($_ -match '^\s+\d+\s+[0-9A-Fa-f]+\s+[0-9A-Fa-f]+\s+(\S+)') { $Matches[1] } })
        Compare-Exports 'win-x64' $exports
    }

    if ($Test) {
        Invoke-Native { & $tool caps } | Out-Host
        Add-Row 'win-x64' 'bsvdtool caps' ($(if ($LASTEXITCODE -eq 0) { 'PASS' } else { 'FAIL' })) "exit $LASTEXITCODE"
        if ($Fixtures -and (Test-Path (Join-Path $Fixtures 'o1080.h264'))) {
            $toolArgs = @('all', $Fixtures, '--dll', $dll)
            if ($Adapter) { $toolArgs += @('--adapter', $Adapter) }
            $output = @(Invoke-Native { & $tool @toolArgs })
            $output | Out-Host
            $exit = $LASTEXITCODE
            $count = ($output | Select-String -Pattern '^(\d+) checks, (\d+) failed' | Select-Object -Last 1)
            $detail = if ($count) { $count.Line } else { "exit $exit" }
            Add-Row 'win-x64' 'bsvdtool all' ($(if ($exit -eq 0) { 'PASS' } else { 'FAIL' })) $detail
        } else {
            Add-Row 'win-x64' 'bsvdtool all' 'SKIPPED' 'no fixtures: python tools\bsvdtool\win_fixtures.py <dir>, then -Fixtures <dir>'
        }
    }

    if (-not $NoStage) {
        Write-DllMeta (Join-Path $plugins 'x86_64\BSVideoDecode.dll.meta')
        Stage-Binary 'win-x64' $dll (Join-Path $plugins 'x86_64\BSVideoDecode.dll')
    }
}

if ($doAndroid) {
    if (-not (Test-Path (Join-Path $root 'src\android\platform.cmake'))) {
        Add-Row 'android-arm64' 'build' 'FAIL' 'src\android\platform.cmake does not exist yet'
    } else {
        if (-not $editorData -and -not $env:BSVD_ANDROID_NDK) { throw 'No Unity project above this package: set BSVD_ANDROID_NDK.' }
        $androidPlayer = if ($editorData) { Join-Path $editorData 'PlaybackEngines\AndroidPlayer' } else { $null }
        $ndk = if ($env:BSVD_ANDROID_NDK) { $env:BSVD_ANDROID_NDK } else { Join-Path $androidPlayer 'NDK' }
        if (-not (Test-Path (Join-Path $ndk 'build\cmake\android.toolchain.cmake'))) {
            throw "Android NDK not found at $ndk - install Android Build Support for Unity $unityVersion or set BSVD_ANDROID_NDK."
        }
        $cmakeBin = Join-Path $androidPlayer 'SDK\cmake\3.22.1\bin'
        $androidCmake = Join-Path $cmakeBin 'cmake.exe'
        $ninja = Join-Path $cmakeBin 'ninja.exe'
        if (-not (Test-Path $androidCmake) -or -not (Test-Path $ninja)) { throw "CMake/Ninja not found in $cmakeBin (Unity's Android SDK)." }
        $llvmBin = Join-Path $ndk 'toolchains\llvm\prebuilt\windows-x86_64\bin'

        $androidBuild = Join-Path $root 'build\android-arm64'
        if ($Clean -and (Test-Path $androidBuild)) { Remove-Item -Recurse -Force $androidBuild }
        $savedNdk = $env:BSVD_ANDROID_NDK
        $savedPath = $env:PATH
        $env:BSVD_ANDROID_NDK = $ndk
        $env:PATH = "$cmakeBin;$env:PATH"
        $hasTool = $false
        Push-Location $root
        try {
            # src\android\platform.cmake forces clang's C++ driver mode itself (CMake names clang++ by its 8.3 short name).
            Invoke-Checked 'cmake configure (android-arm64)' {
                & $androidCmake --preset android-arm64 "-DCMAKE_MAKE_PROGRAM=$ninja" "-DCMAKE_BUILD_TYPE=$Config"
            }
            $hasTool = [bool](Select-String -Path (Join-Path $androidBuild 'build.ninja') -Pattern '^build bsvdtool[: ]' -Quiet)
            $targets = @('BSVideoDecode') + $(if ($hasTool) { @('bsvdtool') } else { @() })
            Invoke-Checked 'build (android-arm64)' { & $androidCmake --build $androidBuild --target @targets }
        } finally {
            Pop-Location
            $env:PATH = $savedPath
            $env:BSVD_ANDROID_NDK = $savedNdk
        }
        Add-Row 'android-arm64' 'build' 'OK' ('BSVideoDecode' + $(if ($hasTool) { ', bsvdtool' } else { ' (no bsvdtool sources yet)' }))
        $so = Join-Path $androidBuild 'libBSVideoDecode.so'
        $stripped = Join-Path $androidBuild 'stripped\libBSVideoDecode.so'
        New-Item -ItemType Directory -Force (Split-Path $stripped -Parent) | Out-Null
        Invoke-Checked 'llvm-strip' { & (Join-Path $llvmBin 'llvm-strip.exe') --strip-unneeded -o $stripped $so }

        if ($Verify) {
            $readelf = Join-Path $llvmBin 'llvm-readelf.exe'
            # What src\android\platform.cmake links, plus the C runtime every NDK library needs.
            $platformCmake = Get-Content (Join-Path $root 'src\android\platform.cmake') -Raw
            $linked = @([regex]::Matches($platformCmake, '(?:list\s*\(\s*APPEND|set\s*\()\s*BSVD_PLATFORM_LIBRARIES\s+([^)]*)\)') |
                ForEach-Object { $_.Groups[1].Value -split '\s+' } | Where-Object { $_ -and $_ -notmatch '^\$|^#' } |
                ForEach-Object { 'lib' + ($_.Trim('"') -replace '^-l', '') + '.so' })
            $allowed = @($linked + @('libc.so', 'libm.so', 'libdl.so') | Sort-Object -Unique)
            $needed = @((Invoke-Native { & $readelf -d $stripped }) | ForEach-Object { if ($_ -match '\(NEEDED\).*\[(.+)\]') { $Matches[1] } })
            $unexpected = @($needed | Where-Object { $allowed -notcontains $_ })
            Add-Row 'android-arm64' 'NEEDED' ($(if ($unexpected.Count -gt 0) { 'FAIL' } else { 'OK' })) (($needed -join ' ') + " (allowed: $($allowed -join ' '))")
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
            $runner = Join-Path $root 'tools\run-android.sh'
            if (-not (Test-Path $runner)) {
                Add-Row 'android-arm64' 'device test' 'SKIPPED' 'tools\run-android.sh does not exist yet'
            } else {
                # Git's bash, never WSL's bash.exe in System32. git can resolve to Git\cmd\git.exe or, from Git Bash, to
                # Git\mingw64\bin\git.exe: look for bin\bash.exe in the folders above it, then in the usual install.
                $git = (Get-Command git -ErrorAction SilentlyContinue).Source
                $bash = $null
                $dir = if ($git) { Split-Path $git -Parent } else { $null }
                for ($i = 0; $dir -and $i -lt 4 -and -not $bash; $i++) {
                    $candidate = Join-Path $dir 'bin\bash.exe'
                    if (Test-Path $candidate) { $bash = $candidate }
                    $dir = Split-Path $dir -Parent
                }
                if (-not $bash) {
                    $candidate = Join-Path $env:ProgramFiles 'Git\bin\bash.exe'
                    if (Test-Path $candidate) { $bash = $candidate }
                }
                if (-not $bash -or -not (Test-Path $bash)) {
                    Add-Row 'android-arm64' 'device test' 'FAIL' "Git's bash.exe not found to run tools\run-android.sh"
                } else {
                    $env:BSVD_ANDROID_NDK = $ndk
                    $env:BSVD_ANDROID_BUILD = $androidBuild
                    $env:BSVD_ADB = Join-Path $androidPlayer 'SDK\platform-tools\adb.exe'
                    try {
                        # The fixture folder as its argument (it also reads BSVD_FIXTURES, which -Fixtures may not come from).
                        $runnerArgs = @($runner) + $(if ($Fixtures) { @($Fixtures) } else { @() })
                        Invoke-Native { & $bash @runnerArgs } | Out-Host
                        $exit = $LASTEXITCODE
                    } finally {
                        $env:BSVD_ANDROID_NDK = $savedNdk
                        Remove-Item Env:BSVD_ANDROID_BUILD, Env:BSVD_ADB -ErrorAction SilentlyContinue
                    }
                    Add-Row 'android-arm64' 'device test' ($(if ($exit -eq 0) { 'PASS' } else { 'FAIL' })) "tools\run-android.sh exit $exit"
                }
            }
        }

        if (-not $NoStage) {
            Stage-Binary 'android-arm64' $stripped (Join-Path $plugins 'Android\arm64-v8a\libBSVideoDecode.so')
        }
    }
}
} finally {
    $env:BSVD_UNITY_PLUGIN_API = $savedApi
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
