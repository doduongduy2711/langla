# Exercise the actual checkbox and grid events without starting a game.
& {
    $original=$grid.Rows[0];$oldPick=$original.Cells['pick'].Value
    $blocked=$grid.Rows[$grid.Rows.Add()];$extra=$grid.Rows[$grid.Rows.Add()]
    $blocked.Tag=[pscustomobject]@{id='auto-open-blocked'}
    $extra.Tag=[pscustomobject]@{id='auto-open-extra'}
    $added=@();$oldLimit=$autoOpenSelectedLimit.Value
    $oldJobs=$script:accountJobs;$oldWindows=$script:accountWindowPids;$oldBeast=$script:beastBatches
    $oldSon=$script:soncapBatches;$oldIzanami=$script:izanamiBatches;$oldQuest=$script:questBatches;$oldDungeon=$script:dungeonBatches
    $oldRuns=@($script:mailRun,$script:distributionRun,$script:cloakRun,$script:chestRun)
    $fixturePids=@();$fixtureJobs=@{};$promoteDuringSnapshot=$false
    $script:autoOpenFixtureBatches=@()
    function Test-AccountOpenBlocked($a){return $a.id -eq 'auto-open-blocked'}
    function Get-OpenGamePids {
        if($promoteDuringSnapshot){$fixtureJobs.race.State='opened';$fixtureJobs.race.ProcessId=13}
        return $fixturePids
    }
    function Get-AccountJob([string]$id){return $fixtureJobs[$id]}
    function Start-SelectedLaunchRows($rows,[bool]$quiet){
        if(!$quiet){throw 'Auto open must not show modal launch errors'}
        $script:autoOpenFixtureBatches+=@(@($rows | ForEach-Object {$_.Tag.id}) -join ',')
    }
    try{
        if($autoOpenSelected.Checked -or $autoOpenSelected.Bounds.Right -gt $options.ClientSize.Width -or $autoOpenSelectedLimit.Bounds.Right -gt $options.ClientSize.Width -or $autoOpenSelectedLimit.ReadOnly){throw 'Auto open checkbox/limit layout invalid'}
        $autoOpenSelectedLimit.Value=0
        $original.Cells['pick'].Value=$false;$blocked.Cells['pick'].Value=$true;$extra.Cells['pick'].Value=$false
        $autoOpenSelected.Checked=$true;Invoke-AutoOpenSelected
        if($script:autoOpenFixtureBatches.Count){throw 'Auto open submitted a blocked account or unchecked row'}
        $original.Cells['pick'].Value=$true;Invoke-AutoOpenSelected;Invoke-AutoOpenSelected
        $extra.Cells['pick'].Value=$true;Invoke-AutoOpenSelected
        if(($script:autoOpenFixtureBatches -join '|') -ne "$($original.Tag.id)|auto-open-extra"){throw 'Auto open did not submit only newly checked accounts once'}
        $autoOpenSelected.Checked=$false;$original.Cells['pick'].Value=$false;$original.Cells['pick'].Value=$true;Invoke-AutoOpenSelected
        if($script:autoOpenFixtureBatches.Count -ne 2){throw 'Auto open submitted after switching off'}
        $fixturePids=@(1..12)
        $fixtureJobs=@{live=[pscustomobject]@{ProcessId=1;State='opened'};pending=[pscustomobject]@{ProcessId=0;State='queued'}}
        $script:accountJobs=@{([string]$original.Tag.id)='live';'auto-open-blocked'='pending'}
        $script:accountWindowPids=@{([string]$original.Tag.id)=1}
        $script:beastBatches=@([pscustomobject]@{state='waiting';members=@([pscustomobject]@{id='auto-open-blocked'},[pscustomobject]@{id='auto-open-extra'})})
        $script:soncapBatches=@();$script:izanamiBatches=@();$script:questBatches=@();$script:dungeonBatches=@()
        $script:mailRun=$null;$script:distributionRun=$null;$script:cloakRun=$null;$script:chestRun=$null
        for($i=1;$i -le 10;$i++){
            $row=$grid.Rows[$grid.Rows.Add()];$row.Tag=[pscustomobject]@{id="cap-$i"};$row.Cells['pick'].Value=$true;$added+=,$row
        }
        $autoOpenSelectedLimit.Value=20
        if($script:settings.autoOpenSelectedLimit -ne 20 -or (Get-AutoOpenOccupancy).Count -ne 14){throw 'Limit not stored or live/queued accounts counted twice'}
        $autoOpenSelected.Checked=$true;Invoke-AutoOpenSelected;Invoke-AutoOpenSelected
        if($autoOpenSelected.Checked -or $script:autoOpenFixtureBatches.Count -ne 3 -or $script:autoOpenFixtureBatches[-1] -ne 'cap-1,cap-2,cap-3,cap-4,cap-5,cap-6'){throw 'Auto open did not stop at 20 including live and queued clients'}
        $autoOpenSelectedLimit.Value=12;$autoOpenSelected.Checked=$true;Invoke-AutoOpenSelected
        if($autoOpenSelected.Checked -or $script:autoOpenFixtureBatches.Count -ne 3){throw 'Auto open submitted at an already full limit'}
        $fixtureJobs.race=[pscustomobject]@{ProcessId=0;State='starting'};$script:accountJobs['race']='race'
        $promoteDuringSnapshot=$true
        if((Get-AutoOpenOccupancy).Count -ne 15){throw 'A job registering during the process snapshot lost its reserved slot'}
    }finally{
        $autoOpenSelected.Checked=$false
        $autoOpenSelectedLimit.Value=$oldLimit
        $script:accountJobs=$oldJobs;$script:accountWindowPids=$oldWindows;$script:beastBatches=$oldBeast
        $script:soncapBatches=$oldSon;$script:izanamiBatches=$oldIzanami;$script:questBatches=$oldQuest;$script:dungeonBatches=$oldDungeon
        $script:mailRun,$script:distributionRun,$script:cloakRun,$script:chestRun=$oldRuns
        foreach($row in $added){$grid.Rows.Remove($row)}
        $grid.Rows.Remove($extra);$grid.Rows.Remove($blocked);$original.Cells['pick'].Value=$oldPick
    }
}
Write-Output 'PASS: Auto open checkbox, explicit selection only, duplicate guard, editable limit and auto stop at cap including live and queued clients.'
