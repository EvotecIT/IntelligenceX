function Resolve-ChatSmokeScenarioPreset {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,

        [string] $PresetName
    )

    if ([string]::IsNullOrWhiteSpace($PresetName)) {
        return @()
    }

    $scenariosRoot = Join-Path $RepoRoot 'IntelligenceX.Chat\scenarios'

    switch ($PresetName) {
        'runtime-only' {
            return @(
                (Join-Path $scenariosRoot 'chat-runtime-introspection-polish-broad-1-turn.json')
            )
        }
        'runtime-and-toolful' {
            return @(
                (Join-Path $scenariosRoot 'chat-runtime-introspection-polish-broad-1-turn.json'),
                (Join-Path $scenariosRoot 'ad-pl-eventlog-capability-followthrough-10-turn.json')
            )
        }
        default {
            throw "Unknown chat smoke scenario preset: $PresetName"
        }
    }
}
