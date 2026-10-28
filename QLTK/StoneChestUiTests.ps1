$scA=$script:accounts[0];$scKey=[string]$scA.id;$scHad=$script:accountJobs.ContainsKey($scKey);$scOld=$script:accountJobs[$scKey]
$scJobs=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null);$scCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0]
$scJ=$scCtor.Invoke(@('C:\synthetic.exe','chest-test','unused',$null,$true));$scJ.State='opened';$scJ.GameSettingsReady=$true;$scJ.TrainReady=$true;$null=$scJobs.TryAdd($scJ.Id,$scJ);$script:accountJobs[$scKey]=$scJ.Id
try{
 $grid.ClearSelection();$grid.Rows[0].Selected=$true;$grid.Rows[0].Cells['pick'].Value=$true
 $scP=Get-StoneChestPreference $null;if($scP.quantity -ne 1 -or !$scP.openAfter){throw 'Chest defaults invalid'}
 $chestQuantity.Value=7;$chestOpen.Checked=$false;Save-StoneChestUi;Update-StoneChestUi
 if($scA.stoneChestQuantity -ne 7 -or $scA.stoneChestOpenAfter -or $scJ.ChestEnabled){throw 'Preferences must not start a purchase'}
 Start-StoneChestSelected $false
 if(!$scJ.ChestEnabled -or $scJ.ChestQuantity -ne 7 -or $scJ.ChestOpenAfter -or $scJ.ChestUseOnly){throw 'Selected purchase configuration invalid'}
 Stop-StoneChestSelected;if($scJ.ChestEnabled){throw 'Stop failed'}
 Start-StoneChestSelected $true;if(!$scJ.ChestUseOnly){throw 'Use existing chests failed'}
 Stop-StoneChestSelected;$scJ.TrainReady=$false;Start-StoneChestSelected $false
 if($scJ.ChestEnabled){throw 'Not-ready account must not start'}
 if(!$chestPage.AutoScroll -or $tabs.TabPages.Count -ne 26){throw 'Chest page layout invalid'}
 Write-Output 'PASS: chest UI quantity, saved preferences without auto-start, selected purchase/open/stop and readiness gate.'
}finally{$scRemoved=$null;$null=$scJobs.TryRemove($scJ.Id,[ref]$scRemoved);if($scHad){$script:accountJobs[$scKey]=$scOld}else{$script:accountJobs.Remove($scKey)}}
