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

$releaseRoot = Join-Path $repoRoot "bin\githubrelease"
$stagingRoot = Join-Path $releaseRoot $modId
$dllPath = Join-Path $repoRoot "bin\Release\net48\$modId.dll"
$zipPath = Join-Path $releaseRoot ("{0}-{1}.zip" -f $modId, $tag)
$notesPath = Join-Path $releaseRoot "release-notes.md"

# Read pre-drafted What's New content before the release folder is wiped
$whatsNewContent = $null
$whatsNewOverridePath = Join-Path $releaseRoot "whats-new.md"
if (Test-Path $whatsNewOverridePath) {
    $whatsNewContent = (Get-Content $whatsNewOverridePath -Raw).Trim()
}

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

$repoUrl = "https://github.com/Jagg111/COI-ResearchQueue"

# Emoji characters (defined via codepoint so the .ps1 file stays ASCII-safe)
$emojiSparkles  = [char]::ConvertFromUtf32(0x2728)   # ✨
$emojiWarning   = [char]::ConvertFromUtf32(0x26A0) + [char]::ConvertFromUtf32(0xFE0F)  # ⚠️
$emojiPackage   = [char]::ConvertFromUtf32(0x1F4E6)  # 📦
$emojiDownArrow = [char]::ConvertFromUtf32(0x2B07) + [char]::ConvertFromUtf32(0xFE0F)  # ⬇️
$emojiFolder    = [char]::ConvertFromUtf32(0x1F4C1)  # 📁

# --- What's New: clean commit messages with auto-linked issue references ---

$previousTag = ""
$previousTags = @(& git tag --sort=-creatordate)
if ($LASTEXITCODE -eq 0 -and $previousTags.Count -gt 0) {
    $previousTag = $previousTags[0].Trim()
}

if ($previousTag) {
    $commitArgs = @("log", "$previousTag..HEAD", "--pretty=format:%s")
} else {
    $commitArgs = @("log", "HEAD", "--pretty=format:%s")
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

    $subject = $line.Trim()

    # Convert #N issue references to markdown links
    $subject = [regex]::Replace($subject, '#(\d+)', "[#`$1]($repoUrl/issues/`$1)")

    $commitBullets += "- $subject"
}

if ($commitBullets.Count -eq 0) {
    $commitBullets += "- No changes found."
}

# --- What's New: override with pre-drafted content if present (written by /ship skill) ---

if ($whatsNewContent) {
    $commitBullets = @($whatsNewContent)
}

# --- Known Issues: auto-pull open issues with 'bug' label ---

$knownIssueBullets = @()
if (Get-Command gh -ErrorAction SilentlyContinue) {
    $bugIssues = Invoke-Gh -Arguments @("issue", "list", "--label", "bug", "--state", "open", "--json", "number,title")
    if ($bugIssues.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($bugIssues.StdOut)) {
        $issues = $bugIssues.StdOut | ConvertFrom-Json
        foreach ($issue in $issues) {
            $knownIssueBullets += "- $($issue.title) (issue [#$($issue.number)]($repoUrl/issues/$($issue.number)))"
        }
    }
}

# --- Build release notes ---

$notes = @(
    "## $emojiSparkles What's New",
    ""
)
$notes += $commitBullets

if ($knownIssueBullets.Count -gt 0) {
    $notes += @(
        "",
        "## $emojiWarning Known Issues",
        ""
    )
    $notes += $knownIssueBullets
}

$backtick3 = '```'

$notes += @(
    "",
    "## $emojiPackage Installation",
    "1. $emojiDownArrow Download the **``$modId-$tag.zip``** file below",
    "2. Extract the zip file",
    "3. Copy the **``ResearchQueue``** folder into your mods directory:",
    "   $backtick3",
    "   %APPDATA%\Captain of Industry\Mods\",
    "   $backtick3",
    "   Your folder structure should look like this:",
    "   $backtick3",
    "   Captain of Industry\Mods\ResearchQueue\",
    "       ResearchQueue.dll",
    "       manifest.json",
    "   $backtick3",
    "4. Launch the game and enable the mod when loading your save. In-game open the research tree (hotkey ``G`` by default) - the queue panel appears on the right side when no research nodes are selected",
    "",
    "<details>",
    "<summary><strong>$emojiFolder Can't find your Mods folder?</strong></summary>",
    "",
    "Press ``Win + R``, paste this path, and hit Enter:",
    $backtick3,
    "%APPDATA%\Captain of Industry\Mods",
    $backtick3,
    "If the ``Mods`` folder doesn't exist yet, create it.",
    "",
    "</details>",
    "",
    "---",
    "Got a bug or suggestion? [Join the discussions on GitHub]($repoUrl/discussions)"
)

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($notesPath, ($notes -join [Environment]::NewLine), $utf8NoBom)

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
