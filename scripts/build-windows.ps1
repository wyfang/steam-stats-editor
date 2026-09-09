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
    'SAM.Batch.Tests/SAM.Batch.Tests.csproj',
    'tests/SAM.Submission.Tests/SAM.Submission.Tests.csproj',
    'SAM.UiChecks/SAM.UiChecks.csproj'
)) {
    Assert-File (Join-Path $repositoryPath $project)
}

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
New-Item -ItemType Directory -Path $uiArtifactPath, $uiBuildPath, $packagePath | Out-Null

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
        'preview-invalid-1140.png', 'preview-invalid-900.png'
    )) {
        Assert-File (Join-Path $uiArtifactPath $screenshot)
    }

    $uploadPath = Join-Path $repositoryPath 'upload'
    $allowedDllNames = @(
        'SAM.API.dll', 'SAM.Batch.dll', 'SAM.Submission.dll',
        'System.Resources.Extensions.dll', 'System.Memory.dll', 'System.Buffers.dll',
        'System.Runtime.CompilerServices.Unsafe.dll', 'System.Numerics.Vectors.dll'
    )
    $requiredApplicationNames = @(
        'SAM.Picker.exe', 'SAM.Game.exe', 'SAM.Game.exe.config',
        'SAM.API.dll', 'SAM.Batch.dll', 'SAM.Submission.dll'
    )
    foreach ($name in $requiredApplicationNames) {
        Assert-File (Join-Path $uploadPath $name)
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
    [IO.File]::WriteAllText((Join-Path $packagePath 'ThirdPartyNotices.txt'), $notices.ToString(), (New-Object System.Text.UTF8Encoding($false)))

    $dllFiles = @(Get-ChildItem -LiteralPath $uploadPath -File -Filter '*.dll')
    foreach ($dll in $dllFiles) {
        if ($allowedDllNames -cnotcontains $dll.Name) {
            throw "Unexpected DLL in upload; review it before packaging: $($dll.Name)"
        }
    }
    $applicationNames = @('SAM.Picker.exe', 'SAM.Game.exe', 'SAM.Picker.exe.config', 'SAM.Game.exe.config')
    foreach ($name in ($applicationNames + $allowedDllNames)) {
        $sourcePath = Join-Path $uploadPath $name
        if (Test-Path -LiteralPath $sourcePath -PathType Leaf) {
            Copy-Item -LiteralPath $sourcePath -Destination $packagePath
        }
    }
    foreach ($name in @('LICENSE.txt', 'NOTICE.md', 'README.md', 'README.en.md')) {
        $sourcePath = Join-Path $repositoryPath $name
        Assert-File $sourcePath
        Copy-Item -LiteralPath $sourcePath -Destination $packagePath
    }

    $packageFiles = @(Get-ChildItem -LiteralPath $packagePath -File)
    $zipPath = Join-Path $runPath 'steam-stats-editor-windows.zip'
    Compress-Archive -LiteralPath $packageFiles.FullName -DestinationPath $zipPath -CompressionLevel Optimal
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $zipNames = @($archive.Entries | ForEach-Object { $_.FullName })
        if (@(Compare-Object -ReferenceObject $packageFiles.Name -DifferenceObject $zipNames -CaseSensitive).Count -ne 0) {
            throw 'Archive entries differ from the reviewed package file list.'
        }
    }
    finally {
        $archive.Dispose()
    }
    Write-Host "Package: $zipPath"
    Write-Host "Offline UI screenshots: $uiArtifactPath"
    Write-Host 'Offline checks do not establish compatibility or successful writes with a real Steam account.'
}
finally {
    Pop-Location
}
