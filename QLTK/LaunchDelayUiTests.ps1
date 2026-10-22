$savedLaunchDelay=$launchDelay.Value
try {
    $launchDelay.Value=0.5
    if([double]$script:settings.launchDelaySeconds -ne 0.5){throw 'Delay input did not update saved setting'}
    $launchPacerField=[TGLL.NativeBackend].GetField('launchPacer',[Reflection.BindingFlags]'NonPublic,Static');$pacer=$launchPacerField.GetValue($null)
    $delayField=$pacer.GetType().GetField('delay',[Reflection.BindingFlags]'NonPublic,Instance')
    if($delayField.GetValue($pacer) -ne 500){throw 'Delay input did not update native milliseconds'}
    if($launchDelay.Bounds.Right -gt $options.ClientSize.Width -or $launchDelay.Bounds.Bottom -gt $options.ClientSize.Height){throw 'Delay input outside options panel'}
    Copy-AccountConfig;$launchDelay.Value=0;Paste-AccountConfigAll
    if($launchDelay.Value -ne 0.5){throw 'Copy/paste config omitted launch delay'}
    $launchDelay.Value=0;if($delayField.GetValue($pacer) -ne 0){throw 'Zero delay not applied'}
    Write-Output 'PASS: seconds input, native milliseconds, panel bounds, global config copy/paste and zero delay.'
}finally{$launchDelay.Value=$savedLaunchDelay}
$savedConcurrency=$launchConcurrency.Value
$launchConcurrency.Value=3
if($script:settings.launchConcurrency -ne 3){throw 'Launch concurrency not saved'}
Copy-AccountConfig;$launchConcurrency.Value=9;Paste-AccountConfigAll
if($launchConcurrency.Value -ne 3){throw 'Launch concurrency missing from copy/paste'}
$launchConcurrency.Value=$savedConcurrency
