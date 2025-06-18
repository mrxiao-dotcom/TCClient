# TCClient 自动发布打包脚本
param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release",
    [switch]$SkipBuild = $false,
    [switch]$CreateInstaller = $true
)

Write-Host "=== TCClient 发布脚本 ===" -ForegroundColor Green
Write-Host "版本号: $Version" -ForegroundColor Yellow
Write-Host "配置: $Configuration" -ForegroundColor Yellow

# 设置路径
$ProjectPath = Join-Path $PSScriptRoot ".." "TCClient"
$OutputPath = Join-Path $PSScriptRoot ".." "Release" $Version
$PublishPath = Join-Path $OutputPath "Published"

# 创建输出目录
if (Test-Path $OutputPath) {
    Remove-Item $OutputPath -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
New-Item -ItemType Directory -Path $PublishPath -Force | Out-Null

try {
    # 1. 更新版本号
    Write-Host "更新版本号..." -ForegroundColor Blue
    $ProjectFile = Join-Path $ProjectPath "TCClient.csproj"
    $ProjectContent = Get-Content $ProjectFile -Raw
    
    # 更新版本信息
    $ProjectContent = $ProjectContent -replace '<AssemblyVersion>.*</AssemblyVersion>', "<AssemblyVersion>$Version.0</AssemblyVersion>"
    $ProjectContent = $ProjectContent -replace '<FileVersion>.*</FileVersion>', "<FileVersion>$Version.0</FileVersion>"
    $ProjectContent = $ProjectContent -replace '<Version>.*</Version>', "<Version>$Version</Version>"
    
    Set-Content -Path $ProjectFile -Value $ProjectContent -Encoding UTF8
    Write-Host "版本号已更新到 $Version" -ForegroundColor Green

    # 2. 清理和构建项目
    if (-not $SkipBuild) {
        Write-Host "清理项目..." -ForegroundColor Blue
        dotnet clean $ProjectPath --configuration $Configuration

        Write-Host "构建项目..." -ForegroundColor Blue
        dotnet build $ProjectPath --configuration $Configuration --no-restore
        
        if ($LASTEXITCODE -ne 0) {
            throw "构建失败"
        }
    }

    # 3. 发布单文件版本（自包含）
    Write-Host "发布自包含版本..." -ForegroundColor Blue
    $SelfContainedPath = Join-Path $PublishPath "SelfContained"
    dotnet publish $ProjectPath `
        --configuration $Configuration `
        --runtime win-x64 `
        --self-contained true `
        --output $SelfContainedPath `
        --property:PublishSingleFile=true `
        --property:IncludeNativeLibrariesForSelfExtract=true `
        --property:PublishTrimmed=false

    if ($LASTEXITCODE -ne 0) {
        throw "发布自包含版本失败"
    }

    # 4. 发布依赖框架版本
    Write-Host "发布框架依赖版本..." -ForegroundColor Blue
    $FrameworkDependentPath = Join-Path $PublishPath "FrameworkDependent"
    dotnet publish $ProjectPath `
        --configuration $Configuration `
        --runtime win-x64 `
        --self-contained false `
        --output $FrameworkDependentPath

    if ($LASTEXITCODE -ne 0) {
        throw "发布框架依赖版本失败"
    }

    # 5. 复制必要文件
    Write-Host "复制配置文件..." -ForegroundColor Blue
    
    # 复制到自包含版本
    $ConfigFiles = @("appsettings.json", "database_config.json")
    foreach ($ConfigFile in $ConfigFiles) {
        $SourceFile = Join-Path $ProjectPath "bin" $Configuration "net9.0-windows" $ConfigFile
        if (Test-Path $SourceFile) {
            Copy-Item $SourceFile $SelfContainedPath -Force
        }
    }
    
    # 复制到框架依赖版本
    foreach ($ConfigFile in $ConfigFiles) {
        $SourceFile = Join-Path $ProjectPath "bin" $Configuration "net9.0-windows" $ConfigFile
        if (Test-Path $SourceFile) {
            Copy-Item $SourceFile $FrameworkDependentPath -Force
        }
    }

    # 6. 创建压缩包
    Write-Host "创建压缩包..." -ForegroundColor Blue
    
    $SelfContainedZip = Join-Path $OutputPath "TCClient_v${Version}_SelfContained.zip"
    $FrameworkDependentZip = Join-Path $OutputPath "TCClient_v${Version}_FrameworkDependent.zip"
    
    Compress-Archive -Path "$SelfContainedPath\*" -DestinationPath $SelfContainedZip -Force
    Compress-Archive -Path "$FrameworkDependentPath\*" -DestinationPath $FrameworkDependentZip -Force

    # 7. 跳过更新配置文件生成（已移除自动更新功能）

    # 8. 创建安装程序（如果需要）
    if ($CreateInstaller) {
        Write-Host "创建安装程序..." -ForegroundColor Blue
        
        # 这里可以集成 NSIS 或 WiX 工具创建安装程序
        # 暂时创建一个批处理安装脚本
        $InstallScriptPath = Join-Path $OutputPath "install.bat"
        $InstallScript = @"
@echo off
echo 安装 TCClient v$Version
echo.

REM 创建程序目录
if not exist "%ProgramFiles%\TCClient" mkdir "%ProgramFiles%\TCClient"

REM 复制程序文件
xcopy /Y /E "SelfContained\*" "%ProgramFiles%\TCClient\"

REM 创建桌面快捷方式
echo 创建桌面快捷方式...
powershell -command "& { `$ws = New-Object -ComObject WScript.Shell; `$s = `$ws.CreateShortcut([Environment]::GetFolderPath('Desktop') + '\TCClient.lnk'); `$s.TargetPath = '%ProgramFiles%\TCClient\TCClient.exe'; `$s.Save() }"

REM 创建开始菜单快捷方式
if not exist "%ProgramData%\Microsoft\Windows\Start Menu\Programs\TCClient" mkdir "%ProgramData%\Microsoft\Windows\Start Menu\Programs\TCClient"
powershell -command "& { `$ws = New-Object -ComObject WScript.Shell; `$s = `$ws.CreateShortcut('%ProgramData%\Microsoft\Windows\Start Menu\Programs\TCClient\TCClient.lnk'); `$s.TargetPath = '%ProgramFiles%\TCClient\TCClient.exe'; `$s.Save() }"

echo.
echo 安装完成！
pause
"@
        Set-Content -Path $InstallScriptPath -Value $InstallScript -Encoding Default
    }

    # 9. 生成发布信息
    Write-Host "生成发布信息..." -ForegroundColor Blue
    $ReleaseInfoPath = Join-Path $OutputPath "ReleaseInfo.txt"
    $ReleaseInfo = @"
TCClient v$Version 发布信息
========================================

发布时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
版本号: $Version
配置: $Configuration

文件列表:
- TCClient_v${Version}_SelfContained.zip (自包含版本，推荐)
- TCClient_v${Version}_FrameworkDependent.zip (需要.NET 9运行时)
- install.bat (安装脚本)

部署说明:
1. 将 TCClient_v${Version}_SelfContained.zip 上传到下载服务器
2. 解压并运行程序或使用install.bat安装

注意事项:
- 自包含版本无需安装.NET运行时
- 框架依赖版本需要安装.NET 9运行时
- 建议使用数字签名保证软件安全性
"@
    
    Set-Content -Path $ReleaseInfoPath -Value $ReleaseInfo -Encoding UTF8

    Write-Host "=== 发布完成 ===" -ForegroundColor Green
    Write-Host "输出目录: $OutputPath" -ForegroundColor Yellow
    Write-Host "自包含版本: TCClient_v${Version}_SelfContained.zip" -ForegroundColor Yellow
    Write-Host "框架依赖版本: TCClient_v${Version}_FrameworkDependent.zip" -ForegroundColor Yellow
    
    # 打开输出目录
    Start-Process "explorer.exe" -ArgumentList $OutputPath

} catch {
    Write-Host "发布失败: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host "按任意键退出..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown') 