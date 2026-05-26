$ErrorActionPreference = "Stop"

$projectDir = $PSScriptRoot
if ([string]::IsNullOrEmpty($projectDir)) {
    $projectDir = Get-Location
}
$mpvDir = Join-Path $projectDir "mpv"
$versionFile = Join-Path $mpvDir "version.txt"

# 1. Fetch RSS feed from SourceForge
$rssUrl = "https://sourceforge.net/projects/mpv-player-windows/rss?path=/libmpv"
Write-Host "Fetching latest libmpv version info from SourceForge..."
try {
    $rssItems = Invoke-RestMethod -Uri $rssUrl -TimeoutSec 15
} catch {
    Write-Warning "Failed to fetch RSS feed: $_. If libmpv-2.dll exists, we will skip download."
    if (Test-Path (Join-Path $mpvDir "libmpv-2.dll")) {
        exit 0
    } else {
        throw "Failed to download libmpv metadata, and libmpv-2.dll is missing!"
    }
}

# Find the latest x86_64-v3 build
$latestItem = $rssItems | Where-Object { $_.title.InnerText -like "*mpv-dev-x86_64-v3-*.7z" } | Select-Object -First 1

if ($null -eq $latestItem) {
    # Fallback to standard x86_64 if v3 is not found
    $latestItem = $rssItems | Where-Object { $_.title.InnerText -like "*mpv-dev-x86_64-*.7z" } | Select-Object -First 1
}

if ($null -eq $latestItem) {
    throw "No matching libmpv package found in RSS feed!"
}

# The title is the filename or path, e.g. "/libmpv/mpv-dev-x86_64-v3-20260524-git-9e06c32.7z"
$latestVersion = Split-Path $latestItem.title.InnerText -Leaf
$downloadUrl = $latestItem.link

Write-Host "Latest version on SourceForge: $latestVersion"

# 2. Check if we already have this version
$currentVersion = ""
if (Test-Path $versionFile) {
    $currentVersion = Get-Content $versionFile -Raw
    $currentVersion = $currentVersion.Trim()
}

$dllExists = Test-Path (Join-Path $mpvDir "libmpv-2.dll")

if ($dllExists -and ($currentVersion -eq $latestVersion)) {
    Write-Host "libmpv is already up to date ($currentVersion)."
    exit 0
}

Write-Host "Updating libmpv from '$currentVersion' to '$latestVersion'..."

# 3. Create temp directory
$tempDir = Join-Path $env:TEMP "mpv_download_$(Get-Random)"
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
$archivePath = Join-Path $tempDir "mpv.7z"

try {
    # 4. Download archive using curl.exe
    Write-Host "Downloading $downloadUrl ..."
    & curl.exe -L -s -S -o "$archivePath" "$downloadUrl"
    if ($LASTEXITCODE -ne 0) {
        throw "curl.exe failed to download the archive with exit code $LASTEXITCODE."
    }

    # 5. Extract archive using built-in Windows tar
    Write-Host "Extracting archive..."
    $extractDir = Join-Path $tempDir "extracted"
    New-Item -ItemType Directory -Path $extractDir -Force | Out-Null
    
    # Run tar.exe to extract
    & tar.exe -xf "$archivePath" -C "$extractDir"
    
    # 6. Copy files to target directory
    if (-not (Test-Path $mpvDir)) {
        New-Item -ItemType Directory -Path $mpvDir -Force | Out-Null
    }

    # Find libmpv-2.dll and include folder in the extracted files
    $dllFile = Get-ChildItem -Path $extractDir -Filter "libmpv-2.dll" -Recurse | Select-Object -First 1
    $includeDir = Get-ChildItem -Path $extractDir -Directory -Filter "include" -Recurse | Select-Object -First 1
    $implibFile = Get-ChildItem -Path $extractDir -Filter "libmpv.dll.a" -Recurse | Select-Object -First 1

    if ($null -eq $dllFile) {
        throw "Could not find libmpv-2.dll in the downloaded archive!"
    }

    Write-Host "Copying libmpv-2.dll to $mpvDir..."
    Copy-Item -Path $dllFile.FullName -Destination $mpvDir -Force

    if ($null -ne $includeDir) {
        Write-Host "Copying include folder to $mpvDir..."
        if (Test-Path (Join-Path $mpvDir "include")) {
            Remove-Item -Path (Join-Path $mpvDir "include") -Recurse -Force
        }
        Copy-Item -Path $includeDir.FullName -Destination $mpvDir -Recurse -Force
    }

    if ($null -ne $implibFile) {
        Write-Host "Copying libmpv.dll.a to $mpvDir..."
        Copy-Item -Path $implibFile.FullName -Destination $mpvDir -Force
    }

    # Save version
    Set-Content -Path $versionFile -Value $latestVersion
    Write-Host "libmpv successfully updated to $latestVersion!"
}
finally {
    # 7. Clean up
    if (Test-Path $tempDir) {
        Write-Host "Cleaning up temporary files..."
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
