# Copyright (c) 2026 Dennis Liu. All rights reserved.
@{
    RootModule = 'NvtGhApp.psm1'
    ModuleVersion = '0.1.0'
    PowerShellVersion = '7.4'
    Copyright = 'Copyright (c) 2026 Dennis Liu. All rights reserved.'
    Description = 'Repository-scoped GitHub App writes and owner-authenticated reads.'
    FunctionsToExport = @(
        'Import-GhAppConfig', 'Push-GhAppBranch', 'New-GhAppPullRequest',
        'Set-GhAppPullRequestBody', 'Update-GhAppPullRequestBranch', 'Add-GhAppComment', 'Sync-GhAppLabels', 'Add-GhAppReviewRecord',
        'Get-GhAppPullRequestReadiness', 'Request-GhAppOwnerReview', 'Merge-GhAppApprovedPullRequest', 'Remove-GhAppMergedBranch',
        'Close-GhAppPullRequest', 'Invoke-GhAppRead'
    )
    CmdletsToExport = @()
    VariablesToExport = @()
    AliasesToExport = @()
}
