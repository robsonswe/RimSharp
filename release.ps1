param (
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet("major", "minor", "patch", "same")]
    [string]$Version,

    [Parameter(Position = 1)]
    [ValidateSet("alpha", "beta")]
    [string]$PreRelease,

    [Parameter(Position = 2)]
    [Alias("m")]
    [string]$Message,

    [Parameter()]
    [switch]$DryRun
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$versionFile = "version.txt"
$releaseNotesFile = "RELEASE_NOTES.md"

if ($DryRun) {
    Write-Host "`n=== [MODO DRY RUN ATIVADO] Nenhum arquivo será alterado e nada irá para o Git ===`n" -ForegroundColor Yellow
}

if (!(Test-Path $versionFile)) {
    Write-Error "version.txt not found in current directory ($(Get-Location))."
    exit 1
}

$currentVersion = (Get-Content $versionFile -Raw).Trim()

if ($currentVersion -match "^v(\d+)\.(\d+)\.(\d+)(?:-(alpha|beta))?$") {
    $major = [int]$matches[1]
    $minor = [int]$matches[2]
    $patch = [int]$matches[3]
} else {
    Write-Error "Invalid version format in '$versionFile': '$currentVersion'. Expected format like v1.2.3 or v1.2.3-beta."
    exit 1
}

# Incrementa ou mantém a versão base
switch ($Version) {
    "major" { $major++; $minor = 0; $patch = 0 }
    "minor" { $minor++; $patch = 0 }
    "patch" { $patch++ }
    "same"  { }
}

$baseVersion = "v$major.$minor.$patch"
$isPreRelease = $PSBoundParameters.ContainsKey('PreRelease') -and -not [string]::IsNullOrWhiteSpace($PreRelease)

if ($isPreRelease) {
    $finalVersion = "$baseVersion-$PreRelease"
} else {
    $finalVersion = $baseVersion
}

Write-Host "Current version: $currentVersion"
Write-Host "Target version:  $finalVersion" -ForegroundColor Green

# Função auxiliar para extrair o git log e filtrar commits automáticos de release
function Get-FilteredGitLog {
    param([string]$Range)
    $logs = if ($Range) { & git log --oneline $Range } else { & git log --oneline HEAD }
    return @($logs | Where-Object { $_ -notmatch '^\w+\s+Release:\s+v' })
}

# --- Construção das Release Notes (Padrão 2: Cumulativo + Delta) ---
$allReachableTags = (& git tag --merged HEAD --sort=-creatordate 2>$null) | Where-Object { $_ -match '^v\d+\.\d+\.\d+' }

$immediateTag = $allReachableTags | Select-Object -First 1
$lastStableTag = $allReachableTags | Where-Object { $_ -match '^v\d+\.\d+\.\d+$' } | Select-Object -First 1

$sections = [System.Collections.Generic.List[string]]::new()

if ($PSBoundParameters.ContainsKey('Message') -and -not [string]::IsNullOrWhiteSpace($Message)) {
    $sections.Add("### 💡 Highlights`n$($Message.Trim())")
}

if ($isPreRelease) {
    Write-Host "Pre-release: '$finalVersion' | Last Stable: '$lastStableTag' | Immediate: '$immediateTag'"

    # 1. Delta imediato: O que mudou desde a tag anterior?
    if (-not [string]::IsNullOrWhiteSpace($immediateTag) -and $immediateTag -ne $lastStableTag) {
        $deltaCommits = Get-FilteredGitLog "$immediateTag..HEAD"
        if ($deltaCommits.Count -gt 0) {
            $sections.Add("### 🆕 Changes in this build (since $immediateTag)`n" + ($deltaCommits -join "`n"))
        }
    }

    # 2. Cumulativo: Escopo geral do ciclo desde a última Estável
    if (-not [string]::IsNullOrWhiteSpace($lastStableTag)) {
        $cycleCommits = Get-FilteredGitLog "$lastStableTag..HEAD"
        if ($cycleCommits.Count -gt 0) {
            $sections.Add("### 📋 Full cycle changes (since $lastStableTag)`n" + ($cycleCommits -join "`n"))
        }
    } else {
        $allCommits = Get-FilteredGitLog
        if ($allCommits.Count -gt 0) {
            $sections.Add("### 📋 All changes up to HEAD`n" + ($allCommits -join "`n"))
        }
    }
} else {
    Write-Host "Stable release: '$finalVersion' | Comparing against last stable: '$lastStableTag'"

    if (-not [string]::IsNullOrWhiteSpace($lastStableTag)) {
        $stableCommits = Get-FilteredGitLog "$lastStableTag..HEAD"
        if ($stableCommits.Count -gt 0) {
            $sections.Add("### 🚀 What's Changed (since $lastStableTag)`n" + ($stableCommits -join "`n"))
        }
    } else {
        $allCommits = Get-FilteredGitLog
        if ($allCommits.Count -gt 0) {
            $sections.Add("### 🚀 Initial Release`n" + ($allCommits -join "`n"))
        }
    }
}

if ($sections.Count -gt 0) {
    $notesContent = ($sections -join "`n`n").Trim()
} else {
    $notesContent = "Release $finalVersion"
}

# --- Saída ou Execução Real ---
if ($DryRun) {
    Write-Host "`n--- PREVIEW DE $releaseNotesFile QUE SERIA GERADO: ---" -ForegroundColor Cyan
    Write-Host $notesContent
    Write-Host "`n--- AÇÕES DO GIT QUE SERIAM EXECUTADAS: ---" -ForegroundColor Cyan
    Write-Host "1. Atualizar $versionFile para '$finalVersion'"
    Write-Host "2. Atualizar $releaseNotesFile com o conteúdo acima"
    Write-Host "3. git add $versionFile $releaseNotesFile"
    Write-Host "4. git commit -m 'Release: $finalVersion'"
    Write-Host "5. git tag $finalVersion"
    Write-Host "6. git push origin HEAD && git push origin $finalVersion"
    Write-Host "`n[DryRun concluído com sucesso. Nada foi alterado!]`n" -ForegroundColor Yellow
    exit 0
}

Set-Content -Path $versionFile -Value $finalVersion -NoNewline -ErrorAction Stop
Set-Content -Path $releaseNotesFile -Value $notesContent -Encoding utf8 -ErrorAction Stop

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
}

Invoke-GitCommand -ArgumentList 'add', $versionFile, $releaseNotesFile -ErrorMessage "Failed to stage files."
Invoke-GitCommand -ArgumentList 'commit', '-m', "Release: $finalVersion" -ErrorMessage "Failed to commit changes."
Invoke-GitCommand -ArgumentList 'tag', $finalVersion -ErrorMessage "Failed to create tag $finalVersion."
Invoke-GitCommand -ArgumentList 'push', 'origin', 'HEAD' -ErrorMessage "Failed to push commit."
Invoke-GitCommand -ArgumentList 'push', 'origin', $finalVersion -ErrorMessage "Failed to push tag."

Write-Output "Successfully released and pushed $finalVersion!"