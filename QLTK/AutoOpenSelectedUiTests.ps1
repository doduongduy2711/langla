# Exercise the actual checkbox and grid events without starting a game.
& {
    $original=$grid.Rows[0];$oldPick=$original.Cells['pick'].Value
    $blocked=$grid.Rows[$grid.Rows.Add()];$extra=$grid.Rows[$grid.Rows.Add()]
    $blocked.Tag=[pscustomobject]@{id='auto-open-blocked'}
    $extra.Tag=[pscustomobject]@{id='auto-open-extra'}
    $script:autoOpenFixtureBatches=@()
    function Test-AccountOpenBlocked($a){return $a.id -eq 'auto-open-blocked'}
    function Start-SelectedLaunchRows($rows,[bool]$quiet){
        if(!$quiet){throw 'Auto open must not show modal launch errors'}
        $script:autoOpenFixtureBatches+=@(@($rows | ForEach-Object {$_.Tag.id}) -join ',')
    }
    try{
        if($autoOpenSelected.Checked -or $autoOpenSelected.Bounds.Right -gt $options.ClientSize.Width){throw 'Auto open checkbox default/layout invalid'}
        $original.Cells['pick'].Value=$false;$blocked.Cells['pick'].Value=$true;$extra.Cells['pick'].Value=$false
        $autoOpenSelected.Checked=$true;Invoke-AutoOpenSelected
        if($script:autoOpenFixtureBatches.Count){throw 'Auto open submitted a blocked account or unchecked row'}
        $original.Cells['pick'].Value=$true;Invoke-AutoOpenSelected;Invoke-AutoOpenSelected
        $extra.Cells['pick'].Value=$true;Invoke-AutoOpenSelected
        if(($script:autoOpenFixtureBatches -join '|') -ne "$($original.Tag.id)|auto-open-extra"){throw 'Auto open did not submit only newly checked accounts once'}
        $autoOpenSelected.Checked=$false;$original.Cells['pick'].Value=$false;$original.Cells['pick'].Value=$true;Invoke-AutoOpenSelected
        if($script:autoOpenFixtureBatches.Count -ne 2){throw 'Auto open submitted after switching off'}
    }finally{
        $autoOpenSelected.Checked=$false
        $grid.Rows.Remove($extra);$grid.Rows.Remove($blocked);$original.Cells['pick'].Value=$oldPick
    }
}
Write-Output 'PASS: Auto open checkbox, explicit selection only, duplicate guard, new selections and off state.'
