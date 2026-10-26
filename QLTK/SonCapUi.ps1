$soncapPage=New-Object Windows.Forms.TabPage;$soncapPage.Text='Auto Sơn Cáp';$soncapPage.BackColor=$form.BackColor;$soncapPage.AutoScroll=$true;$tabs.TabPages.Add($soncapPage)
$script:loadingSonCap=$false;$script:soncapApplied=@{}
$soncapAccount=Ui-Label 'Chọn tài khoản trong bảng.' $soncapPage 16 15 630 25
$soncapCheck=New-Object Windows.Forms.CheckBox;$soncapCheck.Text='Bật Auto Sơn Cáp Myoboku';$soncapCheck.SetBounds(16,45,420,26);$soncapPage.Controls.Add($soncapCheck)
$null=Ui-Label 'Phù 168 → Làng Cát → cùng khu, lập nhóm → trưởng nhóm gặp Fukasaku.' $soncapPage 16 75 635 22
$null=Ui-Label 'Nhóm cố định: 6 tài khoản.' $soncapPage 16 103 235 24
$soncapGroupSize=New-Object Windows.Forms.NumericUpDown;$soncapGroupSize.Minimum=1;$soncapGroupSize.Maximum=6;$soncapGroupSize.Value=6;$soncapGroupSize.Visible=$false;$soncapGroupSize.Enabled=$false
$null=Ui-Label 'Số tài khoản tối đa:' $soncapPage 16 130 230 24
$soncapMax=New-Object Windows.Forms.NumericUpDown;$soncapMax.Minimum=6;$soncapMax.Maximum=10000;$soncapMax.Increment=6;$soncapMax.Value=12;$soncapMax.SetBounds(260,129,80,24);if($script:settings.soncapMaxOnline){$soncapMax.Value=[Math]::Max(6,[Math]::Min(10000,[int]$script:settings.soncapMaxOnline))};$soncapPage.Controls.Add($soncapMax)
$soncapCloseAfter=New-Object Windows.Forms.CheckBox;$soncapCloseAfter.Text='Tự tắt game sau khi hoàn thành Sơn Cáp';$soncapCloseAfter.Checked=[bool]$script:settings.soncapCloseAfter;$soncapCloseAfter.SetBounds(16,160,630,25);$soncapPage.Controls.Add($soncapCloseAfter)
$soncapQueueStart=Ui-NewButton 'Xếp hàng Sơn Cáp đã chọn' {try{Save-SonCapConfig;Add-SonCapQueue @((Get-LaunchRows) | ForEach-Object {$_.Tag})}catch{Show-Error $_.Exception.Message}};Ui-Place $soncapQueueStart $soncapPage 16 190 225 27
$soncapQueueStop=Ui-NewButton 'Hủy hàng chờ Sơn Cáp' {Reset-SonCapQueue;Update-SonCapQueueStatus};Ui-Place $soncapQueueStop $soncapPage 251 190 210 27
$soncapRouteGrid=New-Object Windows.Forms.DataGridView;$soncapRouteGrid.SetBounds(16,230,625,145);$soncapRouteGrid.AllowUserToAddRows=$false;$soncapRouteGrid.AllowUserToDeleteRows=$false;$soncapRouteGrid.AllowUserToResizeRows=$false;$soncapRouteGrid.RowHeadersVisible=$false;$soncapRouteGrid.BackgroundColor=[Drawing.Color]::White
foreach($col in @(@('stage','Map',45),@('map','ID map',60),@('name','Tên hang',225),@('x','Cổng X',90),@('y','Cổng Y',90))){$c=New-Object Windows.Forms.DataGridViewTextBoxColumn;$c.Name=$col[0];$c.HeaderText=$col[1];$c.Width=$col[2];$c.ReadOnly=$col[0] -in @('stage','map','name');$c.Resizable='False';$c.SortMode='NotSortable';$null=$soncapRouteGrid.Columns.Add($c)}
$script:soncapRoutes=@(@(94,'Hang Gamatatsu',2294,264),@(95,'Hang Gama Armored',2480,201),@(97,'Hang Gamahiro',3234,436),@(93,'Hang Gamaken',1480,277),@(96,'Hang Gamabunta',0,0))
for($i=0;$i -lt 5;$i++){$r=$script:soncapRoutes[$i];$x=$r[2];$y=$r[3];if($i -lt 4 -and $script:settings.soncapPortals -and $script:settings.soncapPortals.Count -eq 4){$x=$script:settings.soncapPortals[$i].x;$y=$script:settings.soncapPortals[$i].y};if($i -eq 2 -and [int]$x -eq 3234 -and [int]$y -eq 403){$y=436};$null=$soncapRouteGrid.Rows.Add(($i+1),$r[0],$r[1],$x,$y)}
$soncapRouteGrid.Rows[4].ReadOnly=$true;$soncapPage.Controls.Add($soncapRouteGrid)
function Apply-SonCapConfig {[TGLL.NativeBackend]::SonCapCloseAfterEnabled=$soncapCloseAfter.Checked;
    $soncapGroupSize.Value=6
    $null=$soncapRouteGrid.EndEdit();$xs=@();$ys=@();$portals=@()
    for($i=0;$i -lt 4;$i++){$x=0;$y=0;if(![int]::TryParse([string]$soncapRouteGrid.Rows[$i].Cells['x'].Value,[ref]$x) -or ![int]::TryParse([string]$soncapRouteGrid.Rows[$i].Cells['y'].Value,[ref]$y) -or $x -lt 0 -or $x -gt 32767 -or $y -lt 0 -or $y -gt 32767){throw 'Tọa độ cổng phải từ 0 đến 32767.'};$xs+=$x;$ys+=$y;$portals+=@([pscustomobject]@{map=$script:soncapRoutes[$i][0];x=$x;y=$y})}
    [TGLL.NativeBackend]::ConfigureSonCapPortals([int[]]$xs,[int[]]$ys);[TGLL.NativeBackend]::SonCapTeamSize=[int]$soncapGroupSize.Value
    $script:settings.soncapMaxOnline=[int]$soncapMax.Value;$script:settings.soncapCloseAfter=$soncapCloseAfter.Checked;$script:settings.soncapPortals=$portals;$script:settings.soncapGroupSize=[int]$soncapGroupSize.Value
}
function Save-SonCapConfig {Apply-SonCapConfig;if(!$UiTest){Write-JsonAtomic $script:configPath $script:settings}}
function Sync-SonCapPreferences {
    $ids=@()
    foreach($row in $grid.Rows){$a=$row.Tag;if(!$a){continue};$key=[string]$a.id;$wanted=[bool]$a.soncap;if(${function:Get-AutoAllStage}){$all=Get-AutoAllStage $key;if($all){$wanted=$all -eq 'soncap'}};$j=$null;if($script:accountJobs.ContainsKey($key)){$j=Get-AccountJob $script:accountJobs[$key]}
        if($j -is [TGLL.NativeJob] -and $j.DailyManaged){continue}
        if($j -is [TGLL.NativeJob] -and $j.BeastQuestEnabled){continue}
        if($wanted){$ids+=@($(if($j){$j.Id}else{''}))}
        if(!($j -is [TGLL.NativeJob]) -or !$j.TrainReady -or !$j.GameSettingsReady -or $j.CloseRequested -or $j.State -in @('failed','closed','cancelled')){continue}
        if($j.MailQueueOnly){continue}
        if($script:soncapApplied.ContainsKey($key) -and $script:soncapApplied[$key] -ne $j.Id){[TGLL.NativeBackend]::RebindSonCapJob($script:soncapApplied[$key],$j.Id)}
        try{if($wanted -ne $j.SonCapEnabled){[TGLL.NativeBackend]::SetSonCap($j.Id,$wanted)};$script:soncapApplied[$key]=$j.Id}catch{$row.Cells['status'].Value='Sơn Cáp: '+$_.Exception.Message}
        if($j.SonCapEnabled){Set-AutoCell $row $j.GameAutoWanted;$row.Cells['auto'].ToolTipText=$j.SonCapStatus}
    }
    if($script:soncapBatches.Count){Sync-SonCapTeams}else{[TGLL.NativeBackend]::ConfigureSonCapTeams([string[]]$ids)}
}
function Set-SonCapPreference($row,[bool]$enabled){$row.Tag | Add-Member NoteProperty soncap $enabled -Force;if(!$UiTest){Save-Accounts};Sync-SonCapPreferences}
function Update-SonCapUi {
    $script:loadingSonCap=$true
    try{if(!$grid.SelectedRows.Count){$soncapCheck.Enabled=$false;$soncapCheck.Checked=$false;return};$a=$grid.SelectedRows[0].Tag;$soncapCheck.Enabled=$true;$soncapCheck.Checked=[bool]$a.soncap;$soncapAccount.Text='Tài khoản: '+$a.username
        if($script:accountJobs.ContainsKey([string]$a.id)){$j=Get-AccountJob $script:accountJobs[[string]$a.id];if($j -and $j.SonCapEnabled){$soncapHint.Text=$j.SonCapStatus+"`r`n"+$j.TeamStatus+"`r`n"+$j.ZoneStatus}else{$soncapHint.Text='Bật Sơn Cáp rồi mở acc; tài khoản chưa vào game sẽ chờ sẵn sàng.'}}
    }finally{$script:loadingSonCap=$false}
}
$soncapSave=Ui-NewButton 'Lưu cổng' {try{Save-SonCapConfig;$soncapHint.Text='Đã lưu tọa độ cổng.'}catch{Show-Error $_.Exception.Message}};Ui-Place $soncapSave $soncapPage 16 388 125 27
$soncapStart=Ui-NewButton 'Bật đã chọn' {try{Save-SonCapConfig;foreach($row in @(Get-LaunchRows)){Set-SonCapPreference $row $true};Update-SonCapUi}catch{Show-Error $_.Exception.Message}};Ui-Place $soncapStart $soncapPage 151 388 155 27
$soncapStop=Ui-NewButton 'Dừng đã chọn' {foreach($row in @(Get-LaunchRows)){Set-SonCapPreference $row $false};Update-SonCapUi};Ui-Place $soncapStop $soncapPage 316 388 155 27
$null=Ui-Label 'Từng acc hết quái sống → xác nhận → tắt treo → tự qua cổng; không chờ đồng đội. Map 5 hết quái: hoàn thành.' $soncapPage 16 425 635 35
$soncapHint=Ui-Label '' $soncapPage 16 505 635 85
$soncapGroupSize.Add_ValueChanged({if($script:loadingSonCap){return};try{Save-SonCapConfig;Sync-SonCapPreferences}catch{Show-Error $_.Exception.Message}})
$soncapCheck.Add_CheckedChanged({if(!$script:loadingSonCap -and $grid.SelectedRows.Count){try{Save-SonCapConfig;Set-SonCapPreference $grid.SelectedRows[0] $soncapCheck.Checked;Update-SonCapUi}catch{Show-Error $_.Exception.Message}}})
$grid.Add_SelectionChanged({Update-SonCapUi})
$soncapQueueHint=Ui-Label '' $soncapPage 16 460 635 40
. (Join-Path $PSScriptRoot 'SonCapQueue.ps1')
function Sync-SonCapTeams {
 foreach($b in $script:soncapBatches){if($b.state -in @('waiting','done')){continue}
  foreach($m in $b.members){$null=Get-SonCapMemberJob $m}
  $members=@($b.members | Where-Object {!(Test-ActivityMemberExited $_) -and !(Test-SonCapLowLevelJob ([string]$_.job))})
  foreach($m in $members){$qjob=Get-SonCapMemberJob $m;if($qjob -is [TGLL.NativeJob]){$qjob.SonCapQueueOnly=$true}}
  $ids=[string[]]@($members | ForEach-Object {$_.job})
  if($members.Count -ne $b.members.Count){
   [TGLL.NativeBackend]::ConfigureIzanamiTeam($ids)
   if($ids.Count -gt 0){$leader=Get-AccountJob $ids[0];if($leader -is [TGLL.NativeJob] -and !$leader.CloseRequested -and !$leader.CancelRequested -and $leader.MapId -notin @(94,95,97,93,96) -and !$leader.SonCapEntrySubmitted){$leader.IzanamiRequested=$true}}
  }else{[TGLL.NativeBackend]::ConfigureSonCapTeams($ids)}
 }
}
$soncapMax.Add_ValueChanged({Save-SonCapConfig;Update-SonCapQueueStatus})
$soncapCloseAfter.Add_CheckedChanged({Save-SonCapConfig;Update-SonCapQueueStatus})
Apply-SonCapConfig;Update-SonCapUi;Update-SonCapQueueStatus
