$sw = [System.Diagnostics.Stopwatch]::StartNew()
dnu build src\Microsoft.Framework.Runtime src\Microsoft.Framework.Runtime.Roslyn src\Microsoft.Framework.Runtime.Loader src\Microsoft.Framework.Runtime.Interfaces
$sw.Stop();

$sw