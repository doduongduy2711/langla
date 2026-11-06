if($gemPage.Text -ne 'Auto ghép đá 12' -or !$gemPage.AutoScroll -or !$gemReport.ReadOnly -or $tabs.TabPages.Count -ne 26){throw 'Gem combine tab missing'}
if($gemStop.Text -ne 'Dừng đã chọn' -or @($gemPage.Controls | Where-Object {$_ -is [Windows.Forms.Button] -and $_.Text -eq 'Bật đã chọn'}).Count){throw 'Gem checkboxes must work without a start button'}
Update-GemCombineUi
if(!$gemReport.Text){throw 'Gem combine guidance missing'}
if($gemCheck -isnot [Windows.Forms.CheckBox]){throw 'Per-account gem checkbox missing'}
$gemOldJobs=$script:accountJobs;$gemOldApplied=$script:gemApplied;$gemFns=@{};foreach($n in @('Get-AccountJob','Start-GemCombineJob','Stop-GemCombineJob')){$gemFns[$n]=(Get-Item ('Function:\'+$n)).ScriptBlock}
try{
 $script:accountJobs=@{};$script:gemApplied=@{};$script:gemTestStarts=0;$script:gemTestStops=0
 $gemA=New-Object Windows.Forms.DataGridViewRow;$gemA.Tag=[pscustomobject]@{id='gem-a';username='gem-a';gemCombineEnabled=$false}
 $gemB=New-Object Windows.Forms.DataGridViewRow;$gemB.Tag=[pscustomobject]@{id='gem-b';username='gem-b';gemCombineEnabled=$false}
 Set-GemCombinePreference $gemA $true
 if(!$gemA.Tag.gemCombineEnabled -or $gemB.Tag.gemCombineEnabled -or @($gemA.Tag.gameOverrides | Where-Object {$_.code -eq 307 -and $_.value -eq 1}).Count -ne 1){throw 'Gem offline selection leaked or disabled game mode'}
 Set-GemCombinePreference $gemA $true 8;Set-GemCombinePreference $gemA $true 10
 if((Get-GemCombineTargets $gemA.Tag) -ne 7 -or (Get-GemCombineTargets $gemB.Tag) -ne 0){throw 'Gem 8/10/12 preferences not independent per account'}
 Set-GemCombinePreference $gemA $false 8
 if((Get-GemCombineTargets $gemA.Tag) -ne 6){throw 'Disabling stone8 changed other targets'}
 Set-GemCombinePreference $gemA $false 10
 if((Get-GemCombineTargets $gemA.Tag) -ne 4){throw 'Legacy stone12 preference not preserved'}
 $gemCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'NonPublic,Instance') | Where-Object {$_.GetParameters().Count -eq 5} | Select-Object -First 1
 $script:gemTestJob=$gemCtor.Invoke(@('C:\synthetic.exe','gem-a','unused',$null,$true));$script:gemTestJob.State='opened';$script:gemTestJob.TrainReady=$true;$script:gemTestJob.GameSettingsReady=$true
 function Get-AccountJob($id){return $script:gemTestJob}
 function Start-GemCombineJob($j){$script:gemTestStarts++;$j.GemEnabled=$true;$j.GemSkipped=$false}
 function Stop-GemCombineJob($j){$script:gemTestStops++;$j.GemEnabled=$false}
 $script:accountJobs['gem-a']=$script:gemTestJob.Id
 Sync-GemCombineAccount $gemA.Tag $script:gemTestJob;Sync-GemCombineAccount $gemA.Tag $script:gemTestJob
 if($script:gemTestStarts -ne 1 -or !$script:gemTestJob.GemEnabled){throw 'Saved gem checkbox did not start exactly once after login'}
 $script:gemTestJob.GemEnabled=$false;$script:gemTestJob.GemSkipped=$true;Sync-GemCombineAccount $gemA.Tag $script:gemTestJob
 if($gemA.Tag.gemCombineEnabled -or $script:gemTestStarts -ne 1){throw 'Insufficient-silver skip auto restarted or left checkbox enabled'}
 Set-GemCombinePreference $gemA $true;Set-GemCombinePreference $gemA $false
 if($script:gemTestStarts -ne 2 -or $script:gemTestStops -ne 1 -or $gemA.Tag.gemCombineEnabled -or $gemB.Tag.gemCombineEnabled){throw 'Gem per-account enable/disable isolation failed'}
}finally{
 $script:accountJobs=$gemOldJobs;$script:gemApplied=$gemOldApplied;foreach($n in $gemFns.Keys){Set-Item ('Function:\'+$n) $gemFns[$n]};Update-GemCombineUi
}
Write-Output 'PASS: gem 8/10/12 checkboxes save independently and start directly when ready, untick stops, no start button required, no duplicate start or insufficient-silver restart.'
