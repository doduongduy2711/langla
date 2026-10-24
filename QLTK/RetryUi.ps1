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
 if($batch.state -eq 'waiting' -or $batch.closeOperation){return}
 foreach($m in $batch.members){
  if($m.PSObject.Properties['timeoutTeamRecovery'] -and $m.timeoutTeamRecovery -and (Test-ActivityMemberYielded $m) -and (Test-ActivityTeamEnded $kind $batch ([string]$m.id))){$m.job=$m.retiredJob;$m.slotYielded=$false;$m | Add-Member NoteProperty queueExited $true -Force;$m.timeoutTeamRecovery=$false}
  $closedJob=& ('Get-'+$kind+'MemberJob') $m;$a=$index[$m.id]
  if($closedJob -is [TGLL.NativeJob] -and $closedJob.ActivityTimeoutClosed -and !(& ('Test-'+$kind+'MemberRunning') $m) -and !($a -and (Test-AccountOpenBlocked $a)) -and (Test-ActivityTimeoutCloseFinished $closedJob) -and (Test-ActivityTeamStillRunning $kind $batch ([string]$m.id))){
   $m | Add-Member NoteProperty queueExited $false -Force
   Retire-ActivityMember $batch $m $closedJob;$m.retryAt=[datetime]::UtcNow
   $m | Add-Member NoteProperty timeoutTeamRecovery $true -Force;$m | Add-Member NoteProperty recoveryKind $kind -Force
   $batch.error='Đồng đội còn trong hoạt động; xếp hàng mở lại acc bị tắt do chờ ở làng'
  }
 }
 if($batch.state -eq 'done'){return}
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
  if($m.PSObject.Properties['timeoutTeamRecovery'] -and $m.timeoutTeamRecovery -and !(Test-ActivityTeamStillRunning ([string]$m.recoveryKind) $b ([string]$m.id))){continue}
  if((Test-ActivityMemberYielded $m) -and [datetime]::UtcNow -ge $m.retryAt){$m.slotYielded=$false;$b.state='launching';$restored++}
 }}
 return $restored
}
function Handle-ActivityLaunchError([string]$kind,$batch,$m,$index){
 $j=& ('Get-'+$kind+'MemberJob') $m;$a=$index[$m.id]
 if(!(& ('Test-'+$kind+'MemberRunning') $m) -and !($a -and (Test-AccountOpenBlocked $a))){Retire-ActivityMember $batch $m $j}
}

function Test-ActivityTeamStillRunning([string]$kind,$batch,[string]$excluded){
 if($kind -notin @('SonCap','Izanami')){return $false}
 $now=[datetime]::UtcNow
 foreach($peer in $batch.members){
  if([string]$peer.id -eq $excluded){continue};$j=& ('Get-'+$kind+'MemberJob') $peer
  if(!($j -is [TGLL.NativeJob]) -or $j.CloseRequested -or $j.CancelRequested -or $j.ReconnectRequested -or $j.State -in @('closed','failed','cancelled','reconnecting') -or !(& ('Test-'+$kind+'MemberRunning') $peer)){continue}
  if($j.ZoneReadAt -eq [datetime]::MinValue -or ($now-$j.ZoneReadAt).TotalSeconds -lt 0 -or ($now-$j.ZoneReadAt).TotalSeconds -ge 5){continue}
  if($kind -eq 'SonCap' -and $j.SonCapEnabled -and !$j.SonCapFinished -and $j.MapId -in @(94,95,97,93,96)){return $true}
  if($kind -eq 'Izanami' -and $j.IzanamiEnabled -and !$j.IzanamiRunFinished -and $j.MapId -eq 89){return $true}
 }
 return $false
}

function Test-ActivityTimeoutCloseFinished($j){
 $field=[TGLL.NativeJob].GetField('ActivityTimeoutCloseOperation',[Reflection.BindingFlags]'NonPublic,Instance')
 $operation=$field.GetValue($j);return (!$operation -or $operation.Completed)
}

function Test-ActivityTeamEnded([string]$kind,$batch,[string]$excluded){
 $seen=$false
 foreach($peer in $batch.members){
  if([string]$peer.id -eq $excluded){continue};$seen=$true;$j=& ('Get-'+$kind+'MemberJob') $peer
  if(!($j -is [TGLL.NativeJob])){return $false}
  $finished=if($kind -eq 'SonCap'){$j.SonCapFinished -or $j.SonCapNoAttempts}else{$j.IzanamiRunFinished}
  if(!$finished -and !($j.CloseRequested -or $j.CancelRequested -or $j.State -in @('closed','cancelled'))){return $false}
 }
 return $seen
}
