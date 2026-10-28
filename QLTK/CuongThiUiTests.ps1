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
