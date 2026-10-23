# Synthetic job: defaults must reach combat even if Train tab was never opened.
$sdRow=$grid.Rows[0];$sdTag=$sdRow.Tag;$sdView=$script:skillViewKey
$sdCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0]
$sdJob=$sdCtor.Invoke(@('C:\synthetic.exe','default-skills-test','unused',$null,$true))
$sdJobs=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
$sdKey='default-skills-test';$null=$sdJobs.TryAdd($sdJob.Id,$sdJob)
try {
 $sdRow.Tag=[pscustomobject]@{id=$sdKey;username='synthetic';trainSkills=@();soncap=$false}
 $script:accountJobs[$sdKey]=$sdJob.Id
 $sdJob.GameSettingsReady=$true;$sdJob.TrainReady=$true;$sdJob.State='dungeon_running';$sdJob.ReceiveKeyEnabled=$false
 $sdSkills=@();foreach($spec in @(@(243,26,13,0),@(283,28,8,1),@(219,24,20,0),@(1,30,1,0))){$sk=New-Object TGLL.SkillInfo;$sk.Id=$spec[0];$sk.TemplateId=$spec[1];$sk.Level=$spec[2];$sk.Type=$spec[3];$sdSkills+=$sk}
 $sdJob.Skills=[TGLL.SkillInfo[]]$sdSkills
 Sync-AutoRows
 if(($sdJob.HangingSkillIds -join ',') -ne '243,283,1'){throw 'Empty saved config did not initialize combat skills before visiting Train tab'}
 $sdRow.Tag.PSObject.Properties.Remove('trainSkills');$sdJob.HangingSkillIds=[int[]]@()
 Sync-AutoRows
 if(($sdJob.HangingSkillIds -join ',') -ne '243,283,1'){throw 'New account without saved config did not initialize combat skills'}
 $script:skillViewKey='';Update-TrainUi
 if(((Get-CheckedSkillIds) -join ',') -ne '243,283,1'){throw 'Default three skills not checked in Train tab'}
 $script:trainSelections[$sdKey]=@(219);Sync-AutoRows
 if(($sdJob.HangingSkillIds -join ',') -ne '219'){throw 'Explicit saved skill selection overwritten'}
 $sdHanging=[TGLL.NativeBackend].GetMethod('HangingLevelOne',[Reflection.BindingFlags]'Static,NonPublic')
 $sdJob.GameAutoEnabled=$true;$sdJob.GameAutoWanted=$true;$sdJob.HangingSkillIds=[int[]]@(219,1)
 for($cursor=0;$cursor -lt 4;$cursor++){$sdJob.HangingSkillCursor=$cursor;$chosen=$sdHanging.Invoke($null,@($sdJob));if(!$chosen -or $chosen.Id -ne 1){throw 'Hanging supplement selected ID 219 instead of level one'}}
 $sdJob.HangingSkillIds=[int[]]@(219);if($null -ne $sdHanging.Invoke($null,@($sdJob))){throw 'Water-only saved selection still activates the hanging supplement'}
 Write-Output 'PASS: automatic learned skill selection before Train tab opens, empty/missing profile, three checked UI items; ID 219 excluded from automatic defaults, explicit choice retained. No game action.'
}finally {
 $sdRow.Tag=$sdTag;$script:skillViewKey=$sdView;$script:accountJobs.Remove($sdKey);$script:trainSelections.Remove($sdKey);$script:autoTrainWanted.Remove($sdKey);$script:autoTrainApplied.Remove($sdKey)
 $sdRemoved=$null;$null=$sdJobs.TryRemove($sdJob.Id,[ref]$sdRemoved)
}
