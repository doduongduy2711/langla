$script:failedRetryTimes=@{};$script:failedRetryCounts=@{}
function Retry-FailedAccounts {
    # Submit at most one reopen per second, while native startup keeps its own retries.
    $owned=@{};foreach($queue in @($script:beastBatches)+@($script:soncapBatches)+@($script:izanamiBatches)+@($script:questBatches)+@($script:dungeonBatches)){if($queue.state -ne 'done'){foreach($member in $queue.members){$owned[[string]$member.id]=$true}}}
    $members=@{}
    foreach($pair in @(@('beast',$script:beastBatches),@('soncap',$script:soncapBatches),@('izanami',$script:izanamiBatches),@('quest',$script:questBatches),@('dungeon',$script:dungeonBatches))){foreach($b in $pair[1]){if($b.state -in @('waiting','done','closing','finished')){continue};foreach($m in $b.members){$members[$m.id]=[pscustomobject]@{kind=$pair[0];member=$m;batch=$b}}}}
    foreach($a in $script:accounts){
        $key=[string]$a.id;if(!$script:accountJobs.ContainsKey($key)){continue}
        $j=Get-AccountJob $script:accountJobs[$key]
        if($null -ne $j -and !$j.CloseRequested -and !$j.CancelRequested -and $j.State -in @('manual_entry','startup_unavailable','npc_unavailable')){
            [TGLL.NativeBackend]::RecoverStalledSession([string]$j.Id)
        }
        if($null -eq $j -or ($j.MailQueueOnly -or $j.CloakQueueOnly) -or $j.State -ne 'failed' -or $j.CloseRequested -or $j.CancelRequested){continue}
        if($j.Error -eq 'game_disconnected' -and ![TGLL.NativeBackend]::AutoReconnectEnabled){continue}
        if($owned.ContainsKey($key)){continue}
        $now=[DateTime]::UtcNow
        if(!$script:failedRetryTimes.ContainsKey($key)){$script:failedRetryTimes[$key]=$now.AddSeconds(5);continue}
        if($now -lt $script:failedRetryTimes[$key] -or (Test-AccountOpenBlocked $a)){continue}
        $entry=$members[$key];$marker=''
        if($entry){$marker=$entry.kind+'LaunchingAccountId';Set-Variable -Scope Script -Name $marker -Value $key}
        try{
            Open-Account $a ($j.Error -in @('game_disconnected','character_screen_not_ready','startup_popup_not_ready','game_exited'))
            if($script:accountJobs[$key] -ne $j.Id){
                if($entry){$entry.member.job=$script:accountJobs[$key];$entry.batch.state=if(@($entry.batch.members | Where-Object {!$_.job}).Count){'launching'}else{'running'};$entry.batch.error='';if($entry.kind -eq 'izanami'){Sync-IzanamiTeams};if($entry.kind -eq 'soncap'){Sync-SonCapTeams};if($entry.kind -eq 'beast'){Sync-BeastTeams}}
                $script:failedRetryCounts[$key]=[int]$script:failedRetryCounts[$key]+1
                $status.Text='Đang đăng nhập lại: '+$a.username
            }
        }catch{$status.Text='Chưa mở lại được: '+$a.username}
        finally{if($marker){Set-Variable -Scope Script -Name $marker -Value ''}}
        $delay=[Math]::Min(30,5+[int]$script:failedRetryCounts[$key]*5);$script:failedRetryTimes[$key]=[DateTime]::UtcNow.AddSeconds($delay)
        break
    }
}

# Reservations belong to actual/pending sessions, not permanently failed login attempts.
function Test-ActivityMemberYielded($m){return ($m.PSObject.Properties['slotYielded'] -and $m.slotYielded)}
function Test-ActivityMemberExited($m){return ($m.PSObject.Properties['queueExited'] -and $m.queueExited)}
function Get-ActivityReservedCount($batch){return @($batch.members | Where-Object {!(Test-ActivityMemberYielded $_) -and !(Test-ActivityMemberExited $_)}).Count}
function Retire-ActivityMember($batch,$m,$j){
 if($j -and $j.State -eq 'failed'){$j.CancelRequested=$true}
 $m | Add-Member NoteProperty retiredJob ([string]$m.job) -Force
 $m.job='';$m | Add-Member NoteProperty slotYielded $true -Force
 $m | Add-Member NoteProperty retryAt ([datetime]::UtcNow.AddSeconds(30)) -Force
 $batch.state='launching';$batch.error='Mở game lỗi; đã nhả lượt, chờ thử lại qua hàng đợi'
}
function Sync-ActivityCapacity([string]$kind,$batch,$index){
 if($batch.state -in @('waiting','done') -or $batch.closeOperation){return}
 foreach($m in $batch.members){
  if(Test-ActivityMemberExited $m){continue}
  if(Test-ActivityMemberYielded $m){
   $a=$index[$m.id];if($a -and (Test-AccountOpenBlocked $a) -and $script:accountJobs.ContainsKey([string]$m.id) -and [string]$script:accountJobs[[string]$m.id] -ne [string]$m.retiredJob){$m.job=$script:accountJobs[[string]$m.id];if($kind -eq 'SonCap'){[TGLL.NativeBackend]::RebindSonCapJob([string]$m.retiredJob,[string]$m.job)};$m.slotYielded=$false}else{continue}
  }
  $j=& ('Get-'+$kind+'MemberJob') $m;$a=$index[$m.id]
  if((& ('Test-'+$kind+'MemberRunning') $m) -or ($a -and (Test-AccountOpenBlocked $a))){continue}
  if($j -and $j.State -eq 'failed' -and !$j.CloseRequested -and !$j.CancelRequested){Retire-ActivityMember $batch $m $j}
  elseif($j -and ($j.CloseRequested -or $j.CancelRequested -or $j.State -in @('closed','cancelled'))){
   $m | Add-Member NoteProperty queueExited $true -Force
  }
 }
 if(@($batch.members | Where-Object {!(Test-ActivityMemberExited $_)}).Count -eq 0){$batch.state='done';$batch.error='Game đã thoát; đã trả lượt hoạt động'}
}
function Restore-ActivityReservations($batches,[int]$available){
 $restored=0
 foreach($b in $batches){if($b.state -in @('waiting','done') -or $b.closeOperation){continue};foreach($m in $b.members){
  if($restored -ge $available){return $restored}
  if((Test-ActivityMemberYielded $m) -and [datetime]::UtcNow -ge $m.retryAt){$m.slotYielded=$false;$b.state='launching';$restored++}
 }}
 return $restored
}
function Handle-ActivityLaunchError([string]$kind,$batch,$m,$index){
 $j=& ('Get-'+$kind+'MemberJob') $m;$a=$index[$m.id]
 if(!(& ('Test-'+$kind+'MemberRunning') $m) -and !($a -and (Test-AccountOpenBlocked $a))){Retire-ActivityMember $batch $m $j}
}
