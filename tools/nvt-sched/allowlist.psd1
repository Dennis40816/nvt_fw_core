# Copyright (c) 2026 Dennis Liu. All rights reserved.
@{
    'commander-tick' = @{
        Script = 'tick-shadow.ps1'
        Description = '觀察 session、佇列、磁碟與額度快取，每 {0} 分鐘及登入時執行；每週一 08:30 後產生排程清單。腳本：{1}（由 nvt-sched-runner.ps1 執行）；維護：commander。Observe session, queue, disk and quota caches every {0} minutes and at logon; audit tasks weekly after Monday 08:30. Script: {1} via nvt-sched-runner.ps1. Maintainer: commander.'
        # tick-shadow returns 10 when it observes a new change.
        SuccessExitCodes = @(0, 10)
    }
}
