param(
    [string] $ConfigPath = "$PSScriptRoot\..\powerforge.dotnetpublish.json",
    [string] $Target = 'IntelligenceX.Chat.App,IntelligenceX.Chat.Service',
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $PowerForgeArgs
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. (Join-Path $repoRoot 'Build\Internal\Resolve-PowerForgeCli.ps1')
$cli = Resolve-PowerForgeCliInvocation -RepoRoot $repoRoot
$args = [System.Collections.Generic.List[string]]::new()
$args.AddRange([string[]] $cli.Prefix)
$args.AddRange([string[]] @('dotnet', 'publish', '--config', [System.IO.Path]::GetFullPath($ConfigPath), '--target', $Target, '--style', 'PortableCompat'))
if ($PowerForgeArgs) { $args.AddRange([string[]] $PowerForgeArgs) }
& $cli.Command @args
if (-not $?) { exit 1 }
exit $(if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE })
