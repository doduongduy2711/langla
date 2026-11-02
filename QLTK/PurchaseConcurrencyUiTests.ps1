$pcOldMail=$script:mailRun;$pcOldChest=$script:chestRun;$pcMailMax=$mailMax.Value;$pcChestMax=$chestMax.Value;$pcBudget=$launchConcurrency.Value
$pcNames=@('Sync-MailReceiver','Test-MailReceiverReady','Sync-MailCollectionMember','Start-ChestMember','Sync-ChestMember','Get-ChestMemberJob','Test-ChestMemberRunning','Begin-ChestMemberClose','Get-MailMemberJob','Test-MailMemberRunning','Begin-MailMemberClose')
$pcFunctions=@{};foreach($n in $pcNames){$pcFunctions[$n]=(Get-Item ('Function:\'+$n)).ScriptBlock}
function Sync-MailReceiver {}
function Test-MailReceiverReady {return $true}
function Sync-MailCollectionMember($m){if($m.state -eq 'waiting'){$m.state='launching';$script:pcOpened++}}
function Start-ChestMember($m){$script:pcOpened++}
function Sync-ChestMember($run,$m){}
try{
 $launchConcurrency.Value=2;$mailMax.Value=5;$chestMax.Value=5
 foreach($kind in @('mail','chest')){
  $script:pcOpened=0
  $members=@(foreach($n in 1..137){[pscustomobject]@{id=('fixture-'+$n);state='waiting';job='';status='';started=$false;closeOperation=$null}})
  if($kind -eq 'mail'){$script:mailRun=[pscustomobject]@{active=$true;cursor=0;senders=$members;namedRecipient=$true};Sync-MailCollection}
  else{$script:chestRun=[pscustomobject]@{active=$true;members=$members};Sync-StoneChestQueue}
  if($script:pcOpened -ne 2){throw ($kind+': startup concurrency ignored')}
  foreach($tick in 1..5){if($kind -eq 'mail'){Sync-MailCollection}else{Sync-StoneChestQueue}}
  if($script:pcOpened -ne 5){throw ($kind+': maximum not filled or exceeded')}
  $members[0].state='closing'
  if($kind -eq 'mail'){Sync-MailCollection}else{Sync-StoneChestQueue}
  if($script:pcOpened -ne 5){throw ($kind+': closing client released before actual exit')}
  $members[0].state='done'
  if($kind -eq 'mail'){Sync-MailCollection}else{Sync-StoneChestQueue}
  if($script:pcOpened -ne 6){throw ($kind+': verified exit did not refill exactly one slot')}
  if($kind -eq 'mail'){$mailMax.Value=3}else{$chestMax.Value=3}
  if($kind -eq 'mail'){Sync-MailCollection}else{Sync-StoneChestQueue}
  if($script:pcOpened -ne 6){throw ($kind+': lowering maximum opened more accounts')}
 }
 $script:pcAlive=$true;$script:pcClose=[pscustomobject]@{Completed=$false;Failed=0;Error=''};$script:pcCloses=0
 $script:pcJob=[pscustomobject]@{CloseRequested=$false;CancelRequested=$false;State='opened';ChestEnabled=$false;ChestUseOwned=$false;ChestStatus='Hoàn tất';ChestBought=7;MailEnabled=$false;MailBusy=$false}
 function Get-ChestMemberJob($m){return $script:pcJob}
 function Test-ChestMemberRunning($m){return $script:pcAlive}
 function Begin-ChestMemberClose($m){$script:pcCloses++;return $script:pcClose}
 function Get-MailMemberJob($m){return $script:pcJob}
 function Test-MailMemberRunning($m){return $script:pcAlive}
 function Begin-MailMemberClose($m){$script:pcCloses++;return $script:pcClose}
 $member=[pscustomobject]@{id='close-fixture';job='fake-job';state='running';status='';closeOperation=$null;started=$true}
 $run=[pscustomobject]@{closeAfter=$true;openAfter=$true;useOnly=$false;quantity=7}
 & $pcFunctions['Sync-ChestMember'] $run $member
 if($member.state -ne 'closing' -or $script:pcCloses -ne 1){throw 'Completed chest did not close exactly once'}
 $script:pcClose.Completed=$true;& $pcFunctions['Sync-ChestMember'] $run $member
 if($member.state -ne 'closing'){throw 'Chest slot released while process still exists'}
 $script:pcAlive=$false;& $pcFunctions['Sync-ChestMember'] $run $member
 if($member.state -ne 'done'){throw 'Chest exit not completed'}
 $script:pcAlive=$true;$script:pcClose.Completed=$false;$member.state='blocked';$member.closeOperation=$null
 & $pcFunctions['Sync-MailCollectionMember'] $member
 if($member.state -ne 'closing' -or !$member.stoppedWithoutCompletion){throw 'Stopped sender did not close without claiming successful delivery'}
 $script:pcClose.Completed=$true;$script:pcAlive=$false;& $pcFunctions['Sync-MailCollectionMember'] $member
 if($member.state -ne 'done' -or $member.status -notlike '*dừng*'){throw 'Stopped sender not distinguished from successful send'}
 Write-Output 'PASS: gom do and chest 137-account queues fill max5, obey startup budget2, retain closing slots, refill one actual exit and drain when limit decreases. Chest completes/closes once; stopped senders exit without claiming successful delivery. No game launched.'
}finally{
 foreach($n in $pcNames){Set-Item ('Function:\'+$n) $pcFunctions[$n]}
 $script:mailRun=$pcOldMail;$script:chestRun=$pcOldChest;$mailMax.Value=$pcMailMax;$chestMax.Value=$pcChestMax;$launchConcurrency.Value=$pcBudget
}
