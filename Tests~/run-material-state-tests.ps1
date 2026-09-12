param(
    [Parameter(Mandatory = $true)][string]$UnityProject,
    [Parameter(Mandatory = $true)][string]$UnityEditor,
    [string]$ShaderGraph
)

$ErrorActionPreference = 'Stop'
$projectPath = (Resolve-Path -LiteralPath $UnityProject).Path
$editorPath = (Resolve-Path -LiteralPath $UnityEditor).Path
$pluginRoot = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

# Reuse the project's real Unity references, defines and source list, writing all
# compiler output here instead of replacing the running editor's assemblies.
$responseFile = Get-ChildItem -LiteralPath (Join-Path $projectPath 'Library/Bee/artifacts') -Recurse -Filter 'Assembly-CSharp-Editor.rsp' |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if (!$responseFile) { throw 'Open and compile the Unity project first; no Editor compiler response file was found.' }
$response = [System.IO.File]::ReadAllText($responseFile.FullName)
# A cached Unity response can still list temporary Editor scripts deleted since
# its last compilation. Do not require those removed files to run the regressions.
$response = (($response -split '\r?\n') | Where-Object {
    $sourceEntry = $_.Trim().Trim('"')
    if (!$sourceEntry.StartsWith('-') -and $sourceEntry.EndsWith('.cs')) {
        $sourceFile = if ([System.IO.Path]::IsPathRooted($sourceEntry)) { $sourceEntry } else { Join-Path $projectPath $sourceEntry }
        if (![System.IO.File]::Exists($sourceFile)) {
            Write-Host "Skipping removed source from cached Unity response: $sourceEntry"
            return $false
        }
    }
    return $true
}) -join "`n"
$response = [regex]::Replace($response, '(?m)^-target:.*$', '-target:exe')
$response = [regex]::Replace($response, '(?m)^-out:.*$', ('-out:"' + $outputPath + '/Assembly-CSharp-Editor.dll"'))
$response = [regex]::Replace($response, '(?m)^-refout:.*\r?\n', '')
foreach ($relativeSource in @('Editor/Export/utils/ShaderGraphRenderState.cs', 'Tests~/MaterialRenderStateTests.cs', 'Tests~/CubemapExportTests.cs')) {
    $sourcePath = (Join-Path $pluginRoot $relativeSource).Replace('\', '/')
    if (!$response.Replace('\', '/').Contains($sourcePath)) { $response += "`n" + '"' + $sourcePath + '"' }
}
$testResponseFile = Join-Path $outputPath 'tests.rsp'
[System.IO.File]::WriteAllText($testResponseFile, $response)

$editorData = Join-Path (Split-Path -Parent $editorPath) 'Data'
$compilerHost = Join-Path $editorData 'Tools/netcorerun/netcorerun.exe'
$compiler = Join-Path $editorData 'DotNetSdkRoslyn/csc.dll'
$compileLog = Join-Path $outputPath 'compile.log'
Push-Location $projectPath
try {
    & $compilerHost $compiler ('@' + $testResponseFile) *> $compileLog
    if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $compileLog; throw 'Unity Editor assembly compilation failed.' }
} finally {
    Pop-Location
}

# The tested rules and JSON writer use no native Unity calls. CoreModule is needed
# only to load Material signatures and Unity's actual BlendMode enum definitions.
Copy-Item -LiteralPath (Join-Path $editorData 'Managed/UnityEngine/UnityEngine.CoreModule.dll') -Destination $outputPath -Force
$runtime = @(& dotnet --list-runtimes) | Where-Object { $_ -match '^Microsoft.NETCore.App ' } |
    ForEach-Object { [version]($_.Split(' ')[1]) } | Sort-Object -Descending | Select-Object -First 1
if (!$runtime) { throw 'A .NET runtime is required to execute the tests.' }
$runtimeConfig = @{ runtimeOptions = @{ framework = @{ name = 'Microsoft.NETCore.App'; version = $runtime.ToString() } } }
[System.IO.File]::WriteAllText((Join-Path $outputPath 'Assembly-CSharp-Editor.runtimeconfig.json'), ($runtimeConfig | ConvertTo-Json -Depth 4))
$arguments = @((Join-Path $outputPath 'Assembly-CSharp-Editor.dll'))
if ($ShaderGraph) { $arguments += (Resolve-Path -LiteralPath $ShaderGraph).Path }
& dotnet @arguments | Tee-Object -FilePath (Join-Path $outputPath 'results.txt')
if ($LASTEXITCODE -ne 0) { throw 'Material render state regression tests failed.' }
Write-Output "Unity Editor assembly compiled successfully. Compiler log: $compileLog"
