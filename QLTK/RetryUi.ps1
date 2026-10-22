$script:failedRetryTimes=@{};$script:failedRetryCounts=@{}
function Retry-FailedAccounts {
    # Submit at most one reopen per second, while native startup keeps its own retries.
    $members=@{}
    foreach($pair in @(@('soncap',$script:soncapBatches),@('izanami',$script:izanamiBatches),@('quest',$script:questBatches),@('dungeon',$script:dungeonBatches))){foreach($b in $pair[1]){if($b.state -in @('waiting','done','closing','finished')){continue};foreach($m in $b.members){$members[$m.id]=[pscustomobject]@{kind=$pair[0];member=$m;batch=$b}}}}
    foreach($a in $script:accounts){
        $key=[string]$a.id;if(!$script:accountJobs.ContainsKey($key)){continue}
        $j=Get-AccountJob $script:accountJobs[$key]
        if($null -ne $j -and !$j.CloseRequested -and !$j.CancelRequested -and $j.State -in @('manual_entry','startup_unavailable','npc_unavailable')){
            [TGLL.NativeBackend]::RecoverStalledSession([string]$j.Id)
        }
        if($null -eq $j -or ($j.MailQueueOnly -or $j.CloakQueueOnly) -or $j.State -ne 'failed' -or $j.CloseRequested -or $j.CancelRequested){continue}
        if($j.Error -eq 'game_disconnected' -and ![TGLL.NativeBackend]::AutoReconnectEnabled){continue}
        $now=[DateTime]::UtcNow
        if(!$script:failedRetryTimes.ContainsKey($key)){$script:failedRetryTimes[$key]=$now.AddSeconds(5);continue}
        if($now -lt $script:failedRetryTimes[$key] -or (Test-AccountOpenBlocked $a)){continue}
        $entry=$members[$key];$marker=''
        if($entry){$marker=$entry.kind+'LaunchingAccountId';Set-Variable -Scope Script -Name $marker -Value $key}
        try{
            Open-Account $a ($j.Error -in @('game_disconnected','character_screen_not_ready','startup_popup_not_ready','game_exited'))
            if($script:accountJobs[$key] -ne $j.Id){
                if($entry){$entry.member.job=$script:accountJobs[$key];$entry.batch.state=if(@($entry.batch.members | Where-Object {!$_.job}).Count){'launching'}else{'running'};$entry.batch.error='';if($entry.kind -eq 'izanami'){Sync-IzanamiTeams};if($entry.kind -eq 'soncap'){Sync-SonCapTeams}}
                $script:failedRetryCounts[$key]=[int]$script:failedRetryCounts[$key]+1
                $status.Text='Đang đăng nhập lại: '+$a.username
            }
        }catch{$status.Text='Chưa mở lại được: '+$a.username}
        finally{if($marker){Set-Variable -Scope Script -Name $marker -Value ''}}
        $delay=[Math]::Min(30,5+[int]$script:failedRetryCounts[$key]*5);$script:failedRetryTimes[$key]=[DateTime]::UtcNow.AddSeconds($delay)
        break
    }
}
