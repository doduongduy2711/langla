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
 function Start-CuongThiJob($j){$script:cuongStarts++;$j.CuongThiEnabled=$true}
 function Stop-CuongThiJob($j){$script:cuongStops++;$j.CuongThiEnabled=$false}
 $script:accountJobs['cuong-one']=$script:cuongTestJob.Id
 Sync-CuongThiAccount $one.Tag $script:cuongTestJob;if($script:cuongStarts){throw 'Cuong Thi started before login readiness'}
 $script:cuongTestJob.TrainReady=$true;$script:cuongTestJob.GameSettingsReady=$true
 Sync-CuongThiAccount $one.Tag $script:cuongTestJob;Sync-CuongThiAccount $one.Tag $script:cuongTestJob;if($script:cuongStarts -ne 1){throw 'Cuong Thi start repeated'}
 $script:cuongTestJob.CuongThiEnabled=$false;Sync-CuongThiAccount $one.Tag $script:cuongTestJob;if($script:cuongStarts -ne 2){throw 'Cuong Thi did not resume on a ready reconnected session'}
 Set-CuongThiPreference $one $false;if($script:cuongStops -ne 1 -or $one.Tag.cuongThiEnabled -or $two.Tag.cuongThiEnabled){throw 'Cuong Thi stop/selection isolation failed'}
}finally{$script:accountJobs=$oldJobs;foreach($n in $oldFns.Keys){Set-Item ('Function:\'+$n) $oldFns[$n]};Update-CuongThiUi}
Write-Output 'PASS: Cuong Thi tab, per-account persistence, readiness gate, single start, reconnect resume and selected stop isolation.'
