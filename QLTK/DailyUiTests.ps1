$dailyTestIndex=$grid.Rows.Add();$dailyAccount=[pscustomobject]@{id='daily-ui-account';username='daily-ui';receiveKey=$true;dungeonHigh=$true;izanami=$true;autoQuest=$true;closeAfterKey=$true};$grid.Rows[$dailyTestIndex].Tag=$dailyAccount
$dailyJob=$fc.Invoke(@('C:\synthetic.exe','daily-ui','unused',$null,$true));$dailyJob.State='opened';$dailyJob.ReceiveKeyEnabled=$false;$dailyJob.TrainReady=$true;$dailyJob.GameSettingsReady=$true;$dailyJob.CharacterLevel=54
$null=$fj.TryAdd($dailyJob.Id,$dailyJob);$script:accountJobs[$dailyAccount.id]=$dailyJob.Id
$farmMode.SelectedIndex=3;$farmManualMap.Value=74;$farmManualX.Value=1490;$farmManualY.Value=565;$farmLevel.Value=54;$dailyPriority.Checked=$true
$grid.Rows[$dailyTestIndex].Cells['up'].Value=$true
if(!$dailyJob.DailyManaged -or !$dailyJob.FarmEnabled -or $dailyJob.ReceiveKeyEnabled -or $dailyJob.DungeonHighEnabled -or $dailyJob.IzanamiEnabled -or $dailyJob.QuestEnabled){throw 'All activity selections conflict with farming before midnight'}
if((Get-AccountLaunchMode $dailyAccount) -ne 'farm'){throw 'Bulk open would incorrectly launch an activity queue for Up'}
$dailyJob.FarmEnabled=$false;$dailyJob.DailyActive=$true;$dailyJob.DailyStage=2;$dailyJob.DailyStatus='Đang chạy: Địa cung cao cấp';$dailyJob.DungeonHighEnabled=$true;$dailyJob.GameAutoWanted=$false
Sync-FarmUpRow $grid.Rows[$dailyTestIndex];Sync-AutoRows
if(!$grid.Rows[$dailyTestIndex].Cells['up'].Value -or $dailyJob.GameAutoWanted){throw 'UI refresh cleared Up or restarted combat during daily stage'}
$script:uiTicks=9;Invoke-ManagerTick
if($grid.Rows[$dailyTestIndex].Cells['status'].Value -ne 'Đang chạy: Địa cung cao cấp'){throw 'Daily activity status not shown'}
Set-AccountAuto $grid.Rows[$dailyTestIndex] $true
if($dailyJob.GameAutoWanted){throw 'All Auto overrides the active daily stage'}
$grid.Rows[$dailyTestIndex].Cells['up'].Value=$false
if($dailyJob.DailyManaged -or $dailyJob.DailyActive -or $dailyJob.DungeonHighEnabled){throw 'Untick Up did not cancel daily stage'}
$script:accountJobs.Remove($dailyAccount.id);$removed=$null;$null=$fj.TryRemove($dailyJob.Id,[ref]$removed);$grid.Rows.RemoveAt($dailyTestIndex)
Write-Output 'PASS: all selected activities coexist with Up, daily status/Up retained on refresh, Auto cannot override the active stage, manual cancellation.'
