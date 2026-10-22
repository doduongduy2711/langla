$savedReopenBatches=$script:soncapBatches;$savedReopenJobs=$script:accountJobs
$teamField=[TGLL.NativeJob].GetField('TeamJobIds',[Reflection.BindingFlags]'NonPublic,Instance')
$nativeJobs=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'NonPublic,Static').GetValue($null)
$ctor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'NonPublic,Instance')[0]
$old=$ctor.Invoke(@('synthetic.exe','old','unused',$null,$true));$next=$ctor.Invoke(@('synthetic.exe','next','unused',$null,$true));$peer=$ctor.Invoke(@('synthetic.exe','peer','unused',$null,$true))
try {
    foreach($j in @($old,$next,$peer)){$j.State='opened';$j.SonCapEnabled=$true;$null=$nativeJobs.TryAdd($j.Id,$j)}
    [TGLL.NativeBackend]::ConfigureSonCapTeams([string[]]@($old.Id,$peer.Id));$old.CloseRequested=$true;$old.State='closed'
    $member=[pscustomobject]@{id='reopened-soncap';job=$old.Id};$partner=[pscustomobject]@{id='partner-soncap';job=$peer.Id}
    $script:accountJobs=@{'reopened-soncap'=$next.Id;'partner-soncap'=$peer.Id}
    $script:soncapBatches=@([pscustomobject]@{state='running';members=@($member,$partner)})
    $current=Get-SonCapMemberJob $member
    if($current.Id -ne $next.Id -or $member.job -ne $next.Id -or ($teamField.GetValue($peer)) -contains $old.Id){throw 'Reopened account remains bound to dead SonCap job'}
    Sync-SonCapTeams;Sync-SonCapTeams
    if($member.job -ne $next.Id -or ($teamField.GetValue($peer)) -contains $old.Id -or ($teamField.GetValue($next)) -notcontains $peer.Id){throw ('Team sync restored stale game after rebind; size='+[TGLL.NativeBackend]::SonCapTeamSize+' member='+$member.job+' next='+$next.Id+' team='+(($teamField.GetValue($next)) -join ',')+' peer='+(($teamField.GetValue($peer)) -join ','))}
    Write-Output 'PASS: reopened SonCap member updates queue and all peers; repeated team sync preserves new job. No game launched.'
}finally {
    foreach($j in @($old,$next,$peer)){$removed=$null;$null=$nativeJobs.TryRemove($j.Id,[ref]$removed)}
    $script:soncapBatches=$savedReopenBatches;$script:accountJobs=$savedReopenJobs
}
