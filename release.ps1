param (
    [Parameter(Mandatory = $true)]
    [ValidateSet("major", "minor", "patch")]
    [string]$VersionIncrement, # Renamed for clarity

    [ValidateSet("alpha", "beta")]
    [string]$PreRelease
)

# Use PSScriptRoot for robustness if the script isn't always run from the repo root
# If you ALWAYS run it from the repo root, "$versionFile = 'version.txt'" is fine.
$scriptDir = Split-Path -Path $MyInvocation.MyCommand.Path -Parent
$versionFile = Join-Path $scriptDir "version.txt"
# Or simply: $versionFile = "version.txt" if running from repo root.


Write-Host "Looking for version file at: $versionFile"

if (!(Test-Path $versionFile)) {
    Write-Error "Version file not found at: $versionFile"
    exit 1
}

$currentVersion = Get-Content $versionFile -Raw
Write-Host "Current version found: $currentVersion"

# Match format like v1.2.3 or v1.2.3-beta or v1.2.3-alpha
if ($currentVersion -match "^v(\d+)\.(\d+)\.(\d+)(?:-(alpha|beta))?$") {
    $major = [int]$matches[1]
    $minor = [int]$matches[2]
    $patch = [int]$matches[3]
    # $matches[4] will contain 'alpha' or 'beta' if present, otherwise it's empty/null
} else {
    Write-Error "Invalid version format in '$versionFile'. Expected 'vX.Y.Z' or 'vX.Y.Z-pre'. Found: '$currentVersion'"
    exit 1
}

# Perform version increment
switch ($VersionIncrement) { # Using the renamed parameter
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

# Construct the new version string
$baseVersion = "v$major.$minor.$patch"
$finalVersion = $baseVersion
if ($PSBoundParameters.ContainsKey('PreRelease')) { # Check if PreRelease parameter was actually passed
    $finalVersion = "$baseVersion-$PreRelease"
}

Write-Host "Calculated new version: $finalVersion"

# --- Write new version using Set-Content ---
try {
    Set-Content -Path $versionFile -Value $finalVersion -Encoding UTF8 -Force -ErrorAction Stop
    Write-Host "Successfully updated '$versionFile' with new version."
}
catch {
    Write-Error "Failed to write new version to '$versionFile'. Error: $_"
    exit 1
}

# --- Git Operations ---
Write-Host "Performing Git operations..."

# Function to run git commands and check for errors
function Invoke-GitCommand {
    param(
        [string]$Arguments,
        [string]$ErrorMessage
    )
    Write-Host "Running: git $Arguments"
    git $Arguments
    if ($LASTEXITCODE -ne 0) {
        Write-Error "$ErrorMessage (git $Arguments failed)"
        # Consider cleanup or alternative actions here if needed
        exit 1 # Exit script on Git error
    }
}

Invoke-GitCommand -Arguments "add ""$versionFile""" -ErrorMessage "Failed to stage '$versionFile'"
Invoke-GitCommand -Arguments "commit -m ""Release: $finalVersion""" -ErrorMessage "Failed to commit changes" # Note: -am combines add and commit, but explicit add is safer
Invoke-GitCommand -Arguments "tag $finalVersion" -ErrorMessage "Failed to create tag '$finalVersion'"
Invoke-GitCommand -Arguments "push origin HEAD" -ErrorMessage "Failed to push commit to origin"
Invoke-GitCommand -Arguments "push origin $finalVersion" -ErrorMessage "Failed to push tag '$finalVersion' to origin"

Write-Host "Successfully released $finalVersion"
Write-Output $finalVersion # Output the version string for potential use in pipelines