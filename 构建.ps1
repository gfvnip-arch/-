$ErrorActionPreference='Stop'
$taskRoot=$PSScriptRoot
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskDest=Join-Path $taskRoot 'build'
if(Test-Path -LiteralPath $taskDest){throw 'build目录已存在，请先保留已有结果再指定新的输出目录。'}
New-Item -ItemType Directory $taskDest | Out-Null
$taskSources=@(Get-ChildItem (Join-Path $taskRoot 'src\*.cs') | ForEach-Object FullName)
$taskArgs=@('/nologo','/utf8output','/codepage:65001','/target:winexe','/platform:x64','/optimize+',('/out:'+(Join-Path $taskDest '代理小助手.exe')),('/win32manifest:'+(Join-Path $taskRoot 'src\app.manifest')),('/win32icon:'+(Join-Path $taskRoot 'assets\assistant.ico')),('/resource:'+(Join-Path $taskRoot 'assets\avatar-128.png')+',IPFlyLink.Avatar.png'),'/r:System.dll','/r:System.Core.dll','/r:System.Security.dll','/r:System.Web.Extensions.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll')+$taskSources
& $taskCompiler @taskArgs
if($LASTEXITCODE -ne 0){throw '编译失败'}
Write-Output '构建完成。运行时仍需要交付包中的core目录和说明文件。'
