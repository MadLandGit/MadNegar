$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$projectPath = Join-Path $root 'MadNegar.csproj'
$issPath = Join-Path $root 'installer\MadNegar.iss'
$publishDir = Join-Path $root 'publish'
$outputDir = Join-Path $root 'output'
$stageRoot = Join-Path $root ('.release-staging\installer-' + [Guid]::NewGuid().ToString('N'))
$success = $false

function Get-ProjectVersion {
    param([string]$Path)
    [xml]$xml = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    $node = $xml.SelectSingleNode('/Project/PropertyGroup/Version')
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) { throw 'MadNegar.csproj must define a non-empty PropertyGroup/Version.' }
    $version = $node.InnerText.Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') { throw "Project version '$version' is not a valid release version." }
    return $version
}

function Assert-DotNet10 {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnet) { throw 'The .NET SDK was not found. Install the .NET 10 SDK and retry.' }
    $sdks = & $dotnet.Source --list-sdks
    $sdkExit = $LASTEXITCODE
    if ($sdkExit -ne 0) { throw "dotnet --list-sdks failed with exit code $sdkExit." }
    if (-not ($sdks | Where-Object { $_ -match '^\s*10\.' })) { throw 'The .NET 10 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/10.0.' }
    return $dotnet.Source
}

function Find-InnoCompiler {
    # Inno Setup is not guaranteed to add ISCC.exe to PATH. Winget/user-scope
    # installs commonly live under %LOCALAPPDATA%\Programs, so search both
    # machine-wide and per-user locations, plus registration metadata.
    $candidates = New-Object 'System.Collections.Generic.List[string]'
    $searchRoots = New-Object 'System.Collections.Generic.List[string]'

    if (-not [string]::IsNullOrWhiteSpace($env:ISCC_PATH)) {
        $explicit = [Environment]::ExpandEnvironmentVariables($env:ISCC_PATH.Trim().Trim('"'))
        if (Test-Path -LiteralPath $explicit -PathType Container) {
            $candidates.Add((Join-Path $explicit 'ISCC.exe'))
        }
        else {
            $candidates.Add($explicit)
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($env:INNO_SETUP_HOME)) {
        $home = [Environment]::ExpandEnvironmentVariables($env:INNO_SETUP_HOME.Trim().Trim('"'))
        $candidates.Add((Join-Path $home 'ISCC.exe'))
    }

    foreach ($commandName in @('ISCC.exe', 'ISCC')) {
        $command = Get-Command $commandName -ErrorAction SilentlyContinue
        if ($null -ne $command -and $command.Source) { $candidates.Add([string]$command.Source) }
    }

    foreach ($rootPath in @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:ProgramW6432)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$rootPath) -and -not $searchRoots.Contains([string]$rootPath)) {
            $searchRoots.Add([string]$rootPath)
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $userPrograms = Join-Path $env:LOCALAPPDATA 'Programs'
        if (-not $searchRoots.Contains($userPrograms)) { $searchRoots.Add($userPrograms) }
    }

    foreach ($parent in $searchRoots) {
        foreach ($versionName in @('Inno Setup 7', 'Inno Setup 7 (x64)', 'Inno Setup 6', 'Inno Setup 6 (x64)')) {
            $candidates.Add((Join-Path (Join-Path $parent $versionName) 'ISCC.exe'))
        }
        # Include nonstandard suffixes such as architecture/version labels and
        # installs nested under a vendor folder, but only scan one directory level.
        if (Test-Path -LiteralPath $parent -PathType Container) {
            $childDirectories = @(Get-ChildItem -LiteralPath $parent -Directory -ErrorAction SilentlyContinue)
            foreach ($child in $childDirectories) {
                if ($child.Name -match '(?i)^Inno Setup') {
                    $candidates.Add((Join-Path $child.FullName 'ISCC.exe'))
                }
                elseif ($child.Name -match '(?i)^JRSoftware$') {
                    foreach ($nested in @(Get-ChildItem -LiteralPath $child.FullName -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '(?i)^Inno Setup' })) {
                        $candidates.Add((Join-Path $nested.FullName 'ISCC.exe'))
                    }
                }
            }
        }
    }

    # App Paths registration can provide the executable itself or its folder.
    foreach ($appKeyPath in @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\App Paths\ISCC.exe',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\App Paths\ISCC.exe',
        'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\ISCC.exe'
    )) {
        try {
            $appKey = Get-Item -LiteralPath $appKeyPath -ErrorAction Stop
            $registeredExe = [string]$appKey.GetValue('')
            if (-not [string]::IsNullOrWhiteSpace($registeredExe)) { $candidates.Add($registeredExe) }
            $registeredPath = [string]$appKey.GetValue('Path')
            if (-not [string]::IsNullOrWhiteSpace($registeredPath)) { $candidates.Add((Join-Path $registeredPath 'ISCC.exe')) }
        }
        catch { }
    }

    # Installer/uninstaller registry entries are useful for per-user winget installs.
    foreach ($uninstallKeyPath in @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )) {
        foreach ($entry in @(Get-ItemProperty -Path $uninstallKeyPath -ErrorAction SilentlyContinue)) {
            if ([string]$entry.DisplayName -match '(?i)Inno Setup' -and -not [string]::IsNullOrWhiteSpace([string]$entry.InstallLocation)) {
                $candidates.Add((Join-Path ([string]$entry.InstallLocation) 'ISCC.exe'))
            }
        }
    }

    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace([string]$candidate)) { continue }
        $path = [Environment]::ExpandEnvironmentVariables(([string]$candidate).Trim().Trim('"'))
        if (-not $seen.Add($path)) { continue }
        if (Test-Path -LiteralPath $path -PathType Container) {
            $path = Join-Path $path 'ISCC.exe'
        }
        if ((Test-Path -LiteralPath $path -PathType Leaf) -and ([IO.Path]::GetFileName($path) -ieq 'ISCC.exe')) {
            return (Resolve-Path -LiteralPath $path).Path
        }
    }
    return $null
}
function Test-WindowsExecutable {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    if ((Get-Item -LiteralPath $Path).Length -lt 2) { return $false }
    $stream = [IO.File]::OpenRead($Path)
    try { return ($stream.ReadByte() -eq 0x4D -and $stream.ReadByte() -eq 0x5A) }
    finally { $stream.Dispose() }
}

function Commit-ArtifactSet {
    param([object[]]$Artifacts)
    $id = [Guid]::NewGuid().ToString('N')
    $prepared = [System.Collections.Generic.List[object]]::new()
    $commitSucceeded = $false
    $rollbackSucceeded = $true

    try {
        foreach ($artifact in $Artifacts) {
            $parent = Split-Path -Parent $artifact.Final
            if (-not (Test-Path -LiteralPath $parent -PathType Container)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
            $temp = Join-Path $parent ('.' + [IO.Path]::GetFileName($artifact.Final) + '.' + $id + '.new')
            $backup = Join-Path $parent ('.' + [IO.Path]::GetFileName($artifact.Final) + '.' + $id + '.bak')
            $item = [pscustomobject]@{ Temp = $temp; Final = $artifact.Final; Backup = $backup; HadBackup = $false; Committed = $false }
            $prepared.Add($item)
            Copy-Item -LiteralPath $artifact.Staged -Destination $temp -Force
            if (Test-Path -LiteralPath $artifact.Final -PathType Leaf) {
                Copy-Item -LiteralPath $artifact.Final -Destination $backup -Force
                $item.HadBackup = $true
            }
        }

        foreach ($item in $prepared) {
            Move-Item -LiteralPath $item.Temp -Destination $item.Final -Force
            $item.Committed = $true
        }
        $commitSucceeded = $true
    }
    catch {
        $originalError = $_
        for ($i = $prepared.Count - 1; $i -ge 0; $i--) {
            $item = $prepared[$i]
            if (-not $item.Committed) { continue }
            try {
                if ($item.HadBackup -and (Test-Path -LiteralPath $item.Backup -PathType Leaf)) {
                    Copy-Item -LiteralPath $item.Backup -Destination $item.Final -Force
                }
                elseif (Test-Path -LiteralPath $item.Final -PathType Leaf) {
                    Remove-Item -LiteralPath $item.Final -Force
                }
            }
            catch {
                $rollbackSucceeded = $false
                Write-Warning "Rollback failed for '$($item.Final)'. Backup, if present, is preserved at '$($item.Backup)'. Error: $($_.Exception.Message)"
            }
        }
        if (-not $rollbackSucceeded) {
            $preservedBackups = @($prepared | Where-Object { Test-Path -LiteralPath $_.Backup -PathType Leaf } | ForEach-Object { $_.Backup }) -join '; '
            throw "Artifact commit failed: $($originalError.Exception.Message). Rollback was incomplete; preserve and inspect backups: $preservedBackups"
        }
        throw $originalError
    }
    finally {
        foreach ($item in $prepared) {
            if (Test-Path -LiteralPath $item.Temp) { Remove-Item -LiteralPath $item.Temp -Force -ErrorAction SilentlyContinue }
            if (($commitSucceeded -or $rollbackSucceeded) -and (Test-Path -LiteralPath $item.Backup)) {
                Remove-Item -LiteralPath $item.Backup -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

try {
    $version = Get-ProjectVersion -Path $projectPath
    $dotnetExe = Assert-DotNet10
    $iscc = Find-InnoCompiler
    if (-not $iscc) { throw "ISCC.exe was not found. Checked PATH, ISCC_PATH, INNO_SETUP_HOME, Program Files, per-user LocalAppData\Programs and Inno Setup registry entries. Set `$env:ISCC_PATH to the full path of ISCC.exe and rerun Build-Installer.cmd. Example: `$env:ISCC_PATH = 'C:\Users\<you>\AppData\Local\Programs\Inno Setup 7\ISCC.exe'. No existing release artifacts were changed." }
    Write-Host "Using Inno Setup compiler: $iscc" -ForegroundColor Cyan
    if (-not (Test-Path -LiteralPath $issPath -PathType Leaf)) { throw "Installer definition is missing: $issPath" }

    New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
    $stagePublish = Join-Path $stageRoot 'publish'
    $stageOutput = Join-Path $stageRoot 'output'
    New-Item -ItemType Directory -Path $stagePublish -Force | Out-Null
    New-Item -ItemType Directory -Path $stageOutput -Force | Out-Null

    Write-Host "[1/2] Publishing MadNegar $version for Windows x64..." -ForegroundColor Cyan
    & $dotnetExe publish $projectPath --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --output $stagePublish
    $publishExitCode = $LASTEXITCODE
    if ($publishExitCode -ne 0) { throw "dotnet publish failed with exit code $publishExitCode. Existing published artifacts were not changed." }

    $stagedExe = Join-Path $stagePublish 'MadNegar.exe'
    if (-not (Test-WindowsExecutable -Path $stagedExe)) { throw "Publish returned without a valid MadNegar.exe at '$stagedExe'. Existing artifacts were not changed." }

    Write-Host '[2/2] Compiling the installer in a staging folder...' -ForegroundColor Cyan
    & $iscc "/DMyAppVersion=$version" "/DMyAppPublishDir=$stagePublish" "/O$stageOutput" $issPath
    $isccExitCode = $LASTEXITCODE
    if ($isccExitCode -ne 0) { throw "Inno Setup compilation failed with exit code $isccExitCode. Existing release artifacts were not changed." }

    $stagedInstaller = Join-Path $stageOutput "MadNegar-Setup-$version.exe"
    if (-not (Test-WindowsExecutable -Path $stagedInstaller)) { throw "Installer compiler returned without a valid output: $stagedInstaller. Existing release artifacts were not changed." }

    $stagedExeHash = (Get-FileHash -LiteralPath $stagedExe -Algorithm SHA256).Hash.ToLowerInvariant()
    $stagedInstallerHash = (Get-FileHash -LiteralPath $stagedInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
    $stagedExeHashFile = Join-Path $stagePublish 'MadNegar.exe.sha256'
    $stagedInstallerHashFile = Join-Path $stageOutput "MadNegar-Setup-$version.exe.sha256"
    [IO.File]::WriteAllText($stagedExeHashFile, "$stagedExeHash  MadNegar.exe`r`n", [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($stagedInstallerHashFile, "$stagedInstallerHash  MadNegar-Setup-$($version).exe`r`n", [Text.UTF8Encoding]::new($false))

    $finalInstaller = Join-Path $outputDir "MadNegar-Setup-$version.exe"
    $finalInstallerHash = $finalInstaller + ".sha256"
    Commit-ArtifactSet -Artifacts @(
        [pscustomobject]@{ Staged = $stagedExe; Final = (Join-Path $publishDir 'MadNegar.exe') },
        [pscustomobject]@{ Staged = $stagedExeHashFile; Final = (Join-Path $publishDir 'MadNegar.exe.sha256') },
        [pscustomobject]@{ Staged = $stagedInstaller; Final = $finalInstaller },
        [pscustomobject]@{ Staged = $stagedInstallerHashFile; Final = $finalInstallerHash }
    )

    $success = $true
    Write-Host ''
    Write-Host "SUCCESS: MadNegar $version installer built." -ForegroundColor Green
    Write-Host "Installer: $finalInstaller" -ForegroundColor Green
    Write-Host "SHA-256:   $stagedInstallerHash" -ForegroundColor Green
    Write-Host "Standalone: $(Join-Path $publishDir 'MadNegar.exe')" -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ("ERROR: " + $_.Exception.Message) -ForegroundColor Red
    if (Test-Path -LiteralPath $stageRoot -PathType Container) { Write-Host "Staging files kept for diagnosis: $stageRoot" -ForegroundColor Yellow }
    exit 1
}
finally {
    if ($success -and (Test-Path -LiteralPath $stageRoot -PathType Container)) { Remove-Item -LiteralPath $stageRoot -Recurse -Force -ErrorAction SilentlyContinue }
}
