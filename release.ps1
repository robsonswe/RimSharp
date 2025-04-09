param (
    [Parameter(Mandatory = $true)]
    [ValidateSet("major", "minor", "patch")]
    [string]$Version,

    [ValidateSet("alpha", "beta")]
    [string]$PreRelease
)

$versionFile = "version.txt"

if (!(Test-Path $versionFile)) {
    Write-Error "version.txt not found."
    exit 1
}

$currentVersion = Get-Content $versionFile -Raw

# Match format like v1.2.3 or v1.2.3-beta
if ($currentVersion -match "^v(\d+)\.(\d+)\.(\d+)(?:-(alpha|beta))?$") {
    $major = [int]$matches[1]
    $minor = [int]$matches[2]
    $patch = [int]$matches[3]
} else {
    Write-Error "Invalid version format in version.txt: $currentVersion"
    exit 1
}

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

$baseVersion = "v$major.$minor.$patch"

if ($PreRelease) {
    $finalVersion = "$baseVersion-$PreRelease"
} else {
    $finalVersion = $baseVersion
}

# Write new version
Set-Content -Path $versionFile -Value $finalVersion

# Commit, tag, and push
git add $versionFile
git commit -am "Release: $finalVersion"
git tag $finalVersion
git push origin HEAD
git push origin $finalVersion

Write-Output "Released $finalVersion"
