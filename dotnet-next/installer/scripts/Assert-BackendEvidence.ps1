# Pure evidence validation: no build, filesystem writes, native UI or network.
function Assert-BackendEvidence {
 [CmdletBinding()]
 param([Parameter(Mandatory)]$Evidence,[Parameter(Mandatory)][string]$PublishedShellSha256)
 if ($Evidence.EvidenceScope -cne 'exact-published-dll-backend-and-offscreen') { throw 'Evidence scope must identify backend operations and offscreen smoke only.' }
 foreach ($field in @('ReleasePublish','BackendTestsPassed','OffscreenSmokePassed','MainWorkspaceTestsPassed')) {
  $value=$Evidence.$field
  if ($value -isnot [bool] -or $value -ne $true) { throw "Required backend evidence flag must be JSON true: $field" }
 }
 if ($Evidence.MainWorkspaceKind -cne 'real-pdf') { throw 'TEST 2 requires automated checks of the real-PDF main workspace, not sample-only UX.' }
 foreach ($field in @('NativeUiTested','FidelityAccepted')) {
  $value=$Evidence.$field
  if ($value -isnot [bool] -or $value -ne $false) { throw "Experimental package must explicitly retain JSON false for $field; no native/fidelity acceptance is claimed." }
 }
 if ([string]::IsNullOrWhiteSpace($Evidence.NativeUiLimitation)) { throw 'Native UI limitation must be recorded explicitly.' }
 if ($Evidence.PublishedShellSha256 -notmatch '^[A-Fa-f0-9]{64}$' -or $PublishedShellSha256 -notmatch '^[A-Fa-f0-9]{64}$' -or $Evidence.PublishedShellSha256 -ne $PublishedShellSha256) { throw 'Backend evidence does not match the exact published Shell DLL SHA-256.' }
 foreach ($operation in @('open','render','rotate','delete','extract','combine','save-as','original-preservation')) {
  $value=$Evidence.BackendOperations.$operation
  if ($value -isnot [bool] -or $value -ne $true) { throw "Required backend operation check did not pass: $operation" }
 }
}
