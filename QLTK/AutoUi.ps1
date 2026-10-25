function Set-AutoCell($row,[bool]$enabled) {
    if($row.Cells['auto'].Value -eq $enabled){return}
    $script:updatingAuto=$true
    try {$row.Cells['auto'].Value=$enabled} finally {$script:updatingAuto=$false}
}
function Get-AccountAutoSkills($a,$j) {
    $key=[string]$a.id;$wanted=@()
    if($script:trainSelections.ContainsKey($key)){$wanted=@($script:trainSelections[$key])}
    elseif($a.PSObject.Properties['trainSkills']){$wanted=@($a.trainSkills)}
    elseif($j.TrainSkillIds.Count){$wanted=@($j.TrainSkillIds)}
    return [int[]][TGLL.NativeBackend]::DefaultTrainingSkills($j.Skills,[int[]]$wanted)
}
function Sync-AccountAutoTrain($row,$j) {
    if($j.FarmEnabled -or $j.SonCapEnabled -or $j.QuestEnabled){return}
    $key=[string]$row.Tag.id
    if(!$script:autoTrainWanted[$key]){return}
    if([TGLL.NativeBackend]::UsesInstanceGameCombat($j) -or [TGLL.NativeBackend]::DungeonEntryBlocked($j) -or ($j.MailQueueOnly -or $j.CloakQueueOnly) -or $j.QuestPauseAuto -or !$j.TrainReady -or !$j.Skills.Count -or $j.ReceiveKeyRequested -or $j.IzanamiRequested -or $j.ReplayRequested -or $j.State -in @('waiting_map','waiting_popup','confirming_popup','finding_npc','choosing_key','waiting_dungeon','choosing_dungeon','creating_party','checking_dungeon_keys','finding_onoki','choosing_izanami','waiting_dungeon','choosing_dungeon','creating_party','checking_dungeon_keys','finding_onoki','choosing_izanami')){return}
    $ids=@(Get-AccountAutoSkills $row.Tag $j)
    if(!$ids.Count){$row.Cells['auto'].ToolTipText=$j.GameAutoStatus+' • Chọn 1–4 chiêu trong tab Train quái để bật train.';return}
    if($j.TrainEnabled -and ($ids -join ',') -eq ($j.TrainSkillIds -join ',')){return}
    try{[TGLL.NativeBackend]::StartTraining($j.Id,[int[]]$ids);$row.Cells['auto'].ToolTipText=$j.GameAutoStatus+' • Train: '+($ids -join ', ')}
    catch{$row.Cells['auto'].ToolTipText=$j.GameAutoStatus+' • Chưa bật train: '+$_.Exception.Message}
}
function Set-AccountAuto($row,[bool]$enabled) {
    $key=[string]$row.Tag.id
    if(!$enabled -and $script:farmUpRequests -and $script:farmUpRequests.ContainsKey($key)){Set-AutoCell $row $false;return}
    $script:autoTrainWanted[$key]=$enabled
    if(!$enabled -and [bool]$row.Tag.soncap){$row.Tag | Add-Member NoteProperty soncap $false -Force;if(!$UiTest){Save-Accounts}}
    Set-AutoCell $row $enabled
    if($script:accountJobs.ContainsKey($key)){
        $j=Get-AccountJob $script:accountJobs[$key]
        if($null -ne $j -and $j.PSObject.Properties['GameSettingsReady'] -and !$j.CloseRequested -and $j.State -notin @('failed','closed','cancelled')){
            [TGLL.NativeBackend]::SetGameAuto($j.Id,$enabled);$script:autoTrainApplied[$key]=$j.Id
            if($enabled){Sync-AccountAutoTrain $row $j}else{[TGLL.NativeBackend]::StopTraining($j.Id);if($j.FarmEnabled){[TGLL.NativeBackend]::StopMapFarm($j.Id)}}
        }
    }
    $row.Cells['auto'].ToolTipText=$(if($enabled){'Bật treo + train bằng bộ chiêu riêng; đợi game sẵn sàng nếu cần.'}else{'Tắt treo và train.'})
}
function Sync-AutoRows([int]$maxRows=0) {
    $autoRows=$grid.Rows
    if($maxRows -gt 0 -and $grid.Rows.Count -gt $maxRows){
        if($script:autoSyncCursor -ge $grid.Rows.Count){$script:autoSyncCursor=0}
        $autoRows=@(for($n=0;$n -lt $maxRows;$n++){$grid.Rows[(($script:autoSyncCursor+$n)%$grid.Rows.Count)]})
        $script:autoSyncCursor=($script:autoSyncCursor+$maxRows)%$grid.Rows.Count
    }
    foreach($row in $autoRows){
        $key=[string]$row.Tag.id
        if(!$script:accountJobs.ContainsKey($key)){continue}
        $j=Get-AccountJob $script:accountJobs[$key]
        Update-AccountCharacter $row $j
        if($null -eq $j -or !$j.PSObject.Properties['GameSettingsReady']){continue}
        if($j.State -in @('failed','closed','cancelled') -or $j.CloseRequested){Set-AutoCell $row $false;$script:autoTrainApplied.Remove($key);continue}
        Sync-GemCombineAccount $row.Tag $j
        Sync-TrainPriority $row.Tag $j
        if($j -is [TGLL.NativeJob]){$selected=@(Get-AccountAutoSkills $row.Tag $j);if(($selected -join ',') -ne ($j.HangingSkillIds -join ',')){$j.HangingSkillIds=[int[]]$selected;$j.HangingSkillCursor=0}}
        if($j -is [TGLL.NativeJob] -and $j.IzanamiMapAutoActive){$script:autoTrainWanted[$key]=$true;$script:autoTrainApplied[$key]=$j.Id;Set-AutoCell $row $true;continue}
        if($j -is [TGLL.NativeJob] -and ($j.DailyActive -or $j.SonCapEnabled -or $j.QuestEnabled)){continue}
        if($j.MailQueueOnly -or $j.CloakQueueOnly){continue}
        if($j.PSObject.Properties['ReconnectRequested'] -and $j.ReconnectRequested){continue}
        if($j.EntryAutoActivations -gt 0 -and $j.GameAutoWanted -and !$script:autoTrainWanted.ContainsKey($key)){
            $script:autoTrainWanted[$key]=$true;$script:autoTrainApplied[$key]=$j.Id;Set-AutoCell $row $true
        }
        if($script:autoTrainApplied[$key] -ne $j.Id -and $script:autoTrainWanted.ContainsKey($key)){
            [TGLL.NativeBackend]::SetGameAuto($j.Id,[bool]$script:autoTrainWanted[$key]);$script:autoTrainApplied[$key]=$j.Id
        }
        elseif($script:autoTrainApplied[$key] -eq $j.Id -and $script:autoTrainWanted.ContainsKey($key) -and [bool]$script:autoTrainWanted[$key] -ne $j.GameAutoWanted){
            $script:autoTrainWanted[$key]=$j.GameAutoWanted;Set-AutoCell $row $j.GameAutoWanted
            if(!$j.GameAutoWanted){[TGLL.NativeBackend]::StopTraining($j.Id)}
        }
        if($j.GameSettingsReady){
            if(!$script:autoTrainWanted.ContainsKey($key)){Set-AutoCell $row $j.GameAutoEnabled}
            $row.Cells['auto'].ToolTipText=$j.GameAutoStatus
        }
        Sync-AccountAutoTrain $row $j
    }
    if(Get-Command Sync-FarmUpRow -ErrorAction SilentlyContinue){foreach($farmRow in $autoRows){Sync-FarmUpRow $farmRow}}
    if($maxRows -eq 0 -or $script:autoSyncCursor -lt $maxRows){Update-DungeonUi;Update-IzanamiUi;Update-GameSettingsUi $true}
}
$grid.Add_CellValueChanged({param($sender,$e)
    if($script:updatingAuto -or $e.RowIndex -lt 0 -or $e.ColumnIndex -lt 0 -or $grid.Columns[$e.ColumnIndex].Name -ne 'auto'){return}
    $row=$grid.Rows[$e.RowIndex];if($null -eq $row.Tag){return}
    Set-AccountAuto $row ($row.Cells['auto'].Value -eq $true)
})
$dungeonPage=New-Object Windows.Forms.TabPage;$dungeonPage.Text='Auto địa cung';$dungeonPage.BackColor=$form.BackColor;$tabs.TabPages.Add($dungeonPage)
$script:loadingDungeon=$false
$dungeonAccount=Ui-Label 'Chọn tài khoản trong bảng.' $dungeonPage 16 15 635 25
$receiveKey=New-Object Windows.Forms.CheckBox;$receiveKey.Text='Nhận chìa khóa';$receiveKey.SetBounds(16,50,220,25);$dungeonPage.Controls.Add($receiveKey)
$dungeonHigh=New-Object Windows.Forms.CheckBox;$dungeonHigh.Text='Vào địa cung cao cấp';$dungeonHigh.SetBounds(250,50,250,25);$dungeonPage.Controls.Add($dungeonHigh)
$closeAfterKey=New-Object Windows.Forms.CheckBox;$closeAfterKey.Text='Tự tắt sau khi nhận chìa hoặc báo đã nhận trong ngày';$closeAfterKey.SetBounds(16,78,620,25);$dungeonPage.Controls.Add($closeAfterKey)
$null=Ui-Label "Dùng phù ID 168 → Làng → Làng Cát → Raikage → nhận chìa / vào địa cung.`r`nNếu bật tự tắt: nhận chìa, xác nhận chìa trong túi tăng rồi đóng game trước khi vào địa cung." $dungeonPage 16 110 635 45
$dungeonHint=Ui-Label '' $dungeonPage 16 158 635 42

$dungeonPage.AutoScroll=$true
$dungeonHint.Top=278
$null=Ui-Label 'Số tài khoản tối đa chạy địa cung cao cấp:' $dungeonPage 16 159 300 24
$dungeonMax=New-Object Windows.Forms.NumericUpDown;$dungeonMax.Minimum=1;$dungeonMax.Maximum=10000;$dungeonMax.SetBounds(325,158,80,24);$dungeonMax.Value=12
if($script:settings.dungeonMaxOnline){$dungeonMax.Value=[Math]::Max(1,[Math]::Min(10000,[int]$script:settings.dungeonMaxOnline))};$dungeonPage.Controls.Add($dungeonMax)
$dungeonCloseAfter=New-Object Windows.Forms.CheckBox;$dungeonCloseAfter.Text='Tự tắt game sau khi đi hết chìa địa cung cao cấp';$dungeonCloseAfter.SetBounds(16,185,620,25);$dungeonCloseAfter.Checked=[bool]$script:settings.dungeonCloseAfter;$dungeonPage.Controls.Add($dungeonCloseAfter)
$dungeonStart=Ui-NewButton 'Xếp hàng địa cung đã chọn' {try{Add-DungeonQueue @((Get-LaunchRows) | ForEach-Object {$_.Tag})}catch{Show-Error $_.Exception.Message}};Ui-Place $dungeonStart $dungeonPage 16 214 220 26
$dungeonStop=Ui-NewButton 'Hủy hàng chờ địa cung' {Reset-DungeonQueue;Update-DungeonQueueStatus};Ui-Place $dungeonStop $dungeonPage 248 214 220 26
$dungeonQueueHint=Ui-Label '' $dungeonPage 16 244 635 32
. (Join-Path $PSScriptRoot 'DungeonQueue.ps1')
[TGLL.NativeBackend]::DungeonCloseAfterEnabled=$dungeonCloseAfter.Checked
function Save-DungeonQueueSettings {[TGLL.NativeBackend]::DungeonCloseAfterEnabled=$dungeonCloseAfter.Checked;$script:settings.dungeonMaxOnline=[int]$dungeonMax.Value;$script:settings.dungeonCloseAfter=$dungeonCloseAfter.Checked;if(!$UiTest){Write-JsonAtomic $script:configPath $script:settings};Update-DungeonQueueStatus}
$dungeonMax.Add_ValueChanged({Save-DungeonQueueSettings})
$dungeonCloseAfter.Add_CheckedChanged({Save-DungeonQueueSettings})
Update-DungeonQueueStatus

function Update-DungeonUi {
    $script:loadingDungeon=$true
    try {
        if(!$grid.SelectedRows.Count){$receiveKey.Enabled=$false;$receiveKey.Checked=$false;$dungeonHigh.Enabled=$false;$dungeonHigh.Checked=$false;$closeAfterKey.Enabled=$false;$closeAfterKey.Checked=$false;return}
        $a=$grid.SelectedRows[0].Tag;if($null -eq $a){return}
        $receiveKey.Enabled=$true;$receiveKey.Checked=[bool]$a.receiveKey;$dungeonAccount.Text='Tài khoản: '+$a.username
        $dungeonHigh.Enabled=$true;$dungeonHigh.Checked=[bool]$a.dungeonHigh;$closeAfterKey.Enabled=$true;$closeAfterKey.Checked=[bool]$a.closeAfterKey
        $dungeonHint.Text=$(if($receiveKey.Checked){'Đã bật nhận chìa • Đợi game sẵn sàng.'}else{'Nhận chìa: Tắt'})
        $dungeonHint.Text+=' • Địa cung cao cấp: '+$(if($dungeonHigh.Checked){'Bật'}else{'Tắt'})
        if($script:accountJobs.ContainsKey([string]$a.id)){$j=Get-AccountJob $script:accountJobs[[string]$a.id];if($null -ne $j){$dungeonHint.Text+=' • '+$j.State;if($j.PSObject.Properties['DungeonKeyCount']){$dungeonHint.Text+=' • Chìa đã đọc: '+$j.DungeonKeyCount+' • Lượt gửi: '+$j.DungeonEntries};if($j.Error){$dungeonHint.Text+="`r`n"+(Get-LoginErrorText $j.Error)}}}
    }finally{$script:loadingDungeon=$false}
}
$receiveKey.Add_CheckedChanged({
    if($script:loadingDungeon -or !$grid.SelectedRows.Count){return}
    $a=$grid.SelectedRows[0].Tag;$enabled=$receiveKey.Checked
    try {
        if($script:accountJobs.ContainsKey([string]$a.id)){
            $j=Get-AccountJob $script:accountJobs[[string]$a.id]
            if($null -ne $j -and !$j.CloseRequested -and $j.State -notin @('failed','closed','cancelled')){[TGLL.NativeBackend]::SetReceiveKey($j.Id,$enabled)}
        }
        $a | Add-Member NoteProperty receiveKey $enabled -Force
        if(!$UiTest){Save-Accounts}
        $status.Text=$a.username+': Nhận chìa khóa '+$(if($enabled){'Bật'}else{'Tắt'})
    }catch{$status.Text=$a.username+': '+$_.Exception.Message}
    Update-DungeonUi
})
$grid.Add_SelectionChanged({Update-DungeonUi})
$dungeonHigh.Add_CheckedChanged({
    if($script:loadingDungeon -or !$grid.SelectedRows.Count){return}
    $a=$grid.SelectedRows[0].Tag;$enabled=$dungeonHigh.Checked
    try {
        if($script:accountJobs.ContainsKey([string]$a.id)){
            $j=Get-AccountJob $script:accountJobs[[string]$a.id]
            if($null -ne $j -and !$j.CloseRequested -and $j.State -notin @('failed','closed','cancelled')){[TGLL.NativeBackend]::SetDungeonHigh($j.Id,$enabled)}
        }
        $a | Add-Member NoteProperty dungeonHigh $enabled -Force
        if(!$UiTest){Save-Accounts}
        $status.Text=$a.username+': Vào địa cung cao cấp '+$(if($enabled){'Bật'}else{'Tắt'})
    }catch{$status.Text=$a.username+': '+$_.Exception.Message}
    Update-DungeonUi
})
$closeAfterKey.Add_CheckedChanged({if($script:loadingDungeon -or !$grid.SelectedRows.Count){return};$a=$grid.SelectedRows[0].Tag;$a | Add-Member NoteProperty closeAfterKey $closeAfterKey.Checked -Force;if(!$UiTest){Save-Accounts};if($script:accountJobs.ContainsKey([string]$a.id)){$j=Get-AccountJob $script:accountJobs[[string]$a.id];if($null -ne $j -and !$j.CloseRequested){[TGLL.NativeBackend]::SetCloseAfterKey($j.Id,$closeAfterKey.Checked)}};Update-DungeonUi})
Update-DungeonUi

function Enable-AllAuto {
    foreach($row in $grid.Rows){Set-AccountAuto $row $true}
    $enabled=@($grid.Rows | Where-Object {$_.Cells['auto'].Value -eq $true}).Count
    $status.Text='Đã bật / chờ Auto '+$enabled+'/'+$grid.Rows.Count+' tài khoản. Bật treo + train bằng bộ chiêu riêng từng tài khoản.'
}
$autoAllButton=Ui-NewButton 'Bật auto tất cả' {Enable-AllAuto}
$bottomButtons+=@($autoAllButton)
Resize-UiLayout
