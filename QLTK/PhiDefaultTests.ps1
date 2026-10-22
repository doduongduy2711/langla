$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'NativeBackend.cs') -ReferencedAssemblies System.dll,System.Core.dll
$phi=New-Object TGLL.SkillInfo;$phi.Id=295;$phi.Type=0
$skills=[TGLL.SkillInfo[]]@($phi)
function Assert-Skills($selected,$expected,$available){
    $actual=[TGLL.NativeBackend]::DefaultTrainingSkills([TGLL.SkillInfo[]]$available,[int[]]$selected)
    if(($actual -join ',') -ne $expected){throw "Expected $expected, got $($actual -join ',')"}
}
Assert-Skills @() '295' $skills
Assert-Skills @(285,1) '295,285,1' $skills
Assert-Skills @(368,295,285,1) '295,368,285,1' $skills
$original=@(368,285,1,219)
Assert-Skills $original '295,368,285,1' $skills
if(($original -join ',') -ne '368,285,1,219'){throw 'Changed caller selection'}
Assert-Skills @(285,1) '285,1' @()
$phi.Type=2
Assert-Skills @(285,1) '285,1' $skills
Write-Output 'PASS: default Phi, legacy saved selection, first position, four-skill limit, caller isolation, unavailable skill.'
