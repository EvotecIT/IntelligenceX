param(
    [Parameter(Mandatory = $true)]
    [string]$ToolFile,
    [string]$TestFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content,
        [Parameter(Mandatory = $true)]
        [string]$Pattern,
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if ($Content -notmatch $Pattern) {
        throw $Message
    }
}

if (-not (Test-Path -LiteralPath $ToolFile)) {
    throw "Tool file not found: $ToolFile"
}

$toolContent = Get-Content -LiteralPath $ToolFile -Raw

Assert-Contains -Content $toolContent -Pattern "WithWriteGovernanceMetadata\(\)" -Message "Missing schema extension: WithWriteGovernanceMetadata()."
Assert-Contains -Content $toolContent -Pattern "WithAuthenticationProbeReference\(\)" -Message "Missing schema extension: WithAuthenticationProbeReference()."
Assert-Contains -Content $toolContent -Pattern "GetWriteGovernanceContract\(\)" -Message "Missing GetWriteGovernanceContract() override."
Assert-Contains -Content $toolContent -Pattern "GetAuthenticationContract\(\)" -Message "Missing GetAuthenticationContract() override."
Assert-Contains -Content $toolContent -Pattern "ToolResponse\.OkWriteActionModel\(" -Message "Missing standardized write envelope response."

if (-not [string]::IsNullOrWhiteSpace($TestFile)) {
    if (-not (Test-Path -LiteralPath $TestFile)) {
        throw "Test file not found: $TestFile"
    }

    $testContent = Get-Content -LiteralPath $TestFile -Raw
    Assert-Contains -Content $testContent -Pattern "WriteGovernanceContract" -Message "Tests do not validate write governance contract."
    Assert-Contains -Content $testContent -Pattern "AuthenticationContract" -Message "Tests do not validate authentication contract."
    Assert-Contains -Content $testContent -Pattern "dry-run" -Message "Tests do not validate dry-run mode."
    Assert-Contains -Content $testContent -Pattern "apply" -Message "Tests do not validate apply mode."
}

Write-Host "Write tool checklist passed."
