$giftPage=New-Object Windows.Forms.TabPage;$giftPage.Text='Auto Gift Code';$giftPage.BackColor=$form.BackColor;$giftPage.AutoScroll=$true;$tabs.TabPages.Add($giftPage)
$script:loadingGift=$false;$script:giftApplied=@{};$script:giftDisplayedAccount=''
$giftAccount=Ui-Label 'Chọn tài khoản trong bảng.' $giftPage 16 12 630 23
$giftEnabled=New-Object Windows.Forms.CheckBox;$giftEnabled.Text='Tự nhập gift code khi game sẵn sàng';$giftEnabled.SetBounds(16,40,360,26);$giftPage.Controls.Add($giftEnabled)
$null=Ui-Label 'Mỗi dòng một mã (tối đa 30 ký tự). Mã trùng chỉ gửi một lần trong phiên.' $giftPage 16 70 630 25
$giftCodes=New-Object Windows.Forms.TextBox;$giftCodes.Multiline=$true;$giftCodes.ScrollBars='Vertical';$giftCodes.SetBounds(16,100,400,98);$giftCodes.Anchor='Top,Left,Right';$giftPage.Controls.Add($giftCodes)
$giftStart=New-Object Windows.Forms.Button;$giftStart.Text='Áp dụng và bật đã chọn';$giftStart.SetBounds(16,207,205,28);$giftPage.Controls.Add($giftStart)
$giftStop=New-Object Windows.Forms.Button;$giftStop.Text='Dừng đã chọn';$giftStop.SetBounds(230,207,150,28);$giftPage.Controls.Add($giftStop)
$giftHint=Ui-Label '' $giftPage 16 243 630 65;$giftHint.Anchor='Top,Left,Right'
function Get-GiftCodes($a){[TGLL.NativeBackend]::NormalizeGiftCodes([string[]]@($a.giftCodes))}
function Sync-GiftPreferences {
    foreach($a in $script:accounts){
        $key=[string]$a.id;if(!$script:accountJobs.ContainsKey($key)){continue}
        $j=Get-AccountJob $script:accountJobs[$key];if($null -eq $j -or $j.CloseRequested -or $j.State -in @('failed','closed','cancelled','reconnecting')){continue}
        $codes=[string[]]@(Get-GiftCodes $a);$signature=$j.Id+':'+[bool]$a.giftEnabled+':'+($codes -join "`n")
        if($script:giftApplied[$key] -eq $signature){continue}
        try{[TGLL.NativeBackend]::ConfigureGiftCodes($j.Id,[bool]$a.giftEnabled,$codes);$script:giftApplied[$key]=$signature}catch{$status.Text=$a.username+': '+$_.Exception.Message}
    }
}
function Update-GiftUi {
    $script:loadingGift=$true
    try{
        $has=$grid.SelectedRows.Count -gt 0;$giftEnabled.Enabled=$has;$giftCodes.Enabled=$has
        if(!$has){$giftAccount.Text='Chọn tài khoản trong bảng.';$giftHint.Text='';$script:giftDisplayedAccount='';return}
        $a=$grid.SelectedRows[0].Tag;$giftAccount.Text='Tài khoản: '+$a.username
        $giftEnabled.Checked=[bool]$a.giftEnabled
        if($script:giftDisplayedAccount -ne [string]$a.id -or !$giftCodes.Focused){$giftCodes.Text=@($a.giftCodes) -join "`r`n"};$script:giftDisplayedAccount=[string]$a.id
        $giftHint.Text=$(if($a.giftEnabled){'Đã bật • '+@(Get-GiftCodes $a).Count+' mã.'}else{'Auto Gift Code: Tắt'})
        if($script:accountJobs.ContainsKey([string]$a.id)){$j=Get-AccountJob $script:accountJobs[[string]$a.id];if($null -ne $j){$giftHint.Text=$j.GiftStatus+' • Đã gửi: '+$j.GiftRequests;if($j.GiftResponse){$giftHint.Text+="`r`n"+$j.GiftResponse}}}
    }finally{$script:loadingGift=$false}
}
function Save-GiftUi {
    if($script:loadingGift -or !$grid.SelectedRows.Count){return}
    try{
        $codes=[TGLL.NativeBackend]::NormalizeGiftCodes([string[]]($giftCodes.Text -split '\r?\n'));if($giftEnabled.Checked -and !$codes.Count){throw 'Nhập ít nhất một gift code.'}
        $a=$grid.SelectedRows[0].Tag;$a | Add-Member NoteProperty giftCodes @($codes) -Force;$a | Add-Member NoteProperty giftEnabled $giftEnabled.Checked -Force
        if(!$UiTest){Save-Accounts};Sync-GiftPreferences;Update-GiftUi
    }catch{$status.Text=$_.Exception.Message;Update-GiftUi}
}
function Set-GiftSelected([bool]$enabled) {
    try{
        $rows=@(Get-LaunchRows);if(!$rows.Count){throw 'Tích Chọn các tài khoản cần áp dụng.'}
        $codes=[TGLL.NativeBackend]::NormalizeGiftCodes([string[]]($giftCodes.Text -split '\r?\n'));if($enabled -and !$codes.Count){throw 'Nhập ít nhất một gift code.'}
        foreach($row in $rows){$a=$row.Tag;if($enabled){$a | Add-Member NoteProperty giftCodes @($codes.Clone()) -Force};$a | Add-Member NoteProperty giftEnabled $enabled -Force}
        if(!$UiTest){Save-Accounts};Sync-GiftPreferences;Update-GiftUi;$status.Text=$(if($enabled){'Đã bật gift code cho '}else{'Đã dừng gift code cho '})+$rows.Count+' tài khoản.'
    }catch{$status.Text=$_.Exception.Message}
}
$giftEnabled.Add_CheckedChanged({Save-GiftUi});$giftCodes.Add_Leave({Save-GiftUi});$giftStart.Add_Click({Set-GiftSelected $true});$giftStop.Add_Click({Set-GiftSelected $false})
$grid.Add_SelectionChanged({Update-GiftUi});Update-GiftUi
