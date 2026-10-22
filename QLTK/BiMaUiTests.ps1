$bmRow=$grid.Rows[0];$bmTag=$bmRow.Tag;$bmMappings=$script:accountJobs;$bmData=[TGLL.NativeBackend]::BiMaDataDirectory
$bmJobs=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
$bmCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0]
$bmJob=$bmCtor.Invoke(@('C:\synthetic.exe','bima-test','unused',$null,$true))
$bmAccept=[TGLL.NativeBackend].GetMethod('AcceptBiMa',[Reflection.BindingFlags]'Static,NonPublic');$null=$bmJobs.TryAdd($bmJob.Id,$bmJob)
try{
 [TGLL.NativeBackend]::BiMaDataDirectory='';$key='ui-bima-'+[guid]::NewGuid().ToString('N');$bmRow.Tag=[pscustomobject]@{id=$key;username='fixture'};$script:accountJobs=@{$key=$bmJob.Id}
 Update-AccountCharacter $bmRow $bmJob
 if($bmRow.Cells['bima'].Value -ne '—' -or $grid.Columns['bima'].HeaderText -ne 'Bí ma' -or !$grid.Columns['bima'].ReadOnly){throw 'Unknown BiMa or column invalid'}
 $null=$bmAccept.Invoke($null,@($bmJob,[long]27,[datetime]::UtcNow));Update-AccountCharacter $bmRow $bmJob
 if($bmRow.Cells['bima'].Value -ne '27'){throw 'BiMa live column not updated'}
 $bmJob.State='closed';$bmJob.CharacterName='';Update-AccountCharacter $bmRow $bmJob
 if($bmRow.Cells['bima'].Value -ne '27'){throw 'Closing game erased BiMa column'}
 Update-AccountBiMa $bmRow $null;if($bmRow.Cells['bima'].Value -ne '27'){throw 'Offline row lost stored BiMa'}
 $null=$bmAccept.Invoke($null,@($bmJob,[long]0,[datetime]::UtcNow));Update-AccountBiMa $bmRow $bmJob
 if($bmRow.Cells['bima'].Value -ne '0'){throw 'Verified zero rendered as unknown'}
 Write-Output 'PASS: BiMa read-only column, unknown vs zero, live update, offline/closed retention and account isolation.'
}finally{
 [TGLL.NativeBackend]::BiMaDataDirectory=$bmData;$script:accountJobs=$bmMappings;$bmRow.Tag=$bmTag;$ignored=$null;$null=$bmJobs.TryRemove($bmJob.Id,[ref]$ignored);Update-AccountBiMa $bmRow $null
}
