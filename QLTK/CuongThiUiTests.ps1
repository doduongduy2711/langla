if($tabs.TabPages.Count -ne 26 -or $cuongThiPage.Text -ne 'Auto Cương Thi' -or !$cuongThiPage.AutoScroll -or !$cuongThiReport.ReadOnly){throw 'Cuong Thi tab missing'}
$oldJobs=$script:accountJobs;$oldFns=@{};foreach($n in @('Get-AccountJob','Start-CuongThiJob','Stop-CuongThiJob')){$oldFns[$n]=(Get-Item ('Function:\'+$n)).ScriptBlock}
try{
 $script:accountJobs=@{};$script:cuongStarts=0;$script:cuongStops=0
 $one=New-Object Windows.Forms.DataGridViewRow;$one.Tag=[pscustomobject]@{id='cuong-one';username='cuong-one';cuongThiEnabled=$false}
 $two=New-Object Windows.Forms.DataGridViewRow;$two.Tag=[pscustomobject]@{id='cuong-two';username='cuong-two';cuongThiEnabled=$false}
 Set-CuongThiPreference $one $true;if(!$one.Tag.cuongThiEnabled -or $two.Tag.cuongThiEnabled){throw 'Cuong Thi offline preference leaked'}
 $ctor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'NonPublic,Instance') | Where-Object {$_.GetParameters().Count -eq 5} | Select-Object -First 1
 $script:cuongTestJob=$ctor.Invoke(@('C:\synthetic.exe','cuong-one','unused',$null,$true));$script:cuongTestJob.State='opened'
 function Get-AccountJob($id){return $script:cuongTestJob}
 function Start-CuongThiJob($j,$target=$null){$script:cuongStarts++;$j.CuongThiEnabled=$true;$j.CuongThiDestinationKey=[TGLL.NativeBackend]::CuongThiTargetKey($target)}
 function Stop-CuongThiJob($j){$script:cuongStops++;$j.CuongThiEnabled=$false}
 $script:accountJobs['cuong-one']=$script:cuongTestJob.Id
 Sync-CuongThiAccount $one.Tag $script:cuongTestJob;if($script:cuongStarts){throw 'Cuong Thi started before login readiness'}
 $script:cuongTestJob.TrainReady=$true;$script:cuongTestJob.GameSettingsReady=$true
 Sync-CuongThiAccount $one.Tag $script:cuongTestJob;Sync-CuongThiAccount $one.Tag $script:cuongTestJob;if($script:cuongStarts -ne 1){throw 'Cuong Thi start repeated'}
 $script:cuongTestJob.CuongThiEnabled=$false;Sync-CuongThiAccount $one.Tag $script:cuongTestJob;if($script:cuongStarts -ne 2){throw 'Cuong Thi did not resume on a ready reconnected session'}
 Set-CuongThiPreference $one $false;if($script:cuongStops -ne 1 -or $one.Tag.cuongThiEnabled -or $two.Tag.cuongThiEnabled){throw 'Cuong Thi stop/selection isolation failed'}
 $cuongThiMove.Checked=$true;$cuongThiX.Value=700;$cuongThiY.Value=400;$cuongThiZone.Value=14
 for($n=0;$n -lt $script:cuongThiMapChoices.Count;$n++){if($script:cuongThiMapChoices[$n].Id -eq 69){$cuongThiMap.SelectedIndex=$n;break}}
 $one.Tag | Add-Member NoteProperty upEnabled $true -Force;$script:farmUpRequests['cuong-one']='fixture';$script:farmUpRequests['cuong-two']='other'
 Set-CuongThiPreference $one $true
 if($one.Tag.upEnabled -or $script:farmUpRequests.ContainsKey('cuong-one') -or !$script:farmUpRequests.ContainsKey('cuong-two')){throw 'Boss navigation cleared another account Up request'}
 $saved=$one.Tag.cuongThiConfig | ConvertTo-Json -Compress | ConvertFrom-Json;$target=Get-CuongThiDestination $saved
 if($target.MapId -ne 69 -or $target.X -ne 700 -or $target.Y -ne 400 -or $target.Zone -ne 14 -or $two.Tag.cuongThiConfig){throw 'Independent destination persistence failed'}
 $before=$script:cuongStarts;Sync-CuongThiAccount $one.Tag $script:cuongTestJob;if($script:cuongStarts -ne $before){throw 'Destination restarted without change'}
 Disable-CuongThiForUp $one;if($one.Tag.cuongThiEnabled -or !$one.Tag.cuongThiConfig.Move -or $one.Tag.cuongThiConfig.Zone -ne 14){throw 'Up handoff lost saved boss config'}
 $script:farmUpRequests.Remove('cuong-two');$script:autoTrainWanted.Remove('cuong-one');$script:autoTrainApplied.Remove('cuong-one');$cuongThiMove.Checked=$false
}finally{$script:accountJobs=$oldJobs;foreach($n in $oldFns.Keys){Set-Item ('Function:\'+$n) $oldFns[$n]};Update-CuongThiUi}
Write-Output 'PASS: Cuong Thi tab, per-account persistence, readiness gate, single start, reconnect resume and selected stop isolation.'

$ctSavedAccounts=$script:accounts;$ctSavedJobs=$script:accountJobs;$ctSavedClip=$script:copiedAccountConfig;$ctSaveFn=(Get-Item Function:Save-Accounts).ScriptBlock;$ctUiTest=$UiTest
try{
 $script:accountJobs=@{};$ctSource=[pscustomobject]@{id='ct-copy-1';username='ct-source';cuongThiEnabled=$true;cuongThiConfig=[pscustomobject]@{Move=$true;MapId=69;MapName='Làng Mây';X=700;Y=400;Zone=14}}
 $ctDest=[pscustomobject]@{id='ct-copy-2';username='ct-dest';secret='keep';proxy='keep';cuongThiEnabled=$false;cuongThiConfig=[pscustomobject]@{Move=$false;MapId=59;X=111;Y=222;Zone=-1}}
 $script:accounts=@($ctSource,$ctDest);Refresh-Grid;$grid.ClearSelection();$grid.Rows[0].Selected=$true;Update-CuongThiUi
 # Saved Move is false; user checks Move and edits coordinates without pressing Save.
 $ctSource.cuongThiConfig.Move=$false;$ctSource.cuongThiConfig.X=100;$cuongThiMove.Checked=$true;$cuongThiX.Value=700;Copy-AccountConfig
 if($ctSource.cuongThiConfig.Move -or $ctSource.cuongThiConfig.X -ne 100){throw 'Copy unexpectedly changed running source preference'}
 Set-AccountAuto $grid.Rows[0] $false $true;if(!$ctSource.cuongThiEnabled){throw 'Game close cleared Cuong Thi preference'}
 $ctCopy=$script:copiedAccountConfig | ConvertFrom-Json;if(!$ctCopy.cuongThiEnabled -or $ctCopy.cuongThiConfig.Zone -ne 14 -or !$ctCopy.cuongThiConfig.Move -or $ctCopy.cuongThiConfig.X -ne 700){throw 'Cuong Thi clipboard must capture unsaved visible Move and XY'}
 $ctSource.cuongThiConfig.X=900;Paste-AccountConfigAll
 if(!$ctDest.cuongThiEnabled -or !$ctDest.cuongThiConfig.Move -or $ctDest.cuongThiConfig.MapId -ne 69 -or $ctDest.cuongThiConfig.X -ne 700 -or $ctDest.cuongThiConfig.Y -ne 400 -or $ctDest.cuongThiConfig.Zone -ne 14){throw 'Cuong Thi destination paste failed'}
 $ctSource.cuongThiConfig.Zone=2;if($ctDest.cuongThiConfig.Zone -ne 14){throw 'Pasted boss configs share object'}
 $grid.ClearSelection();$grid.Rows[1].Selected=$true;Update-CuongThiUi
 if(!$cuongThiMove.Checked -or $cuongThiX.Value -ne 700 -or $cuongThiZone.Value -ne 14 -or !$cuongThiCheck.Checked){throw 'Pasted boss config not displayed'}
 $cuongThiMove.Checked=$false;$cuongThiZone.Value=9;Copy-AccountConfig;$ctUnchecked=$script:copiedAccountConfig | ConvertFrom-Json;if($ctUnchecked.cuongThiConfig.Move -or $ctUnchecked.cuongThiConfig.Zone -ne 9){throw 'Unchecked draft Move was replaced by saved config'}
 if($ctDest.secret -ne 'keep' -or $ctDest.proxy -ne 'keep'){throw 'Boss config paste changed credentials'}
 $ctCopy.PSObject.Properties.Remove('cuongThiEnabled');$ctCopy.PSObject.Properties.Remove('cuongThiConfig');$script:copiedAccountConfig=$ctCopy | ConvertTo-Json -Depth 12;Paste-AccountConfigAll
 if(!$ctDest.cuongThiEnabled -or $ctDest.cuongThiConfig.Zone -ne 14){throw 'Legacy clipboard cleared boss preferences'}
 $script:copiedAccountConfig='{"cuongThiEnabled":false,"cuongThiConfig":{"Move":false,"MapId":59,"X":1,"Y":2,"Zone":3}}'
 function Save-Accounts {throw 'synthetic_save_failure'}
 $UiTest=$false;$ctFailed=$false;try{Paste-AccountConfigAll}catch{$ctFailed=$true}finally{$UiTest=$ctUiTest}
 if(!$ctFailed -or !$ctDest.cuongThiEnabled -or $ctDest.cuongThiConfig.Zone -ne 14){throw 'Boss clipboard rollback failed'}
 Write-Output 'PASS: Cuong Thi clipboard includes enable/map/XY/zone/movement, frozen independent snapshots, tab refresh, legacy preservation, private fields and save rollback.'
}finally{
 $UiTest=$ctUiTest;Set-Item Function:Save-Accounts $ctSaveFn;$script:accounts=$ctSavedAccounts;$script:accountJobs=$ctSavedJobs;$script:copiedAccountConfig=$ctSavedClip;$script:cuongThiShownAccount='';Refresh-Grid;Update-CuongThiUi
}
