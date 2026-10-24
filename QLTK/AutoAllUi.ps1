# One complete queue per stage; never advance while a game from that stage is alive or launching.
$script:autoAllRun=$null;$script:loadingAutoAll=$false;$script:autoAllPumping=$false
$script:autoAllKinds=@('soncap','izanami','dungeon','quest','beast')
$script:autoAllNames=@('Sơn Cáp','Cấm thuật','Địa cung','Nhiệm vụ tuần hoàn','Linh thú')
$autoAllCheck=New-Object Windows.Forms.CheckBox;$autoAllCheck.Appearance='Button';$autoAllCheck.Text='Auto tất cả';$autoAllCheck.TextAlign='MiddleCenter';$autoAllCheck.SetBounds(235,84,115,22);$options.Controls.Add($autoAllCheck)
$autoAllTip=New-Object Windows.Forms.ToolTip
$autoAllTip.SetToolTip($autoAllCheck,'Chọn tất cả acc: Sơn Cáp → Cấm thuật → Địa cung → Nhiệm vụ tuần hoàn → Linh thú. Tắt game sau mỗi hoạt động; chờ đóng hết mới chuyển đợt.')
function Get-AutoAllStage([string]$id){
 if($script:autoAllRun -and $script:autoAllRun.active -and $script:autoAllRun.ids.ContainsKey($id) -and $script:autoAllRun.stage -lt 5){return $script:autoAllKinds[$script:autoAllRun.stage]};return ''
}
function Get-AutoAllBatches([string]$kind){$value=Get-Variable -Scope Script -Name ($kind+'Batches') -ValueOnly;foreach($batch in $value){Write-Output $batch}}
function Reset-AutoAllQueues {Reset-BeastQueue;Reset-SonCapQueue;Reset-IzanamiQueue;Reset-DungeonQueue;Reset-QuestQueue}
function Get-AutoAllAccounts {
 return @($script:accounts | Where-Object {$script:autoAllRun.ids.ContainsKey([string]$_.id)})
}
function Test-AutoAllAccountOpen([string]$id){
 $a=$script:accounts | Where-Object {[string]$_.id -eq $id} | Select-Object -First 1
 if(!$a){return $false};return (Test-AccountOpenBlocked $a)
}
function Suspend-AutoAllUp {
 foreach($a in @(Get-AutoAllAccounts)){
  if(!$script:accountJobs.ContainsKey([string]$a.id)){continue};$j=Get-AccountJob $script:accountJobs[[string]$a.id]
  if(!($j -is [TGLL.NativeJob]) -or $j.CloseRequested -or $j.CancelRequested -or $j.State -in @('closed','failed','cancelled')){continue}
  if($j.UpRequested -or $j.FarmEnabled -or $j.DailyManaged){[TGLL.NativeBackend]::StopMapFarm($j.Id)}
  $j.UpRequested=$false
 }
}
function Prepare-AutoAllGames {
 Suspend-AutoAllUp
 foreach($a in @(Get-AutoAllAccounts)){
  if(!$script:accountJobs.ContainsKey([string]$a.id)){continue};$j=Get-AccountJob $script:accountJobs[[string]$a.id]
  if(!($j -is [TGLL.NativeJob]) -or $j.CloseRequested -or $j.CancelRequested -or $j.State -in @('closed','failed','cancelled')){continue}
  if($j.BeastQuestEnabled){[TGLL.NativeBackend]::SetAutoBeast($j.Id,$false)}
  if($j.SonCapEnabled){[TGLL.NativeBackend]::SetSonCap($j.Id,$false)}
  [TGLL.NativeBackend]::SetAutoQuest($j.Id,$false);[TGLL.NativeBackend]::SetIzanami($j.Id,$false)
  [TGLL.NativeBackend]::SetReceiveKey($j.Id,$false);[TGLL.NativeBackend]::SetDungeonHigh($j.Id,$false);[TGLL.NativeBackend]::SetCloseAfterKey($j.Id,$false)
 }
}
function Set-AutoAllSelection {foreach($row in $grid.Rows){if($row.Tag -and $script:autoAllRun.ids.ContainsKey([string]$row.Tag.id) -and $row.Cells['pick'].Value -ne $true){$row.Cells['pick'].Value=$true}}}
function Set-AutoAllCloseOptions {foreach($check in @($soncapCloseAfter,$izanamiCloseAfter,$dungeonCloseAfter,$questCloseAfter,$beastCloseAfter)){if(!$check.Checked){$check.Checked=$true}}}
function Submit-AutoAllStage([string]$kind,$accounts){
 # Queue builders may set activity preferences; use copies to preserve each account's own saved configuration.
 $copies=@($accounts | ForEach-Object {$_.PSObject.Copy()})
 switch($kind){
  'soncap' {Save-SonCapConfig;Add-SonCapQueue $copies}
  'izanami' {Save-IzanamiQueueSettings;Add-IzanamiQueue $copies}
  'dungeon' {Save-DungeonQueueSettings;Add-DungeonQueue $copies}
  'quest' {Save-QuestQueueSettings;Add-QuestQueue $copies}
  'beast' {Save-BeastSettings;Add-BeastQueue $copies}
 }
}
function Start-AutoAll {
 if($script:autoAllRun -and $script:autoAllRun.active){return}
 if(!$script:accounts.Count){throw 'Chưa có tài khoản để chạy Auto tất cả.'}
 if(($script:mailRun -and $script:mailRun.active) -or ($script:distributionRun -and $script:distributionRun.active) -or ($script:cloakRun -and $script:cloakRun.active)){throw 'Dừng gom đồ/chia đồ/mua áo trước khi bật Auto tất cả.'}
 if($script:closingAll){throw 'Đợi tắt tất cả game xong rồi bật Auto tất cả.'}
 $ids=@{};foreach($a in $script:accounts){$ids[[string]$a.id]=$true}
 $draining=@();foreach($kind in $script:autoAllKinds){foreach($b in @(Get-AutoAllBatches $kind)){if($b.closeOperation){foreach($m in $b.members){$draining+=@([pscustomobject]@{id=$m.id;operation=$b.closeOperation})}}}}
 $script:autoAllRun=[pscustomobject]@{active=$true;stage=0;submitted=$false;prepared=$false;ids=$ids;draining=$draining;retryAt=[datetime]::MinValue;message='Chuẩn bị Auto tất cả'}
 Reset-AutoAllQueues;$autoLoginCheck.Checked=$true;Set-AutoAllSelection;Set-AutoAllCloseOptions
 $script:loadingAutoAll=$true;try{$autoAllCheck.Checked=$true}finally{$script:loadingAutoAll=$false}
 Pump-AutoAll
}
function Stop-AutoAll([bool]$completed=$false){
 if(!$script:autoAllRun -or !$script:autoAllRun.active){return}
 $script:autoAllRun.active=$false
 if(!$completed){
  Reset-AutoAllQueues
  foreach($id in $script:autoAllRun.ids.Keys){if($script:accountJobs.ContainsKey($id)){$j=Get-AccountJob $script:accountJobs[$id];if($j -and $j.ProcessId -le 0 -and $j.State -in @('queued','starting','retrying')){$j.CancelRequested=$true}}}
 }
 $script:loadingAutoAll=$true;try{$autoAllCheck.Checked=$false;$autoAllCheck.Text='Auto tất cả'}finally{$script:loadingAutoAll=$false}
 $script:autoAllRun.message=$(if($completed){'Auto tất cả: đã xong 5/5 hoạt động và tắt hết game'}else{'Đã dừng Auto tất cả; không xếp thêm lượt hoặc chuyển hoạt động'})
 $status.Text=$script:autoAllRun.message;$autoAllTip.SetToolTip($autoAllCheck,$script:autoAllRun.message)
}
function Pump-AutoAll {
 if($script:autoAllPumping -or !$script:autoAllRun -or !$script:autoAllRun.active -or $script:closingAll){return}
 $script:autoAllPumping=$true
 try{
  Set-AutoAllSelection;Set-AutoAllCloseOptions;Suspend-AutoAllUp
  $run=$script:autoAllRun;$kind=$script:autoAllKinds[$run.stage];$name=$script:autoAllNames[$run.stage]
  if(!$run.prepared){
   foreach($pending in $run.draining){if(!$pending.operation.Completed -or (Test-AutoAllAccountOpen $pending.id)){$status.Text='Auto tất cả: chờ lượt tắt game cũ hoàn tất';return}}
   Prepare-AutoAllGames;$run.prepared=$true
  }
  if(!$run.submitted){
   if([datetime]::UtcNow -lt $run.retryAt){return}
   $accounts=@(Get-AutoAllAccounts)
   if($accounts.Count -ne $run.ids.Count){throw 'Danh sách acc đã thay đổi; dừng rồi bật lại Auto tất cả.'}
   try{Submit-AutoAllStage $kind $accounts;$run.submitted=$true}
   catch{$run.retryAt=[datetime]::UtcNow.AddSeconds(5);$run.message='Auto tất cả: chưa xếp được '+$name+' • '+$_.Exception.Message;$status.Text=$run.message;return}
  }
  $batches=@(Get-AutoAllBatches $kind)
  if(!$batches.Count){$run.message='Auto tất cả: hàng chờ '+$name+' đã bị hủy; bỏ tích rồi bật lại để chạy lại';$status.Text=$run.message;return}
  $remaining=@($batches | Where-Object {$_.state -ne 'done'}).Count
  $run.message='Auto tất cả '+($run.stage+1)+'/5: '+$name+' • Còn '+$remaining+' lượt; đợi xong và tắt hết game'
  $autoAllCheck.Text='Auto tất cả '+($run.stage+1)+'/5';$autoAllTip.SetToolTip($autoAllCheck,$run.message);$status.Text=$run.message
  if($remaining){return}
  foreach($id in $run.ids.Keys){if(Test-AutoAllAccountOpen $id){$run.message='Auto tất cả: '+$name+' đã xong; đợi xác nhận tắt hết game';$status.Text=$run.message;return}}
  Reset-AutoAllQueues;$run.stage++;$run.submitted=$false
  if($run.stage -ge 5){Stop-AutoAll $true}
 }catch{if($script:autoAllRun){$script:autoAllRun.message='Auto tất cả: '+$_.Exception.Message;$status.Text=$script:autoAllRun.message}}
 finally{$script:autoAllPumping=$false}
}
$autoAllCheck.Add_CheckedChanged({if($script:loadingAutoAll){return};if($autoAllCheck.Checked){try{Start-AutoAll}catch{$script:loadingAutoAll=$true;try{$autoAllCheck.Checked=$false}finally{$script:loadingAutoAll=$false};Show-Error $_.Exception.Message}}else{Stop-AutoAll}})
