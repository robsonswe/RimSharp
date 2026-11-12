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

    # Tag range the notes cover: 'v2.4.2...v2.4.4', 'v2.4.2..v2.4.4' or a single
    # tag (end defaults to HEAD). Overrides the auto-detected last stable tag.
    [Parameter()]
    [string]$Range,

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

# ---------------------------------------------------------------------------
# Release notes builders
#
# The notes are grouped by merged pull request when the cycle has them: each
# PR becomes a section titled by its real title (fetched from gh), with the
# branch's commits as bullets underneath, ordered by their conventional-commit
# type. Commits that landed on main without a PR are grouped by type under
# "Other changes". A cycle without PR merges falls back to the plain list.
# ---------------------------------------------------------------------------

# Conventional-commit types, in display order. Unknown subjects sort last.
$commitTypeOrder = @('feat', 'fix', 'perf', 'refactor', 'test', 'docs', 'ci', 'chore', 'style', 'build', 'other')

function Get-CommitType {
    param([string]$Subject)
    if ($Subject -match '^(feat|fix|perf|refactor|test|docs|ci|chore|style|build)(\(|:)') {
        return $matches[1]
    }
    return 'other'
}

function Get-CommitSubject {
    # Strips a conventional-commit prefix for grouped lists ("feat(filter): x" -> "filter: x").
    param([string]$Subject)
    if ($Subject -match '^(?:feat|fix|perf|refactor|test|docs|ci|chore|style|build)(?:\([^)]*\))?:\s*(.+)$') {
        return $matches[1]
    }
    return $Subject
}

# Resolves the optional -Range parameter into a base..end pair. Accepts
# 'a...b', 'a..b' or a single tag (end defaults to HEAD). The two tags must
# be ancestor-related; the ancestor is the base.
function Resolve-NoteRange {
    param([string]$RangeSpec)

    if ([string]::IsNullOrWhiteSpace($RangeSpec)) {
        return @{ Base = $null; End = $null }
    }

    $tokens = @($RangeSpec -split '\.{2,3}' | Where-Object { $_ })
    if ($tokens.Count -gt 2) {
        Write-Error "Invalid range '$RangeSpec'. Expected 'tagA...tagB' or a single tag."
        exit 1
    }

    foreach ($token in $tokens) {
        & git rev-parse --verify --quiet "$token^{commit}" | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Tag or ref '$token' from range '$RangeSpec' was not found."
            exit 1
        }
    }

    if ($tokens.Count -eq 1) {
        return @{ Base = $tokens[0]; End = $null }
    }

    & git merge-base --is-ancestor $tokens[0] $tokens[1] 2>$null
    if ($LASTEXITCODE -eq 0) { return @{ Base = $tokens[0]; End = $tokens[1] } }

    & git merge-base --is-ancestor $tokens[1] $tokens[0] 2>$null
    if ($LASTEXITCODE -eq 0) { return @{ Base = $tokens[1]; End = $tokens[0] } }

    Write-Error "Tags '$($tokens[0])' and '$($tokens[1])' are not on a common line - cannot order the range '$RangeSpec'."
    exit 1
}

# Fetches the titles of the merged PRs once, so the notes show real titles
# instead of branch names. Silently degrades to a numbered fallback when gh
# is unavailable or fails.
function Get-PrTitles {
    param([int[]]$Numbers)
    $map = @{}
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { return $map }

    foreach ($number in $Numbers) {
        $title = & gh pr view $number --json title --jq .title 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($title)) {
            $map[$number] = ($title | Select-Object -First 1)
        }
    }
    return $map
}

function Build-GroupedNotes {
    param([string]$BaseTag, [string]$EndRef)

    $end = if ([string]::IsNullOrWhiteSpace($EndRef)) { 'HEAD' } else { $EndRef }

    # First-parent mainline: merge commits and direct-to-main commits only -
    # the branch commits hang off the second parents and are collected per PR.
    $raw = & git log --first-parent --format='%H%x09%s' "$BaseTag..$end"
    if (-not $raw) { return $null }

    $mainline = @($raw | ForEach-Object {
        $parts = $_ -split "`t", 2
        $prNumber = 0
        $branch = ''
        if ($parts[1] -match '^Merge pull request #(\d+) from ([^/]+)/(.+)$') {
            $prNumber = [int]$matches[1]
            $branch = $matches[2]
        }
        [pscustomobject]@{ Hash = $parts[0]; Subject = $parts[1]; PrNumber = $prNumber; Branch = $branch }
    })

    $prMerges = @($mainline | Where-Object { $_.PrNumber -gt 0 })

    $prNumbers = @($prMerges | ForEach-Object { $_.PrNumber })
    $titles = if ($prMerges.Count -gt 0) { Get-PrTitles -Numbers $prNumbers } else { @{} }

    $sections = [System.Collections.Generic.List[string]]::new()

    foreach ($merge in $prMerges) {
        $prNumber = $merge.PrNumber

        # The PR's own commits: reachable from the branch tip (second parent)
        # but not from the mainline before the merge (first parent).
        $branchRaw = & git log --no-merges --format='%s' "$($merge.Hash)^1..$($merge.Hash)^2" 2>$null
        if (-not $branchRaw) { continue }

        $index = 0
        $commits = @($branchRaw | ForEach-Object {
            [pscustomobject]@{ Subject = $_; Index = $index++ }
        })

        $title = $titles[$prNumber]
        if (-not $title) {
            # No gh - name the section after the merged branch instead.
            $title = "Pull request #$prNumber"
            if ($merge.Branch) { $title += " ($($merge.Branch))" }
        }

        $sections.Add("#### $title (#$prNumber)`n" + (($commits | ForEach-Object { "- $($_.Subject)" }) -join "`n"))
    }

    # Commits that landed on main directly, without a PR: everything on the
    # first-parent path that is not a PR merge. Automatic release commits
    # ("Release: vX") are bookkeeping, not changes - they stay out.
    $directCommits = @(
        for ($i = $mainline.Count - 1; $i -ge 0; $i--) {
            if ($mainline[$i].PrNumber -eq 0 -and $mainline[$i].Subject -notmatch '^Release: v') {
                [pscustomobject]@{ Subject = $mainline[$i].Subject; Index = $mainline.Count - $i }
            }
        }
    )

    if ($directCommits.Count -gt 0) {
        $grouped = [System.Collections.Generic.List[string]]::new()
        foreach ($type in $commitTypeOrder) {
            $ofType = @($directCommits | Where-Object { (Get-CommitType $_.Subject) -eq $type })
            if ($ofType.Count -eq 0) { continue }

            $label = $type
            $bullets = ($ofType | Sort-Object Index | ForEach-Object { "- $(Get-CommitSubject $_.Subject)" }) -join "`n"
            $grouped.Add("*$label*`n$bullets")
        }

        if ($grouped.Count -gt 0) {
            # The wrapper only makes sense next to PR sections - when the
            # cycle had none, every commit is an "other" change and the type
            # groups go straight under the section heading.
            if ($sections.Count -gt 0) {
                $sections.Add("##### Other changes`n" + ($grouped -join "`n`n"))
            } else {
                foreach ($group in $grouped) { $sections.Add($group) }
            }
        }
    }

    if ($sections.Count -eq 0) { return $null }
    return $sections
}

# Função auxiliar para extrair o git log e filtrar commits automáticos de release
function Get-FilteredGitLog {
    param([string]$Range)
    $logs = if ($Range) { & git log --oneline --no-merges $Range } else { & git log --oneline --no-merges HEAD }
    return @($logs | Where-Object { $_ -notmatch '^\w+\s+Release:\s+v' })
}

function Build-FlatNotes {
    param([string]$BaseTag, [string]$EndRef)
    $end = if ([string]::IsNullOrWhiteSpace($EndRef)) { $null } else { $EndRef }
    $range = if ($end) { "$BaseTag..$end" } elseif ($BaseTag) { "$BaseTag..HEAD" } else { $null }
    $commits = Get-FilteredGitLog -Range $range
    if ($commits.Count -eq 0) { return $null }
    return $commits -join "`n"
}

# Adds one notes section: grouped by PR when the range has PR merges, flat
# commit list otherwise.
function Add-NotesSection {
    param([string]$Heading, [string]$BaseTag, [string]$EndRef)

    $grouped = Build-GroupedNotes -BaseTag $BaseTag -EndRef $EndRef
    if ($grouped) {
        $sections.Add("$Heading`n" + ($grouped -join "`n`n"))
        return
    }

    $flat = Build-FlatNotes -BaseTag $BaseTag -EndRef $EndRef
    if ($flat) {
        $sections.Add("$Heading`n$flat")
    }
}

# --- Construção das Release Notes (Cumulativo + Delta, agrupado por PR) ---
$allReachableTags = (& git tag --merged HEAD --sort=-creatordate 2>$null) | Where-Object { $_ -match '^v\d+\.\d+\.\d+' }

$immediateTag = $allReachableTags | Select-Object -First 1
$lastStableTag = $allReachableTags | Where-Object { $_ -match '^v\d+\.\d+\.\d+$' } | Select-Object -First 1

$sections = [System.Collections.Generic.List[string]]::new()

if ($PSBoundParameters.ContainsKey('Message') -and -not [string]::IsNullOrWhiteSpace($Message)) {
    $sections.Add("### 💡 Highlights`n$($Message.Trim())")
}

$noteRange = Resolve-NoteRange -RangeSpec $Range
if ($noteRange.Base) {
    $endLabel = if ($noteRange.End) { $noteRange.End } else { 'HEAD' }
    Write-Host "Notes range: $($noteRange.Base)..$endLabel"
    if ($noteRange.End) {
        Write-Warning "The range ends at $($noteRange.End), but the tag will still be created on HEAD."
    }
}

if ($isPreRelease) {
    Write-Host "Pre-release: '$finalVersion' | Last Stable: '$lastStableTag' | Immediate: '$immediateTag'"

    # 1. Delta imediato: O que mudou desde a tag anterior?
    if (-not [string]::IsNullOrWhiteSpace($immediateTag) -and $immediateTag -ne $lastStableTag) {
        Add-NotesSection "### 🆕 Changes in this build (since $immediateTag)" $immediateTag $null
    }

    # 2. Cumulativo: Escopo geral do ciclo (a custom range wins over the auto-detected stable tag)
    $cycleBase = if ($noteRange.Base) { $noteRange.Base } else { $lastStableTag }
    if (-not [string]::IsNullOrWhiteSpace($cycleBase)) {
        Add-NotesSection "### 📋 Full cycle changes (since $cycleBase)" $cycleBase $noteRange.End
    } else {
        $allCommits = Get-FilteredGitLog
        if ($allCommits.Count -gt 0) {
            $sections.Add("### 📋 All changes up to HEAD`n" + ($allCommits -join "`n"))
        }
    }
} else {
    Write-Host "Stable release: '$finalVersion' | Comparing against last stable: '$lastStableTag'"

    # A custom range wins over the auto-detected last stable tag.
    $stableBase = if ($noteRange.Base) { $noteRange.Base } else { $lastStableTag }
    if (-not [string]::IsNullOrWhiteSpace($stableBase)) {
        Add-NotesSection "### 🚀 What's Changed (since $stableBase)" $stableBase $noteRange.End
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
