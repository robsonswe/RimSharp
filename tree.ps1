function Show-Tree {
    param (
        [string]$Path = (Get-Location),
        [string]$Indent = "",
        [string[]]$Ignore = @('bin', 'obj', 'tmp', '.github', '.vscode'),
        [string]$SpecialFolder = 'ublock',
        [ref]$Output = [ref]::new('')
    )

    $Items = Get-ChildItem -Path $Path | Where-Object {
        ($_.Name -notin $Ignore -or $_.Name -eq $SpecialFolder) -and
        ($_.Extension -ne '.ps1')
    }

    foreach ($Item in $Items) {
        $Line = "$Indent|-- $($Item.Name)"
        $Output.Value += "$Line`n"

        if ($Item.PSIsContainer) {
            Write-Host $Line -ForegroundColor Cyan

            if ($Item.Name -ne $SpecialFolder) {
                Show-Tree -Path $Item.FullName -Indent "$Indent|   " -Ignore $Ignore -SpecialFolder $SpecialFolder -Output $Output
            }
        } else {
            Write-Host $Line -ForegroundColor White
        }
    }
}

# Create a ref to capture output
$outputRef = [ref]::new('')
Show-Tree -Output $outputRef

# Copy result to clipboard
$outputRef.Value | Set-Clipboard
