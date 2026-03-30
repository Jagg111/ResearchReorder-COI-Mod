[CmdletBinding()]
param(
    [switch]$PackageOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if (Test-Path Variable:\PSNativeCommandUseErrorActionPreference) {
    $PSNativeCommandUseErrorActionPreference = $false
}

trap {
    Write-Host ("ERROR: {0}" -f $_.Exception.Message)
    exit 1
}

function Fail([string]$Message) {
    throw $Message
}

function Invoke-Gh {
    param(
        [string[]]$Arguments
    )

    $stdoutPath = [System.IO.Path]::GetTempFileName()
    $stderrPath = [System.IO.Path]::GetTempFileName()
    $quotedArguments = $Arguments | ForEach-Object {
        if ($_ -match '[\s"]') {
            '"' + ($_.Replace('"', '\"')) + '"'
        } else {
            $_
        }
    }

    try {
        $process = Start-Process -FilePath "gh" -ArgumentList $quotedArguments -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath

        return [PSCustomObject]@{
            ExitCode = $process.ExitCode
            StdOut = if (Test-Path $stdoutPath) { Get-Content $stdoutPath -Raw } else { "" }
            StdErr = if (Test-Path $stderrPath) { Get-Content $stderrPath -Raw } else { "" }
        }
    }
    finally {
        Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
    }
}

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

$manifestPath = Join-Path $repoRoot "manifest.json"
if (-not (Test-Path $manifestPath)) {
    Fail "manifest.json was not found at $manifestPath"
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($manifest.id)) {
    Fail "manifest.json is missing the 'id' field."
}

if ([string]::IsNullOrWhiteSpace($manifest.display_name)) {
    Fail "manifest.json is missing the 'display_name' field."
}

if ([string]::IsNullOrWhiteSpace($manifest.version)) {
    Fail "manifest.json is missing the 'version' field."
}

$modId = [string]$manifest.id
$displayName = [string]$manifest.display_name
$version = [string]$manifest.version
$tag = "v$version"
$title = "$displayName $tag"

$releaseRoot = Join-Path $repoRoot "githubrelease"
$stagingRoot = Join-Path $releaseRoot $modId
$dllPath = Join-Path $repoRoot "bin\Release\net48\$modId.dll"
$zipPath = Join-Path $releaseRoot ("{0}-{1}.zip" -f $modId, $tag)
$notesPath = Join-Path $releaseRoot "release-notes.md"

$localTagExists = @(& git tag --list $tag)
if ($LASTEXITCODE -eq 0 -and $localTagExists.Count -gt 0) {
    Fail "Git tag '$tag' already exists locally. Choose a new manifest version before creating a release."
}

Write-Host "Building Release..."
& dotnet build .\ResearchQueue.sln -c Release /p:LangVersion=latest /p:DeployToModsFolder=false
if ($LASTEXITCODE -ne 0) {
    Fail "Release build failed."
}

if (-not (Test-Path $dllPath)) {
    Fail "Expected build output was not found at $dllPath"
}

if (Test-Path $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
Copy-Item -LiteralPath $dllPath -Destination (Join-Path $stagingRoot "$modId.dll")
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $stagingRoot "manifest.json")

Compress-Archive -Path $stagingRoot -DestinationPath $zipPath -Force
if (-not (Test-Path $zipPath)) {
    Fail "Release zip was not created at $zipPath"
}

$previousTag = ""
$previousTags = @(& git tag --sort=-creatordate)
if ($LASTEXITCODE -eq 0 -and $previousTags.Count -gt 0) {
    $previousTag = $previousTags[0].Trim()
}

if ($previousTag) {
    $commitArgs = @("log", "$previousTag..HEAD", "--pretty=format:%s|%h")
    $changeHeading = "## Changes since $previousTag"
} else {
    $commitArgs = @("log", "HEAD", "--pretty=format:%s|%h")
    $changeHeading = "## Changes included in this release"
}

$commitLines = @(& git @commitArgs)
if ($LASTEXITCODE -ne 0) {
    Fail "Failed to collect git history for release notes."
}

$commitBullets = @()
foreach ($line in $commitLines) {
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }

    $parts = $line -split "\|", 2
    $subject = $parts[0].Trim()
    $hash = if ($parts.Count -gt 1) { $parts[1].Trim() } else { "" }

    if ($hash) {
        $commitBullets += ('- {0} (`{1}`)' -f $subject, $hash)
    } else {
        $commitBullets += "- $subject"
    }
}

if ($commitBullets.Count -eq 0) {
    $commitBullets += "- No commits found."
}

$notes = @(
    "# $title",
    "",
    $changeHeading,
    ""
)

$notes += $commitBullets
$notes += @(
    "",
    "## Installation",
    "",
    "1. Download the **ResearchQueue.zip** file below",
    "2. Copy the ``ResearchQueue`` folder into your mods directory:",
    "   ``````",
    "   %APPDATA%\Captain of Industry\Mods\",
    "   ``````",
    "   The final structure should look like:",
    "   ``````",
    "   Captain of Industry\Mods\ResearchQueue\",
    "       ResearchQueue.dll",
    "       manifest.json",
    "   ``````",
    "3. Launch the game",
    "4. Go to load your save and enable the mod",
    "5. Open the research tree -- the queue panel appears on the right side",
    "",
    "### Finding your Mods folder",
    "",
    "Press **Win + R**, paste this path, and hit Enter:",
    "``````",
    "%APPDATA%\Captain of Industry\Mods",
    "``````",
    "If the ``Mods`` folder doesn't exist yet, create it."
)

Set-Content -LiteralPath $notesPath -Value ($notes -join [Environment]::NewLine)

if ($PackageOnly) {
    Write-Host "Package created without GitHub draft release:"
    Write-Host "  Zip:   $zipPath"
    Write-Host "  Notes: $notesPath"
    exit 0
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Fail "GitHub CLI ('gh') is not installed or not on PATH."
}

$authStatus = Invoke-Gh -Arguments @("auth", "status")
if ($authStatus.ExitCode -ne 0) {
    $authMessage = (($authStatus.StdOut + [Environment]::NewLine + $authStatus.StdErr).Trim())
    if ($authMessage) {
        Fail "GitHub CLI is not authenticated. Run 'gh auth login' and try again.`n$authMessage"
    }

    Fail "GitHub CLI is not authenticated. Run 'gh auth login' and try again."
}

$existingRelease = Invoke-Gh -Arguments @("release", "view", $tag)
if ($existingRelease.ExitCode -eq 0) {
    Fail "A GitHub release for tag '$tag' already exists."
}

Write-Host "Creating GitHub draft release..."
$releaseCreate = Invoke-Gh -Arguments @("release", "create", $tag, $zipPath, "--title", $title, "--notes-file", $notesPath, "--draft")
if ($releaseCreate.ExitCode -ne 0) {
    $releaseMessage = (($releaseCreate.StdOut + [Environment]::NewLine + $releaseCreate.StdErr).Trim())
    if ($releaseMessage) {
        Fail "GitHub draft release creation failed.`n$releaseMessage"
    }

    Fail "GitHub draft release creation failed."
}

Write-Host "Draft release created for $title"
