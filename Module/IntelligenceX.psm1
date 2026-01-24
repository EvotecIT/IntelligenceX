$script:IxClient = $null

function Get-IntelligenceXClient {
    param(
        [Parameter(ValueFromPipeline = $true)]
        $Client
    )
    if ($null -ne $Client) {
        return $Client
    }
    return $script:IxClient
}

function Initialize-IntelligenceXAssembly {
    $moduleRoot = $PSScriptRoot
    $libRoot = Join-Path $moduleRoot 'Lib'
    $framework = if ($PSEdition -eq 'Desktop') { 'net472' } else { 'net8.0' }
    $primaryPath = Join-Path (Join-Path $libRoot $framework) 'IntelligenceX.PowerShell.dll'
    $fallbackPath = Join-Path $libRoot 'IntelligenceX.PowerShell.dll'

    $assemblyPath = if (Test-Path $primaryPath) { $primaryPath } elseif (Test-Path $fallbackPath) { $fallbackPath } else { $null }
    if (-not $assemblyPath) {
        throw "IntelligenceX.PowerShell.dll not found. Run Module/Build/Build-Module.ps1 to build the module."
    }

    if (-not ([System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.Location -eq $assemblyPath })) {
        Add-Type -Path $assemblyPath
    }
}

function Connect-IntelligenceX {
    [CmdletBinding()]
    param(
        [string]$ExecutablePath,
        [string]$Arguments,
        [string]$WorkingDirectory
    )

    Initialize-IntelligenceXAssembly
    $client = [IntelligenceX.PowerShell.PowerShellBridge]::Connect($ExecutablePath, $Arguments, $WorkingDirectory)
    $script:IxClient = $client
    return $client
}

function Disconnect-IntelligenceX {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true)]
        $Client
    )

    Initialize-IntelligenceXAssembly
    $resolved = Get-IntelligenceXClient -Client $Client
    if ($null -eq $resolved) {
        return
    }

    [IntelligenceX.PowerShell.PowerShellBridge]::Disconnect($resolved)
    if ($resolved -eq $script:IxClient) {
        $script:IxClient = $null
    }
}

function Initialize-IntelligenceX {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true)]
        $Client,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$Title,
        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    Initialize-IntelligenceXAssembly
    $resolved = Get-IntelligenceXClient -Client $Client
    if ($null -eq $resolved) {
        throw 'No active IntelligenceX client. Use Connect-IntelligenceX first.'
    }

    [IntelligenceX.PowerShell.PowerShellBridge]::Initialize($resolved, $Name, $Title, $Version)
}

function Start-IntelligenceXChatGptLogin {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true)]
        $Client
    )

    Initialize-IntelligenceXAssembly
    $resolved = Get-IntelligenceXClient -Client $Client
    if ($null -eq $resolved) {
        throw 'No active IntelligenceX client. Use Connect-IntelligenceX first.'
    }

    return [IntelligenceX.PowerShell.PowerShellBridge]::StartChatGptLogin($resolved)
}

function Start-IntelligenceXApiKeyLogin {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true)]
        $Client,
        [Parameter(Mandatory = $true)]
        [string]$ApiKey
    )

    Initialize-IntelligenceXAssembly
    $resolved = Get-IntelligenceXClient -Client $Client
    if ($null -eq $resolved) {
        throw 'No active IntelligenceX client. Use Connect-IntelligenceX first.'
    }

    [IntelligenceX.PowerShell.PowerShellBridge]::LoginWithApiKey($resolved, $ApiKey)
}

function Wait-IntelligenceXLogin {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true)]
        $Client,
        [string]$LoginId,
        [int]$TimeoutSeconds = 300
    )

    Initialize-IntelligenceXAssembly
    $resolved = Get-IntelligenceXClient -Client $Client
    if ($null -eq $resolved) {
        throw 'No active IntelligenceX client. Use Connect-IntelligenceX first.'
    }

    [IntelligenceX.PowerShell.PowerShellBridge]::WaitForLogin($resolved, $LoginId, $TimeoutSeconds)
}

function Get-IntelligenceXAccount {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true)]
        $Client
    )

    Initialize-IntelligenceXAssembly
    $resolved = Get-IntelligenceXClient -Client $Client
    if ($null -eq $resolved) {
        throw 'No active IntelligenceX client. Use Connect-IntelligenceX first.'
    }

    return [IntelligenceX.PowerShell.PowerShellBridge]::GetAccount($resolved)
}

function Start-IntelligenceXThread {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true)]
        $Client,
        [Parameter(Mandatory = $true)]
        [string]$Model,
        [string]$CurrentDirectory,
        [string]$ApprovalPolicy,
        [string]$Sandbox
    )

    Initialize-IntelligenceXAssembly
    $resolved = Get-IntelligenceXClient -Client $Client
    if ($null -eq $resolved) {
        throw 'No active IntelligenceX client. Use Connect-IntelligenceX first.'
    }

    return [IntelligenceX.PowerShell.PowerShellBridge]::StartThread($resolved, $Model, $CurrentDirectory, $ApprovalPolicy, $Sandbox)
}

function Send-IntelligenceXMessage {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true)]
        $Client,
        [Parameter(Mandatory = $true)]
        [string]$ThreadId,
        [Parameter(Mandatory = $true)]
        [string]$Text
    )

    Initialize-IntelligenceXAssembly
    $resolved = Get-IntelligenceXClient -Client $Client
    if ($null -eq $resolved) {
        throw 'No active IntelligenceX client. Use Connect-IntelligenceX first.'
    }

    return [IntelligenceX.PowerShell.PowerShellBridge]::StartTurn($resolved, $ThreadId, $Text)
}

Export-ModuleMember -Function \
    'Connect-IntelligenceX', \
    'Disconnect-IntelligenceX', \
    'Initialize-IntelligenceX', \
    'Start-IntelligenceXChatGptLogin', \
    'Start-IntelligenceXApiKeyLogin', \
    'Wait-IntelligenceXLogin', \
    'Get-IntelligenceXAccount', \
    'Start-IntelligenceXThread', \
    'Send-IntelligenceXMessage'
