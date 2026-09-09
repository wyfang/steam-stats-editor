#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$ArtifactDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )
    Write-Host ('> {0} {1}' -f $FilePath, ($Arguments -join ' '))
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw ('Command failed with exit code {0}: {1}' -f $LASTEXITCODE, $FilePath)
    }
}

function Assert-File {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required file is missing: $Path"
    }
}

function Invoke-PackageCheck {
    param(
        [Parameter(Mandatory = $true)][string]$LauncherPath,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$OutputPath,
        [int]$ExpectedExitCode = 0,
        [switch]$LaunchFixture
    )
    $startParameters = @{
        FilePath = $LauncherPath
        WorkingDirectory = $WorkingDirectory
        PassThru = $true
        RedirectStandardOutput = "$OutputPath.stdout.txt"
        RedirectStandardError = "$OutputPath.stderr.txt"
    }
    if (-not $LaunchFixture) { $startParameters.ArgumentList = '--check-package' }
    $process = Start-Process @startParameters
    [void]$process.Handle
    if (-not $process.WaitForExit(10000)) {
        & taskkill.exe /PID $process.Id /T /F | Out-Null
        throw 'Launcher package check exceeded 10 seconds; its process tree was terminated.'
    }
    $process.WaitForExit()
    if ($process.ExitCode -ne $ExpectedExitCode) {
        throw "Launcher check returned $($process.ExitCode), expected $ExpectedExitCode. See $OutputPath.stderr.txt."
    }
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'This script requires Windows. It does not install build tools.'
}

$repositoryPath = Split-Path -Parent $PSScriptRoot
$dotnetPath = (Get-Command 'dotnet' -CommandType Application -ErrorAction Stop).Source
$msbuildPath = (Get-Command 'msbuild' -CommandType Application -ErrorAction Stop).Source
$installedSdks = @(& $dotnetPath --list-sdks)
if ($LASTEXITCODE -ne 0 -or -not ($installedSdks -match '^8\.')) {
    throw 'Install the .NET 8 SDK before running this script.'
}
$referenceRoot = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
if ([string]::IsNullOrEmpty($referenceRoot)) {
    throw 'The .NET Framework 4.8 targeting pack location could not be resolved.'
}
Assert-File (Join-Path $referenceRoot 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\mscorlib.dll')

foreach ($project in @(
    'SAM.sln',
    'SAM.Launcher/SAM.Launcher.csproj',
    'SAM.Batch.Tests/SAM.Batch.Tests.csproj',
    'tests/SAM.Submission.Tests/SAM.Submission.Tests.csproj',
    'SAM.UiChecks/SAM.UiChecks.csproj'
)) {
    Assert-File (Join-Path $repositoryPath $project)
}

[xml]$launcherProject = Get-Content -LiteralPath (Join-Path $repositoryPath 'SAM.Launcher/SAM.Launcher.csproj') -Raw
$versionNodes = @($launcherProject.SelectNodes('/Project/PropertyGroup/Version'))
if ($versionNodes.Count -ne 1) {
    throw 'The launcher project must declare exactly one package version.'
}
$packageVersion = $versionNodes[0].InnerText.Trim()
if ($packageVersion -cnotmatch '\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z') {
    throw "Package version must use major.minor.patch with no leading zeros: $packageVersion"
}
$archiveRootName = "steam-stats-editor-v$packageVersion-windows-x86"

if ([string]::IsNullOrWhiteSpace($ArtifactDirectory)) {
    $ArtifactDirectory = Join-Path $repositoryPath 'artifacts'
}
elseif (-not [IO.Path]::IsPathRooted($ArtifactDirectory)) {
    $ArtifactDirectory = Join-Path $repositoryPath $ArtifactDirectory
}
$ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
# Use a fresh directory without deleting or overwriting a previous run.
$runName = 'windows-{0}-{1}' -f [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'), [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runPath = Join-Path $ArtifactDirectory $runName
$uiArtifactPath = Join-Path $runPath 'ui'
$uiBuildPath = Join-Path $runPath 'ui-build'
$packagePath = Join-Path $runPath 'package'
$applicationPath = Join-Path $packagePath 'app'
$licensePath = Join-Path $packagePath 'licenses'
$packageCheckPath = Join-Path $runPath 'package-checks'
New-Item -ItemType Directory -Path $uiArtifactPath, $uiBuildPath, $applicationPath, $licensePath, $packageCheckPath | Out-Null

Push-Location $repositoryPath
try {
    # Both pure-library test runners must pass before building the Windows UI.
    Invoke-CheckedCommand $dotnetPath @('run', '--project', 'SAM.Batch.Tests/SAM.Batch.Tests.csproj', '--configuration', 'Release')
    Invoke-CheckedCommand $dotnetPath @('run', '--project', 'tests/SAM.Submission.Tests/SAM.Submission.Tests.csproj', '--configuration', 'Release')

    Invoke-CheckedCommand $msbuildPath @(
        'SAM.sln', '/restore', '/t:Rebuild', '/m',
        '/p:Configuration=Release', '/p:Platform=x86', '/verbosity:minimal'
    )
    Invoke-CheckedCommand $msbuildPath @(
        'SAM.UiChecks/SAM.UiChecks.csproj', '/restore', '/t:Rebuild',
        '/p:Configuration=Release', '/p:Platform=x86',
        "/p:OutputPath=$uiBuildPath/", '/p:AppendTargetFrameworkToOutputPath=false', '/verbosity:minimal'
    )

    # This checker renders synthetic preview data; it never connects to Steam.
    $uiExecutablePath = Join-Path $uiBuildPath 'SAM.UiChecks.exe'
    Assert-File $uiExecutablePath
    $uiProcess = Start-Process -FilePath $uiExecutablePath -ArgumentList ('"{0}"' -f $uiArtifactPath) `
        -WorkingDirectory $uiBuildPath -PassThru `
        -RedirectStandardOutput (Join-Path $uiArtifactPath 'stdout.txt') `
        -RedirectStandardError (Join-Path $uiArtifactPath 'stderr.txt')
    # Retain the process handle so a quick exit still has a readable exit code.
    [void]$uiProcess.Handle
    if (-not $uiProcess.WaitForExit(60000)) {
        & taskkill.exe /PID $uiProcess.Id /T /F | Out-Null
        throw 'Offline UI checks exceeded 60 seconds; their process tree was terminated.'
    }
    # Flush redirected streams after the bounded wait.
    $uiProcess.WaitForExit()
    if ($uiProcess.ExitCode -ne 0) {
        throw "Offline UI checks failed with exit code $($uiProcess.ExitCode). See $uiArtifactPath."
    }
    foreach ($screenshot in @(
        'preview-valid-1140.png', 'preview-valid-900.png',
        'preview-invalid-1140.png', 'preview-invalid-900.png',
        'picker-default.png', 'picker-minimum.png', 'picker-scale150-simulated.png',
        'manager-default.png', 'manager-minimum.png', 'manager-minimum-running.png',
        'manager-achievements-minimum.png', 'manager-scale150-simulated.png'
    )) {
        Assert-File (Join-Path $uiArtifactPath $screenshot)
    }

    $uploadPath = Join-Path $repositoryPath 'upload'
    $allowedDllNames = @(
        'SAM.API.dll', 'SAM.Batch.dll', 'SAM.Submission.dll',
        'System.Resources.Extensions.dll', 'System.Memory.dll', 'System.Buffers.dll',
        'System.Runtime.CompilerServices.Unsafe.dll', 'System.Numerics.Vectors.dll'
    )
    $launcherName = 'SteamStatsEditor.exe'
    Assert-File (Join-Path $uploadPath $launcherName)
    $applicationNames = @('SAM.Picker.exe', 'SAM.Game.exe', 'SAM.Picker.exe.config', 'SAM.Game.exe.config')
    $requiredApplicationNames = @($applicationNames + $allowedDllNames)
    foreach ($name in $requiredApplicationNames) {
        Assert-File (Join-Path $uploadPath $name)
    }
    foreach ($name in @('SAM.Picker.exe.config', 'SAM.Game.exe.config')) {
        [xml]$configuration = Get-Content -LiteralPath (Join-Path $uploadPath $name) -Raw
        if ($configuration.configuration.startup.supportedRuntime.sku -ne '.NETFramework,Version=v4.8') {
            throw "Packaged runtime requirement must match .NET Framework 4.8: $name"
        }
    }

    # Cross-check NuGet's resolved net48 runtime dependencies against the explicit
    # DLL allowlist. An added dependency must be reviewed before it can be shipped.
    $notices = New-Object System.Text.StringBuilder
    [void]$notices.AppendLine('Third-party runtime notices, preserved from the resolved NuGet packages.')
    $noticedPackages = @{}
    foreach ($project in @('SAM.Game', 'SAM.Picker')) {
        $assetsPath = Join-Path $repositoryPath "$project/obj/project.assets.json"
        Assert-File $assetsPath
        $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
        $target = $assets.targets.PSObject.Properties['.NETFramework,Version=v4.8']
        if ($null -eq $target) {
            throw "No net48 restore target found in $assetsPath."
        }
        foreach ($library in $target.Value.PSObject.Properties) {
            $runtime = $library.Value.PSObject.Properties['runtime']
            if ($null -eq $runtime) { continue }
            foreach ($asset in $runtime.Value.PSObject.Properties) {
                $name = [IO.Path]::GetFileName($asset.Name)
                if ([IO.Path]::GetExtension($name) -ne '.dll') { continue }
                if ($allowedDllNames -cnotcontains $name) {
                    throw "Runtime dependency needs explicit packaging review: $name"
                }
                Assert-File (Join-Path $uploadPath $name)
            }
            if ($library.Value.type -eq 'package' -and -not $noticedPackages.ContainsKey($library.Name)) {
                $packageInfo = $assets.libraries.PSObject.Properties[$library.Name].Value
                $packageDirectory = $null
                foreach ($folder in $assets.packageFolders.PSObject.Properties) {
                    $candidate = Join-Path $folder.Name $packageInfo.path
                    if (Test-Path -LiteralPath $candidate -PathType Container) { $packageDirectory = $candidate; break }
                }
                if ($null -eq $packageDirectory) { throw "Resolved package directory is missing: $($library.Name)" }
                [void]$notices.AppendLine("`r`n=== $($library.Name) ===")
                foreach ($legalName in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
                    $legalPath = Join-Path $packageDirectory $legalName
                    Assert-File $legalPath
                    [void]$notices.AppendLine("`r`n--- $legalName ---")
                    [void]$notices.AppendLine([IO.File]::ReadAllText($legalPath))
                }
                $noticedPackages[$library.Name] = $true
            }
        }
    }
    [IO.File]::WriteAllText((Join-Path $licensePath 'ThirdPartyNotices.txt'), $notices.ToString(), (New-Object System.Text.UTF8Encoding($false)))

    $dllFiles = @(Get-ChildItem -LiteralPath $uploadPath -File -Filter '*.dll')
    foreach ($dll in $dllFiles) {
        if ($allowedDllNames -cnotcontains $dll.Name) {
            throw "Unexpected DLL in upload; review it before packaging: $($dll.Name)"
        }
    }
    Copy-Item -LiteralPath (Join-Path $uploadPath $launcherName) -Destination $packagePath
    foreach ($name in $requiredApplicationNames) {
        $sourcePath = Join-Path $uploadPath $name
        Copy-Item -LiteralPath $sourcePath -Destination $applicationPath
    }
    foreach ($name in @('LICENSE.txt', 'NOTICE.md')) {
        $sourcePath = Join-Path $repositoryPath $name
        Assert-File $sourcePath
        Copy-Item -LiteralPath $sourcePath -Destination $licensePath
    }

    # Every shipped entry is explicit. Dependencies stay beside the original executables;
    # the application folder has only the launcher and the app/ and licenses/ directories.
    $packageRelativePaths = @($launcherName) +
        @($requiredApplicationNames | ForEach-Object { "app/$_" }) +
        @('licenses/LICENSE.txt', 'licenses/NOTICE.md', 'licenses/ThirdPartyNotices.txt')
    $actualRelativePaths = @()
    foreach ($directory in @('', 'app', 'licenses')) {
        $directoryPath = if ($directory -eq '') { $packagePath } else { Join-Path $packagePath $directory }
        foreach ($entry in @(Get-ChildItem -LiteralPath $directoryPath -Force)) {
            if ($entry.PSIsContainer) {
                if ($directory -ne '' -or @('app', 'licenses') -cnotcontains $entry.Name) {
                    throw "Unexpected package directory: $($entry.FullName)"
                }
            }
            else {
                $actualRelativePaths += if ($directory -eq '') { $entry.Name } else { "$directory/$($entry.Name)" }
            }
        }
    }
    if (@(Compare-Object -ReferenceObject $packageRelativePaths -DifferenceObject $actualRelativePaths -CaseSensitive).Count -ne 0) {
        throw 'Package contents differ from the explicit file allowlist.'
    }

    # A single versioned directory prevents extraction from scattering files into
    # the destination. Keep the outer ZIP filename stable for the CI artifact path.
    $archiveRelativePaths = @($packageRelativePaths | ForEach-Object { "$archiveRootName/$_" })
    $zipPath = Join-Path $runPath 'steam-stats-editor-windows.zip'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($relativePath in $packageRelativePaths) {
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, (Join-Path $packagePath $relativePath), "$archiveRootName/$relativePath", [IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally {
        $archive.Dispose()
    }
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $zipNames = @($archive.Entries | ForEach-Object { $_.FullName })
        if (@(Compare-Object -ReferenceObject $archiveRelativePaths -DifferenceObject $zipNames -CaseSensitive).Count -ne 0) {
            throw 'Archive entries differ from the reviewed package file list.'
        }
    }
    finally {
        $archive.Dispose()
    }

    # Exercise the actual ZIP after extraction into a path with spaces and Unicode.
    # The calling working directory is deliberately outside the application bundle.
    $unpackedPath = Join-Path $runPath '解压 检查'
    [IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $unpackedPath)
    $unpackedApplicationPath = Join-Path $unpackedPath $archiveRootName
    $checkOutputPath = Join-Path $packageCheckPath 'complete'
    Invoke-PackageCheck -LauncherPath (Join-Path $unpackedApplicationPath $launcherName) -WorkingDirectory $runPath -OutputPath $checkOutputPath
    $diagnostic = @(Get-Content -LiteralPath "$checkOutputPath.stdout.txt" -Encoding UTF8)
    $expectedChild = Join-Path $unpackedApplicationPath 'app\SAM.Picker.exe'
    $expectedWorkingDirectory = Join-Path $unpackedApplicationPath 'app'
    if ($diagnostic -cnotcontains "Executable: $expectedChild" -or $diagnostic -cnotcontains "WorkingDirectory: $expectedWorkingDirectory") {
        throw 'Launcher did not resolve its child and working directory relative to its own executable.'
    }

    # Compile a harmless .NET Framework child in a separate bundle. Launch the real
    # bootstrapper against this fixture to check Process.Start, including its cwd.
    # No real Steam executable is launched by these package checks.
    $fixturePath = Join-Path $runPath 'launcher fixture 中文'
    $fixtureAppPath = Join-Path $fixturePath 'app'
    New-Item -ItemType Directory -Path $fixtureAppPath | Out-Null
    Copy-Item -LiteralPath (Join-Path $packagePath $launcherName) -Destination $fixturePath
    foreach ($name in $requiredApplicationNames) {
        Copy-Item -LiteralPath (Join-Path $applicationPath $name) -Destination $fixtureAppPath
    }
    $fixtureSource = Join-Path $runPath 'LauncherFixture.cs'
    $fixtureResultPath = Join-Path $packageCheckPath 'child-result.txt'
    $fixtureCode = @'
using System;
using System.IO;
using System.Reflection;
internal static class LauncherFixture
{
    private static int Main()
    {
        string result = Environment.GetEnvironmentVariable("STEAM_STATS_EDITOR_SMOKE_RESULT");
        File.WriteAllLines(result + ".tmp",
            new[] { Assembly.GetExecutingAssembly().Location, Environment.CurrentDirectory });
        File.Move(result + ".tmp", result);
        return 0;
    }
}
'@
    [IO.File]::WriteAllText($fixtureSource, $fixtureCode, (New-Object System.Text.UTF8Encoding($false)))
    $compilerPath = Join-Path ([Environment]::GetEnvironmentVariable('WINDIR')) 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
    Assert-File $compilerPath
    Invoke-CheckedCommand $compilerPath @('/nologo', '/target:winexe', '/platform:x86', "/out:$(Join-Path $fixtureAppPath 'SAM.Picker.exe')", $fixtureSource)
    $previousFixtureResult = [Environment]::GetEnvironmentVariable('STEAM_STATS_EDITOR_SMOKE_RESULT')
    try {
        [Environment]::SetEnvironmentVariable('STEAM_STATS_EDITOR_SMOKE_RESULT', $fixtureResultPath)
        Invoke-PackageCheck -LauncherPath (Join-Path $fixturePath $launcherName) -WorkingDirectory $runPath `
            -OutputPath (Join-Path $packageCheckPath 'launch') -LaunchFixture
        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        while (-not (Test-Path -LiteralPath $fixtureResultPath -PathType Leaf) -and [DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 100
        }
        Assert-File $fixtureResultPath
        $childResult = @(Get-Content -LiteralPath $fixtureResultPath -Encoding UTF8)
        if ($childResult.Count -ne 2 -or $childResult[0] -cne (Join-Path $fixtureAppPath 'SAM.Picker.exe') -or $childResult[1] -cne $fixtureAppPath) {
            throw 'Launcher fixture started with the wrong executable or working directory.'
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable('STEAM_STATS_EDITOR_SMOKE_RESULT', $previousFixtureResult)
    }

    # A missing dependency must be reported instead of launching a broken program.
    Remove-Item -LiteralPath (Join-Path $fixtureAppPath 'SAM.Batch.dll')
    Invoke-PackageCheck -LauncherPath (Join-Path $fixturePath $launcherName) -WorkingDirectory $runPath `
        -OutputPath (Join-Path $packageCheckPath 'incomplete') -ExpectedExitCode 2
    [IO.File]::WriteAllLines((Join-Path $packageCheckPath 'package-files.txt'), $archiveRelativePaths)
    Write-Host "Package: $zipPath"
    Write-Host "Launcher checks: $packageCheckPath"
    Write-Host "Offline UI screenshots: $uiArtifactPath"
    Write-Host 'Offline checks do not establish compatibility or successful writes with a real Steam account.'
}
finally {
    Pop-Location
}
