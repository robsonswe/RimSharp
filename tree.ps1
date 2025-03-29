function Show-Tree {
    param (
        [string]$Path = (Get-Location),
        [string]$Indent = "",
        [string[]]$Ignore = @('bin', 'obj'),
        [string]$SpecialFolder = 'ublock'
    )
    
    $Items = Get-ChildItem -Path $Path | Where-Object { $_.Name -notin $Ignore -or $_.Name -eq $SpecialFolder }
    
    foreach ($Item in $Items) {
        if ($Item.PSIsContainer) {
            Write-Host "$Indent|-- $($Item.Name)" -ForegroundColor Cyan
            
            # Only recurse if it's not the special folder
            if ($Item.Name -ne $SpecialFolder) {
                Show-Tree -Path $Item.FullName -Indent "$Indent|   " -Ignore $Ignore -SpecialFolder $SpecialFolder
            }
        } else {
            Write-Host "$Indent|-- $($Item.Name)" -ForegroundColor White
        }
    }
}

Show-Tree
