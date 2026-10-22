$watch=[Diagnostics.Stopwatch]::StartNew()
$sortButton.GetType().GetMethod('OnClick',[Reflection.BindingFlags]'Instance,NonPublic').Invoke($sortButton,@([EventArgs]::Empty))
if($watch.ElapsedMilliseconds -gt 750){throw 'Arrange handler blocked UI'}
if($null -eq $script:arrangeOperation -or $sortButton.Enabled){throw 'Arrange busy state missing'}
$pending=$script:arrangeOperation
$sortButton.PerformClick()
if($script:arrangeOperation -ne $pending){throw 'Duplicate arrange started'}
$watch.Restart()
while($null -ne $script:arrangeOperation -and $watch.ElapsedMilliseconds -lt 3000){[Windows.Forms.Application]::DoEvents();Start-Sleep -Milliseconds 20}
if($null -ne $script:arrangeOperation -or !$sortButton.Enabled -or $sortButton.Text -ne 'Sắp xếp'){throw 'Arrange completion failed to restore button'}
Write-Output 'PASS: arrange handler returns promptly, disables repeat clicks and restores button after background completion.'
