param(
    [string] $ConfigPath = (Join-Path $PSScriptRoot 'project.build.json'),
    [Nullable[bool]] $UpdateVersions,
    [Nullable[bool]] $Build,
    [Nullable[bool]] $PublishNuget = $false,
    [Nullable[bool]] $PublishGitHub = $false,
    [Nullable[bool]] $Plan,
    [string] $PlanPath
)

Import-Module PSPublishModule -MinimumVersion 3.0.126 -Force -ErrorAction Stop

$invokeParameters = @{
    ConfigPath = $ConfigPath
}
if ($null -ne $UpdateVersions) { $invokeParameters.UpdateVersions = $UpdateVersions }
if ($null -ne $Build) { $invokeParameters.Build = $Build }
if ($null -ne $PublishNuget) { $invokeParameters.PublishNuget = $PublishNuget }
if ($null -ne $PublishGitHub) { $invokeParameters.PublishGitHub = $PublishGitHub }
if ($null -ne $Plan) { $invokeParameters.Plan = $Plan }
if (-not [string]::IsNullOrWhiteSpace($PlanPath)) { $invokeParameters.PlanPath = $PlanPath }

$result = Invoke-ProjectBuild @invokeParameters
$result

if ($null -ne $result -and
    $result.PSObject.Properties.Name -contains 'Success' -and
    -not $result.Success) {
    $message = if ([string]::IsNullOrWhiteSpace([string] $result.ErrorMessage)) {
        'Project build failed without an error message.'
    } else {
        [string] $result.ErrorMessage
    }
    throw $message
}
