$cloakPage=New-Object Windows.Forms.TabPage;$cloakPage.Text='Mua áo choàng';$cloakPage.BackColor=$form.BackColor;$cloakPage.AutoScroll=$true;$tabs.TabPages.Add($cloakPage)
$script:loadingCloak=$false;$script:cloakOffers=@([TGLL.NativeBackend]::DefaultCloakOffers())
$cloakAccount=Ui-Label 'Chọn tài khoản trong bảng.' $cloakPage 16 12 650 24
$null=Ui-Label 'Loại áo' $cloakPage 16 49 90 25
$cloakChoice=New-Object Windows.Forms.ComboBox;$cloakChoice.DropDownStyle='DropDownList';$cloakChoice.SetBounds(110,47,440,27);$cloakPage.Controls.Add($cloakChoice)
foreach($o in $script:cloakOffers){$null=$cloakChoice.Items.Add($o)};$cloakChoice.SelectedIndex=3
$null=Ui-Label 'Số lượng / acc' $cloakPage 16 89 140 25
$cloakQuantity=New-Object Windows.Forms.NumericUpDown;$cloakQuantity.Minimum=1;$cloakQuantity.Maximum=100;$cloakQuantity.Value=1;$cloakQuantity.SetBounds(165,87,120,27);$cloakPage.Controls.Add($cloakQuantity)
$null=Ui-Label 'Số acc mở cùng lúc' $cloakPage 300 89 158 25
$cloakMax=New-Object Windows.Forms.NumericUpDown;$cloakMax.Minimum=1;$cloakMax.Maximum=10000;$cloakMax.Value=6;$cloakMax.SetBounds(465,87,85,27);$cloakPage.Controls.Add($cloakMax)
if($script:settings.ContainsKey('cloakMaxOnline')){$cloakMax.Value=[Math]::Max(1,[Math]::Min(10000,[int]$script:settings.cloakMaxOnline))}
function Save-CloakQueueSettings {$script:settings.cloakMaxOnline=[int]$cloakMax.Value;if(!$UiTest){Write-JsonAtomic $script:configPath $script:settings}}
$cloakMax.Add_ValueChanged({Save-CloakQueueSettings})
$cloakPrice=Ui-Label '' $cloakPage 16 125 650 24
$null=Ui-Label 'Tự dùng phù ID 168 về Trường Konoha Gakuen và mua tại Senju Tobirama.' $cloakPage 16 154 650 26
$null=Ui-Label 'Áo Đệ Tam trong ảnh có hạn 2 tuần. Kiểm tra yêu cầu cấp/Tài phú trong game.' $cloakPage 16 181 650 26
$cloakBuy=New-Object Windows.Forms.Button;$cloakBuy.Text='Mua đã chọn';$cloakBuy.SetBounds(16,216,160,29);$cloakPage.Controls.Add($cloakBuy)
$cloakRead=New-Object Windows.Forms.Button;$cloakRead.Text='Đọc giá cửa hàng';$cloakRead.SetBounds(184,216,180,29);$cloakPage.Controls.Add($cloakRead)
$cloakStop=New-Object Windows.Forms.Button;$cloakStop.Text='Dừng đã chọn';$cloakStop.SetBounds(372,216,160,29);$cloakPage.Controls.Add($cloakStop)
$cloakHint=Ui-Label '' $cloakPage 16 260 650 85;$cloakHint.Anchor='Top,Left,Right'
function Get-CloakPreference($a){
 $id=131;$quantity=1
 if($null -ne $a){if($a.PSObject.Properties['cloakId']){$id=[int]$a.cloakId};if($a.PSObject.Properties['cloakQuantity']){$quantity=[Math]::Max(1,[Math]::Min(100,[int]$a.cloakQuantity))}}
 [pscustomobject]@{id=$id;quantity=$quantity}
}
function Update-CloakPrice {
 $o=$cloakChoice.SelectedItem;if($null -eq $o){$cloakPrice.Text='Chọn loại áo.';return}
 $cloakPrice.Text='Giá đã chọn: '+$o.CrystalPrice+' tinh thạch/áo • Tổng: '+([long]$o.CrystalPrice*[int]$cloakQuantity.Value)+' tinh thạch'
}
function Update-CloakUi {
 $script:loadingCloak=$true
 try{
  $has=$grid.SelectedRows.Count -gt 0;$cloakChoice.Enabled=$has;$cloakQuantity.Enabled=$has
  if(!$has){$cloakAccount.Text='Chọn tài khoản trong bảng.';$cloakHint.Text='';return}
  $a=$grid.SelectedRows[0].Tag;if($null -eq $a){return};if($null -ne $cloakEquipClose){$cloakEquipClose.Checked=(!$a.PSObject.Properties['cloakEquipClose']) -or [bool]$a.cloakEquipClose};$p=Get-CloakPreference $a;$cloakAccount.Text='Tài khoản: '+$a.username
  for($i=0;$i -lt $cloakChoice.Items.Count;$i++){if($cloakChoice.Items[$i].TemplateId -eq $p.id){$cloakChoice.SelectedIndex=$i;break}}
  if(!$cloakQuantity.Focused){$cloakQuantity.Value=$p.quantity};Update-CloakPrice
  $cloakHint.Text='Dừng Up/các auto hoạt động trước khi mua. Tích Chọn các acc đã mở rồi bấm Mua. Mua xong giữ áo trong túi.'
  if($script:accountJobs.ContainsKey([string]$a.id)){$j=Get-AccountJob $script:accountJobs[[string]$a.id];if($j -is [TGLL.NativeJob]){$cloakHint.Text=$j.CloakStatus+"`r`n"+'Đã mua: '+$j.CloakBought+'/'+$j.CloakQuantity}}
  if($script:cloakRun){$waiting=@($script:cloakRun.members | Where-Object {$_.state -eq 'waiting'}).Count;$busy=@($script:cloakRun.members | Where-Object {$_.state -in @('launching','running','closing')}).Count;$held=@($script:cloakRun.members | Where-Object {$_.state -eq 'blocked'}).Count;$cloakHint.Text+="`r`n"+'Đang chạy/đóng: '+$busy+'/'+[int]$cloakMax.Value+' • Chờ: '+$waiting+' • Xong: '+$script:cloakRun.cursor+'/'+$script:cloakRun.members.Count+' • Cần kiểm tra: '+$held}
 }finally{$script:loadingCloak=$false}
}
function Save-CloakUi {
 if($script:loadingCloak -or !$grid.SelectedRows.Count -or $null -eq $cloakChoice.SelectedItem){return}
 $a=$grid.SelectedRows[0].Tag;$a | Add-Member NoteProperty cloakId ([int]$cloakChoice.SelectedItem.TemplateId) -Force;$a | Add-Member NoteProperty cloakQuantity ([int]$cloakQuantity.Value) -Force
 if(!$UiTest){Save-Accounts};Update-CloakPrice
}
function Start-CloakDirectSelected {
 $rows=@(Get-LaunchRows);if(!$rows.Count){$status.Text='Tích Chọn các acc cần mua áo.';return}
 $offer=$cloakChoice.SelectedItem;if($null -eq $offer){$status.Text='Chọn áo cần mua.';return};$quantity=[int]$cloakQuantity.Value;$started=0;$errors=New-Object Collections.Generic.List[string]
 foreach($row in $rows){$a=$row.Tag
  try{
   if(!$script:accountJobs.ContainsKey([string]$a.id)){throw 'Acc chưa mở.'}
   [TGLL.NativeBackend]::Get($script:accountJobs[[string]$a.id]).CloakEquipAfter=$false;[TGLL.NativeBackend]::StartCloakPurchase($script:accountJobs[[string]$a.id],$offer.TemplateId,$quantity,$offer.CrystalPrice)
   $a | Add-Member NoteProperty cloakId ([int]$offer.TemplateId) -Force;$a | Add-Member NoteProperty cloakQuantity $quantity -Force;$started++
  }catch{$errors.Add($a.username+': '+$_.Exception.Message)}
 }
 if(!$UiTest){Save-Accounts};Update-CloakUi;$status.Text='Đã bắt đầu '+$started+' acc.';if($errors.Count){$status.Text+=' '+($errors -join ' | ')}
}
function Read-CloakPrices {
 try{
  if(!$grid.SelectedRows.Count){throw 'Chọn acc đang ở cửa hàng áo choàng.'}
  $a=$grid.SelectedRows[0].Tag;$key=[string]$a.id;if(!$script:accountJobs.ContainsKey($key)){throw 'Acc chưa mở.'}
  $offers=@([TGLL.NativeBackend]::GetCloakOffers($script:accountJobs[$key]));if(!$offers.Count){throw 'Mở mục Áo choàng ở Senju Tobirama để đọc giá hiện tại.'}
  $selectedId=[int]$cloakChoice.SelectedItem.TemplateId;$script:loadingCloak=$true
  try{$cloakChoice.Items.Clear();foreach($o in $offers){$null=$cloakChoice.Items.Add($o)};$cloakChoice.SelectedIndex=0;for($i=0;$i -lt $cloakChoice.Items.Count;$i++){if($cloakChoice.Items[$i].TemplateId -eq $selectedId){$cloakChoice.SelectedIndex=$i;break}}}finally{$script:loadingCloak=$false}
  Save-CloakUi;Update-CloakPrice;$status.Text='Đã đọc '+$offers.Count+' loại áo và giá tinh thạch hiện tại.'
 }catch{$status.Text=$_.Exception.Message}
}
function Stop-CloakSelected {foreach($row in @(Get-LaunchRows)){$key=[string]$row.Tag.id;if($script:accountJobs.ContainsKey($key)){[TGLL.NativeBackend]::StopCloakPurchase($script:accountJobs[$key])}};Update-CloakUi}
$cloakChoice.Add_SelectedIndexChanged({Save-CloakUi});$cloakQuantity.Add_ValueChanged({Save-CloakUi})
$cloakBuy.Add_Click({Start-CloakSelected});$cloakRead.Add_Click({Read-CloakPrices});$cloakStop.Add_Click({Stop-CloakSelected})
$grid.Add_SelectionChanged({Update-CloakUi});Update-CloakUi
$script:cloakRun=$null;$script:cloakLaunchingAccountId=''
$cloakEquipClose=New-Object Windows.Forms.CheckBox;$cloakEquipClose.Text='Mua xong mặc áo và tắt game • chạy acc đã chọn theo giới hạn';$cloakEquipClose.Checked=$true;$cloakEquipClose.SetBounds(16,213,640,27);$cloakPage.Controls.Add($cloakEquipClose)
$cloakBuy.Top=247;$cloakRead.Top=247;$cloakStop.Top=247;$cloakHint.Top=291
$cloakHint.Height=100
function Get-CloakQueuedMember($id){if(!$script:cloakRun -or !$script:cloakRun.active -or $script:cloakLaunchingAccountId -eq [string]$id){return $null};foreach($m in $script:cloakRun.members){if($m.id -eq [string]$id -and $m.state -eq 'waiting'){return $m}};return $null}
function Get-CloakMemberJob($m){if($m.job){return Get-AccountJob $m.job};return $null}
function Test-CloakMemberRunning($m){$j=Get-CloakMemberJob $m;return ($null -ne $j -and $j.ProcessId -gt 0 -and [TGLL.ManagedGames]::IsRunning([int]$j.ProcessId))}
function Begin-CloakMemberClose($m){return [TGLL.NativeBackend]::BeginCloseBatch([string[]]@($m.job))}
function Start-CloakQueue {
 if(@($script:beastBatches | Where-Object {$_.state -ne 'done'}).Count){throw 'Dừng hàng chờ linh thú trước.'}
 if($script:cloakRun -and $script:cloakRun.active){throw 'Dừng lượt mua áo hiện tại trước.'}
 foreach($b in @($script:questBatches)+@($script:izanamiBatches)+@($script:dungeonBatches)){if($b.state -ne 'done'){throw 'Dừng các hàng chờ hoạt động trước khi mua áo.'}}
 if($script:distributionRun -and $script:distributionRun.active){throw 'Dừng chia đồ trước khi mua áo.'};if($script:mailRun -and $script:mailRun.active){throw 'Dừng gom đồ trước khi mua áo.'}
 if(!$autoLoginCheck.Checked){throw 'Bật Đăng nhập trực tiếp để mở hàng chờ.'}
 $rows=@(Get-LaunchRows | Sort-Object Index);if(!$rows.Count){throw 'Tích Chọn các acc cần mua áo.'}
 $offer=$cloakChoice.SelectedItem;if($null -eq $offer){throw 'Chọn loại áo.'}
 $members=@(foreach($row in $rows){[pscustomobject]@{id=[string]$row.Tag.id;username=[string]$row.Tag.username;job='';state='waiting';status='Chờ mua/mặc áo';closeOperation=$null}})
 $script:cloakRun=[pscustomobject]@{active=$true;cursor=0;members=$members;templateId=[int]$offer.TemplateId;quantity=[int]$cloakQuantity.Value;price=[int]$offer.CrystalPrice}
 Update-CloakUi
}
function Start-CloakQueueMember($m){
 $a=@($script:accounts | Where-Object {[string]$_.id -eq $m.id}) | Select-Object -First 1;if(!$a){throw 'Tài khoản đã bị xóa.'}
 if($script:accountJobs.ContainsKey($m.id)){$m.job=$script:accountJobs[$m.id]}
 if(!(Test-CloakMemberRunning $m)){$script:cloakLaunchingAccountId=$m.id;try{Open-Account $a $true}finally{$script:cloakLaunchingAccountId=''};if(!$script:accountJobs.ContainsKey($m.id)){throw 'Chưa xếp mở game được.'};$m.job=$script:accountJobs[$m.id]}
 $j=Get-CloakMemberJob $m;if($null -eq $j -or !$j.AutoLoginEnabled){throw 'Cần phiên đăng nhập trực tiếp.'}
 $j.CloakQueueOnly=$true;$j.CloakEquipAfter=$true
 [TGLL.NativeBackend]::StopMapFarm($j.Id);[TGLL.NativeBackend]::SetSonCap($j.Id,$false)
 [TGLL.NativeBackend]::SetReceiveKey($j.Id,$false);[TGLL.NativeBackend]::SetDungeonHigh($j.Id,$false);[TGLL.NativeBackend]::SetCloseAfterKey($j.Id,$false);[TGLL.NativeBackend]::SetIzanami($j.Id,$false);[TGLL.NativeBackend]::SetAutoQuest($j.Id,$false)
 [TGLL.NativeBackend]::StopMail($j.Id);[TGLL.NativeBackend]::StopStoneChest($j.Id);[TGLL.NativeBackend]::ConfigureGiftCodes($j.Id,$false,[string[]]@());[TGLL.NativeBackend]::SetGameAuto($j.Id,$false);[TGLL.NativeBackend]::StopTraining($j.Id)
}
function Sync-CloakQueueMember($run,$m) {
 if($m.state -in @('closing','blocked') -and $m.closeOperation){
  if(!$m.closeOperation.Completed){return}
  # A reopened session must not be mistaken for the old session that was closed.
  if($script:accountJobs.ContainsKey($m.id) -and $script:accountJobs[$m.id] -ne $m.job){$m.state='blocked';$m.status='Phiên game thay đổi; giữ lượt để kiểm tra';return}
  if(Test-CloakMemberRunning $m){
   if($m.closeOperation.Failed -or $m.closeOperation.Error){$m.state='blocked';$m.status='Chưa tắt được game; giữ lượt để kiểm tra'}else{$m.status='Đợi game tắt hẳn'}
   return
  }
  $m.state='done';$m.status='Đã mua, mặc áo và tắt game';return
 }
 if($m.state -notin @('launching','running')){return}
 $j=Get-CloakMemberJob $m
 if($null -eq $j -or ($script:accountJobs.ContainsKey($m.id) -and $script:accountJobs[$m.id] -ne $m.job)){$m.state='blocked';$m.status='Phiên game thay đổi; kiểm tra túi trước khi chạy lại';return}
 if($j.CloseRequested -or $j.CancelRequested -or $j.State -in @('failed','closed','cancelled','reconnecting')){$m.state='blocked';$m.status='Game lỗi/thoát trước khi mặc xong; giữ lượt để kiểm tra';return}
 if($m.state -eq 'launching'){
  if(!$j.GameSettingsReady -or !$j.TrainReady){return}
  try{if($run.useOwned){[TGLL.NativeBackend]::UseLongestOwnedCloak($j.Id,$run.templateId)}else{[TGLL.NativeBackend]::StartCloakPurchase($j.Id,$run.templateId,$run.quantity,$run.price)};$m.state='running';$m.status='Đang mua/mặc áo'}catch{$m.state='blocked';$m.status=$_.Exception.Message};return
 }
 $m.status=$j.CloakStatus
 if($j.CloakDone -and !$j.CloakEnabled){try{$m.closeOperation=Begin-CloakMemberClose $m;$m.state='closing';$m.status='Đã mặc áo; đang tắt game'}catch{$m.state='blocked';$m.status='Chưa bắt đầu tắt được game'};return}
 if(!$j.CloakEnabled){$m.state='blocked';$m.status=$j.CloakStatus+' • giữ game và lượt để kiểm tra'}
}
function Sync-CloakQueue {
 if(!$script:cloakRun -or !$script:cloakRun.active){return}
 $run=$script:cloakRun
 foreach($m in $run.members){Sync-CloakQueueMember $run $m}
 $run.cursor=@($run.members | Where-Object {$_.state -eq 'done'}).Count
 if($run.cursor -eq $run.members.Count){$run.active=$false;$status.Text='Đã mua, mặc áo và tắt toàn bộ acc trong hàng chờ.';return}
 # Acc đang đăng nhập/chạy/đóng hoặc đang giữ lỗi đều giữ chỗ, chưa thoát hẳn thì chưa mở bù.
 $occupied=@($run.members | Where-Object {$_.state -notin @('waiting','done')}).Count
 $budget=[int]$launchConcurrency.Value
 foreach($m in $run.members){
  if($occupied -ge [int]$cloakMax.Value -or $budget -le 0){break}
  if($m.state -ne 'waiting'){continue}
  $m.state='launching';$m.status='Đang mở game';$occupied++;$budget--
  try{Start-CloakQueueMember $m}catch{$m.state='blocked';$m.status=$_.Exception.Message}
 }
}

function Start-CloakSelected {try{if($cloakEquipClose.Checked){Start-CloakQueue}else{Start-CloakDirectSelected}}catch{$status.Text=$_.Exception.Message}}
function Stop-CloakSelected {
 if($script:cloakRun){$script:cloakRun.active=$false;foreach($m in $script:cloakRun.members){if($m.job){[TGLL.NativeBackend]::StopCloakPurchase($m.job)}else{$m.status='Đã dừng'}}}
 foreach($row in @(Get-LaunchRows)){$key=[string]$row.Tag.id;if($script:accountJobs.ContainsKey($key)){[TGLL.NativeBackend]::StopCloakPurchase($script:accountJobs[$key])}}
 Update-CloakUi
}
$cloakEquipClose.Add_CheckedChanged({if(!$script:loadingCloak -and $grid.SelectedRows.Count){$grid.SelectedRows[0].Tag | Add-Member NoteProperty cloakEquipClose $cloakEquipClose.Checked -Force;if(!$UiTest){Save-Accounts}}})


$cloakUseOwned=New-Object Windows.Forms.Button;$cloakUseOwned.Text='Dùng áo HSD dài nhất (không mua)';$cloakUseOwned.SetBounds(16,386,330,29);$cloakPage.Controls.Add($cloakUseOwned)
$cloakUseOwned.Add_Click({
 try{if($script:cloakRun -and $script:cloakRun.active){throw 'Dừng hàng mua áo trước khi dùng áo đang có.'};if($cloakEquipClose.Checked){Start-CloakQueue;$script:cloakRun | Add-Member NoteProperty useOwned $true -Force;$status.Text='Dùng áo HSD dài nhất đang có rồi tắt game; không mua thêm.';return};$o=$cloakChoice.SelectedItem;if(!$o){throw 'Chọn loại áo.'};foreach($row in @(Get-LaunchRows)){$key=[string]$row.Tag.id;if(!$script:accountJobs.ContainsKey($key)){throw ($row.Tag.username+': acc chưa mở.')};[TGLL.NativeBackend]::UseLongestOwnedCloak($script:accountJobs[$key],$o.TemplateId)};$status.Text='Đã yêu cầu dùng áo HSD dài nhất trong túi; không mua thêm.'}catch{$status.Text=$_.Exception.Message}
})
