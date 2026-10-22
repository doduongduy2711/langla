$clA=$script:accounts[0];$clKey=[string]$clA.id;$clHad=$script:accountJobs.ContainsKey($clKey);$clOld=$script:accountJobs[$clKey]
$clJobs=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null);$clCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0]
$clJ=$clCtor.Invoke(@('C:\synthetic.exe','cloak-test','unused',$null,$true));$clJ.State='opened';$clJ.TrainReady=$true;$clJ.GameSettingsReady=$true;$clJ.ReceiveKeyEnabled=$false;$null=$clJobs.TryAdd($clJ.Id,$clJ);$script:accountJobs[$clKey]=$clJ.Id
try{
 $grid.ClearSelection();$grid.Rows[0].Selected=$true;$grid.Rows[0].Cells['pick'].Value=$true
 $clP=Get-CloakPreference $null;if($clP.id -ne 131 -or $clP.quantity -ne 1){throw 'Cloak defaults invalid'}
 $cloakChoice.SelectedIndex=3;$cloakQuantity.Value=2;Save-CloakUi
 if($clA.cloakId -ne 131 -or $clA.cloakQuantity -ne 2 -or $clJ.CloakEnabled){throw 'Saving must not buy'}
 Start-CloakDirectSelected;if(!$clJ.CloakEnabled -or $clJ.CloakExpectedPrice -ne 900 -or $clJ.CloakQuantity -ne 2){throw 'Selected cloak purchase invalid'}
 Stop-CloakSelected;if($clJ.CloakEnabled){throw 'Cloak stop failed'}
 $clJ.FarmEnabled=$true;Start-CloakDirectSelected;if($clJ.CloakEnabled){throw 'Must block conflicting Up automation'}
 if($cloakChoice.Items.Count -ne 9 -or $tabs.TabPages.Count -ne 22 -or !$cloakPage.AutoScroll){throw 'Cloak page layout invalid'}
 Write-Output 'PASS: cloak page 9 offers, ID131/900 crystals, quantity, no purchase on save, selected start/stop and conflicting Up gate.'
}finally{$clRemoved=$null;$null=$clJobs.TryRemove($clJ.Id,[ref]$clRemoved);if($clHad){$script:accountJobs[$clKey]=$clOld}else{$script:accountJobs.Remove($clKey)}}
