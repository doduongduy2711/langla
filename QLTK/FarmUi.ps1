$script:farmUpRequests=@{}
$farmPage=New-Object Windows.Forms.TabPage;$farmPage.Text='Auto bãi quái';$farmPage.BackColor=$form.BackColor;$farmPage.AutoScroll=$true;$tabs.TabPages.Add($farmPage)
$null=Ui-Label 'Chế độ chạy bãi' $farmPage 16 10 220 24
$farmMode=New-Object Windows.Forms.ComboBox;$farmMode.DropDownStyle='DropDownList';$farmMode.Items.AddRange([object[]]@('Theo level nhân vật','Nhập level quái (gần nhất)','Lấy bãi acc dòng 1','Nhập map và tọa độ'));$farmMode.SelectedIndex=3;$farmMode.SetBounds(16,38,220,26);$farmPage.Controls.Add($farmMode)
$farmLevel=New-Object Windows.Forms.NumericUpDown;$farmLevel.Minimum=1;$farmLevel.Maximum=500;$farmLevel.Value=54;$farmLevel.SetBounds(245,38,70,26);$farmLevel.Enabled=$true;$farmPage.Controls.Add($farmLevel);$farmMode.Add_SelectedIndexChanged({$farmLevel.Enabled=$farmMode.SelectedIndex -in @(1,3)})
$script:farmMapCatalogue=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'MapCatalog.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$farmMaps=New-Object Windows.Forms.ComboBox;$farmMaps.DropDownStyle='DropDownList';$farmMaps.SetBounds(326,38,315,26);foreach($m in $script:farmMapCatalogue){$null=$farmMaps.Items.Add(('{0} • {1}' -f $m.Id,$(if($m.Name){$m.Name}else{'(game không có tên)'})))};$farmMaps.SelectedIndex=59;$farmPage.Controls.Add($farmMaps)
# Tọa độ nhập tay dùng trực tiếp khi Bắt đầu; không cần acc tham chiếu.
$null=Ui-Label 'Level quái' $farmPage 245 16 80 20
$null=Ui-Label 'ID map' $farmPage 16 132 64 20
$farmManualMap=New-Object Windows.Forms.NumericUpDown;$farmManualMap.Minimum=0;$farmManualMap.Maximum=106;$farmManualMap.Value=74;$farmManualMap.SetBounds(16,156,80,26);$farmPage.Controls.Add($farmManualMap)
$null=Ui-Label 'X' $farmPage 112 132 60 20
$farmManualX=New-Object Windows.Forms.NumericUpDown;$farmManualX.Minimum=0;$farmManualX.Maximum=32767;$farmManualX.Value=1490;$farmManualX.SetBounds(112,156,92,26);$farmPage.Controls.Add($farmManualX)
$null=Ui-Label 'Y' $farmPage 220 132 60 20
$farmManualY=New-Object Windows.Forms.NumericUpDown;$farmManualY.Minimum=0;$farmManualY.Maximum=32767;$farmManualY.Value=565;$farmManualY.SetBounds(220,156,92,26);$farmPage.Controls.Add($farmManualY)
$null=Ui-Label 'Tên quái (tùy chọn)' $farmPage 328 132 180 20
$farmManualName=New-Object Windows.Forms.TextBox;$farmManualName.Text='Dị cây';$farmManualName.SetBounds(328,156,170,26);$farmPage.Controls.Add($farmManualName)
function Get-ManualFarmSpot {
    $map=@($script:farmMapCatalogue | Where-Object {$_.Id -eq [int]$farmManualMap.Value})
    if($map.Count -ne 1 -or ![TGLL.NativeBackend]::IsOrdinaryFarmMap([int]$map[0].Id,[int]$map[0].Type,[string]$map[0].Name)){throw 'ID map phải là bãi quái thường, không phải làng hoặc phụ bản'}
    $o=New-Object TGLL.FarmSpot;$o.MapId=[int]$map[0].Id;$o.MapName=[string]$map[0].Name;$o.MobId=-1;$o.MobName=$farmManualName.Text.Trim();if(!$o.MobName){$o.MobName='Bãi nhập tay'};$o.Level=[int]$farmLevel.Value;$o.X=[int]$farmManualX.Value;$o.Y=[int]$farmManualY.Value;return $o
}
$saveManualFarm=Ui-NewButton 'Lưu bãi nhập' {try{$o=Get-ManualFarmSpot;$script:farmSpots=@($script:farmSpots | Where-Object {!($_.MapId -eq $o.MapId -and $_.MobId -eq -1 -and $_.Level -eq $o.Level -and $_.X -eq $o.X -and $_.Y -eq $o.Y)})+@($o);Save-FarmSpots;$farmHint.Text='Đã lưu bãi nhập: '+$o}catch{Show-Error $_.Exception.Message}};Ui-Place $saveManualFarm $farmPage 514 155 127 27
$script:farmStore=Join-Path $script:dataDir 'farm-spots.json';$script:farmSpots=@();if(Test-Path -LiteralPath $script:farmStore){try{$loadedFarm=Read-JsonData $script:farmStore;$script:farmSpots=@($loadedFarm)}catch{}}
$farmGrid=New-Object Windows.Forms.DataGridView;$farmGrid.SetBounds(16,232,625,140);$farmGrid.AllowUserToAddRows=$false;$farmGrid.AllowUserToDeleteRows=$false;$farmGrid.AllowUserToResizeColumns=$false;$farmGrid.AllowUserToResizeRows=$false;$farmGrid.RowHeadersVisible=$false;$farmGrid.ReadOnly=$true;$farmGrid.SelectionMode='FullRowSelect';$farmGrid.MultiSelect=$false;$farmGrid.ScrollBars='Both';$farmGrid.Anchor='Top,Left,Right';$farmPage.Controls.Add($farmGrid)
foreach($spec in @(@('map','ID map',52),@('name','Tên map',175),@('mob','Quái',165),@('level','Level',55),@('x','X',60),@('y','Y',60))){$c=New-Object Windows.Forms.DataGridViewTextBoxColumn;$c.Name=$spec[0];$c.HeaderText=$spec[1];$c.Width=[int]$spec[2];$c.SortMode='NotSortable';$null=$farmGrid.Columns.Add($c)}
function Show-FarmSpots {$farmGrid.Rows.Clear();foreach($s in $script:farmSpots){$i=$farmGrid.Rows.Add([object[]]@($s.MapId,$s.MapName,$s.MobName,$s.Level,$s.X,$s.Y));$farmGrid.Rows[$i].Tag=$s}}
function Save-FarmSpots {if(!$UiTest){if(!(Test-Path -LiteralPath $script:dataDir)){$null=New-Item -ItemType Directory -Path $script:dataDir};ConvertTo-Json -InputObject @($script:farmSpots) -Depth 5 | Set-Content -LiteralPath $script:farmStore -Encoding UTF8};Show-FarmSpots}
$readFarm=Ui-NewButton 'Lưu bãi các acc đang mở' {
    try {$added=0;foreach($id in @($script:accountJobs.Values)){$j=Get-AccountJob $id;if($null -eq $j -or !$j.PSObject.Properties['FarmObserved']){continue};foreach($s in $j.FarmObserved){$script:farmSpots=@($script:farmSpots | Where-Object {!($_.MapId -eq $s.MapId -and $_.MobId -eq $s.MobId -and $_.Level -eq $s.Level)})+@($s);$added++}}
        Save-FarmSpots;$farmHint.Text=$(if($added){'Đã lưu '+$added+' bãi quái. Có thể chọn hai chế độ level để chạy.'}else{'Chưa có bãi thường: cho một acc ra bãi quái, đợi 5 giây rồi lưu lại.'})
    }catch{Show-Error $_.Exception.Message}
};Ui-Place $readFarm $farmPage 16 71 220 27
function Get-FarmRunConfig {
$spots=New-Object 'System.Collections.Generic.List[TGLL.FarmSpot]';foreach($s in $script:farmSpots){$o=New-Object TGLL.FarmSpot;$o.MapId=[int]$s.MapId;$o.MapName=[string]$s.MapName;$o.MobId=[int]$s.MobId;$o.MobName=[string]$s.MobName;$o.Level=[int]$s.Level;$o.X=[int]$s.X;$o.Y=[int]$s.Y;$spots.Add($o)}
        $referenceId=$null;if($farmMode.SelectedIndex -eq 2){if(!$grid.Rows.Count){throw 'Không có tài khoản dòng 1'};$referenceId=[string]$grid.Rows[0].Tag.id;if(!$script:accountJobs.ContainsKey($referenceId)){throw 'Mở acc dòng 1 tại bãi muốn up trước'};$referenceSpot=[TGLL.NativeBackend]::GetReferenceFarmSpot($script:accountJobs[$referenceId]);$spots.Clear();$spots.Add($referenceSpot)}
        if($farmMode.SelectedIndex -eq 3){$spots.Clear();$spots.Add((Get-ManualFarmSpot))}
    return [pscustomobject]@{Spots=$spots.ToArray();ReferenceId=$referenceId;CharacterMode=($farmMode.SelectedIndex -eq 0);Level=$(if($farmMode.SelectedIndex -eq 2){[int]$referenceSpot.Level}else{[int]$farmLevel.Value});Quiet=$farmQuiet.Checked}
}
function Save-UpPreference($row,[bool]$enabled,$config=$null) {
    $row.Tag | Add-Member NoteProperty upEnabled $enabled -Force
    if($null -ne $config){$row.Tag | Add-Member NoteProperty upConfig $config -Force}
    if(!$UiTest){Save-Accounts}
}
function Restore-UpRequests {
    foreach($a in $script:accounts){
        $key=[string]$a.id;if(!$a.upEnabled -or $script:farmUpRequests.ContainsKey($key)){continue}
        if(!$a.upConfig){$script:farmUpRequests[$key]=[pscustomobject]@{Config=$null;StartedJob='';BlockedJob='';RetryAt=[datetime]::MinValue};continue}
        try{
            $c=$a.upConfig;$spots=@();foreach($s in $c.Spots){$o=New-Object TGLL.FarmSpot;foreach($f in @('MapId','MapName','MobId','MobName','Level','X','Y')){$o.$f=$s.$f};$spots+=,$o}
            $config=[pscustomobject]@{Spots=[TGLL.FarmSpot[]]$spots;ReferenceId=$c.ReferenceId;CharacterMode=[bool]$c.CharacterMode;Level=[int]$c.Level;Quiet=[bool]$c.Quiet}
            $script:farmUpRequests[$key]=[pscustomobject]@{Config=$config;StartedJob='';BlockedJob='';RetryAt=[datetime]::MinValue}
        }catch{}
    }
}
function Get-UpActivityGroups([string]$field,[int]$size) {
    $ids=@();foreach($row in $grid.Rows){if($row.Tag -and $script:farmUpRequests.ContainsKey([string]$row.Tag.id) -and [bool]$row.Tag.$field){$id='';if($script:accountJobs.ContainsKey([string]$row.Tag.id)){$id=[string]$script:accountJobs[[string]$row.Tag.id]};$ids+=,$id}}
    $groups=New-Object 'System.Collections.Generic.List[string[]]';for($n=0;$n -lt $ids.Count;$n+=$size){$last=[Math]::Min($n+$size-1,$ids.Count-1);$groups.Add([string[]]$ids[$n..$last])};return ,$groups.ToArray()
}
function Set-UpCell($row,[bool]$enabled) {
    $previous=$script:updatingUp;$script:updatingUp=$true
    try{if($row.Cells['up'].Value -ne $enabled){$row.Cells['up'].Value=$enabled}}finally{$script:updatingUp=$previous}
}
function Start-AccountFarm($row,$config) {
    $key=[string]$row.Tag.id
    if($config.ReferenceId -and $key -eq $config.ReferenceId){throw 'Acc dòng 1 đang làm tham chiếu; chọn Up ở acc khác'}
    if(!$script:accountJobs.ContainsKey($key)){throw 'Mở tài khoản và chờ vào game trước khi tích Up'}
    $id=$script:accountJobs[$key];$j=Get-AccountJob $id
    if(![TGLL.NativeBackend]::UpSessionReady($j)){throw 'Chờ nhân vật hoàn tất đăng nhập rồi chạy Up.'}
    if($script:farmUpRequests.ContainsKey($key)){$priorRequest=$script:farmUpRequests[$key];if($priorRequest.StartedJob -and $priorRequest.StartedJob -ne $id){if([TGLL.NativeBackend]::TransferDailyPriority($priorRequest.StartedJob,$id)){$priorRequest.StartedJob=$id}}}
    [TGLL.NativeBackend]::PrepareUpFarmDestination($id,$config.CharacterMode,$config.Level,[TGLL.FarmSpot[]]$config.Spots,$config.Quiet)
    [TGLL.NativeBackend]::ConfigureUpActivities($id,$dailyPriority.Checked,[bool]$row.Tag.receiveKey,[bool]$row.Tag.dungeonHigh,[bool]$row.Tag.izanami,[bool]$row.Tag.autoQuest,[bool]$row.Tag.soncap)
    if($j.DailyActive){$script:farmUpRequests[$key]=[pscustomobject]@{Config=$config;StartedJob=$id;BlockedJob='';RetryAt=[datetime]::MinValue};Save-UpPreference $row $true $config;Set-UpCell $row $true;return}
    if(!$j.FarmEnabled){[TGLL.NativeBackend]::StartMapFarm($id,$config.CharacterMode,$config.Level,[TGLL.FarmSpot[]]$config.Spots,$config.Quiet)}
    $script:farmUpRequests[$key]=[pscustomobject]@{Config=$config;StartedJob=$id;BlockedJob='';RetryAt=[datetime]::MinValue}
    Save-UpPreference $row $true $config
    Set-UpCell $row $true;Set-AutoCell $row $true;$script:autoTrainWanted[$key]=$true;$script:autoTrainApplied[$key]=$id
    $row.Cells['up'].ToolTipText='Đang chạy tới bãi đã chọn; tới nơi mới bật treo. Bỏ tích để dừng.'
}
# Giữ lựa chọn Up cả khi acc chưa mở; chỉ khởi động một lần khi game sẵn sàng.
function Request-AccountFarm($row,$config) {
    $key=[string]$row.Tag.id;if($config.ReferenceId -and $key -eq $config.ReferenceId){return}
    if(!$script:farmUpRequests.ContainsKey($key)){$script:farmUpRequests[$key]=[pscustomobject]@{Config=$config;StartedJob='';BlockedJob='';RetryAt=[datetime]::MinValue}}
    else{$script:farmUpRequests[$key].Config=$config;$script:farmUpRequests[$key].BlockedJob=''}
    Save-UpPreference $row $true $config
    Set-UpCell $row $true;Sync-FarmUpRow $row
}
function Clear-FarmAllUp {
    $upPreviousSave=$script:deferAccountSave;$script:deferAccountSave=$true
    try {
    $count=0;$errors=0
    foreach($row in $grid.Rows){
        if(!$row.Tag){continue};$key=[string]$row.Tag.id;$job=$null
        if($script:accountJobs.ContainsKey($key)){$job=Get-AccountJob $script:accountJobs[$key]}
        $hadUp=$script:farmUpRequests.ContainsKey($key) -or $row.Cells['up'].Value -eq $true -or ($job -is [TGLL.NativeJob] -and ($job.UpRequested -or $job.FarmEnabled -or $job.DailyManaged))
        $script:farmUpRequests.Remove($key);Set-UpCell $row $false;Save-UpPreference $row $false
        if($hadUp){$count++}
        if($job -is [TGLL.NativeJob]){
            $job.UpRequested=$false
            if($job.FarmEnabled -or $job.DailyManaged){try{[TGLL.NativeBackend]::StopMapFarm($job.Id);Set-AccountAuto $row $false}catch{$errors++;$row.Cells['up'].ToolTipText=$_.Exception.Message}}
        }
    }
    $message='Đã bỏ chọn Up toàn bộ: '+$count+' acc, hủy cả yêu cầu Up đang chờ.'
    if($errors){$message+=' Cần kiểm tra '+$errors+' acc chưa dừng được.'}
    $farmHint.Text=$message;$status.Text=$message

    }finally{$script:deferAccountSave=$upPreviousSave;if(!$UiTest -and !$upPreviousSave -and $script:accountSavePending){$script:accountSavePending=$false;Save-Accounts}}
}
function Start-FarmAllOpen {
    $upPreviousSave=$script:deferAccountSave;$script:deferAccountSave=$true
    try {
    try{
        $config=Get-FarmRunConfig;$chosen=0;$skipped=0
        foreach($row in $grid.Rows){
            if($null -eq $row.Tag){continue};$key=[string]$row.Tag.id
            if($config.ReferenceId -and $key -eq $config.ReferenceId){$skipped++;continue}
            Request-AccountFarm $row $config;$chosen++
        }
        $message='Đã chọn Up '+$chosen+' acc. Acc chưa sẵn sàng sẽ bắt đầu khi vào game.'
        if($skipped){$message+=' Bỏ qua acc dòng 1 đang làm tham chiếu.'}
        $farmHint.Text=$message;$status.Text=$message
    }catch{$farmHint.Text=$_.Exception.Message;$status.Text='Chưa chọn Up: '+$_.Exception.Message}

    }finally{$script:deferAccountSave=$upPreviousSave;if(!$UiTest -and !$upPreviousSave -and $script:accountSavePending){$script:accountSavePending=$false;Save-Accounts}}
}
function Sync-FarmUpRow($row) {
    if($null -eq $row.Tag){return};$key=[string]$row.Tag.id;$j=$null
    if($script:accountJobs.ContainsKey($key)){$j=Get-AccountJob $script:accountJobs[$key]}
    if($j -is [TGLL.NativeJob] -and $j.MailQueueOnly){return}
    if(!$script:farmUpRequests.ContainsKey($key) -and $row.Tag.upEnabled){Restore-UpRequests}
    if(!$script:farmUpRequests.ContainsKey($key)){
        if($j -is [TGLL.NativeJob]){$j.UpRequested=$false}
        $enabled=$j -and $j.PSObject.Properties['FarmEnabled'] -and $j.FarmEnabled -and !$j.CloseRequested -and !$j.CancelRequested -and $j.State -notin @('closed','failed','cancelled')
        Set-UpCell $row ([bool]$enabled);return
    }
    $request=$script:farmUpRequests[$key];Set-UpCell $row $true
    if($j -is [TGLL.NativeJob]){$j.UpRequested=$true}
    if($j -is [TGLL.NativeJob] -and $j.DailyActive){$row.Cells['up'].ToolTipText=$j.DailyStatus;return}
    if($null -eq $j -or !($j -is [TGLL.NativeJob]) -or ![TGLL.NativeBackend]::UpSessionReady($j)){
        $row.Cells['up'].ToolTipText='Đã chọn Up — chờ mở acc và nhân vật vào game';return
    }
    if($request.StartedJob -eq $j.Id){
        if($j.FarmEnabled){return}
        if($j.PersistentUp -and $j.DailyManaged){$row.Cells['up'].ToolTipText=$j.DailyStatus;return}
        $request.StartedJob='';$request.BlockedJob=''
        $row.Cells['up'].ToolTipText='Up vẫn được giữ; đang chuẩn bị chạy lại bãi'
    }
    if($request.BlockedJob -eq $j.Id -and $request.PSObject.Properties['RetryAt'] -and [datetime]::UtcNow -lt $request.RetryAt){return}
    try{Start-AccountFarm $row $request.Config}
    catch{$request.BlockedJob=$j.Id;$request | Add-Member NoteProperty RetryAt ([datetime]::UtcNow.AddSeconds(5)) -Force;$row.Cells['up'].ToolTipText='Up đang chờ: '+$_.Exception.Message;$row.Cells['status'].Value='Up đang chờ: '+$_.Exception.Message;$farmHint.Text=$row.Tag.username+': '+$_.Exception.Message}
}
$grid.Add_CellValueChanged({param($sender,$e)
    if($script:updatingUp -or $e.RowIndex -lt 0 -or $e.ColumnIndex -lt 0 -or $grid.Columns[$e.ColumnIndex].Name -ne 'up'){return}
    $row=$grid.Rows[$e.RowIndex];if($null -eq $row.Tag){return}
    if($row.Cells['up'].Value -eq $true){
        try{Request-AccountFarm $row (Get-FarmRunConfig);$farmHint.Text=$row.Tag.username+': đã chọn Up ở bãi đang chọn'}
        catch{Save-UpPreference $row $true;Restore-UpRequests;Set-UpCell $row $true;$row.Cells['up'].ToolTipText=$_.Exception.Message;$farmHint.Text=$row.Tag.username+': Up đang chờ cấu hình: '+$_.Exception.Message}
    }else{
        $key=[string]$row.Tag.id;$script:farmUpRequests.Remove($key);Save-UpPreference $row $false;if($script:accountJobs.ContainsKey($key)){$j=Get-AccountJob $script:accountJobs[$key];if($j -is [TGLL.NativeJob]){$j.UpRequested=$false};if($j -and ($j.FarmEnabled -or $j.DailyActive)){[TGLL.NativeBackend]::StopMapFarm($j.Id);Set-AccountAuto $row $false}}
        $farmHint.Text=$row.Tag.username+': đã dừng Up'
    }
})
$startFarm=Ui-NewButton 'Bắt đầu đã chọn' {
    try{$config=Get-FarmRunConfig;$ok=0;$err=@();foreach($row in @(Get-LaunchRows)){
        if($config.ReferenceId -and [string]$row.Tag.id -eq $config.ReferenceId){continue}
        try{Start-AccountFarm $row $config;$ok++}catch{$err+=($row.Tag.username+': '+$_.Exception.Message)}
    };$farmHint.Text='Đã bật '+$ok+' tài khoản.'+$(if($err.Count){"`r`n"+($err -join '; ')}else{''})}catch{Show-Error $_.Exception.Message}
};Ui-Place $startFarm $farmPage 245 71 160 27
$stopFarm=Ui-NewButton 'Dừng đã chọn' {foreach($row in @(Get-LaunchRows)){$script:farmUpRequests.Remove([string]$row.Tag.id);Save-UpPreference $row $false;if($script:accountJobs.ContainsKey($row.Tag.id)){[TGLL.NativeBackend]::StopMapFarm($script:accountJobs[$row.Tag.id])};Set-UpCell $row $false;Set-AccountAuto $row $false};$farmHint.Text='Đã dừng chạy bãi và tắt treo.'};Ui-Place $stopFarm $farmPage 415 71 125 27
$deleteFarm=Ui-NewButton 'Xóa bãi' {if($farmGrid.SelectedRows.Count){$s=$farmGrid.SelectedRows[0].Tag;$script:farmSpots=@($script:farmSpots | Where-Object {!($_.MapId -eq $s.MapId -and $_.MobId -eq $s.MobId -and $_.Level -eq $s.Level -and $_.X -eq $s.X -and $_.Y -eq $s.Y)});Save-FarmSpots}};Ui-Place $deleteFarm $farmPage 548 71 93 27
$farmQuiet=New-Object Windows.Forms.CheckBox;$farmQuiet.Text='Chọn khu ít người khi tới bãi và mỗi 5 phút';$farmQuiet.Checked=$true;$farmQuiet.SetBounds(16,102,420,25);$farmPage.Controls.Add($farmQuiet)
$dailyPriority=New-Object Windows.Forms.CheckBox;$dailyPriority.Text='Lặp lại sau 00:00: nhận chìa → địa cung → cấm thuật → nhiệm vụ → Sơn Cáp → Up';$dailyPriority.SetBounds(16,188,625,27);$dailyPriority.Checked=if($script:settings.ContainsKey('dailyPriority')){[bool]$script:settings.dailyPriority}else{$true};$farmPage.Controls.Add($dailyPriority)
function Sync-DailyPriorityPreferences {
    foreach($row in $grid.Rows){$key=[string]$row.Tag.id;if(!$script:farmUpRequests.ContainsKey($key) -or !$script:accountJobs.ContainsKey($key)){continue};$j=Get-AccountJob $script:accountJobs[$key];if(!$j -or !($j -is [TGLL.NativeJob]) -or !$j.DailyManaged){continue};[TGLL.NativeBackend]::ConfigureUpActivities($j.Id,$dailyPriority.Checked,[bool]$row.Tag.receiveKey,[bool]$row.Tag.dungeonHigh,[bool]$row.Tag.izanami,[bool]$row.Tag.autoQuest,[bool]$row.Tag.soncap)}
    [TGLL.NativeBackend]::ConfigureUpActivityTeams((Get-UpActivityGroups 'izanami' ([int]$izanamiGroupSize.Value)),(Get-UpActivityGroups 'soncap' 6))
    [TGLL.NativeBackend]::DailyDungeonLimit=[int]$dungeonMax.Value;[TGLL.NativeBackend]::DailyIzanamiLimit=[int]$izanamiMax.Value;[TGLL.NativeBackend]::DailyQuestLimit=[int]$questMax.Value;[TGLL.NativeBackend]::DailySonCapLimit=[int]$soncapMax.Value
    [TGLL.NativeBackend]::PulseDailyPriority([datetime]::UtcNow)
}
$dailyPriority.Add_CheckedChanged({$script:settings.dailyPriority=$dailyPriority.Checked;if(!$UiTest){Write-JsonAtomic $script:configPath $script:settings};Sync-DailyPriorityPreferences})
$farmHint=Ui-Label 'Nhập map, X/Y và level quái rồi Bắt đầu đã chọn. Có thể Lưu bãi nhập để dùng lại.' $farmPage 16 378 625 50
function Update-FarmUi {if(!$grid.SelectedRows.Count){return};$a=$grid.SelectedRows[0].Tag;if($null -eq $a -or !$a.id){return};if(!$script:accountJobs.ContainsKey($a.id)){return};$j=Get-AccountJob $script:accountJobs[$a.id];if($j -and $j.PSObject.Properties['FarmStatus']){$farmHint.Text=$a.username+' • Level nhân vật '+$j.CharacterLevel+"`r`n"+$j.FarmStatus}}
Show-FarmSpots
$grid.Add_SelectionChanged({Update-FarmUi})

Restore-UpRequests
