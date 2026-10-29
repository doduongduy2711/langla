$ErrorActionPreference='Stop'
# Function definitions only; never initialize the manager, load game code or start auto.
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'SocketApi.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Socket source did not parse'}
foreach($fn in $ast.EndBlock.Statements | Where-Object {$_ -is [Management.Automation.Language.FunctionDefinitionAst] -and $_.Name -in @('Get-ManagerPreviewSnapshot','Invoke-ManagerPreviewAction','Handle-SocketJson')}){. ([scriptblock]::Create($fn.Extent.Text))}
$script:socketToken='synthetic-token';$script:accounts=@([pscustomobject]@{id='first';username='test1';secret='MUST-NOT-LEAK'},[pscustomobject]@{id='second';username='test2';secret='MUST-NOT-LEAK'})
$script:accountJobs=@{};$script:autoAllRun=$null;$script:calls=@()
$cells=@{};foreach($key in @('character','zone','bima','proxy','status','auto','up','visibility')){$cells[$key]=[pscustomobject]@{Value=$(if($key -in @('auto','up')){$false}else{'synthetic'})}}
$grid=[pscustomobject]@{Rows=@([pscustomobject]@{Tag=$script:accounts[0];Cells=$cells})}
function Open-Account($account){$script:calls+=,$account.id}
function Close-AccountGame($row){$script:calls+=,('close-'+$row.Tag.id)}
function Start-AutoAll {$script:calls+=,'start'}
function Stop-AutoAll {$script:calls+=,'stop'}
function Request($command,$extra=@{}){$request=@{token=$script:socketToken;command=$command};foreach($k in $extra.Keys){$request[$k]=$extra[$k]};return (Handle-SocketJson (ConvertTo-Json $request -Depth 5 -Compress))}
$raw=Request 'manager_snapshot';$snapshot=ConvertFrom-Json $raw
if(!$snapshot.ok -or $snapshot.preview_version -ne 1 -or @($snapshot.accounts).Count -ne 1 -or $raw -like '*MUST-NOT-LEAK*' -or $raw -like '*secret*'){throw 'Snapshot missing or leaked credentials'}
$denied=Handle-SocketJson '{"token":"wrong","command":"manager_snapshot"}' | ConvertFrom-Json
if($denied.error -ne 'unauthorized'){throw 'Preview bypassed existing socket authentication'}
$bad=Request 'manager_action' @{action='open_selected';account_ids=@('first','unknown')} | ConvertFrom-Json
if($bad.ok -or $script:calls.Count){throw 'Partial open happened before validating all IDs'}
$result=Request 'manager_action' @{action='open_selected';account_ids=@('second','first','first')} | ConvertFrom-Json
if(!$result.ok -or $result.count -ne 2 -or ($script:calls -join ',') -ne 'first,second'){throw 'Selected open changed account order, lost or duplicated an account'}
$null=Request 'manager_action' @{action='auto_all_start'};$null=Request 'manager_action' @{action='auto_all_stop'}
if($script:calls[-2] -ne 'start' -or $script:calls[-1] -ne 'stop'){throw 'Preview did not use existing master-auto commands'}
$result=Request 'manager_action' @{action='close_selected';account_ids=@('first')} | ConvertFrom-Json
if(!$result.ok -or $script:calls[-1] -ne 'close-first'){throw 'Preview bypassed the existing close-account flow'}
$bad=Request 'manager_action' @{action='arbitrary-command'} | ConvertFrom-Json
if($bad.ok){throw 'Unknown command accepted'}
Write-Output 'PASS: authenticated cached preview snapshot without secrets/game reads; all-ID validation before mutation, deduplicated selected open and existing master-auto start/stop. No real game started.'
