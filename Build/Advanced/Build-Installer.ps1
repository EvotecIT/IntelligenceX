param(
    [string] $ConfigPath = "$PSScriptRoot\..\powerforge.dotnetpublish.json",
    [string] $Target = 'IntelligenceX.Chat.App,IntelligenceX.Chat.Service',
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $PowerForgeArgs
)

$cli = if ($env:POWERFORGE_CLI_PATH) { [System.IO.Path]::GetFullPath($env:POWERFORGE_CLI_PATH) } else { 'powerforge' }
$args = @('dotnet', 'publish', '--config', [System.IO.Path]::GetFullPath($ConfigPath), '--target', $Target, '--style', 'PortableCompat') + $PowerForgeArgs
if ([System.IO.Path]::GetExtension($cli) -ieq '.dll') { & dotnet $cli @args } else { & $cli @args }
exit $LASTEXITCODE
