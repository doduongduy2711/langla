function Sync-IzanamiTeams {
    if($script:izanamiBatches.Count){foreach($b in $script:izanamiBatches | Where-Object {$_.state -notin @('waiting','done','closing')}){foreach($m in $b.members){$null=Get-IzanamiMemberJob $m};[TGLL.NativeBackend]::ConfigureIzanamiTeam([string[]]@($b.members | Where-Object {!(Test-ActivityMemberExited $_)} | ForEach-Object {$_.job}))};return}
    $ids=[string[]]@($grid.Rows | ForEach-Object {if($script:accountJobs.ContainsKey([string]$_.Tag.id)){$teamJob=Get-AccountJob $script:accountJobs[[string]$_.Tag.id];if($teamJob -and !$teamJob.SonCapEnabled){$teamJob.Id}else{''}}else{''}})
    [TGLL.NativeBackend]::ConfigureIzanamiTeams($ids)
}
$izanamiPage=New-Object Windows.Forms.TabPage;$izanamiPage.Text='Auto cấm thuật';$izanamiPage.BackColor=$form.BackColor;$tabs.TabPages.Add($izanamiPage)
$script:loadingIzanami=$false
$izanamiAccount=Ui-Label 'Chọn tài khoản trong bảng.' $izanamiPage 16 15 630 25
$izanamiCheck=New-Object Windows.Forms.CheckBox;$izanamiCheck.Text='Vào Cấm thuật Izanami';$izanamiCheck.SetBounds(16,45,450,27);$izanamiPage.Controls.Add($izanamiCheck)
$null=Ui-Label 'Phù 168 → Làng Cát → cùng khu, tạo nhóm → trưởng nhóm vào Onoki' $izanamiPage 16 70 635 18
$null=Ui-Label 'Số tài khoản tối đa chạy cấm thuật:' $izanamiPage 16 91 250 24
$izanamiMax=New-Object Windows.Forms.NumericUpDown;$izanamiMax.Minimum=1;$izanamiMax.Maximum=10000;$izanamiMax.Increment=1;$izanamiMax.SetBounds(275,90,80,24);$izanamiMax.Value=12
if($script:settings.izanamiMaxOnline){$izanamiMax.Value=[Math]::Max(1,[Math]::Min(10000,[int]$script:settings.izanamiMaxOnline))};$izanamiPage.Controls.Add($izanamiMax)
$null=Ui-Label 'Số người mỗi nhóm (1–6):' $izanamiPage 16 116 250 24
$izanamiGroupSize=New-Object Windows.Forms.NumericUpDown;$izanamiGroupSize.Minimum=1;$izanamiGroupSize.Maximum=6;$izanamiGroupSize.SetBounds(275,115,80,24);$izanamiGroupSize.Value=6
if($script:settings.izanamiGroupSize){$izanamiGroupSize.Value=[Math]::Max(1,[Math]::Min(6,[int]$script:settings.izanamiGroupSize))};$izanamiPage.Controls.Add($izanamiGroupSize)
$null=Ui-Label 'Số lượt:' $izanamiPage 390 116 65 24
$izanamiRounds=New-Object Windows.Forms.NumericUpDown;$izanamiRounds.Minimum=1;$izanamiRounds.Maximum=1000;$izanamiRounds.Value=1;$izanamiRounds.SetBounds(460,115,80,24)
if($script:settings.izanamiRounds){$izanamiRounds.Value=[Math]::Max(1,[Math]::Min(1000,[int]$script:settings.izanamiRounds))};$izanamiPage.Controls.Add($izanamiRounds)
[TGLL.NativeBackend]::IzanamiRoundLimit=[int]$izanamiRounds.Value
[TGLL.NativeBackend]::IzanamiTeamSize=[int]$izanamiGroupSize.Value
$izanamiCloseAfter=New-Object Windows.Forms.CheckBox;$izanamiCloseAfter.Text='Tự tắt game sau khi xong cấm thuật';$izanamiCloseAfter.SetBounds(16,144,620,25);$izanamiCloseAfter.Checked=[bool]$script:settings.izanamiCloseAfter;$izanamiPage.Controls.Add($izanamiCloseAfter)
$izanamiQueueStart=Ui-NewButton 'Xếp hàng cấm thuật đã chọn' {try{Add-IzanamiQueue @((Get-LaunchRows) | ForEach-Object {$_.Tag})}catch{Show-Error $_.Exception.Message}};Ui-Place $izanamiQueueStart $izanamiPage 16 172 220 26
$izanamiQueueStop=Ui-NewButton 'Hủy hàng chờ cấm thuật' {Reset-IzanamiQueue;Update-IzanamiQueueStatus;$status.Text='Đã hủy hàng chờ cấm thuật; các game đã mở vẫn giữ nguyên.'};Ui-Place $izanamiQueueStop $izanamiPage 248 172 220 26
$izanamiQueueHint=Ui-Label '' $izanamiPage 16 204 635 40
$izanamiHint=Ui-Label '' $izanamiPage 16 250 635 70
$izanamiPage.AutoScroll=$true
. (Join-Path $PSScriptRoot 'IzanamiQueue.ps1')
[TGLL.NativeBackend]::IzanamiCloseAfterEnabled=$izanamiCloseAfter.Checked
function Sync-IzanamiMaxStep {
    $step=[int]$izanamiGroupSize.Value
    $izanamiMax.Increment=$step
    $izanamiMax.Minimum=$step
    $izanamiMax.Maximum=([Math]::Floor(10000.0/$step)*$step)
    $izanamiMax.Value=[Math]::Min([double]$izanamiMax.Maximum,([Math]::Ceiling([double]$izanamiMax.Value/$step)*$step))
}
Sync-IzanamiMaxStep
function Save-IzanamiQueueSettings {$script:settings.izanamiRounds=[int]$izanamiRounds.Value;[TGLL.NativeBackend]::IzanamiRoundLimit=[int]$izanamiRounds.Value;$script:settings.izanamiGroupSize=[int]$izanamiGroupSize.Value;[TGLL.NativeBackend]::IzanamiTeamSize=[int]$izanamiGroupSize.Value;[TGLL.NativeBackend]::IzanamiCloseAfterEnabled=$izanamiCloseAfter.Checked;$script:settings.izanamiMaxOnline=[int]$izanamiMax.Value;$script:settings.izanamiCloseAfter=$izanamiCloseAfter.Checked;if(!$UiTest){Write-JsonAtomic $script:configPath $script:settings};Update-IzanamiQueueStatus}
$izanamiRounds.Add_ValueChanged({Save-IzanamiQueueSettings;$status.Text='Số lượt áp dụng cho lần bật/mở tiếp theo; tổ đang chạy giữ số lượt đã chốt.'})
$izanamiMax.Add_ValueChanged({Save-IzanamiQueueSettings})
$izanamiGroupSize.Add_ValueChanged({Sync-IzanamiMaxStep;Save-IzanamiQueueSettings;Sync-IzanamiTeams;$status.Text='Đã đổi số người mỗi nhóm; các tổ đã xếp hàng giữ nguyên.'})
$izanamiCloseAfter.Add_CheckedChanged({Save-IzanamiQueueSettings})
Update-IzanamiQueueStatus
function Update-IzanamiUi {
    $script:loadingIzanami=$true
    try {
        if(!$grid.SelectedRows.Count){$izanamiCheck.Enabled=$false;$izanamiCheck.Checked=$false;$izanamiAccount.Text='Chọn tài khoản trong bảng.';$izanamiHint.Text='';return}
        $a=$grid.SelectedRows[0].Tag;$izanamiCheck.Enabled=$true;$izanamiCheck.Checked=[bool]$a.izanami;$izanamiAccount.Text='Tài khoản: '+$a.username
        $izanamiHint.Text=$(if($izanamiCheck.Checked){'Đã bật • Đợi game và nhóm sẵn sàng.'}else{'Auto cấm thuật: Tắt'})
        if($script:accountJobs.ContainsKey([string]$a.id)){
            $j=Get-AccountJob $script:accountJobs[[string]$a.id]
            if($null -ne $j -and $j.PSObject.Properties['IzanamiEnabled']){
                if($j.PSObject.Properties['GameAutoStatus']){$izanamiAccount.Text+=' • '+$j.GameAutoStatus}
                if($j.IzanamiRequested){$izanamiHint.Text+=' • Đang chờ xử lý.'}
                $izanamiHint.Text+=' • Lượt xong: '+$j.IzanamiRoundsCompleted+'/'+$j.IzanamiRoundsTarget
                if($j.IzanamiNoAttempts){$izanamiHint.Text='Hết lượt tham gia cấm thuật • '+$(if($izanamiCloseAfter.Checked){'Chờ tắt game theo tùy chọn.'}else{'Giữ game vì chưa tích tự tắt.'})+"`r`n"+$j.IzanamiLimitNotice}
                if($j.State -in @('finding_onoki','choosing_izanami','izanami_submitted','izanami_unavailable','creating_party')){
                    $text=@{finding_onoki='Đang tìm Onoki';choosing_izanami='Đang chọn Cấm thuật Izanami';izanami_submitted='Đã gửi vào Cấm thuật Izanami';izanami_unavailable='Chưa vào được cấm thuật';creating_party='Đang tạo/đợi nhóm'}[$j.State];$izanamiHint.Text+=' • '+$text
                    if($j.Error){$izanamiHint.Text+="`r`n"+(Get-LoginErrorText $j.Error)}
                }
                $izanamiHint.Text+=' • Lượt gửi: '+$j.IzanamiEntries; if($j.TeamStatus){$izanamiHint.Text+="`r`n"+$j.TeamStatus};if($j.ZoneStatus){$izanamiHint.Text+="`r`n"+$j.ZoneStatus};if($j.FollowStatus){$izanamiHint.Text+="`r`n"+$j.FollowStatus}
            }
        }
    }finally{$script:loadingIzanami=$false}
}
$izanamiCheck.Add_CheckedChanged({
    if($script:loadingIzanami -or !$grid.SelectedRows.Count){return}
    $a=$grid.SelectedRows[0].Tag;$enabled=$izanamiCheck.Checked
    try {
        if($script:accountJobs.ContainsKey([string]$a.id)){
            $j=Get-AccountJob $script:accountJobs[[string]$a.id]
            if($null -ne $j -and !$j.CloseRequested -and $j.State -notin @('failed','closed','cancelled')){[TGLL.NativeBackend]::SetIzanami($j.Id,$enabled)}
        }
        $a | Add-Member NoteProperty izanami $enabled -Force;if(!$UiTest){Save-Accounts}
        $status.Text=$a.username+': Auto Cấm thuật Izanami '+$(if($enabled){'Bật'}else{'Tắt'})
    }catch{$status.Text=$a.username+': '+$_.Exception.Message}
    Update-IzanamiUi
})
$grid.Add_SelectionChanged({Update-IzanamiUi})
Update-IzanamiUi
