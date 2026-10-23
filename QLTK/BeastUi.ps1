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
function Save-BeastSettings {$script:settings.beastMaxOnline=[int]$beastMax.Value;$script:settings.beastCloseAfter=$beastCloseAfter.Checked;if(!$UiTest){Write-JsonAtomic $script:configPath $script:settings}}
$beastMax.Add_ValueChanged({Save-BeastSettings});$beastCloseAfter.Add_CheckedChanged({Save-BeastSettings})
function Get-BeastQueuedMember($id){foreach($b in $script:beastBatches){if($b.state -eq 'waiting'){foreach($m in $b.members){if($m.id -eq $id){return $m}}}};return $null}
function Get-BeastMemberJob($m){if($script:accountJobs.ContainsKey($m.id)){$m.job=[string]$script:accountJobs[$m.id]};if($m.job){return Get-AccountJob $m.job};return $null}
function Test-BeastMemberRunning($m){$j=Get-BeastMemberJob $m;return ($j -and $j.ProcessId -gt 0 -and [TGLL.ManagedGames]::IsRunning($j.ProcessId))}
function Start-BeastMember($m){
 $a=$script:beastAccountIndex[$m.id];if(!$a){throw 'Tài khoản trong hàng chờ đã bị xóa.'}
 if(!(Test-AccountOpenBlocked $a)){$script:beastLaunchingAccountId=$m.id;try{Open-Account $a}finally{$script:beastLaunchingAccountId=''}}
 if(!$script:accountJobs.ContainsKey($m.id)){throw 'Linh thú cần phiên đăng nhập trực tiếp.'};$m.job=$script:accountJobs[$m.id];[TGLL.NativeBackend]::SetAutoBeast($m.job,$true)
}
function Begin-BeastTeamClose($b){return [TGLL.NativeBackend]::BeginAutoCloseBatch([string[]]@($b.members | ForEach-Object {$_.job}))}
function Sync-BeastTeams {foreach($b in $script:beastBatches){if($b.state -in @('launching','running','blocked') -and !$b.closeOperation){foreach($m in $b.members){$j=Get-BeastMemberJob $m;if($j -and !$j.CloseRequested -and !$j.CancelRequested){[TGLL.NativeBackend]::ConfigureBeastSolo($j.Id)}}}}}
function Add-BeastQueue($accounts){
 if(!$autoLoginCheck.Checked){throw 'Bật Đăng nhập trực tiếp trước.'}
 if(($script:distributionRun -and $script:distributionRun.active) -or ($script:mailRun -and $script:mailRun.active) -or ($script:cloakRun -and $script:cloakRun.active)){throw 'Dừng chia đồ/gom đồ/mua áo trước khi chạy linh thú.'}
 foreach($a in $accounts){foreach($b in @($script:soncapBatches)+@($script:izanamiBatches)+@($script:questBatches)+@($script:dungeonBatches)){if($b.state -ne 'done' -and @($b.members | Where-Object {$_.id -eq [string]$a.id}).Count){throw 'Acc đã có trong hàng chờ hoạt động khác.'}}}
 $known=@{};foreach($b in $script:beastBatches){foreach($m in $b.members){$known[$m.id]=$true}}
 $fresh=@($accounts | Where-Object {!$known.ContainsKey([string]$_.id)})
 if(!$fresh.Count){throw 'Các acc đã có trong hàng chờ linh thú.'}
 foreach($a in $fresh){$members=@([pscustomobject]@{id=[string]$a.id;job=''});$script:beastBatches+=@([pscustomobject]@{state='waiting';members=$members;closeOperation=$null;error=''})}
 Save-BeastSettings;$script:beastQueuePaused=$false;Pump-BeastQueue;Update-BeastUi
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
  if($b.state -in @('running','blocked') -and !$b.closeOperation){
   $all=$true;foreach($m in $b.members){$j=Get-BeastMemberJob $m;if(!$j -or !$j.QuestFinished){$all=$false}}
   if($all){$b.state='finished';$b.error=''}
  }
  if($b.state -eq 'finished' -and $beastCloseAfter.Checked -and [TGLL.NativeBackend]::CanAutoCloseBatch([string[]]@($b.members | ForEach-Object {$_.job}))){try{$b.closeOperation=Begin-BeastTeamClose $b;$b.state='closing'}catch{$b.state='blocked';$b.error='Chưa bắt đầu đóng được game linh thú'}}
  if($b.state -eq 'finished' -and ![TGLL.NativeBackend]::CanAutoCloseBatch([string[]]@($b.members | ForEach-Object {$_.job}))){foreach($m in $b.members){$j=Get-BeastMemberJob $m;if($j -and !$j.CloseRequested){[TGLL.NativeBackend]::SetAutoBeast($j.Id,$false)}};$b.state='done';$b.error='Đã xong linh thú; quay lại Up'}
  if($b.state -in @('closing','blocked') -and $b.closeOperation -and $b.closeOperation.Completed){
   $allClosed=$true;foreach($m in $b.members){if(Test-BeastMemberRunning $m){$allClosed=$false}}
   if($allClosed){$b.state='done';$b.error=''}elseif($b.closeOperation.Failed -or $b.closeOperation.Error){$b.state='blocked';$b.error='Chưa tắt được game; tiếp tục giữ lượt'}
  }
 }
 $occupied=0;foreach($b in $script:beastBatches){if($b.state -notin @('waiting','done')){$occupied+=$b.members.Count}}
 foreach($b in $script:beastBatches){if($b.state -ne 'waiting'){continue};if($occupied+$b.members.Count -gt [int]$beastMax.Value){break};$b.state='launching';$occupied+=$b.members.Count}
 $budget=[int]$launchConcurrency.Value
 foreach($b in $script:beastBatches){if($b.state -ne 'launching'){continue};foreach($m in $b.members){if($m.job){continue};if($budget -le 0){break};$budget--;try{Start-BeastMember $m}catch{$b.state='blocked';$b.error=$_.Exception.Message;break}};if($b.state -eq 'launching' -and @($b.members | Where-Object {!$_.job}).Count -eq 0){$b.state='running'}}
 Sync-BeastTeams
}
function Update-BeastUi {
 $live=0;$waiting=0;$done=0;foreach($b in $script:beastBatches){if($b.state -eq 'waiting'){$waiting+=$b.members.Count}elseif($b.state -eq 'done'){$done+=$b.members.Count}else{$live+=$b.members.Count}}
 $text="Đang giữ lượt: $live / $([int]$beastMax.Value) • Hàng chờ: $waiting • Đã xong: $done"
 if($grid.SelectedRows.Count){$key=[string]$grid.SelectedRows[0].Tag.id;if($script:accountJobs.ContainsKey($key)){$j=Get-AccountJob $script:accountJobs[$key];if($j -and $j.BeastQuestEnabled){$text+="`r`n"+$j.QuestStatus+"`r`n"+$j.TeamStatus}}}
 $errors=@($script:beastBatches | Where-Object {$_.error});if($errors.Count){$text+="`r`n"+$errors[0].error};$beastHint.Text=$text
}
$grid.Add_SelectionChanged({Update-BeastUi});Update-BeastUi

function Assert-BeastAccountsAvailable($accounts){foreach($a in $accounts){foreach($b in $script:beastBatches){if($b.state -ne 'done' -and @($b.members | Where-Object {$_.id -eq [string]$a.id}).Count){throw 'Acc đang trong hàng chờ linh thú; dừng hàng chờ đó trước.'}}}}
