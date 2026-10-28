$bulkSavedAccounts=$script:accounts;$bulkSaveFn=(Get-Item Function:\Save-Accounts).ScriptBlock;$bulkRefreshFn=(Get-Item Function:\Refresh-Grid).ScriptBlock
$bulkDialog=$null
try {
    $parsed=@(ConvertFrom-BulkAccountText "u1,p1,127.0.0.1:1080`r`nu2,p2,proxy.example:2080:pu:pp:tail`n`nu3,p3,")
    if($parsed.Count -ne 3 -or $parsed[0].proxy.port -ne 1080 -or $parsed[1].proxy.password -ne 'pp:tail' -or $null -ne $parsed[2].proxy){throw 'Bulk account proxy parsing failed'}
    foreach($bad in @('',"u,pw,host:0", "u,pw,host:1080:user:","u,,host:1080", "u,p,`nu,q,")){
        $rejected=$false;try{$null=ConvertFrom-BulkAccountText $bad}catch{$rejected=$true};if(!$rejected){throw 'Invalid or duplicate bulk line accepted'}
    }
    $colon=@(ConvertFrom-BulkAccountText 'withcolon,p:a:ss,127.0.0.1:1080');if($colon[0].password -ne 'p:a:ss'){throw 'Colon in account password was split'}
    $many=@(ConvertFrom-BulkAccountText ((1..2000 | ForEach-Object {"many${_},pw,127.0.0.1:1080"}) -join "`n"))
    if($many.Count -ne 2000){throw 'Bulk import truncated a large list'}
    $existing=[pscustomobject]@{id='bulk-fixture';username='u1';secret=(Protect-Password 'old');proxy=$null;autoQuest=$true;soncap=$true;cloakId=77;stoneChestQuantity=100;gameOverrides=@('keep')}
    $script:accounts=@($existing);$plan=Prepare-AccountBatch $parsed
    if($plan.updated.Count -ne 3 -or $plan.updated[0].id -ne 'bulk-fixture' -or !$plan.updated[0].autoQuest -or !$plan.updated[0].soncap -or $plan.updated[0].cloakId -ne 77 -or $plan.updated[0].stoneChestQuantity -ne 100){throw 'Updating credentials dropped existing auto preferences'}
    if((Unprotect-Password $plan.updated[0].secret) -ne 'p1'){throw 'Password not updated'}
    $script:bulkTestSaves=0;function Save-Accounts {$script:bulkTestSaves++};function Refresh-Grid {}
    $bulkDialog=New-BulkAccountDialog
    if(!$bulkDialog.Tag.Input.Multiline -or $bulkDialog.Tag.Input.WordWrap -or $bulkAccountButton.Parent -ne $top){throw 'Bulk entry UI missing'}
    $bulkDialog.Tag.Input.Text="u1:new`nu4:pw";$bulkDialog.Tag.Proxies.Text="127.0.0.1:1080"
    Save-BulkAccountDialog $bulkDialog.Tag.Save
    if($script:accounts.Count -ne 2 -or $script:bulkTestSaves -ne 1 -or !$script:accounts[0].autoQuest){throw 'Bulk dialog did not commit once'}
    $bulkDialog.Dispose();$bulkDialog=New-BulkAccountDialog
    $bulkDialog.Tag.Input.Text="u5:pw`nu6:secret";$bulkDialog.Tag.Proxies.Text="127.0.0.1:1080`ninvalid";Save-BulkAccountDialog $bulkDialog.Tag.Save
    if($script:accounts.Count -ne 2 -or $script:bulkTestSaves -ne 1 -or $bulkDialog.Tag.Note.Text -notmatch 'dòng 2' -or $bulkDialog.Tag.Note.Text -match 'secret'){throw 'Invalid line partially saved or exposed input'}
    function Save-Accounts {throw 'synthetic failure'}
    $bulkDialog.Tag.Input.Text='u5:pw';$bulkDialog.Tag.Proxies.Clear();Save-BulkAccountDialog $bulkDialog.Tag.Save
    if($script:accounts.Count -ne 2 -or $bulkDialog.Tag.Note.Text -notmatch 'save_failed'){throw 'Failed save did not roll back'}
    $splitSaved=$script:accounts
    try {
        $script:accounts=@()
        $newParsed=@(ConvertFrom-BulkSplitText "n1:p:a:ss`n`nn2:pw" "host.example:1000:pu:pp:tail`nhost.example:1001")
        if($newParsed.Count -ne 2 -or $newParsed[0].password -ne 'p:a:ss'){throw 'Two-box account/password parsing failed'}
        $balanced=@(ConvertFrom-BulkSplitText ((1..25 | ForEach-Object {"n${_}:pw"}) -join "`n") "host.example:1000`nhost.example:1001`nhost.example:1002")
        $counts=@($balanced | Group-Object {$_.proxy.host+':'+$_.proxy.port} | ForEach-Object {$_.Count})
        if($counts.Count -ne 3 -or (($counts | Measure-Object -Maximum).Maximum-($counts | Measure-Object -Minimum).Minimum) -gt 1){throw 'Proxy allocation not even or exceeds 12'}
        $full=@(ConvertFrom-BulkSplitText ((1..24 | ForEach-Object {"f${_}:pw"}) -join "`n") "host.example:1000`nhost.example:1001")
        if(@($full | Group-Object {$_.proxy.port} | Where-Object {$_.Count -ne 12}).Count){throw 'Exact proxy capacity failed'}
        foreach($pool in @('host.example:1000',"host.example:1000`nhost.example:1000")){
            $unlimited=@(ConvertFrom-BulkSplitText ((1..196 | ForEach-Object {"x${_}:pw"}) -join "`n") $pool)
            if($unlimited.Count -ne 196 -or @($unlimited | Group-Object {$_.proxy.port}).Count -ne 1){throw 'Unlimited single proxy or duplicate deduplication failed'}
        }
        $large=@(ConvertFrom-BulkSplitText ((1..196 | ForEach-Object {"x${_}:pw"}) -join "`n") "host.example:1000`nhost.example:1001`nhost.example:1002")
        $largeCounts=@($large | Group-Object {$_.proxy.port} | ForEach-Object {$_.Count} | Sort-Object)
        if(($largeCounts -join ',') -ne '65,65,66'){throw '196 accounts should balance 65/65/66'}
        $script:accounts=@(1..11 | ForEach-Object {[pscustomobject]@{username="old$_";proxy=[pscustomobject]@{host='HOST.EXAMPLE';port=1000}}})
        $existingCapacity=@(ConvertFrom-BulkSplitText 'new1:pw' 'host.example:1000')
        if($existingCapacity.Count -ne 1){throw 'Existing-account capacity missing'}
        $more=@(ConvertFrom-BulkSplitText "new1:pw`nnew2:pw" 'host.example:1000');if($more.Count -ne 2){throw 'Old 12-account cap remains'}
        $reassigned=@(ConvertFrom-BulkSplitText "old1:pw`nnew2:pw" 'host.example:1000');if($reassigned.Count -ne 2){throw 'Updating existing account did not release its old slot'}
        $withoutProxy=@(ConvertFrom-BulkSplitText 'direct:pw' '');if($null -ne $withoutProxy[0].proxy){throw 'Blank proxy list should use no proxy'}
        $bulkDialog.Dispose();$bulkDialog=New-BulkAccountDialog
        if(!$bulkDialog.Tag.Proxies.Multiline -or $bulkDialog.Tag.Proxies.WordWrap -or $bulkDialog.Tag.Input -eq $bulkDialog.Tag.Proxies){throw 'Separate proxy textbox missing'}
        $bulkDialog.ClientSize=New-Object Drawing.Size(920,600);$bulkDialog.PerformLayout()
        if($bulkDialog.Tag.Input.Bounds.IntersectsWith($bulkDialog.Tag.Proxies.Bounds)){throw 'Two boxes overlap after resize'}
        Write-Output 'PASS: two-box user:pass/proxy input, colon passwords, balanced random distribution, unlimited accounts, 196-account balance, duplicate endpoint, existing-account load, reassignment, blank proxies and resize.'
    }finally{$script:accounts=$splitSaved}

    Write-Output 'PASS: bulk tk,mk,proxy dialog, authenticated proxy/colon password, blank lines, validation, duplicate rejection, 2000 accounts, credential update preserving all auto settings, one save and rollback.'
}finally {
    if($bulkDialog){$bulkDialog.Dispose()};$script:accounts=$bulkSavedAccounts;Set-Item Function:\Save-Accounts $bulkSaveFn;Set-Item Function:\Refresh-Grid $bulkRefreshFn
}

$httpTest=Normalize-Proxy 'http://127.0.0.1:8080:user:pass:colon'
if($httpTest.type -ne 'http' -or $httpTest.password -ne 'pass:colon' -or (Normalize-Proxy '127.0.0.1:8080').type -ne 'http'){throw 'HTTP default/prefix normalization failed'}
if((Normalize-Proxy 'socks5://127.0.0.1:1080').type -ne 'socks5'){throw 'Explicit SOCKS compatibility failed'}
$httpStored=Protect-Proxy $httpTest;$httpRead=Read-Proxy ([pscustomobject]@{proxy=$httpStored})
if($httpStored.type -ne 'http' -or $httpRead.type -ne 'http' -or $httpRead.password -ne $httpTest.password -or (Proxy-Summary $httpRead).type -ne 'http'){throw 'HTTP type lost in storage or summary'}
Write-Output 'PASS: HTTP default and scheme, colon password, protocol persistence/readback, summary and explicit SOCKS compatibility.'
