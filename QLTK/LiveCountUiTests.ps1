$countSavedPids=(Get-Item Function:\Get-OpenGamePids).ScriptBlock
$countSavedJobs=(Get-Item Function:\Get-AccountJob).ScriptBlock
$countSavedMappings=$script:accountJobs.Clone();$countSavedText=$summaryLabel.Text
try {
    function Get-OpenGamePids {return [int[]]@(1,2,3,4,5,6,7,8,9,10)}
    $script:accountJobs[[string]$grid.Rows[0].Tag.id]='stale-job'
    $script:countFixture=[pscustomobject]@{State='opened';ProcessId=999;CharacterName='';Stage='';NpcId=-1;Error=''}
    function Get-AccountJob($id){return $script:countFixture}
    Update-UiSummary
    if($summaryLabel.Text -notmatch 'Đang mở: 10'){throw 'Summary counted stale job instead of live processes'}
    $script:countFixture.State='queued';$script:countFixture.ProcessId=0;Update-UiSummary
    if($summaryLabel.Text -notmatch 'Đang mở: 10' -or $summaryLabel.Text -notmatch 'Đang chờ: 1'){throw 'Queued job counted as live'}
    function Get-OpenGamePids {return [int[]]@()}
    $script:countFixture.State='opened';Update-UiSummary
    if($summaryLabel.Text -notmatch 'Đang mở: 0'){throw 'Exited game remained counted'}
    Write-Output 'PASS: live process count, stale opened job excluded, queued jobs separate, zero running processes.'
}finally{
    Set-Item Function:\Get-OpenGamePids $countSavedPids;Set-Item Function:\Get-AccountJob $countSavedJobs
    $script:accountJobs=$countSavedMappings;$summaryLabel.Text=$countSavedText
}
