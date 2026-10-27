$mailPage=New-Object Windows.Forms.TabPage;$mailPage.Text='Auto gom đồ';$mailPage.BackColor=$form.BackColor;$tabs.TabPages.Add($mailPage)
$script:mailRun=$null;$script:mailLoading=$false;$script:mailLaunchingAccountId=''
$mailReceiver=Ui-Label 'Người nhận: nhân vật của tài khoản ở dòng 1.' $mailPage 14 8 650 24
$mailNamedCheck=New-Object Windows.Forms.CheckBox;$mailNamedCheck.Text='Gửi tới nhân vật chỉ định';$mailNamedCheck.SetBounds(14,35,285,24);$mailPage.Controls.Add($mailNamedCheck)
$mailNamedRecipient=New-Object Windows.Forms.TextBox;$mailNamedRecipient.SetBounds(310,35,240,24);$mailNamedRecipient.MaxLength=64;$mailPage.Controls.Add($mailNamedRecipient)
$null=Ui-Label 'Mỗi dòng: ID, số lượng (-1 = tất cả đồ không khóa). Ví dụ: 434,-1' $mailPage 14 69 650 23
$mailItems=New-Object Windows.Forms.TextBox;$mailItems.Multiline=$true;$mailItems.ScrollBars='Vertical';$mailItems.SetBounds(14,94,280,78);$mailPage.Controls.Add($mailItems)
$null=Ui-Label 'Mỗi acc gửi cách (giây)' $mailPage 310 94 180 24
$mailDelay=New-Object Windows.Forms.NumericUpDown;$mailDelay.Minimum=1;$mailDelay.Maximum=3600;$mailDelay.Value=5;$mailDelay.SetBounds(310,120,95,26);$mailPage.Controls.Add($mailDelay)
$mailStart=Ui-NewButton 'Bắt đầu gom đồ' {try{Start-MailCollection}catch{Show-Error $_.Exception.Message}};$mailStart.SetBounds(310,148,145,27);$mailPage.Controls.Add($mailStart)
$mailStop=Ui-NewButton 'Dừng' {Stop-MailCollection};$mailStop.SetBounds(462,148,90,27);$mailPage.Controls.Add($mailStop)
$script:inboxApplied='';$script:inboxWanted=$false
$mailInboxCheck=New-Object Windows.Forms.CheckBox;$mailInboxCheck.Text='Acc nhận: Nhận tất cả và Xóa đã đọc';$mailInboxCheck.SetBounds(14,177,460,24);$mailPage.Controls.Add($mailInboxCheck);$mailInboxCheck.Checked=$true
$mailInboxStatus=Ui-Label 'Nhận thư: Tắt' $mailPage 14 204 650 24
$mailHint=Ui-Label 'Mở từng acc từ dòng 2; gửi xong sẽ tắt và mở acc kế tiếp. Giữ acc dòng 1 online. Chỉ gửi đồ không khóa.' $mailPage 14 230 650 30
$mailReport=New-Object Windows.Forms.TextBox;$mailReport.Multiline=$true;$mailReport.ReadOnly=$true;$mailReport.ScrollBars='Vertical';$mailReport.SetBounds(14,264,650,60);$mailReport.Anchor='Top,Bottom,Left,Right';$mailPage.Controls.Add($mailReport)
if($script:settings.ContainsKey('mailItems')){$mailItems.Text=[string]$script:settings.mailItems}
if($script:settings.ContainsKey('mailSeconds')){$mailDelay.Value=[Math]::Max(1,[Math]::Min(3600,[int]$script:settings.mailSeconds))}
if($script:settings.ContainsKey('mailNamedRecipient')){$mailNamedRecipient.Text=[string]$script:settings.mailNamedRecipient}
if($script:settings.ContainsKey('mailNamedEnabled')){$mailNamedCheck.Checked=[bool]$script:settings.mailNamedEnabled}
function Save-MailSettings {
    if($script:mailLoading){return}
    $script:settings.mailNamedEnabled=$mailNamedCheck.Checked;$script:settings.mailNamedRecipient=$mailNamedRecipient.Text;$script:settings.mailItems=$mailItems.Text;$script:settings.mailSeconds=[int]$mailDelay.Value
    if(!$UiTest){Write-JsonAtomic $script:configPath $script:settings}
}
function Get-MailItems([string]$text) {
    $result=New-Object 'System.Collections.Generic.List[object]';$seen=@{}
    foreach($line in ($text -split '\r?\n')){
        if([string]::IsNullOrWhiteSpace($line)){continue}
        if($line -notmatch '^\s*(\d+)\s*[,;:\s]\s*(-?\d+)\s*$'){throw ('Dòng không hợp lệ: '+$line+'. Nhập ID,số lượng; mỗi ID một dòng.')}
        [int]$id=0;[int]$quantity=0
        if(![int]::TryParse($Matches[1],[ref]$id) -or ![int]::TryParse($Matches[2],[ref]$quantity) -or $id -gt 65535 -or ($quantity -ne -1 -and $quantity -le 0)){throw ('ID hoặc số lượng không hợp lệ: '+$line)}
        if($seen.ContainsKey($id)){throw ('ID '+$id+' bị trùng. Chỉ nhập một lần mỗi ID.')};$seen[$id]=$true
        $result.Add([pscustomobject]@{id=$id;quantity=$quantity})
        if($result.Count -gt 1000){throw 'Tối đa 1000 ID.'}
    }
    if(!$result.Count){throw 'Nhập ít nhất một ID và số lượng.'}
    return $result.ToArray()
}
function Get-MailReceiverName {
    if(!$grid.Rows.Count){return ''};$a=$grid.Rows[0].Tag;$key=[string]$a.id
    if($script:accountJobs.ContainsKey($key)){$j=Get-AccountJob $script:accountJobs[$key];if($null -ne $j -and ![string]::IsNullOrWhiteSpace($j.CharacterName)){return [string]$j.CharacterName}}
    return [string]$grid.Rows[0].Cells['character'].Value
}
function Start-MailCollection {
 if(@($script:beastBatches | Where-Object {$_.state -ne 'done'}).Count){throw 'Dừng hàng chờ linh thú trước.'}if($script:distributionRun -and $script:distributionRun.active){throw 'Dừng auto chia đồ trước.'}
    if($script:cloakRun -and $script:cloakRun.active){throw 'Dừng hàng mua áo trước khi gom đồ.'}
    if($script:mailRun -and $script:mailRun.active){throw 'Dừng lượt gom đồ hiện tại trước khi bắt đầu lượt mới.'}
    if(!$autoLoginCheck.Checked){throw 'Bật Đăng nhập trực tiếp trước khi chạy hàng chờ gom đồ.'}
    $null=$grid.EndEdit()
    $items=@(Get-MailItems $mailItems.Text)
    $named=$mailNamedCheck.Checked;$receiverId=''
    if($named){
        $receiver=$mailNamedRecipient.Text.Trim()
        if([string]::IsNullOrWhiteSpace($receiver) -or $receiver -match '[\x00-\x1f\x7f]'){throw 'Nhập tên nhân vật nhận thư hợp lệ.'}
    }else{
        if($grid.Rows.Count -lt 2){throw 'Cần tài khoản nhận ở dòng 1 và ít nhất một tài khoản gửi ở dòng 2.'}
        $receiver=Get-MailReceiverName;$receiverId=[string]$grid.Rows[0].Tag.id
        if([string]::IsNullOrWhiteSpace($receiver)){throw 'Mở tài khoản ở dòng 1 để lấy đúng tên nhân vật nhận thư, rồi bấm Bắt đầu.'}
    }
    foreach($pair in @($script:questBatches,$script:izanamiBatches,$script:dungeonBatches)){foreach($batch in $pair){if($batch.state -ne 'done'){throw 'Hủy hàng chờ địa cung/nhiệm vụ/cấm thuật trước khi chạy gom đồ.'}}}
    # Chốt người nhận và danh sách gửi tại lúc bắt đầu; sắp xếp bảng không đổi đích.
    $senders=@(foreach($row in $grid.Rows){if($row.Cells['pick'].Value -eq $true -and ((!$named -and $row.Index -gt 0) -or ($named -and [string]$row.Cells['character'].Value -ne $receiver))){[pscustomobject]@{id=[string]$row.Tag.id;username=[string]$row.Tag.username;job='';status='Chờ lượt gom đồ';state='waiting';closeOperation=$null;started=$false}}})
    if(!$senders.Count){throw 'Tích Chọn ít nhất một tài khoản gửi, khác nhân vật nhận.'}
    $waiting=@{};foreach($m in $senders){$waiting[$m.id]=$m}
    $script:mailRun=[pscustomobject]@{cursor=0;waiting=$waiting;active=$true;recipient=$receiver;receiverId=$receiverId;namedRecipient=$named;items=$items;seconds=[int]$mailDelay.Value;senders=$senders}
    $script:inboxWanted=(!$named -and $mailInboxCheck.Checked);$script:inboxApplied='';Save-MailSettings;Sync-MailCollection;Update-MailUi
}
function Stop-MailCollection {
    $script:inboxWanted=$false;if($script:inboxApplied){try{[TGLL.NativeBackend]::ConfigureInbox($script:inboxApplied,$false)}catch{};$script:inboxApplied=''};$mailInboxStatus.Text='Nhận thư: Đã dừng'
    if($script:mailRun){$script:mailRun.active=$false;foreach($sender in $script:mailRun.senders){if($sender.job){[TGLL.NativeBackend]::StopMail($sender.job)}else{$sender.status='Đã dừng'}}}
    Update-MailUi
}
function Get-MailMemberJob($member) {if($member.job){return Get-AccountJob $member.job};return $null}
function Test-MailMemberRunning($member) {$j=Get-MailMemberJob $member;return ($null -ne $j -and $j.ProcessId -gt 0 -and [TGLL.ManagedGames]::IsRunning([int]$j.ProcessId))}
function Begin-MailMemberClose($member) {return [TGLL.NativeBackend]::BeginCloseBatch([string[]]@($member.job))}
function Get-MailQueuedMember($id) {
    if(!$script:mailRun -or !$script:mailRun.active -or [string]$script:mailLaunchingAccountId -eq [string]$id){return $null}
    if($script:mailRun.waiting.ContainsKey([string]$id)){$m=$script:mailRun.waiting[[string]$id];if($m.state -eq 'waiting'){return $m}}
    return $null
}
function Test-MailReceiverReady {
    if($script:mailRun.namedRecipient){return $true}
    if(!$script:accountJobs.ContainsKey($script:mailRun.receiverId)){return $false}
    $j=Get-AccountJob $script:accountJobs[$script:mailRun.receiverId]
    return ($null -ne $j -and $j.GameSettingsReady -and $j.CharacterName -eq $script:mailRun.recipient -and !$j.CloseRequested -and !$j.CancelRequested -and $j.State -notin @('failed','closed','cancelled','reconnecting') -and $j.ProcessId -gt 0 -and [TGLL.ManagedGames]::IsRunning([int]$j.ProcessId))
}
function Start-MailMember($member) {
    $a=@($script:accounts | Where-Object {[string]$_.id -eq $member.id}) | Select-Object -First 1
    if(!$a){throw 'Tài khoản trong hàng chờ đã bị xóa.'}
    $script:mailLaunchingAccountId=$member.id
    try{Open-Account $a $true}finally{$script:mailLaunchingAccountId=''}
    if(!$script:accountJobs.ContainsKey($member.id)){throw 'Không có phiên đăng nhập tự động cho tài khoản.'}
    $member.job=$script:accountJobs[$member.id]
    $j=Get-MailMemberJob $member
    if($null -eq $j -or !$j.AutoLoginEnabled){throw 'Mở lại tài khoản bằng đăng nhập trực tiếp.'}
    $j.MailQueueOnly=$true
    [TGLL.NativeBackend]::SetReceiveKey($j.Id,$false);[TGLL.NativeBackend]::SetDungeonHigh($j.Id,$false);[TGLL.NativeBackend]::SetCloseAfterKey($j.Id,$false);[TGLL.NativeBackend]::SetIzanami($j.Id,$false);[TGLL.NativeBackend]::SetAutoQuest($j.Id,$false)
    [TGLL.NativeBackend]::SetGameAuto($j.Id,$false);[TGLL.NativeBackend]::StopTraining($j.Id)
}
function Sync-MailReceiver {
    if(!$script:inboxWanted){return}
    $receiverId='';if($script:mailRun){$receiverId=$script:mailRun.receiverId}elseif($grid.Rows.Count){$receiverId=[string]$grid.Rows[0].Tag.id}
    if(!$receiverId -or !$script:accountJobs.ContainsKey($receiverId)){return}
    $jobId=$script:accountJobs[$receiverId];$j=Get-AccountJob $jobId
    if($null -eq $j -or !$j.GameSettingsReady -or !$j.AutoLoginEnabled){return}
    if(!$j.InboxSupported){$mailInboxStatus.Text='Nhận thư: '+$j.InboxStatus;return}
    if($script:inboxApplied -ne $jobId){if($script:inboxApplied){try{[TGLL.NativeBackend]::ConfigureInbox($script:inboxApplied,$false)}catch{}};[TGLL.NativeBackend]::ConfigureInbox($jobId,$true);$script:inboxApplied=$jobId}
    $mailInboxStatus.Text='Nhận thư: '+$j.InboxStatus+' • '+$j.InboxReceived+' đã nhận / '+$j.InboxDeleted+' đã xóa'
}
function Sync-MailCollection {
    Sync-MailReceiver
    if($script:mailRun -and $script:mailRun.namedRecipient){$mailInboxStatus.Text='Nhận thư: nhân vật chỉ định tự nhận trong game'}
    if(!$script:mailRun -or !$script:mailRun.active){return}
    while($script:mailRun.cursor -lt $script:mailRun.senders.Count -and $script:mailRun.senders[$script:mailRun.cursor].state -eq 'done'){$script:mailRun.cursor++}
    if($script:mailRun.cursor -ge $script:mailRun.senders.Count){$script:mailRun.active=$false;return}
    $member=$script:mailRun.senders[$script:mailRun.cursor]
    if($member.state -eq 'blocked'){return}
    if($member.state -eq 'closing'){
        if(!$member.closeOperation.Completed){return}
        if($member.closeOperation.Failed -or $member.closeOperation.Error){$member.state='blocked';$member.status='Chưa tắt được game; giữ hàng chờ để kiểm tra';return}
        if(Test-MailMemberRunning $member){$member.status='Đợi game tắt hẳn';return}
        $member.state='done';$member.status='Đã gửi xong và tắt game';return
    }
    if(!(Test-MailReceiverReady)){$member.status='Đợi acc nhận online đúng nhân vật';return}
    if($member.state -eq 'waiting'){
        $script:mailRun.waiting.Remove($member.id);$member.state='launching';$member.status='Đang mở game'
        try{Start-MailMember $member}catch{$member.state='blocked';$member.status=[string]$_.Exception.Message}
        return
    }
    $j=Get-MailMemberJob $member
    if($null -eq $j -or ($script:accountJobs.ContainsKey($member.id) -and $script:accountJobs[$member.id] -ne $member.job)){$member.state='blocked';$member.status='Phiên game đã đổi; kiểm tra thư trước khi bắt đầu lại';return}
    if($j.CloseRequested -or $j.CancelRequested -or $j.State -in @('failed','closed','cancelled','reconnecting')){$member.state='blocked';$member.status='Game lỗi/thoát trước khi gom xong; giữ hàng chờ';return}
    if($member.state -eq 'launching'){
        if(!$j.GameSettingsReady -or [string]::IsNullOrWhiteSpace($j.CharacterName)){return}
        if($j.CharacterName -eq $script:mailRun.recipient){$member.state='done';$member.status='Bỏ qua: trùng nhân vật nhận';return}
        try{[TGLL.NativeBackend]::ConfigureMail($j.Id,$script:mailRun.recipient,[int[]]@($script:mailRun.items | ForEach-Object {$_.id}),[int[]]@($script:mailRun.items | ForEach-Object {$_.quantity}),($script:mailRun.seconds*1000),$(if($script:mailRun.receiverId){[string]$script:accountJobs[$script:mailRun.receiverId]}else{''}));$member.state='running';$member.started=$true;$member.status='Đang gom đồ'}catch{$member.state='blocked';$member.status=[string]$_.Exception.Message}
        return
    }
    if($member.state -eq 'running'){
        if($j.MailDone -and !$j.MailBusy){try{$member.closeOperation=Begin-MailMemberClose $member;$member.state='closing';$member.status='Gom xong; đang tắt game'}catch{$member.state='blocked';$member.status='Không bắt đầu được việc tắt game'};return}
        if(!$j.MailEnabled -and !$j.MailBusy){$member.state='blocked';$member.status=$j.MailStatus+' • giữ hàng chờ để kiểm tra'}
    }
}
function Update-MailUi {
    $active=$script:mailRun -and $script:mailRun.active;$mailStart.Enabled=!$active;$mailItems.Enabled=!$active;$mailDelay.Enabled=!$active;$mailNamedCheck.Enabled=!$active;$mailNamedRecipient.Enabled=(!$active -and $mailNamedCheck.Checked);$mailInboxCheck.Enabled=!$mailNamedCheck.Checked
    $mailStart.Text=$(if($mailNamedCheck.Checked){'Bắt đầu gửi thư'}else{'Bắt đầu gom đồ'})
    $mailHint.Text=$(if($mailNamedCheck.Checked){'Gửi từ các acc tích Chọn; mở từng acc, gửi xong tắt rồi mở acc kế tiếp. Chỉ gửi đồ không khóa.'}else{'Chỉ mở acc tích Chọn từ dòng 2 để gom về acc dòng 1; gửi xong tắt rồi mở acc kế tiếp. Giữ acc dòng 1 online.'})
    if($script:mailRun){
        $mailReceiver.Text='Người nhận: '+$script:mailRun.recipient+' • Đã xong: '+$script:mailRun.cursor+'/'+$script:mailRun.senders.Count+' • Mỗi lượt 1 acc'
        $lines=@(foreach($sender in $script:mailRun.senders){$text=$sender.status
            if($sender.started -and $sender.job){$j=Get-AccountJob $sender.job;if($null -ne $j){$text=$sender.status+' • '+$j.MailLetters+' thư / '+$j.MailQuantity+' món';if($sender.state -eq 'running'){$text=$j.MailStatus+' • '+$j.MailLetters+' thư / '+$j.MailQuantity+' món'};if($script:accountJobs.ContainsKey($sender.id) -and $script:accountJobs[$sender.id] -ne $sender.job){$text+=' • Phiên game đã đổi, không tự gửi lại'}}}
            $sender.username+': '+$text})
        $value=$lines -join "`r`n";if($mailReport.Text -ne $value){$mailReport.Text=$value}
    }elseif($mailNamedCheck.Checked){$mailReceiver.Text='Người nhận chỉ định: '+$mailNamedRecipient.Text.Trim()}else{$name=Get-MailReceiverName;$mailReceiver.Text='Người nhận (dòng 1): '+$(if($name){$name}else{'Chưa có tên nhân vật; mở acc dòng 1 trước'})}
}
function Resize-MailUi {
    $mailReceiver.Width=[Math]::Max(200,$mailPage.ClientSize.Width-28);$mailHint.Width=[Math]::Max(200,$mailPage.ClientSize.Width-28)
    $mailReport.SetBounds(14,264,[Math]::Max(200,$mailPage.ClientSize.Width-28),[Math]::Max(35,$mailPage.ClientSize.Height-277))
}
$mailInboxCheck.Add_CheckedChanged({$script:inboxWanted=$mailInboxCheck.Checked;if($script:inboxWanted){$script:inboxApplied=''};if(!$script:inboxWanted -and $script:inboxApplied){[TGLL.NativeBackend]::ConfigureInbox($script:inboxApplied,$false);$script:inboxApplied='';$mailInboxStatus.Text='Nhận thư: Tắt'}else{Sync-MailReceiver}})
$mailPage.AutoScroll=$true;$mailPage.Add_Resize({Resize-MailUi});$mailItems.Add_TextChanged({Save-MailSettings});$mailDelay.Add_ValueChanged({Save-MailSettings})
$mailNamedCheck.Add_CheckedChanged({Save-MailSettings;Update-MailUi});$mailNamedRecipient.Add_TextChanged({Save-MailSettings;Update-MailUi})
$grid.Add_SelectionChanged({Update-MailUi});Resize-MailUi;Update-MailUi
