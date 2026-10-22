if($farmMaps.Items.Count -ne 107 -or $script:farmMapCatalogue[59].Name -ne 'Làng Cát' -or $farmMode.Items.Count -ne 4){throw 'Farm map catalogue/modes invalid'}
$oldSpots=$script:farmSpots;$script:farmSpots=@()
$fa=$script:accounts[0];$fk=[string]$fa.id
$fj=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'NonPublic,Static').GetValue($null)
$fc=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0]
$fixture=$fc.Invoke(@('C:\synthetic.exe','farm-test','unused',$null,$true));$fixture.State='opened';$fixture.ReceiveKeyEnabled=$false;$fixture.TrainReady=$true;$fixture.GameSettingsReady=$true;$fixture.CharacterLevel=52
$spot=New-Object TGLL.FarmSpot;$spot.MapId=74;$spot.MapName='Dòng Sông Kusagakure';$spot.MobId=99;$spot.MobName='Quái thường';$spot.Level=50;$spot.X=500;$spot.Y=428;$fixture.FarmObserved=[TGLL.FarmSpot[]]@($spot)
$null=$fj.TryAdd($fixture.Id,$fixture);$script:accountJobs[$fk]=$fixture.Id;$grid.Rows[0].Cells['pick'].Value=$true
$fixture.ZoneId=7;$fixture.ZoneReadAt=[datetime]::UtcNow;Update-UiSummary
if($grid.Rows[0].Cells['zone'].Value -ne '7' -or $grid.Columns['zone'].DisplayIndex -ne 6 -or $grid.Columns['zone'].Width -ne 55){throw 'Zone column value/order/width invalid'}
$fixture.ZoneReadAt=[datetime]::UtcNow.AddSeconds(-10);Update-UiSummary;if($grid.Rows[0].Cells['zone'].Value -ne '—'){throw 'Stale zone must not be shown'}
$fixture.ZoneId=0;$fixture.ZoneReadAt=[datetime]::UtcNow;Update-UiSummary;if($grid.Rows[0].Cells['zone'].Value -ne '0'){throw 'Valid zone zero hidden'}
if(!$farmQuiet.Checked){throw 'Quiet-zone default not enabled'}
$click=[Windows.Forms.Control].GetMethod('OnClick',[Reflection.BindingFlags]'Instance,NonPublic')
$null=$click.Invoke($readFarm,[object[]]@([EventArgs]::Empty));$null=$click.Invoke($readFarm,[object[]]@([EventArgs]::Empty))
if($script:farmSpots.Count -ne 1 -or $farmGrid.Rows.Count -ne 1){throw 'Farm capture not saved or duplicated'}
$farmMode.SelectedIndex=1;$farmLevel.Value=51
$null=$click.Invoke($startFarm,[object[]]@([EventArgs]::Empty))
if(!$fixture.FarmQuietZone -or !$fixture.FarmEnabled -or $fixture.FarmArrived -or !$fixture.GameAutoWanted -or !$grid.Rows[0].Cells['auto'].Value){throw 'Farm start failed or idles before arrival'}
$null=$click.Invoke($stopFarm,[object[]]@([EventArgs]::Empty));if($fixture.FarmEnabled -or $fixture.GameAutoWanted){throw 'Farm stop failed'}
$farmMode.SelectedIndex=0;if($farmLevel.Enabled){throw 'Character mode must not use manual level input'}
$null=$click.Invoke($startFarm,[object[]]@([EventArgs]::Empty));if(!$fixture.FarmEnabled){throw 'Character mode start failed'}
Set-AccountAuto $grid.Rows[0] $false;if(!$fixture.FarmEnabled){throw 'Auto toggle must retain Up'};$grid.Rows[0].Cells['up'].Value=$false
$testAddedRow=$grid.Rows.Add();$grid.Rows[$testAddedRow].Tag=[pscustomobject]@{id='farm-second-account';username='farm-second'}
# Acc thứ hai lấy tọa độ nhân vật dòng 1, dù chưa lưu bãi nào.
$fb=$grid.Rows[1].Tag;$fbk=[string]$fb.id;$second=$fc.Invoke(@('C:\synthetic.exe','farm-second','unused',$null,$true));$second.State='opened';$second.ReceiveKeyEnabled=$false;$second.TrainReady=$true;$second.GameSettingsReady=$true
$null=$fj.TryAdd($second.Id,$second);$script:accountJobs[$fbk]=$second.Id
$fixture.FarmPositionMap=74;$fixture.FarmPositionX=555;$fixture.FarmPositionY=444;$fixture.FarmPositionReadAt=[datetime]::UtcNow
$reference=[TGLL.NativeBackend]::GetReferenceFarmSpot($fixture.Id);if($reference.X -ne 555 -or $reference.Y -ne 444 -or $reference.MapId -ne 74){throw 'Reference uses mob coordinates instead of character coordinates'}
$fixture.FarmPositionReadAt=[datetime]::UtcNow.AddSeconds(-20);$staleRejected=$false;try{$null=[TGLL.NativeBackend]::GetReferenceFarmSpot($fixture.Id)}catch{$staleRejected=$true};if(!$staleRejected){throw 'Stale reference accepted'}
$fixture.FarmPositionReadAt=[datetime]::UtcNow;$script:farmSpots=@();$grid.Rows[0].Cells['pick'].Value=$true;$grid.Rows[1].Cells['pick'].Value=$true;$farmMode.SelectedIndex=2
$null=$click.Invoke($startFarm,[object[]]@([EventArgs]::Empty))
$choices=[TGLL.NativeJob].GetField('FarmChoices',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($second)
if(!$second.FarmEnabled -or $fixture.FarmEnabled -or $choices.Count -ne 1 -or $choices[0].X -ne 555 -or $choices[0].Y -ne 444){throw 'Second account did not start from row-one reference without saved spots'}
[TGLL.NativeBackend]::StopMapFarm($second.Id);$grid.Rows[1].Cells['pick'].Value=$false;$script:accountJobs.Remove($fbk);$removed=$null;$null=$fj.TryRemove($second.Id,[ref]$removed)
# Nhập tay chạy được khi không có acc tham chiếu hoặc bãi đã lưu.
$script:farmSpots=@();$fixture.FarmPositionReadAt=[datetime]::MinValue;$fixture.FarmObserved=[TGLL.FarmSpot[]]@();$farmMode.SelectedIndex=3
if(!$farmLevel.Enabled){throw 'Manual coordinate level disabled'}
$farmManualMap.Value=74;$farmManualX.Value=1490;$farmManualY.Value=565;$farmLevel.Value=54;$farmManualName.Text='Dị cây'
$null=$click.Invoke($startFarm,[object[]]@([EventArgs]::Empty))
$manualChoices=[TGLL.NativeJob].GetField('FarmChoices',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($fixture)
if(!$fixture.FarmEnabled -or $manualChoices.Count -ne 1 -or $manualChoices[0].MapId -ne 74 -or $manualChoices[0].X -ne 1490 -or $manualChoices[0].Y -ne 565 -or $manualChoices[0].Level -ne 54){throw 'Manual coordinates were not passed directly'}
$null=$click.Invoke($stopFarm,[object[]]@([EventArgs]::Empty))
$null=$click.Invoke($saveManualFarm,[object[]]@([EventArgs]::Empty));$null=$click.Invoke($saveManualFarm,[object[]]@([EventArgs]::Empty));if($script:farmSpots.Count -ne 1){throw 'Manual save duplicated'}
$farmManualMap.Value=59;$rejected=$false;try{$null=Get-ManualFarmSpot}catch{$rejected=$true};if(!$rejected){throw 'Manual village accepted'}
$farmManualMap.Value=74
if($grid.Columns['up'].DisplayIndex -ne 3 -or $grid.Columns['up'].Width -ne 41 -or $grid.Columns['up'].ReadOnly){throw 'Up column order/width/editability invalid'}
$grid.Rows[0].Cells['pick'].Value=$false;$grid.Rows[1].Cells['pick'].Value=$true
$grid.Rows[0].Cells['up'].Value=$true
if(!$fixture.FarmEnabled -or !$grid.Rows[0].Cells['auto'].Value -or $fixture.FarmArrived){throw 'Up checkbox failed or idled before arrival'}
$routeStamp=$fixture.FarmPositionReadAt
for($upTick=0;$upTick -lt 20;$upTick++){Sync-FarmUpRow $grid.Rows[0]}
if(!$fixture.FarmEnabled -or $second.FarmEnabled){throw 'Up refresh restarted or affected another account'}
$grid.Rows[0].Cells['up'].Value=$false
if($fixture.FarmEnabled -or $fixture.GameAutoWanted -or $grid.Rows[0].Cells['auto'].Value){throw 'Untick Up failed to stop'}
$grid.Rows[0].Cells['up'].Value=$true;Set-AccountAuto $grid.Rows[0] $false
if(!$grid.Rows[0].Cells['up'].Value -or !$fixture.FarmEnabled){throw 'Untick Auto cleared persistent Up'}
$grid.Rows[0].Cells['up'].Value=$false
# Cảnh báo bùa ưu tiên trạng thái khác, chỉ trên acc bị bùa và chỉ khi dữ liệu mới.
$fixture.CurseActive=$true;$fixture.CurseReadAt=[datetime]::UtcNow
$script:uiTicks=9;Invoke-ManagerTick
if($grid.Rows[0].Cells['status'].Value -ne 'Bị bùa — cần nhập mã'){throw 'Curse status not shown with priority'}
Update-UiSummary;if(!$detailsLabel.Text.Contains('Bị bùa')){throw 'Curse detail missing'}
$fixture.CurseActive=$false;$script:uiTicks=9;Invoke-ManagerTick
if($grid.Rows[0].Cells['status'].Value -eq 'Bị bùa — cần nhập mã'){throw 'Curse status did not clear'}
$fixture.CurseActive=$true;$fixture.CurseReadAt=[datetime]::UtcNow.AddSeconds(-10);$script:uiTicks=9;Invoke-ManagerTick
if($grid.Rows[0].Cells['status'].Value -eq 'Bị bùa — cần nhập mã'){throw 'Stale curse status shown'}
$fixture.CurseActive=$false
$script:accountJobs.Remove($fk);$grid.Rows[0].Cells['up'].Value=$true
if(!$grid.Rows[0].Cells['up'].Value -or $fixture.FarmEnabled){throw 'Up selection was not retained for a closed account'}
for($waitTick=0;$waitTick -lt 20;$waitTick++){Sync-FarmUpRow $grid.Rows[0]};if(!$grid.Rows[0].Cells['up'].Value){throw 'Refresh cleared pending Up'}
$grid.Rows[0].Cells['up'].Value=$false
# Up all dùng tất cả acc đã mở, độc lập cột Chọn và không khởi động lại luồng đang chạy.
$script:accountJobs[$fk]=$fixture.Id;$null=$fj.TryAdd($second.Id,$second);$script:accountJobs[$fbk]=$second.Id
$grid.Rows[0].Cells['pick'].Value=$false;$grid.Rows[1].Cells['pick'].Value=$false
$closedRowIndex=$grid.Rows.Add();$grid.Rows[$closedRowIndex].Tag=[pscustomobject]@{id='up-all-closed';username='closed'}
$null=$click.Invoke($upAllButton,[object[]]@([EventArgs]::Empty))
if(!$fixture.FarmEnabled -or !$second.FarmEnabled -or !$grid.Rows[0].Cells['up'].Value -or !$grid.Rows[1].Cells['up'].Value -or !$grid.Rows[$closedRowIndex].Cells['up'].Value){throw 'Up all did not start open accounts or retain selection for closed accounts'}
$farmChoicesField=[TGLL.NativeJob].GetField('FarmChoices',[Reflection.BindingFlags]'Instance,NonPublic');$firstChoices=$farmChoicesField.GetValue($fixture)
$null=$click.Invoke($upAllButton,[object[]]@([EventArgs]::Empty))
if(![object]::ReferenceEquals($firstChoices,$farmChoicesField.GetValue($fixture))){throw 'Up all restarted an active farm'}
$pendingFixture=$fc.Invoke(@('C:\synthetic.exe','farm-later','unused',$null,$true));$pendingFixture.State='opened';$pendingFixture.ReceiveKeyEnabled=$false;$pendingFixture.TrainReady=$true;$pendingFixture.GameSettingsReady=$true
$null=$fj.TryAdd($pendingFixture.Id,$pendingFixture);$script:accountJobs['up-all-closed']=$pendingFixture.Id
Sync-FarmUpRow $grid.Rows[$closedRowIndex]
if(!$pendingFixture.FarmEnabled){throw 'Pending Up did not start on game readiness'}
$pendingChoices=$farmChoicesField.GetValue($pendingFixture);for($pendingTick=0;$pendingTick -lt 20;$pendingTick++){Sync-FarmUpRow $grid.Rows[$closedRowIndex]}
if(![object]::ReferenceEquals($pendingChoices,$farmChoicesField.GetValue($pendingFixture))){throw 'Pending Up restarted each refresh'}
$grid.Rows[$closedRowIndex].Cells['up'].Value=$false;$script:accountJobs.Remove('up-all-closed');$removed=$null;$null=$fj.TryRemove($pendingFixture.Id,[ref]$removed)
$grid.Rows[0].Cells['up'].Value=$false;$grid.Rows[1].Cells['up'].Value=$false
$fixture.FarmObserved=[TGLL.FarmSpot[]]@($spot);$fixture.FarmPositionMap=74;$fixture.FarmPositionX=555;$fixture.FarmPositionY=444;$fixture.FarmPositionReadAt=[datetime]::UtcNow;$farmMode.SelectedIndex=2
$null=$click.Invoke($upAllButton,[object[]]@([EventArgs]::Empty))
if($fixture.FarmEnabled -or !$second.FarmEnabled){throw 'Up all failed to exclude reference account'}
$grid.Rows[1].Cells['up'].Value=$false;$script:accountJobs.Remove($fbk);$removed=$null;$null=$fj.TryRemove($second.Id,[ref]$removed);$grid.Rows.RemoveAt($closedRowIndex)
$script:accountJobs.Remove($fk);$removed=$null;$null=$fj.TryRemove($fixture.Id,[ref]$removed);$script:farmSpots=$oldSpots;Show-FarmSpots
$grid.Rows.RemoveAt($testAddedRow)
Write-Output 'PASS: 107 maps, level/reference/manual modes, exact manual coordinates without source, invalid map rejection, deduplicated saves, per-row Up checkbox, refresh isolation, Up all selections retained while closed, start once on readiness, reference exclusion and stop.'
