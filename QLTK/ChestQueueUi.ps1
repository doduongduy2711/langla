$script:chestRun=$null;$script:chestLaunchingAccountId=''
$null=Ui-Label 'Số tài khoản tối đa' $chestPage 330 85 155 24
$chestMax=New-Object Windows.Forms.NumericUpDown;$chestMax.Minimum=1;$chestMax.Maximum=10000;$chestMax.Value=6;$chestMax.SetBounds(490,85,85,25);$chestPage.Controls.Add($chestMax)
if($script:settings.ContainsKey('chestMaxOnline')){$chestMax.Value=[Math]::Max(1,[Math]::Min(10000,[int]$script:settings.chestMaxOnline))}
$chestMax.Add_ValueChanged({$script:settings.chestMaxOnline=[int]$chestMax.Value;if(!$UiTest){Write-JsonAtomic $script:configPath $script:settings}})
$chestClose=New-Object Windows.Forms.CheckBox;$chestClose.Text='Hoàn tất mua/mở rương thì tắt game';$chestClose.Checked=$true;$chestClose.SetBounds(16,174,600,25);$chestPage.Controls.Add($chestClose)
$chestBuy.Top=207;$chestUse.Top=207;$chestStop.Top=207;$chestHint.Top=248
$chestQueueStatus=Ui-Label '' $chestPage 16 345 650 48
function Get-ChestQueuedMember($id){if(!$script:chestRun -or !$script:chestRun.active -or $script:chestLaunchingAccountId -eq [string]$id){return $null};foreach($m in $script:chestRun.members){if($m.id -eq [string]$id -and $m.state -eq 'waiting'){return $m}};return $null}
function Get-ChestMemberJob($m){if($m.job){return Get-AccountJob $m.job};return $null}
function Test-ChestMemberRunning($m){$j=Get-ChestMemberJob $m;return ($j -and $j.ProcessId -gt 0 -and [TGLL.ManagedGames]::IsRunning([int]$j.ProcessId))}
function Begin-ChestMemberClose($m){return [TGLL.NativeBackend]::BeginCloseBatch([string[]]@($m.job))}
function Start-ChestMember($m){
 $a=@($script:accounts | Where-Object {[string]$_.id -eq $m.id}) | Select-Object -First 1;if(!$a){throw 'Tài khoản đã bị xóa.'}
 if($script:accountJobs.ContainsKey($m.id)){$m.job=$script:accountJobs[$m.id]}
 if(!(Test-ChestMemberRunning $m)){$script:chestLaunchingAccountId=$m.id;try{Open-Account $a $true}finally{$script:chestLaunchingAccountId=''};if(!$script:accountJobs.ContainsKey($m.id)){throw 'Chưa xếp mở game được.'};$m.job=$script:accountJobs[$m.id]}
 $j=Get-ChestMemberJob $m;if(!$j -or !$j.AutoLoginEnabled){throw 'Cần phiên đăng nhập trực tiếp.'}
 # This dedicated purchase worker uses the existing purchase-only session isolation.
 [TGLL.NativeBackend]::StopMapFarm($j.Id);$j.CloakQueueOnly=$true
 [TGLL.NativeBackend]::SetSonCap($j.Id,$false);[TGLL.NativeBackend]::SetReceiveKey($j.Id,$false);[TGLL.NativeBackend]::SetDungeonHigh($j.Id,$false);[TGLL.NativeBackend]::SetIzanami($j.Id,$false);[TGLL.NativeBackend]::SetAutoQuest($j.Id,$false);[TGLL.NativeBackend]::SetCloseAfterKey($j.Id,$false)
 [TGLL.NativeBackend]::SetGameAuto($j.Id,$false);[TGLL.NativeBackend]::StopTraining($j.Id)
}
function Start-StoneChestSelected([bool]$useOnly){
 try{
  if($script:chestRun -and $script:chestRun.active){throw 'Dừng hàng rương hiện tại trước.'}
  if(($script:mailRun -and $script:mailRun.active) -or ($script:cloakRun -and $script:cloakRun.active) -or ($script:distributionRun -and $script:distributionRun.active)){throw 'Dừng gom/chia đồ và mua áo trước.'}
  foreach($b in @($script:questBatches)+@($script:izanamiBatches)+@($script:dungeonBatches)+@($script:beastBatches)+@($script:soncapBatches)){if($b -and $b.state -ne 'done'){throw 'Dừng hàng chờ hoạt động khác trước.'}}
  $rows=@(Get-LaunchRows | Sort-Object Index);if(!$rows.Count){throw 'Tích Chọn các acc cần mua/mở rương.'}
  $members=@(foreach($row in $rows){$row.Tag | Add-Member NoteProperty stoneChestQuantity ([int]$chestQuantity.Value) -Force;$row.Tag | Add-Member NoteProperty stoneChestOpenAfter ([bool]$chestOpen.Checked) -Force;[pscustomobject]@{id=[string]$row.Tag.id;job='';state='waiting';status='Chờ lượt';closeOperation=$null}})
  $script:chestRun=[pscustomobject]@{active=$true;members=$members;quantity=[int]$chestQuantity.Value;openAfter=[bool]$chestOpen.Checked;useOnly=$useOnly;closeAfter=[bool]$chestClose.Checked}
  if(!$UiTest){Save-Accounts};Sync-StoneChestQueue
 }catch{$status.Text=$_.Exception.Message}
}
function Sync-ChestMember($run,$m){
 if($m.state -eq 'closing'){
  if(!$m.closeOperation.Completed){return}
  if($script:accountJobs.ContainsKey($m.id) -and $script:accountJobs[$m.id] -ne $m.job){$m.state='blocked';$m.status='Phiên game đã thay đổi';return}
  if(Test-ChestMemberRunning $m){if($m.closeOperation.Failed -or $m.closeOperation.Error){$m.state='blocked';$m.status='Chưa tắt được game'};return}
  $m.state='done';$m.status='Hoàn tất và đã tắt game';return
 }
 if($m.state -eq 'finished'){if(!(Test-ChestMemberRunning $m)){$m.state='done'};return}
 if($m.state -notin @('launching','running')){return}
 $j=Get-ChestMemberJob $m
 if(!$j -or ($script:accountJobs.ContainsKey($m.id) -and $script:accountJobs[$m.id] -ne $m.job)){$m.state='blocked';$m.status='Phiên game thay đổi; kiểm tra túi';return}
 if($j.CloseRequested -or $j.CancelRequested -or $j.State -in @('failed','closed','cancelled','reconnecting')){$m.state='blocked';$m.status='Game lỗi/thoát; kiểm tra trước khi mua lại';return}
 if($m.state -eq 'launching'){
  if(!$j.GameSettingsReady -or !$j.TrainReady){return}
  try{[TGLL.NativeBackend]::StartStoneChest($j.Id,$run.quantity,$run.openAfter,$run.useOnly);$m.state='running'}catch{$m.state='blocked';$m.status=$_.Exception.Message};return
 }
 $m.status=$j.ChestStatus
 if(!$j.ChestEnabled -and !$j.ChestUseOwned){
  $complete=$j.ChestStatus.StartsWith('Hoàn tất') -or (!$run.openAfter -and !$run.useOnly -and $j.ChestBought -eq $run.quantity)
  if(!$complete){$m.state='blocked';return}
  if(!$run.closeAfter){$m.state='finished';$m.status='Hoàn tất; giữ game, vẫn tính vào giới hạn';return}
  try{$m.closeOperation=Begin-ChestMemberClose $m;$m.state='closing'}catch{$m.state='blocked';$m.status='Chưa bắt đầu tắt được game'}
 }
}
function Sync-StoneChestQueue {
 if(!$script:chestRun -or !$script:chestRun.active){return};$run=$script:chestRun
 foreach($m in $run.members){Sync-ChestMember $run $m}
 $done=@($run.members | Where-Object {$_.state -eq 'done'}).Count
 if($done -eq $run.members.Count){$run.active=$false}
 $occupied=@($run.members | Where-Object {$_.state -notin @('waiting','done')}).Count;$budget=[int]$launchConcurrency.Value
 foreach($m in $run.members){if($occupied -ge [int]$chestMax.Value -or $budget -le 0){break};if($m.state -ne 'waiting'){continue};$m.state='launching';$occupied++;$budget--;try{Start-ChestMember $m}catch{$m.state='blocked';$m.status=$_.Exception.Message}}
 $occupied=@($run.members | Where-Object {$_.state -notin @('waiting','done')}).Count
 $chestQueueStatus.Text='Đang giữ lượt: '+$occupied+'/'+[int]$chestMax.Value+' • Chờ: '+@($run.members | Where-Object {$_.state -eq 'waiting'}).Count+' • Xong: '+$done+'/'+$run.members.Count
}
function Stop-StoneChestSelected {
 if($script:chestRun){$script:chestRun.active=$false;foreach($m in $script:chestRun.members){if($m.job){[TGLL.NativeBackend]::StopStoneChest($m.job)};$m.status='Đã dừng'}}
 foreach($row in @(Get-LaunchRows)){$key=[string]$row.Tag.id;if($script:accountJobs.ContainsKey($key)){[TGLL.NativeBackend]::StopStoneChest($script:accountJobs[$key])}}
 Update-StoneChestUi
}
