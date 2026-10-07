param([switch]$Check,[string]$OutFile='dist\莉莉娅中文修改器.exe')
$ErrorActionPreference='Stop'
$outfitRoot=$PSScriptRoot
$outfitOutPath=if([IO.Path]::IsPathRooted($OutFile)){$OutFile}else{Join-Path $outfitRoot $OutFile}
$outfitOutDir=Split-Path -Parent $outfitOutPath
if($outfitOutDir){[void](New-Item -ItemType Directory -Force -Path $outfitOutDir)}
$outfitCompilerRoots=@(
    'C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC',
    'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC'
)
$outfitMsvc=$null
foreach($candidate in $outfitCompilerRoots) {
    if(Test-Path -LiteralPath $candidate) {
        $outfitMsvc=Get-ChildItem -LiteralPath $candidate -Directory | Sort-Object Name -Descending |
            Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'bin\Hostx64\x64\cl.exe') } | Select-Object -First 1
        if($outfitMsvc){break}
    }
}
if(-not $outfitMsvc){throw '需要 MSVC x64 编译器。'}
$outfitSdk=Get-ChildItem -LiteralPath 'C:\Program Files (x86)\Windows Kits\10\Include' -Directory |
    Where-Object Name -match '^10\.' | Sort-Object Name -Descending | Select-Object -First 1
if(-not $outfitSdk){throw '需要 Windows 10/11 SDK。'}
$outfitInclude=$outfitSdk.FullName
$outfitLib='C:\Program Files (x86)\Windows Kits\10\Lib\'+$outfitSdk.Name
$outfitCl=Join-Path $outfitMsvc.FullName 'bin\Hostx64\x64\cl.exe'
$outfitCommon=@('/nologo','/W4','/EHsc','/std:c++17','/O2','/MT',
    ('/I'+$outfitMsvc.FullName+'\include'),('/I'+$outfitInclude+'\ucrt'),
    ('/I'+$outfitInclude+'\shared'),('/I'+$outfitInclude+'\um'))
$outfitLibraries=@(('/LIBPATH:'+$outfitMsvc.FullName+'\lib\x64'),
    ('/LIBPATH:'+$outfitLib+'\ucrt\x64'),('/LIBPATH:'+$outfitLib+'\um\x64'),'/INCREMENTAL:NO')
[void](New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot 'temp'))
Push-Location $PSScriptRoot
try {
    & $outfitCl @outfitCommon /LD /Fo:temp\OutfitBridge.obj OutfitBridge.cpp /link /OUT:temp\LilliaOutfitBridge.dll /IMPLIB:temp\LilliaOutfitBridge.lib @outfitLibraries
    if($LASTEXITCODE -ne 0){throw '原生换装组件编译失败。'}
    $outfitCsc=Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $outfitCsc /nologo /codepage:65001 /target:winexe /platform:x64 /r:System.Windows.Forms.dll /r:System.Drawing.dll '/resource:temp\LilliaOutfitBridge.dll,LilliaOutfitBridge.dll' ('/out:'+$outfitOutPath) ChineseTrainer.cs OutfitFeature.cs WarehouseFeature.cs WarehouseCapacity.cs
    if($LASTEXITCODE -ne 0){throw '中文修改器编译失败。'}
    if($Check) {
        & $outfitCl @outfitCommon /Fo:temp\OutfitCoreTests.obj /Fe:temp\OutfitCoreTests.exe OutfitCoreTests.cpp /link @outfitLibraries
        if($LASTEXITCODE -ne 0){throw '换装测试编译失败。'}
        & '.\temp\OutfitCoreTests.exe'
        if($LASTEXITCODE -ne 0){throw '换装布局测试失败。'}
        $outfitStart=New-Object Diagnostics.ProcessStartInfo
        $outfitStart.FileName=$outfitOutPath
        $outfitStart.WorkingDirectory=$outfitRoot
        $outfitStart.Arguments='--selftest'
        $outfitStart.UseShellExecute=$false
        $outfitStart.CreateNoWindow=$true
        $outfitStart.RedirectStandardOutput=$true
        $outfitStart.RedirectStandardError=$true
        $outfitProcess=New-Object Diagnostics.Process
        try {
            $outfitProcess.StartInfo=$outfitStart
            [void]$outfitProcess.Start()
            $outfitOutput=$outfitProcess.StandardOutput.ReadToEnd()
            $outfitError=$outfitProcess.StandardError.ReadToEnd()
            $outfitProcess.WaitForExit()
            Write-Output $outfitOutput
            if($outfitError){Write-Output $outfitError}
            if($outfitProcess.ExitCode -ne 0){throw '修改器自检失败。'}
        } finally {$outfitProcess.Dispose()}
    }
} finally {Pop-Location}
