$sw = [System.Diagnostics.Stopwatch]::StartNew()
Write-Host "===Building Runtime==="
dnu build src\Microsoft.Framework.Runtime
Write-Host "===Building Roslyn==="
dnu build src\Microsoft.Framework.Runtime.Roslyn
Write-Host "===Building Loader==="
dnu build src\Microsoft.Framework.Runtime.Loader
Write-Host "===Building Interfaces==="
dnu build src\Microsoft.Framework.Runtime.Interfaces
$sw.Stop();

$sw