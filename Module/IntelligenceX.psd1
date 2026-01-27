@{
    AliasesToExport      = @()
    Author               = 'Przemyslaw Klys'
    CmdletsToExport      = @('Backup-IntelligenceXThread', 'Connect-IntelligenceX', 'Disconnect-IntelligenceX', 'New-IntelligenceXThreadFork', 'Get-IntelligenceXAccount', 'Get-IntelligenceXCollaborationMode', 'Get-IntelligenceXConfig', 'Get-IntelligenceXConfigRequirements', 'Get-IntelligenceXCopilotInstall', 'Get-IntelligenceXHealth', 'Get-IntelligenceXLoadedThread', 'Get-IntelligenceXMcpServerStatus', 'Get-IntelligenceXModel', 'Get-IntelligenceXSkill', 'Get-IntelligenceXThread', 'Get-IntelligenceXTurnOutput', 'Initialize-IntelligenceX', 'Install-IntelligenceXCopilotCli', 'Invoke-IntelligenceXChat', 'Invoke-IntelligenceXCommand', 'Invoke-IntelligenceXRpc', 'Invoke-IntelligenceXMcpServerConfigReload', 'Request-IntelligenceXUserInput', 'Resume-IntelligenceXThread', 'Restore-IntelligenceXThread', 'Send-IntelligenceXFeedback', 'Send-IntelligenceXMessage', 'Set-IntelligenceXConfigBatch', 'Set-IntelligenceXConfigValue', 'Set-IntelligenceXSkill', 'Start-IntelligenceXApiKeyLogin', 'Start-IntelligenceXChatGptLogin', 'Start-IntelligenceXMcpOAuthLogin', 'Start-IntelligenceXReview', 'Start-IntelligenceXThread', 'Stop-IntelligenceXTurn', 'Wait-IntelligenceXLogin', 'Watch-IntelligenceXEvent')
    CompanyName          = 'Evotec'
    CompatiblePSEditions = @('Desktop', 'Core')
    Copyright            = '(c) 2011 - 2026 Przemyslaw Klys @ Evotec. All rights reserved.'
    Description          = 'IntelligenceX is a PowerShell module for the Codex app-server client.'
    FunctionsToExport    = @()
    GUID                 = '8fc3e038-c57b-44f3-bc7f-714beb3bd65a'
    HelpInfoURI          = 'https://github.com/EvotecIT/IntelligenceX/blob/master/README.md'
    ModuleVersion        = '0.1.0'
    PowerShellVersion    = '5.1'
    PrivateData          = @{
        PSData = @{
            IconUri                  = 'https://raw.githubusercontent.com/EvotecIT/IntelligenceX/master/Assets/Icons/IntelligenceX_128x128.png'
            ProjectUri               = 'https://github.com/EvotecIT/IntelligenceX'
            RequireLicenseAcceptance = $false
            Tags                     = @('Windows', 'MacOS', 'Linux')
        }
    }
    RootModule           = 'IntelligenceX.psm1'
}