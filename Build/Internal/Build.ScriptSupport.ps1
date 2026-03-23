function Write-Header {
    param([Parameter(Mandatory)][string] $Text)

    Write-Host "`n=== $Text ===" -ForegroundColor Cyan
}

function Write-Step {
    param([Parameter(Mandatory)][string] $Text)

    Write-Host "[+] $Text" -ForegroundColor Yellow
}

function Write-Ok {
    param([Parameter(Mandatory)][string] $Text)

    Write-Host "[OK] $Text" -ForegroundColor Green
}

function Write-Warn {
    param([Parameter(Mandatory)][string] $Text)

    Write-Host "[!] $Text" -ForegroundColor DarkYellow
}

function Format-CommandLine {
    param(
        [Parameter(Mandatory)]
        [string] $Command,
        [string[]] $Arguments
    )

    $parts = [System.Collections.Generic.List[string]]::new()
    $parts.Add($Command)

    foreach ($argument in @($Arguments)) {
        if ($null -eq $argument) {
            continue
        }

        $text = [string] $argument
        if ($text.IndexOfAny([char[]]@(' ', "`t", '"', "'")) -ge 0) {
            $parts.Add("'" + $text.Replace("'", "''") + "'")
        } else {
            $parts.Add($text)
        }
    }

    return ($parts -join ' ')
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)]
        [string[]] $Args,
        [Parameter(Mandatory)]
        [string] $WorkingDirectory,
        [string] $FailureContext,
        [string] $FailureHint
    )

    $commandLine = Format-CommandLine -Command 'dotnet' -Arguments $Args

    Push-Location $WorkingDirectory
    try {
        & dotnet @Args
        $exitCode = $LASTEXITCODE
    } finally {
        Pop-Location
    }

    if ($exitCode -eq 0) {
        return
    }

    $message = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($FailureContext)) {
        $message.Add($FailureContext)
    }
    $message.Add("dotnet command failed with exit code ${exitCode}.")
    $message.Add("Command: $commandLine")
    if (-not [string]::IsNullOrWhiteSpace($FailureHint)) {
        $message.Add("Hint: $FailureHint")
    }

    throw ($message -join [Environment]::NewLine)
}

function Invoke-ScriptFile {
    param(
        [Parameter(Mandatory)]
        [string] $ScriptPath,
        [Parameter(Mandatory)]
        [hashtable] $Parameters,
        [string] $FailureContext,
        [string] $FailureHint
    )

    & $ScriptPath @Parameters
    $exitCode = $LASTEXITCODE
    if ($exitCode -eq 0) {
        return
    }

    $message = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($FailureContext)) {
        $message.Add($FailureContext)
    }
    $message.Add("Script failed with exit code ${exitCode}: $ScriptPath")
    if (-not [string]::IsNullOrWhiteSpace($FailureHint)) {
        $message.Add("Hint: $FailureHint")
    }

    throw ($message -join [Environment]::NewLine)
}
