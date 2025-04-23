param (
    [Parameter(Mandatory = $true)]
    [ValidateSet("major", "minor", "patch")]
    [string]$Version,

    [ValidateSet("alpha", "beta")]
    [string]$PreRelease
)

$versionFile = "version.txt"

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
if ($PSBoundParameters.ContainsKey('PreRelease')) {
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

# --- Git Operations ---
Write-Host "Staging, committing, tagging, and pushing $finalVersion..."

# Function to run Git commands and check for errors
function Invoke-GitCommand {
    param(
        # Use an array for the arguments
        [Parameter(Mandatory=$true)]
        [string[]]$ArgumentList,

        [Parameter(Mandatory=$true)]
        [string]$ErrorMessage
    )
    # Display the command clearly
    Write-Host "Running: git $($ArgumentList -join ' ')"

    # --- CHANGE HERE: Use the call operator '&' and pass the array ---
    # PowerShell will correctly pass each element of $ArgumentList as a separate argument to git.exe
    $output = & git @ArgumentList 2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Error "$ErrorMessage`nGit output:`n$output"
        # Consider attempting to revert the file change here if desired
        # try { git checkout HEAD -- $versionFile } catch {}
        exit 1
    }
    # Write-Host "Git command successful." # Optional: Reduce noise
    Write-Verbose "Git Output:`n$output" # Show output only with -Verbose
}

# --- CHANGES HERE: Call Invoke-GitCommand with argument arrays ---
Invoke-GitCommand -ArgumentList 'add', $versionFile -ErrorMessage "Failed to stage $versionFile."
Invoke-GitCommand -ArgumentList 'commit', '-m', "Release: $finalVersion" -ErrorMessage "Failed to commit changes." # '-m' and the message are separate arguments
Invoke-GitCommand -ArgumentList 'tag', $finalVersion -ErrorMessage "Failed to create tag $finalVersion."
Invoke-GitCommand -ArgumentList 'push', 'origin', 'HEAD' -ErrorMessage "Failed to push commit to origin." # 'push', 'origin', 'HEAD' are separate arguments
Invoke-GitCommand -ArgumentList 'push', 'origin', $finalVersion -ErrorMessage "Failed to push tag $finalVersion to origin." # 'push', 'origin', <tag> are separate arguments

Write-Output "Successfully released and pushed $finalVersion"