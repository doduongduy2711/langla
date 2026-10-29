$trainPage=New-Object Windows.Forms.TabPage;$trainPage.Text='Train quái';$trainPage.BackColor=$form.BackColor;$tabs.TabPages.Add($trainPage)
$null=Ui-Label 'Tự chọn 3 chiêu đã học khi cấu hình trống' $trainPage 16 12 630 24
$skillChoice=New-Object Windows.Forms.CheckedListBox;$skillChoice.CheckOnClick=$true;$skillChoice.SetBounds(16,43,455,95);$trainPage.Controls.Add($skillChoice)
$scanSkills=Ui-NewButton 'Đọc ID chiêu' {
    try {$a=Selected-Account;if(!$script:accountJobs.ContainsKey($a.id)){throw 'Mở tài khoản bằng phiên QLTK này trước.'};[TGLL.NativeBackend]::ScanSkills($script:accountJobs[$a.id]);$trainHint.Text='Đang đọc danh sách chiêu...'}catch{Show-Error $_.Exception.Message}
};Ui-Place $scanSkills $trainPage 483 43 150 27
$null=Ui-Label 'Tự chọn Phi Lôi, phân thân và kiếm cơ bản khi chưa chọn chiêu.' $trainPage 483 78 150 60
$script:trainSelections=@{};$script:skillViewKey='';$script:loadingSkills=$false
function Get-CheckedSkillIds {return [int[]]@($skillChoice.CheckedItems | ForEach-Object {$_.Id})}
function Save-TrainChoice {if($grid.SelectedRows.Count){$script:trainSelections[[string]$grid.SelectedRows[0].Tag.id]=@(Get-CheckedSkillIds)}}
function Save-TrainPreferences {
    foreach($a in $script:accounts){if($script:trainSelections.ContainsKey([string]$a.id)){$a | Add-Member -MemberType NoteProperty -Name trainSkills -Value @($script:trainSelections[[string]$a.id]) -Force}}
    if(!$UiTest){Save-Accounts}
}
$startTrain=Ui-NewButton 'Bật train đã chọn' {
    try {
        $a=Selected-Account;if(!$script:accountJobs.ContainsKey($a.id)){throw 'Mở tài khoản bằng phiên QLTK này trước.'};$j=Get-AccountJob $script:accountJobs[$a.id];$ids=@([TGLL.NativeBackend]::DefaultTrainingSkills($j.Skills,[int[]]@(Get-CheckedSkillIds)));if($ids.Count -lt 1 -or $ids.Count -gt 4){throw 'Tích chọn từ 1 đến 4 chiêu.'};Save-TrainChoice
        $opened=0;$failed=0
        foreach($row in @(Get-LaunchRows)){
            try {if(!$script:accountJobs.ContainsKey($row.Tag.id)){throw 'not_open'};[TGLL.NativeBackend]::StartTraining($script:accountJobs[$row.Tag.id],[int[]]$ids);$script:trainSelections[[string]$row.Tag.id]=@($ids);$opened++}catch{$failed++}
        }
        $status.Text='Đã bật train '+$opened+' tài khoản • Chiêu: '+($ids -join ', ')
        if($failed){$status.Text+=' • '+$failed+' tài khoản chưa có đủ chiêu hoặc chưa mở.'}
        Save-TrainPreferences
    }catch{Show-Error $_.Exception.Message}
};Ui-Place $startTrain $trainPage 16 145 175 28
$stopTrain=Ui-NewButton 'Dừng train đã chọn' {foreach($row in @(Get-LaunchRows)){Set-AccountAuto $row $false};$status.Text='Đã yêu cầu dừng train các tài khoản được chọn.'};Ui-Place $stopTrain $trainPage 203 145 175 28
$trainHint=Ui-Label 'Mở tài khoản để đọc chiêu.' $trainPage 16 183 635 42
$null=Ui-Label 'Dùng chiêu qua nút skill của game • Treo game dùng bộ chiêu; QLTK chỉ bổ sung chiêu cấp 1 đã tích, mỗi 10 ms. Đợi đòn trước hoàn tất.' $trainPage 16 227 635 24
$trainPriorityThiQuy=New-Object Windows.Forms.CheckBox
$trainPriorityThiQuy.Text='Ưu tiên đánh Thi Quỷ';$trainPriorityThiQuy.SetBounds(390,145,245,25);$trainPage.Controls.Add($trainPriorityThiQuy)
$script:loadingTrainPriority=$false
function Sync-TrainPriority($account,$job) {
    if($job -is [TGLL.NativeJob] -and $job.PrioritizeThiQuy -ne [bool]$account.trainPriorityThiQuy){[TGLL.NativeBackend]::SetThiQuyPriority($job.Id,[bool]$account.trainPriorityThiQuy)}
}
function Save-TrainPriority {
    if($script:loadingTrainPriority -or !$grid.SelectedRows.Count){return}
    $account=$grid.SelectedRows[0].Tag
    $account | Add-Member NoteProperty trainPriorityThiQuy $trainPriorityThiQuy.Checked -Force
    if(!$UiTest){Save-Accounts}
    if($script:accountJobs.ContainsKey([string]$account.id)){Sync-TrainPriority $account (Get-AccountJob $script:accountJobs[[string]$account.id])}
}
$trainPriorityThiQuy.Add_CheckedChanged({try{Save-TrainPriority}catch{$status.Text=$_.Exception.Message}})
function Update-TrainUi {
    if(!$grid.SelectedRows.Count){return}
    $a=$grid.SelectedRows[0].Tag;$key=[string]$a.id
    $script:loadingTrainPriority=$true;try{$trainPriorityThiQuy.Checked=[bool]$a.trainPriorityThiQuy}finally{$script:loadingTrainPriority=$false}
    if(!$script:accountJobs.ContainsKey($key)){$script:loadingSkills=$true;$skillChoice.Items.Clear();$script:loadingSkills=$false;$script:skillViewKey='';$trainHint.Text='Tài khoản chưa mở trong phiên QLTK này.';return}
    $j=Get-AccountJob $script:accountJobs[$key]
    if(!$j.Skills.Count){$script:loadingSkills=$true;$skillChoice.Items.Clear();$script:loadingSkills=$false;$script:skillViewKey='';$trainHint.Text='Đang đợi danh sách chiêu từ nhân vật...';return}
    $signature=$key+':'+[string]::Join(',',@($j.Skills | Where-Object Usable | ForEach-Object {$_.ToString()}))
    if($signature -ne $script:skillViewKey){
        $wanted=@(Get-AccountAutoSkills $a $j)
        $script:loadingSkills=$true;$skillChoice.Items.Clear()
        foreach($skill in $j.Skills){if($skill.Usable){$null=$skillChoice.Items.Add($skill,($wanted -contains $skill.Id))}}
        $script:loadingSkills=$false;$script:skillViewKey=$signature;Save-TrainChoice
    }
    $text=$a.username+' • Auto skill: '+$(if($j.TrainEnabled -or $j.GameAutoWanted){'Bật'}else{'Tắt'})
    if($a.trainPriorityThiQuy){$text+=' • '+$j.ThiQuyPriorityStatus}
    if($trainHint.Text -ne $text){$trainHint.Text=$text}
}
$skillChoice.Add_ItemCheck({
    if($script:loadingSkills){return}
    if($skillChoice.Items[$_.Index].Id -eq 295 -and $_.NewValue -eq [Windows.Forms.CheckState]::Unchecked){$_.NewValue=[Windows.Forms.CheckState]::Checked;$status.Text='Phi Lôi Thần Thuật là chiêu mặc định. Bỏ Auto để dừng train.';return}
    $ids=New-Object 'System.Collections.Generic.List[int]'
    for($i=0;$i -lt $skillChoice.Items.Count;$i++){$checked=$skillChoice.GetItemChecked($i);if($i -eq $_.Index){$checked=$_.NewValue -eq [Windows.Forms.CheckState]::Checked};if($checked){$ids.Add($skillChoice.Items[$i].Id)}}
    if($ids.Count -gt 4){$_.NewValue=[Windows.Forms.CheckState]::Unchecked;$status.Text='Chỉ chọn tối đa 4 chiêu.';return}
    if($grid.SelectedRows.Count){$key=[string]$grid.SelectedRows[0].Tag.id;$script:trainSelections[$key]=@($ids.ToArray());if($script:accountJobs.ContainsKey($key)){$j=Get-AccountJob $script:accountJobs[$key];if($j.TrainEnabled){if(!$ids.Count){[TGLL.NativeBackend]::StopTraining($j.Id)}else{try{[TGLL.NativeBackend]::StartTraining($j.Id,[int[]]$ids.ToArray())}catch{$status.Text=$_.Exception.Message}}}}}
    try{Save-TrainPreferences}catch{$status.Text='Chưa lưu được lựa chọn chiêu.'}
})
$grid.Add_SelectionChanged({Update-TrainUi})
