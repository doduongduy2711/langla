$script:distributionRun=$null;$script:distributionLaunchingId=''
$distributionPage=New-Object Windows.Forms.TabPage;$distributionPage.Text='Auto chia đồ';$distributionPage.BackColor=$form.BackColor;$distributionPage.AutoScroll=$true;$tabs.TabPages.Add($distributionPage)
$null=Ui-Label 'Acc dòng 1 chỉ gửi cho acc tích Chọn từ dòng 2. Mỗi dòng: ID,số lượng MỖI ACC.' $distributionPage 14 10 650 28
$distributionItems=New-Object Windows.Forms.TextBox;$distributionItems.Multiline=$true;$distributionItems.ScrollBars='Vertical';$distributionItems.SetBounds(14,43,290,110);$distributionPage.Controls.Add($distributionItems)
$null=Ui-Label 'Cách mỗi thư (giây):' $distributionPage 320 44 200 24
$distributionDelay=New-Object Windows.Forms.NumericUpDown;$distributionDelay.Minimum=1;$distributionDelay.Maximum=3600;$distributionDelay.Value=5;$distributionDelay.SetBounds(320,72,90,25);$distributionPage.Controls.Add($distributionDelay)
$distributionStart=Ui-NewButton 'Bắt đầu chia đồ' {try{Start-Distribution}catch{Show-Error $_.Exception.Message}};Ui-Place $distributionStart $distributionPage 320 112 145 28
$distributionStop=Ui-NewButton 'Dừng' {Stop-Distribution};Ui-Place $distributionStop $distributionPage 475 112 90 28
$null=Ui-Label 'Chỉ gửi đồ không khóa. Mở từng acc → gửi đủ các ID → tự nhận thư → túi tăng đủ từng ID → tắt acc nhận. Giữ acc đầu online.' $distributionPage 14 160 660 48
$distributionReport=New-Object Windows.Forms.TextBox;$distributionReport.Multiline=$true;$distributionReport.ReadOnly=$true;$distributionReport.ScrollBars='Vertical';$distributionReport.SetBounds(14,212,650,95);$distributionReport.Anchor='Top,Bottom,Left,Right';$distributionPage.Controls.Add($distributionReport)
if($script:settings.ContainsKey('distributionItems')){$distributionItems.Text=[string]$script:settings.distributionItems}
if($script:settings.ContainsKey('distributionSeconds')){$distributionDelay.Value=[Math]::Max(1,[Math]::Min(3600,[int]$script:settings.distributionSeconds))}
function Get-DistributionItems([string]$text){$items=@(Get-MailItems $text);if(@($items | Where-Object {$_.quantity -le 0}).Count){throw 'Chia đồ cần số lượng mỗi acc lớn hơn 0; không dùng -1.'};return $items}
function Get-DistributionJob($id){if($script:accountJobs.ContainsKey([string]$id)){return Get-AccountJob $script:accountJobs[[string]$id]};return $null}
function Test-DistributionSessionLive($j){return ($j.ProcessId -gt 0 -and [TGLL.ManagedGames]::IsRunning([int]$j.ProcessId))}
function Get-DistributionNow {return [datetime]::UtcNow}
function Test-DistributionReady($j){
    if($null -eq $j -or !$j.TrainReady -or !$j.GameSettingsReady -or [string]::IsNullOrWhiteSpace([string]$j.CharacterName) -or $j.CloseRequested -or $j.CancelRequested -or $j.ReconnectRequested){return $false}
    if($j.State -in @('queued','starting','typing','submitted','selecting_character','character_submitted','waiting_map','waiting_popup','confirming_popup','retrying','reconnecting','manual_entry','startup_unavailable','failed','closed','cancelled')){return $false}
    if($j.MapId -lt 0 -or $j.ZoneId -lt 0 -or !$j.ZoneReadAt -or ([datetime]::UtcNow-$j.ZoneReadAt).TotalSeconds -gt 5){return $false}
    return (Test-DistributionSessionLive $j)
}
function Get-DistributionSessionKey($j){return ([string]$j.Id+'|'+$j.ProcessId+'|'+$j.CharacterName+'|'+$j.MapId+'|'+$j.ZoneId)}
function Reset-DistributionConfirmation($owner){$owner | Add-Member NoteProperty readyKey '' -Force;$owner | Add-Member NoteProperty readyAt ([datetime]::MinValue) -Force}
function Test-DistributionConfirmed($owner,$j){
    if(!(Test-DistributionReady $j)){Reset-DistributionConfirmation $owner;return $false}
    $key=Get-DistributionSessionKey $j;$now=Get-DistributionNow
    if(!$owner.PSObject.Properties['readyKey'] -or $owner.readyKey -ne $key){$owner | Add-Member NoteProperty readyKey $key -Force;$owner | Add-Member NoteProperty readyAt $now -Force;return $false}
    return (($now-$owner.readyAt).TotalSeconds -ge 2)
}
function Open-DistributionAccount($id){
    $a=@($script:accounts | Where-Object {[string]$_.id -eq $id}) | Select-Object -First 1;if(!$a){throw 'Tài khoản đã bị xóa.'}
    $script:distributionLaunchingId=$id;$script:mailLaunchingAccountId=$id
    try{Open-Account $a $true}finally{$script:mailLaunchingAccountId='';$script:distributionLaunchingId=''}
    if(!(Get-DistributionJob $id)){throw 'Cần đăng nhập trực tiếp để chia đồ.'}
}
function Prepare-DistributionJob($j){
    if(!$j.AutoLoginEnabled){throw 'Cần mở acc bằng đăng nhập trực tiếp.'}
    if($j.MailBusy -or $j.MailEnabled -or $j.InboxBusy -or $j.ReceiveFlowActive){throw 'Acc đang thao tác; đợi thao tác cũ xong trước khi chia đồ.'}
    [TGLL.NativeBackend]::SetGameAuto($j.Id,$false);[TGLL.NativeBackend]::SetSonCap($j.Id,$false)
    $j.MailQueueOnly=$true;$j.GiftEnabled=$false;$j.AutoItemEnabled=$false;$j.ChestEnabled=$false;$j.CloakEnabled=$false
    [TGLL.NativeBackend]::SetReceiveKey($j.Id,$false);[TGLL.NativeBackend]::SetDungeonHigh($j.Id,$false);[TGLL.NativeBackend]::SetIzanami($j.Id,$false);[TGLL.NativeBackend]::SetAutoQuest($j.Id,$false);[TGLL.NativeBackend]::SetCloseAfterKey($j.Id,$false)
}
function Request-DistributionInventory($j){[TGLL.NativeBackend]::ScanInventory($j.Id)}
function Set-DistributionInbox($j,[bool]$enabled){[TGLL.NativeBackend]::ConfigureInbox($j.Id,$enabled)}
function Send-DistributionItems($j,$name,$items,$seconds){[TGLL.NativeBackend]::ConfigureMail($j.Id,$name,[int[]]@($items | ForEach-Object {$_.id}),[int[]]@($items | ForEach-Object {$_.quantity}),($seconds*1000))}
function Get-DistributionStock($bag,[int]$id,[bool]$unlocked=$false){[long]$total=0;foreach($item in $bag.Items){if($item.TemplateId -eq $id -and (!$unlocked -or (!$item.Locked -and !$item.Equipment -and $item.Slot -eq $item.BagIndex))){$total+=[long]$item.Quantity}};return $total}
function Begin-DistributionClose($m){return [TGLL.NativeBackend]::BeginCloseBatch([string[]]@($m.job))}
function Test-DistributionRunning($m){$j=Get-AccountJob $m.job;return ($j -and $j.ProcessId -gt 0 -and [TGLL.ManagedGames]::IsRunning($j.ProcessId))}
function Get-DistributionQueuedMember($id){if(!$script:distributionRun -or !$script:distributionRun.active -or $script:distributionLaunchingId -eq $id){return $null};return @($script:distributionRun.members | Where-Object {$_.id -eq $id -and $_.state -eq 'waiting'}) | Select-Object -First 1}
function Start-Distribution {
 if(@($script:beastBatches | Where-Object {$_.state -ne 'done'}).Count){throw 'Dừng hàng chờ linh thú trước.'}
    if($script:distributionRun -and $script:distributionRun.active){throw 'Đang chia đồ.'}
    if($grid.Rows.Count -lt 2){throw 'Cần acc dòng 1 và ít nhất một acc nhận ở dòng 2.'}
    if(!$autoLoginCheck.Checked){throw 'Bật đăng nhập trực tiếp trước.'}
    if(($script:mailRun -and $script:mailRun.active) -or ($script:cloakRun -and $script:cloakRun.active)){throw 'Dừng gom đồ/mua áo trước.'}
    foreach($list in @($script:questBatches,$script:izanamiBatches,$script:dungeonBatches,$script:soncapBatches)){foreach($b in $list){if($b.state -ne 'done'){throw 'Hủy hàng chờ auto khác trước khi chia đồ.'}}}
    $items=@(Get-DistributionItems $distributionItems.Text)
    $members=@(foreach($row in $grid.Rows){if($row.Index -gt 0 -and $row.Cells['pick'].Value -eq $true){[pscustomobject]@{id=[string]$row.Tag.id;username=[string]$row.Tag.username;job='';state='waiting';status='Chờ chia đồ';at=[datetime]::UtcNow;scanAt=[datetime]::MinValue;baseline=@{};received=0;deleted=0;letters=0;sentAt=[datetime]::MinValue;closeOperation=$null}}})
    if($members.Count -eq 0){throw 'Tích cột Chọn cho ít nhất một acc nhận từ dòng 2.'}
    $script:distributionRun=[pscustomobject]@{active=$true;sourceId=[string]$grid.Rows[0].Tag.id;sourceJob='';sourcePrepared=$false;sourceOpened=$false;cursor=0;members=$members;items=$items;seconds=[int]$distributionDelay.Value;error=''}
    $script:settings.distributionItems=$distributionItems.Text;$script:settings.distributionSeconds=[int]$distributionDelay.Value;if(!$UiTest){Write-JsonAtomic $script:configPath $script:settings}
    $script:inboxWanted=$false;if($script:inboxApplied){try{[TGLL.NativeBackend]::ConfigureInbox($script:inboxApplied,$false)}catch{};$script:inboxApplied=''}
    Sync-Distribution;Update-DistributionUi
}
function Stop-Distribution {
    if(!$script:distributionRun){return};$script:distributionRun.active=$false
    if($script:distributionRun.sourceJob){[TGLL.NativeBackend]::StopMail($script:distributionRun.sourceJob)}
    foreach($m in $script:distributionRun.members){if($m.job){try{[TGLL.NativeBackend]::ConfigureInbox($m.job,$false)}catch{}}}
    $script:distributionRun.error='Đã dừng. Kiểm tra thư đã gửi trước khi bắt đầu lại.';Update-DistributionUi
}
function Release-DistributionJobs($run){
    if(!$run){return}
    $ids=@($run.sourceJob)+@($run.members | ForEach-Object {$_.job})
    foreach($id in $ids){if(!$id){continue};$job=Get-AccountJob $id;if($job -is [TGLL.NativeJob] -and !$job.MailBusy -and !$job.InboxBusy){$job.MailQueueOnly=$false}}
}
function Sync-Distribution {
    $run=$script:distributionRun;if(!$run -or !$run.active){Release-DistributionJobs $run;return}
    try {
        if($run.cursor -ge $run.members.Count){$run.active=$false;$run.error='Đã chia xong; giữ acc đầu online.';return}
        $m=$run.members[$run.cursor];if($m.state -eq 'blocked'){return}
        if($m.state -eq 'closing'){
            if(!$m.closeOperation.Completed){return}
            if($m.closeOperation.Failed -or $m.closeOperation.Error){throw 'Không tắt được acc nhận; giữ hàng chờ.'}
            if(Test-DistributionRunning $m){return}
            $m.state='done';$m.status='Đã nhận thư và tắt game';$run.cursor++;return
        }
        $source=Get-DistributionJob $run.sourceId
        if(!$source -or (!$run.sourcePrepared -and ($source.CloseRequested -or $source.State -in @('closed','failed','cancelled')))){if(!$run.sourceOpened){$run.sourceOpened=$true;Open-DistributionAccount $run.sourceId};$m.status='Đợi mở acc đầu';return}
        if($run.sourceJob -and $run.sourceJob -ne $source.Id){throw 'Phiên acc gửi đã đổi; kiểm tra thư, không tự gửi lại.'}
        if(!(Test-DistributionReady $source)){Reset-DistributionConfirmation $run;if($run.sourcePrepared -or $source.State -in @('failed','closed','cancelled','reconnecting')){throw 'Acc gửi lỗi hoặc mất kết nối; kiểm tra thư trước khi tiếp tục.'};$m.status='Đợi acc đầu sẵn sàng';return}
        if(!(Test-DistributionConfirmed $run $source)){$m.status='Xác nhận acc đầu đã vào game ổn định';return}
        if(!$run.sourcePrepared){Prepare-DistributionJob $source;$run.sourceJob=$source.Id;$run.sourcePrepared=$true}
        if($m.state -eq 'waiting'){
            $m.state='opening';$m.at=[datetime]::UtcNow
            $existing=Get-DistributionJob $m.id;if(!$existing -or $existing.CloseRequested -or $existing.State -in @('closed','failed','cancelled')){Open-DistributionAccount $m.id}
            $m.status='Đang mở acc nhận';return
        }
        $receiver=Get-DistributionJob $m.id
        if($m.job -and (!$receiver -or $m.job -ne $receiver.Id)){throw 'Phiên acc nhận đã đổi; kiểm tra thư trước khi gửi tiếp.'}
        if((([datetime]::UtcNow)-$m.at).TotalSeconds -gt [Math]::Max(600,($run.seconds*$run.items.Count*20)+180)){throw 'Quá thời gian mở/gửi/nhận; kiểm tra game và thư trước khi chạy lại.'}
        if(!(Test-DistributionReady $receiver)){
            Reset-DistributionConfirmation $m
            if($m.job){throw 'Acc nhận lỗi hoặc mất kết nối; không tự gửi lại.'};$m.status='Đợi acc nhận vào game';return
        }
        if(!(Test-DistributionConfirmed $m $receiver)){$m.status='Xác nhận acc nhận đã vào game ổn định';return}
        if($receiver.CharacterName -eq $source.CharacterName){throw 'Hai acc trùng tên nhân vật; kiểm tra đúng server.'}
        if($m.state -eq 'opening'){
            Prepare-DistributionJob $receiver;$m.job=$receiver.Id;Set-DistributionInbox $receiver $true;$m.state='preparing';$m.scanAt=[datetime]::UtcNow;Request-DistributionInventory $receiver;Request-DistributionInventory $source;$m.status='Đọc túi và xử lý thư cũ';return
        }
        if($m.state -eq 'preparing'){
            if(!$receiver.InboxEnabled){throw ('Không nhận được thư: '+$receiver.InboxStatus)}
            if($receiver.InboxBusy -or $receiver.InboxPendingAttachments -ne 0 -or $receiver.InboxObservedAt -lt $m.scanAt){return}
            Set-DistributionInbox $receiver $false;$m.scanAt=[datetime]::UtcNow;Request-DistributionInventory $receiver;Request-DistributionInventory $source;$m | Add-Member NoteProperty stockReadyKey ((Get-DistributionSessionKey $source)+'>'+(Get-DistributionSessionKey $receiver)) -Force;$m.state='stock';return
        }
        if($m.state -eq 'stock'){
            $stockKey=(Get-DistributionSessionKey $source)+'>'+ (Get-DistributionSessionKey $receiver)
            if(!$m.PSObject.Properties['stockReadyKey'] -or $m.stockReadyKey -ne $stockKey){$m | Add-Member NoteProperty stockReadyKey $stockKey -Force;$m.scanAt=[datetime]::UtcNow;Request-DistributionInventory $receiver;Request-DistributionInventory $source;$m.status='Đã xác nhận vào game; đọc lại túi trước khi gửi';return}
            if(!$source.Inventory.Ready -or !$receiver.Inventory.Ready -or $source.Inventory.ReadAt -lt $m.scanAt -or $receiver.Inventory.ReadAt -lt $m.scanAt){return}
            foreach($item in $run.items){if((Get-DistributionStock $source.Inventory $item.id $true) -lt $item.quantity){throw ('Acc đầu thiếu đồ không khóa ID '+$item.id+' để chia đủ cho '+$m.username)};$m.baseline[$item.id]=Get-DistributionStock $receiver.Inventory $item.id}
            $m.received=$receiver.InboxReceived;$m.deleted=$receiver.InboxDeleted
            Set-DistributionInbox $receiver $true
            if(!(Test-DistributionConfirmed $run $source) -or !(Test-DistributionConfirmed $m $receiver)){$m.status='Đợi xác nhận lại acc trước khi gửi';return}
            Send-DistributionItems $source $receiver.CharacterName $run.items $run.seconds;$m.state='sending';$m.status='Đang gửi tới '+$receiver.CharacterName;return
        }
        if($m.state -eq 'sending'){
            if(!$source.MailDone){if(!$source.MailEnabled -and !$source.MailBusy){throw ('Gửi chưa hoàn tất: '+$source.MailStatus)};return}
            if($source.MailBusy){return}
            [long]$expected=0;foreach($item in $run.items){$expected+=$item.quantity};if($source.MailQuantity -ne $expected -or $source.MailLetters -lt 1){throw 'Số đồ gửi chưa đủ cấu hình; giữ acc nhận để kiểm tra, không gửi bù tự động.'}
            $m.letters=$source.MailLetters;$m.sentAt=[datetime]::UtcNow;$m.state='receiving';$m.status='Đã gửi đủ; đợi xác nhận nhận thư';return
        }
        if($m.state -eq 'receiving'){
            # InboxReceived advances only after the inbox worker confirms receipt, not on send/click.
            # The baseline is captured before sending; receipts may arrive before MailDone.
            if(([long]$receiver.InboxReceived-[long]$m.received) -lt $m.letters){
                if(!$receiver.InboxEnabled -and !$receiver.InboxBusy){throw ('Nhận thư chưa hoàn tất: '+$receiver.InboxStatus)}
                return
            }
            if($receiver.InboxBusy){return}
            Set-DistributionInbox $receiver $false;$m.closeOperation=Begin-DistributionClose $m;$m.state='closing';$m.status='Đã xác nhận nhận thư; đang tắt game'
        }
    }catch{$run.error=$_.Exception.Message;if($run.cursor -lt $run.members.Count){$run.members[$run.cursor].state='blocked';$run.members[$run.cursor].status=$run.error}}
}
function Update-DistributionUi {
    foreach($control in $distributionPage.Controls){if($control -is [Windows.Forms.Label] -and $control.Text.StartsWith('Chỉ gửi đồ không khóa.')){$control.Text='Chỉ gửi đồ không khóa. Mở từng acc → xác nhận gửi đủ → xác nhận nhận thư → tắt acc nhận. Giữ acc đầu online.'}}
    $run=$script:distributionRun;$active=$run -and $run.active;$distributionStart.Enabled=!$active;$distributionItems.Enabled=!$active;$distributionDelay.Enabled=!$active
    if($run){$lines=@('Đã xong: '+$run.cursor+'/'+$run.members.Count+' • '+$run.error);$lines+=@($run.members | ForEach-Object {$_.username+': '+$_.status});$text=$lines -join "`r`n";if($distributionReport.Text -ne $text){$distributionReport.Text=$text}}
}
