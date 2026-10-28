param (
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet("major", "minor", "patch")]
    [string]$Version,

    [Parameter(Position = 1)]
    [ValidateSet("alpha", "beta")]
    [string]$PreRelease,

    [Parameter(Position = 2)]
    [Alias("m")]
    [string]$Message
)

# Garante leitura/escrita correta de caracteres UTF-8 no console e git
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$versionFile = "version.txt"
$releaseNotesFile = "RELEASE_NOTES.md"

# Check if version file exists
if (!(Test-Path $versionFile)) {
    Write-Error "version.txt not found in the current directory ($(Get-Location))."
    exit 1
}

# Read current version
$currentVersion = Get-Content $versionFile -Raw

# Match format like v1.2.3 or v1.2.3-beta
if ($currentVersion -match "^v(\d+)\.(\d+)\.(\d+)(?:-(alpha|beta))?$") {
    $major = [int]$matches[1]
    $minor = [int]$matches[2]
    $patch = [int]$matches[3]
} else {
    Write-Error "Invalid version format in version.txt: '$currentVersion'. Expected format like v1.2.3 or v1.2.3-beta."
    exit 1
}

# Increment version based on input
switch ($Version) {
    "major" {
        $major++
        $minor = 0
        $patch = 0
    }
    "minor" {
        $minor++
        $patch = 0
    }
    "patch" {
        $patch++
    }
}

# Construct the base version string
$baseVersion = "v$major.$minor.$patch"

# Add pre-release tag if specified
if ($PSBoundParameters.ContainsKey('PreRelease') -and -not [string]::IsNullOrWhiteSpace($PreRelease)) {
    $finalVersion = "$baseVersion-$PreRelease"
} else {
    $finalVersion = $baseVersion
}

# Write new version WITHOUT adding a trailing newline
Write-Host "Updating $versionFile to $finalVersion..."
try {
    Set-Content -Path $versionFile -Value $finalVersion -NoNewline -ErrorAction Stop
} catch {
    Write-Error "Failed to write new version to $versionFile. Error: $_"
    exit 1
}

# --- Gerar / Atualizar RELEASE_NOTES.md ---
if ($PSBoundParameters.ContainsKey('Message') -and -not [string]::IsNullOrWhiteSpace($Message)) {
    Write-Host "Using custom message provided via -m for $releaseNotesFile..."
    $notesContent = $Message.Trim()
} else {
    Write-Host "No message provided. Extracting commits from git log..."
    # Localiza a tag mais recente alcançável a partir do HEAD
    $lastTag = (& git describe --tags --abbrev=0 2>$null)
    
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($lastTag)) {
        $lastTag = $lastTag.Trim()
        Write-Host "Fetching git oneline logs between '$lastTag' and HEAD..."
        $changelog = & git log --oneline "$lastTag..HEAD"
    } else {
        Write-Host "No previous tag found. Fetching all commits up to HEAD..."
        $changelog = & git log --oneline HEAD
    }

    if ($changelog) {
        $notesContent = ($changelog -join "`n").Trim()
    } else {
        $notesContent = "Release $finalVersion"
    }
}

Write-Host "Updating $releaseNotesFile..."
try {
    Set-Content -Path $releaseNotesFile -Value $notesContent -Encoding utf8 -ErrorAction Stop
} catch {
    Write-Error "Failed to write to $releaseNotesFile. Error: $_"
    exit 1
}

# --- Git Operations ---
Write-Host "Staging, committing, tagging, and pushing $finalVersion..."

function Invoke-GitCommand {
    param(
        [Parameter(Mandatory=$true)]
        [string[]]$ArgumentList,

        [Parameter(Mandatory=$true)]
        [string]$ErrorMessage
    )
    Write-Host "Running: git $($ArgumentList -join ' ')"
    $output = & git @ArgumentList 2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Error "$ErrorMessage`nGit output:`n$output"
        exit 1
    }
    Write-Verbose "Git Output:`n$output"
}

# Adiciona tanto o version.txt quanto o RELEASE_NOTES.md ao commit
Invoke-GitCommand -ArgumentList 'add', $versionFile, $releaseNotesFile -ErrorMessage "Failed to stage $versionFile and $releaseNotesFile."
Invoke-GitCommand -ArgumentList 'commit', '-m', "Release: $finalVersion" -ErrorMessage "Failed to commit changes."
Invoke-GitCommand -ArgumentList 'tag', $finalVersion -ErrorMessage "Failed to create tag $finalVersion."
Invoke-GitCommand -ArgumentList 'push', 'origin', 'HEAD' -ErrorMessage "Failed to push commit to origin."
Invoke-GitCommand -ArgumentList 'push', 'origin', $finalVersion -ErrorMessage "Failed to push tag $finalVersion to origin."

Write-Output "Successfully released and pushed $finalVersion with release notes!"