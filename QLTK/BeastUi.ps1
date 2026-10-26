$script:loadingBeast=$false;$script:beastApplied=@{}
$script:beastBatches=@();$script:beastQueuePaused=$false;$script:beastLaunchingAccountId=''
$beastPage=New-Object Windows.Forms.TabPage;$beastPage.Text='Auto Linh thú';$beastPage.BackColor=$form.BackColor;$beastPage.AutoScroll=$true;$tabs.TabPages.Add($beastPage)
$null=Ui-Label 'Chạy riêng từng tài khoản theo thứ tự đã chọn; không mời nhóm và không chờ tài khoản khác.' $beastPage 16 15 635 40
$null=Ui-Label 'Mỗi acc tự nhận/trả tại Rasa; tới bãi chọn khu khác các acc đang đánh linh thú cùng map, rồi đánh đúng quái.' $beastPage 16 58 635 50
$null=Ui-Label 'Số tài khoản tối đa:' $beastPage 16 116 230 24
$beastMax=New-Object Windows.Forms.NumericUpDown;$beastMax.Minimum=1;$beastMax.Maximum=9999;$beastMax.Increment=1;$beastMax.Value=12;$beastMax.SetBounds(260,114,85,25);if($script:settings.beastMaxOnline){$beastMax.Value=[Math]::Max(1,[Math]::Min(9999,[int]$script:settings.beastMaxOnline))};$beastPage.Controls.Add($beastMax)
$beastCloseAfter=New-Object Windows.Forms.CheckBox;$beastCloseAfter.Text='Tắt từng game khi acc đã trả xong hết nhiệm vụ linh thú';$beastCloseAfter.Checked=[bool]$script:settings.beastCloseAfter;$beastCloseAfter.SetBounds(16,150,630,27);$beastPage.Controls.Add($beastCloseAfter)
$beastStart=Ui-NewButton 'Xếp hàng linh thú đã chọn' {try{Add-BeastQueue @((Get-LaunchRows) | ForEach-Object {$_.Tag})}catch{Show-Error $_.Exception.Message}};Ui-Place $beastStart $beastPage 16 191 235 28
$beastStop=Ui-NewButton 'Dừng hàng chờ linh thú' {Reset-BeastQueue;Update-BeastUi};Ui-Place $beastStop $beastPage 266 191 215 28
$beastHint=Ui-Label '' $beastPage 16 232 635 175
# Keep the same per-account toggle convention as Quest/SonCap.
foreach($control in $beastPage.Controls){$control.Top+=68}
$beastCheck=New-Object Windows.Forms.CheckBox;$beastCheck.Text='Auto Linh thú (tối đa 3 lượt)';$beastCheck.SetBounds(16,12,580,27);$beastPage.Controls.Add($beastCheck)
$beastAccount=Ui-Label 'Chọn tài khoản trong bảng.' $beastPage 16 43 630 25
function Save-BeastSettings {$script:settings.beastMaxOnline=[int]$beastMax.Value;$script:settings.beastCloseAfter=$beastCloseAfter.Checked;if(!$UiTest){Write-JsonAtomic $script:configPath $script:settings}}
$beastMax.Add_ValueChanged({Save-BeastSettings});$beastCloseAfter.Add_CheckedChanged({Save-BeastSettings})
function Get-BeastQueuedMember($id){foreach($b in $script:beastBatches){if($b.state -eq 'waiting'){foreach($m in $b.members){if($m.id -eq $id){return $m}}}};return $null}
function Get-BeastMemberJob($m){if($script:accountJobs.ContainsKey($m.id)){$current=[string]$script:accountJobs[$m.id];if($m.PSObject.Properties['retiredJob'] -and $m.retiredJob -eq $current){return $null};$m.job=$current};if($m.job){return Get-AccountJob $m.job};return $null}
function Test-BeastMemberRunning($m){$j=Get-BeastMemberJob $m;return ($j -and $j.ProcessId -gt 0 -and [TGLL.ManagedGames]::IsRunning($j.ProcessId))}
function Start-BeastMember($m){
 $a=$script:beastAccountIndex[$m.id];if(!$a){throw 'Tài khoản trong hàng chờ đã bị xóa.'}
 if(!(Test-AccountOpenBlocked $a)){$script:beastLaunchingAccountId=$m.id;try{Open-Account $a}finally{$script:beastLaunchingAccountId=''}}
 if(!$script:accountJobs.ContainsKey($m.id)){throw 'Linh thú cần phiên đăng nhập trực tiếp.'};$m.job=$script:accountJobs[$m.id];[TGLL.NativeBackend]::SetAutoBeast($m.job,$true)
}
function Begin-BeastTeamClose($b){return [TGLL.NativeBackend]::BeginAutoCloseBatch([string[]]@($b.members | ForEach-Object {$_.job}))}
function Sync-BeastTeams {
 foreach($b in $script:beastBatches){if($b.state -notin @('launching','running','blocked') -or $b.closeOperation){continue}
  foreach($m in $b.members){$j=Get-BeastMemberJob $m
   if(!$j -or $j.CloseRequested -or $j.CancelRequested -or $j.State -in @('failed','closed','cancelled','reconnecting')){continue}
   try{[TGLL.NativeBackend]::ConfigureBeastSolo($j.Id);if($b.state -eq 'blocked' -and $j.BeastQuestEnabled -and ($j.QuestEnabled -or $j.QuestFinished)){$b.state='running';$b.error=''}}
   catch{$b.state='blocked';$b.error=[string]$_.Exception.Message}
  }
 }
}
function Add-BeastQueue($accounts){
 if(!$autoLoginCheck.Checked){throw 'Bật Đăng nhập trực tiếp trước.'}
 if(($script:distributionRun -and $script:distributionRun.active) -or ($script:mailRun -and $script:mailRun.active) -or ($script:cloakRun -and $script:cloakRun.active)){throw 'Dừng chia đồ/gom đồ/mua áo trước khi chạy linh thú.'}
 foreach($a in $accounts){foreach($b in @($script:soncapBatches)+@($script:izanamiBatches)+@($script:questBatches)+@($script:dungeonBatches)){if($b.state -ne 'done' -and @($b.members | Where-Object {$_.id -eq [string]$a.id}).Count){throw 'Acc đã có trong hàng chờ hoạt động khác.'}}}
 $known=@{};foreach($b in $script:beastBatches){foreach($m in $b.members){$known[$m.id]=$true}}
 $fresh=@($accounts | Where-Object {!$known.ContainsKey([string]$_.id)})
 if(!$fresh.Count){throw 'Các acc đã có trong hàng chờ linh thú.'}
 foreach($a in $fresh){$a | Add-Member NoteProperty autoBeast $true -Force;$members=@([pscustomobject]@{id=[string]$a.id;job=''});$script:beastBatches+=@([pscustomobject]@{state='waiting';members=$members;closeOperation=$null;error=''})}
 if(!$UiTest){Save-Accounts};Save-BeastSettings;$script:beastQueuePaused=$false;Pump-BeastQueue;Update-BeastUi
}
function Reset-BeastQueue {
 $script:beastQueuePaused=$true
 foreach($b in $script:beastBatches){foreach($m in $b.members){$j=Get-BeastMemberJob $m;if($j -and !$j.CloseRequested -and !$j.CancelRequested){if($j.State -eq 'failed'){$j.CancelRequested=$true}else{try{[TGLL.NativeBackend]::SetAutoBeast($j.Id,$false)}catch{}}}}}
 $script:beastBatches=@()
}
function Pump-BeastQueue {
 if($script:beastQueuePaused -or !$script:beastBatches.Count){return}
 $script:beastAccountIndex=@{};foreach($a in $script:accounts){$script:beastAccountIndex[[string]$a.id]=$a}
 Sync-BeastTeams
 foreach($b in $script:beastBatches){
  if($b.state -notin @('waiting','done') -and !$b.closeOperation){
   foreach($m in $b.members){$j=Get-BeastMemberJob $m;$a=$script:beastAccountIndex[$m.id]
    $running=Test-BeastMemberRunning $m;$opening=$a -and (Test-AccountOpenBlocked $a)
    if(!$running -and !$opening -and $j -and ($j.CloseRequested -or $j.CancelRequested -or $j.State -in @('closed','cancelled'))){$b.state='done';$b.error='Game đã thoát; đã trả lượt linh thú';break}
    if(!$running -and !$opening -and $j -and ($j.State -eq 'failed' -or (!$j.ReconnectRequested -and (Test-ActivityExitedSession $j)))){
     # Retry through this queue, not the global reopen loop, so a dead job cannot hold a slot forever.
     $j.CancelRequested=$true;$m | Add-Member NoteProperty retiredJob ([string]$m.job) -Force;$m.job='';$b.state='waiting';$b.error='Mở game lỗi; tạm nhường lượt và thử lại sau 30 giây'
     $b | Add-Member NoteProperty retryAt ([datetime]::UtcNow.AddSeconds(30)) -Force;break
    }
   }
  }
  if($b.state -in @('running','blocked') -and !$b.closeOperation){
   $all=$true;foreach($m in $b.members){$j=Get-BeastMemberJob $m;if(!$j -or !$j.QuestFinished){$all=$false}}
   if($all){$b.state='finished';$b.error=''}
  }
  if($b.state -eq 'finished' -and $beastCloseAfter.Checked -and [TGLL.NativeBackend]::CanAutoCloseBatch([string[]]@($b.members | ForEach-Object {$_.job}))){try{$b.closeOperation=Begin-BeastTeamClose $b;$b.state='closing'}catch{$b.state='blocked';$b.error='Chưa bắt đầu đóng được game linh thú'}}
  if($b.state -eq 'finished' -and ![TGLL.NativeBackend]::CanAutoCloseBatch([string[]]@($b.members | ForEach-Object {$_.job}))){foreach($m in $b.members){$j=Get-BeastMemberJob $m;if($j -and !$j.CloseRequested){[TGLL.NativeBackend]::SetAutoBeast($j.Id,$false)}};$b.state='done';$b.error='Đã xong linh thú; quay lại Up'}
  if($b.state -in @('closing','blocked') -and $b.closeOperation -and $b.closeOperation.Completed){
   $allClosed=$true;foreach($m in $b.members){$j=Get-BeastMemberJob $m;$a=$script:beastAccountIndex[$m.id]
    if(!$j -or (Test-BeastMemberRunning $m) -or ($a -and (Test-AccountOpenBlocked $a)) -or (!$j.CloseRequested -and !$j.CancelRequested -and $j.State -notin @('closed','cancelled') -and !(Test-ActivityExitedSession $j))){$allClosed=$false}}
   if($allClosed){$b.state='done';$b.error=''}elseif($b.closeOperation.Failed -or $b.closeOperation.Error){$b.state='blocked';$b.error='Chưa tắt được game; tiếp tục giữ lượt'}
  }
 }
 $occupied=0;foreach($b in $script:beastBatches){if($b.state -notin @('waiting','done')){$occupied+=$b.members.Count}}
 # A completed account reopened outside this queue also consumes a real slot.
 $included=@{};foreach($b in $script:beastBatches){if($b.state -notin @('waiting','done')){foreach($m in $b.members){$included[$m.id]=$true}}}
 $outside=0;foreach($a in $script:accounts){if(!$included.ContainsKey([string]$a.id) -and $script:accountJobs.ContainsKey([string]$a.id)){$j=Get-AccountJob $script:accountJobs[[string]$a.id];if($j -and $j.BeastQuestEnabled -and (Test-AccountOpenBlocked $a)){$outside++}}}
 foreach($b in $script:beastBatches){
  if($b.state -ne 'waiting' -or ($b.PSObject.Properties['retryAt'] -and [datetime]::UtcNow -lt $b.retryAt)){continue}
  $already=0;foreach($m in $b.members){$j=Get-BeastMemberJob $m;$a=$script:beastAccountIndex[$m.id];if($j -and $j.BeastQuestEnabled -and $a -and (Test-AccountOpenBlocked $a)){$already++}}
  if($occupied+$outside+$b.members.Count-$already -gt [int]$beastMax.Value){continue}
  $b.state='launching';$occupied+=$b.members.Count;$outside-=$already
 }
 $budget=[int]$launchConcurrency.Value
 foreach($b in $script:beastBatches){if($b.state -ne 'launching'){continue};foreach($m in $b.members){if($m.job){continue};if($budget -le 0){break};$budget--;try{Start-BeastMember $m}catch{$b.state='blocked';$b.error=$_.Exception.Message;$j=Get-BeastMemberJob $m;$a=$script:beastAccountIndex[$m.id]
   if(!(Test-BeastMemberRunning $m) -and !($a -and (Test-AccountOpenBlocked $a))){if($j -and $j.State -eq 'failed'){$j.CancelRequested=$true};$m | Add-Member NoteProperty retiredJob ([string]$m.job) -Force;$m.job='';$b.state='waiting';$b | Add-Member NoteProperty retryAt ([datetime]::UtcNow.AddSeconds(30)) -Force};break}};if($b.state -eq 'launching' -and @($b.members | Where-Object {!$_.job}).Count -eq 0){$b.state='running'}}
 Sync-BeastTeams
}
function Sync-BeastPreferences {
 foreach($a in $script:accounts){
  $all='';if(${function:Get-AutoAllStage}){$all=Get-AutoAllStage ([string]$a.id)}
  if(!$all -and !$a.PSObject.Properties['autoBeast']){continue}
  $key=[string]$a.id;$wanted=if($all){$all -eq 'beast'}else{[bool]$a.autoBeast};$j=$null
  if($script:accountJobs.ContainsKey($key)){$j=Get-AccountJob $script:accountJobs[$key]}
  $signature=$(if($j){$j.Id}else{''})+':'+$wanted
  if($script:beastApplied[$key] -eq $signature){continue}
  try{
   if(!$wanted){
    if($j -is [TGLL.NativeJob] -and $j.BeastQuestEnabled -and !$j.CloseRequested -and !$j.CancelRequested){[TGLL.NativeBackend]::SetAutoBeast($j.Id,$false)}
    $script:beastBatches=@($script:beastBatches | Where-Object {!@($_.members | Where-Object {$_.id -eq $key}).Count})
   }elseif($j -and !$j.CloseRequested -and !$j.CancelRequested -and $j.State -notin @('failed','closed','cancelled','reconnecting')){
    $known=@($script:beastBatches | Where-Object {@($_.members | Where-Object {$_.id -eq $key}).Count})
    if(!$known.Count){Add-BeastQueue @($a)}
   }
   $script:beastApplied[$key]=$signature
  }catch{$status.Text=$a.username+': '+$_.Exception.Message}
 }
}
function Set-BeastPreference($row,[bool]$enabled){
 $key=[string]$row.Tag.id;$row.Tag | Add-Member NoteProperty autoBeast $enabled -Force
 $script:beastApplied.Remove($key);if(!$UiTest){Save-Accounts};Sync-BeastPreferences;Update-BeastUi
}
function Update-BeastUi {
 $script:loadingBeast=$true
 try{
  $beastCheck.Enabled=($grid.SelectedRows.Count -gt 0)
  if($grid.SelectedRows.Count){$a=$grid.SelectedRows[0].Tag;$beastCheck.Checked=[bool]$a.autoBeast;$beastAccount.Text='Tài khoản: '+$a.username}
  else{$beastCheck.Checked=$false;$beastAccount.Text='Chọn tài khoản trong bảng.'}
 $live=0;$waiting=0;$done=0;foreach($b in $script:beastBatches){if($b.state -eq 'waiting'){$waiting+=$b.members.Count}elseif($b.state -eq 'done'){$done+=$b.members.Count}else{$live+=$b.members.Count}}
 $opened=0;$launching=0;$faulted=0
 foreach($b in $script:beastBatches){if($b.state -in @('waiting','done')){continue};foreach($m in $b.members){if(Test-BeastMemberRunning $m){$opened++}else{$launching++}};if($b.error){$faulted+=$b.members.Count}}
 $text="Đang giữ lượt: $live / $([int]$beastMax.Value) • Hàng chờ: $waiting • Đã xong: $done`r`nGame đang mở: $opened • Đang mở/đăng nhập: $launching • Lượt có lỗi: $faulted"
 if($grid.SelectedRows.Count){$key=[string]$grid.SelectedRows[0].Tag.id;if($script:accountJobs.ContainsKey($key)){$j=Get-AccountJob $script:accountJobs[$key];if($j -and $j.BeastQuestEnabled){$text+="`r`n"+$j.QuestStatus+"`r`n"+$j.TeamStatus}}}
 $errors=@($script:beastBatches | Where-Object {$_.error});if($errors.Count){$text+="`r`n"+$errors[0].error};$beastHint.Text=$text
 }finally{$script:loadingBeast=$false}
}
$beastCheck.Add_CheckedChanged({if($script:loadingBeast -or !$grid.SelectedRows.Count){return};try{Set-BeastPreference $grid.SelectedRows[0] $beastCheck.Checked}catch{Show-Error $_.Exception.Message}})
$grid.Add_SelectionChanged({Update-BeastUi});Update-BeastUi

function Assert-BeastAccountsAvailable($accounts){foreach($a in $accounts){foreach($b in $script:beastBatches){if($b.state -ne 'done' -and @($b.members | Where-Object {$_.id -eq [string]$a.id}).Count){throw 'Acc đang trong hàng chờ linh thú; dừng hàng chờ đó trước.'}}}}
