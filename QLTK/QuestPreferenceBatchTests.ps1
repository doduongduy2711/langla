$ErrorActionPreference='Stop'
if(!('TGLL.NativeBackend' -as [type])){Add-Type -Path (Join-Path $PSScriptRoot 'NativeBackend.cs') -ReferencedAssemblies System.dll,System.Core.dll}
$tokens=$null;$parseErrors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'QuestUi.ps1'),[ref]$tokens,[ref]$parseErrors)
if($parseErrors.Count){throw 'Quest source syntax invalid'}
$definition=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Sync-QuestPreferences'},$true)
. ([scriptblock]::Create($definition.Extent.Text))
function Get-AccountJob($id){return [TGLL.NativeBackend]::Get($id)}
function Get-AutoAllStage($id){return ''}
function Get-AccountLaunchMode($account){return 'quest'}
$script:accounts=@();$script:accountJobs=@{};$script:questApplied=@{};$script:questPreferenceCursor=0
$jobs=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
$ctor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0];$created=@()
try{
 for($n=0;$n -lt 239;$n++){
  $account=[pscustomobject]@{id=('batch-'+$n);autoQuest=$false}
  $job=$ctor.Invoke(@('synthetic.exe','fixture','unused',$null,$true));$job.State='opened';$job.ReceiveKeyEnabled=$false;$jobs[$job.Id]=$job;$created+=$job.Id
  $script:accounts+=,$account;$script:accountJobs[$account.id]=$job.Id
 }
 Sync-QuestPreferences 8
 if($script:questApplied.Count -ne 8){throw 'Scheduled refresh scanned all accounts instead of bounded batch'}
 Sync-QuestPreferences 8
 if($script:questApplied.Count -ne 16){throw 'Next batch skipped or repeated accounts'}
 for($n=0;$n -lt 28;$n++){Sync-QuestPreferences 8}
 if($script:questApplied.Count -ne 239){throw 'Batch rotation did not cover every account'}
 $last=$script:accounts[-1];$last.autoQuest=$true;Sync-QuestPreferences
 if(!(Get-AccountJob $script:accountJobs[$last.id]).QuestEnabled){throw 'Manual change waited for batch cursor'}
 Write-Output 'PASS: 239-account preference refresh checks eight at a time, rotates without skipping, and applies manual changes immediately. No real game or UI action.'
}finally{foreach($id in $created){$removed=$null;$null=$jobs.TryRemove($id,[ref]$removed)}}
