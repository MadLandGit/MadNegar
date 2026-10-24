$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$projectPath = Join-Path $root 'MadNegar.csproj'
$publishDir = Join-Path $root 'publish'
$finalExe = Join-Path $publishDir 'MadNegar.exe'
$finalHash = Join-Path $publishDir 'MadNegar.exe.sha256'
$stageRoot = Join-Path $root ('.release-staging\publish-' + [Guid]::NewGuid().ToString('N'))
$success = $false

function Get-ProjectVersion {
    param([string]$Path)
    [xml]$xml = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    $node = $xml.SelectSingleNode('/Project/PropertyGroup/Version')
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
        throw 'MadNegar.csproj must define a non-empty PropertyGroup/Version.'
    }
    $version = $node.InnerText.Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
        throw "Project version '$version' is not a valid release version."
    }
    return $version
}

function Assert-DotNet10 {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnet) { throw 'The .NET SDK was not found. Install the .NET 10 SDK and retry.' }
    $sdks = & $dotnet.Source --list-sdks
    $sdkExit = $LASTEXITCODE
    if ($sdkExit -ne 0) { throw "dotnet --list-sdks failed with exit code $sdkExit." }
    if (-not ($sdks | Where-Object { $_ -match '^\s*10\.' })) {
        throw 'The .NET 10 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/10.0.'
    }
    return $dotnet.Source
}

function Test-WindowsExecutable {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    $file = Get-Item -LiteralPath $Path
    if ($file.Length -lt 2) { return $false }
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        return ($stream.ReadByte() -eq 0x4D -and $stream.ReadByte() -eq 0x5A)
    }
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
    New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
    $stagePublish = Join-Path $stageRoot 'publish'
    New-Item -ItemType Directory -Path $stagePublish -Force | Out-Null

    Write-Host "Publishing MadNegar $version for Windows x64 (self-contained)..." -ForegroundColor Cyan
    & $dotnetExe publish $projectPath --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --output $stagePublish
    $publishExitCode = $LASTEXITCODE
    if ($publishExitCode -ne 0) { throw "dotnet publish failed with exit code $publishExitCode. Existing published artifacts were not changed." }

    $stagedExe = Join-Path $stagePublish 'MadNegar.exe'
    if (-not (Test-WindowsExecutable -Path $stagedExe)) { throw "Publish did not produce a valid non-empty Windows executable: $stagedExe. Existing published artifacts were not changed." }

    $hash = (Get-FileHash -LiteralPath $stagedExe -Algorithm SHA256).Hash.ToLowerInvariant()
    $stagedHash = Join-Path $stagePublish 'MadNegar.exe.sha256'
    [IO.File]::WriteAllText($stagedHash, "$hash  MadNegar.exe`r`n", [Text.UTF8Encoding]::new($false))

    Commit-ArtifactSet -Artifacts @(
        [pscustomobject]@{ Staged = $stagedExe; Final = $finalExe },
        [pscustomobject]@{ Staged = $stagedHash; Final = $finalHash }
    )

    $success = $true
    Write-Host "SUCCESS: MadNegar $version was published to $finalExe" -ForegroundColor Green
    Write-Host "SHA-256: $hash" -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ("ERROR: " + $_.Exception.Message) -ForegroundColor Red
    if (Test-Path -LiteralPath $stageRoot -PathType Container) {
        Write-Host "Staging files kept for diagnosis: $stageRoot" -ForegroundColor Yellow
    }
    exit 1
}
finally {
    if ($success -and (Test-Path -LiteralPath $stageRoot -PathType Container)) {
        Remove-Item -LiteralPath $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
