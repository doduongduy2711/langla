$questPage=New-Object Windows.Forms.TabPage;$questPage.Text='Auto Nhiệm vụ';$questPage.BackColor=$form.BackColor;$tabs.TabPages.Add($questPage)
$script:loadingQuest=$false;$script:questApplied=@{}
$questAccount=Ui-Label 'Chọn tài khoản trong bảng.' $questPage 16 15 630 25
$questCheck=New-Object Windows.Forms.CheckBox;$questCheck.Text='Auto nhiệm vụ tuần hoàn (tối đa 10 lượt)';$questCheck.SetBounds(16,50,450,27);$questPage.Controls.Add($questCheck)
$null=Ui-Label 'Số tài khoản tối đa chạy nhiệm vụ:' $questPage 16 84 250 24
$questMax=New-Object Windows.Forms.NumericUpDown;$questMax.Minimum=1;$questMax.Maximum=10000;$questMax.Increment=1;$questMax.SetBounds(275,82,80,24);$questMax.Value=12
if($script:settings.questMaxOnline){$questMax.Value=[Math]::Max(1,[Math]::Min(10000,[int]$script:settings.questMaxOnline))};$questPage.Controls.Add($questMax)
$questCloseAfter=New-Object Windows.Forms.CheckBox;$questCloseAfter.Text='Tự tắt game sau khi xong nhiệm vụ';$questCloseAfter.SetBounds(16,112,620,25);$questCloseAfter.Checked=[bool]$script:settings.questCloseAfter;$questPage.Controls.Add($questCloseAfter)
$questQueueStart=Ui-NewButton 'Xếp hàng nhiệm vụ đã chọn' {try{Add-QuestQueue @((Get-LaunchRows) | ForEach-Object {$_.Tag})}catch{Show-Error $_.Exception.Message}};Ui-Place $questQueueStart $questPage 16 144 220 26
$questQueueStop=Ui-NewButton 'Hủy hàng chờ nhiệm vụ' {Reset-QuestQueue;Update-QuestQueueStatus;$status.Text='Đã hủy hàng chờ nhiệm vụ; các game đã mở vẫn giữ nguyên.'};Ui-Place $questQueueStop $questPage 248 144 220 26
$questQueueHint=Ui-Label '' $questPage 16 176 635 40
$questHint=Ui-Label '' $questPage 16 222 635 70;$questPage.AutoScroll=$true
. (Join-Path $PSScriptRoot 'QuestQueue.ps1')
[TGLL.NativeBackend]::QuestCloseAfterEnabled=$questCloseAfter.Checked
function Save-QuestQueueSettings {[TGLL.NativeBackend]::QuestCloseAfterEnabled=$questCloseAfter.Checked;$script:settings.questMaxOnline=[int]$questMax.Value;$script:settings.questCloseAfter=$questCloseAfter.Checked;if(!$UiTest){Write-JsonAtomic $script:configPath $script:settings};Update-QuestQueueStatus}
$questMax.Add_ValueChanged({Save-QuestQueueSettings});$questCloseAfter.Add_CheckedChanged({Save-QuestQueueSettings});Update-QuestQueueStatus
function Sync-QuestPreferences {
    foreach($a in $script:accounts){$key=[string]$a.id;if(!$script:accountJobs.ContainsKey($key)){continue};$j=Get-AccountJob $script:accountJobs[$key]
        if($null -eq $j -or $j.CloseRequested -or $j.State -in @('closed','cancelled','failed','reconnecting')){continue}
        if($j -is [TGLL.NativeJob] -and ($j.DailyManaged -or $j.BeastQuestEnabled)){continue}
        $enabled=!$j.MailQueueOnly -and !$j.CloakQueueOnly -and [bool]$a.autoQuest -and ($j.QuestQueueOnly -or (Get-AccountLaunchMode $a) -eq 'quest')
        $signature=$j.Id+':'+$enabled
        if($script:questApplied[$key] -ne $signature){try{[TGLL.NativeBackend]::SetAutoQuest($j.Id,$enabled);$script:questApplied[$key]=$signature}catch{$status.Text=$a.username+': '+$_.Exception.Message}}
    }
}
function Update-QuestUi {
    $script:loadingQuest=$true
    try{if(!$grid.SelectedRows.Count){$questCheck.Enabled=$false;$questHint.Text='';return}
        $a=$grid.SelectedRows[0].Tag;if($null -eq $a){return};$questCheck.Enabled=$true;$questCheck.Checked=[bool]$a.autoQuest;$questAccount.Text='Tài khoản: '+$a.username
        $questHint.Text=$(if($questCheck.Checked){'Đã bật; đợi game sẵn sàng.'}else{'Auto nhiệm vụ: Tắt'})
        if($script:accountJobs.ContainsKey([string]$a.id)){$j=Get-AccountJob $script:accountJobs[[string]$a.id];if($null -ne $j){$questHint.Text=$(if($j -is [TGLL.NativeJob] -and [TGLL.NativeBackend]::QuestDisplayStatus($j)){[TGLL.NativeBackend]::QuestDisplayStatus($j)}else{$j.QuestStatus})+"`r`n"+'Đã trả trong phiên: '+$j.QuestCompleted+' • Lượt còn lại theo game: '+$j.QuestRemaining}}
    }finally{$script:loadingQuest=$false}
}
$questCheck.Add_CheckedChanged({if($script:loadingQuest -or !$grid.SelectedRows.Count){return};$a=$grid.SelectedRows[0].Tag;if($null -eq $a){return};$a | Add-Member NoteProperty autoQuest $questCheck.Checked -Force;if(!$UiTest){Save-Accounts};Sync-QuestPreferences;Update-QuestUi})
$grid.Add_SelectionChanged({Update-QuestUi});Update-QuestUi
