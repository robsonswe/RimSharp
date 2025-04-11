function Show-Tree {
    param (
        [string]$Path = (Get-Location),
        [string]$Indent = "",
        [string[]]$Ignore = @('bin', 'obj', 'tmp', '.github', '.vscode'),
        [string]$SpecialFolder = 'ublock'
    )

    $Items = Get-ChildItem -Path $Path | Where-Object {
        ($_.Name -notin $Ignore -or $_.Name -eq $SpecialFolder) -and
        ($_.Extension -ne '.ps1')
    }

    foreach ($Item in $Items) {
        if ($Item.PSIsContainer) {
            Write-Host "$Indent|-- $($Item.Name)" -ForegroundColor Cyan

            if ($Item.Name -ne $SpecialFolder) {
                Show-Tree -Path $Item.FullName -Indent "$Indent|   " -Ignore $Ignore -SpecialFolder $SpecialFolder
            }
        } else {
            Write-Host "$Indent|-- $($Item.Name)" -ForegroundColor White
        }
    }
}

Show-Tree
