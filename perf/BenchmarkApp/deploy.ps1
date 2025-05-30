#!/usr/bin/env pwsh
<#
.SYNOPSIS
    PulseRPC BenchmarkApp 部署脚本

.DESCRIPTION
    自动化构建、测试和部署 BenchmarkApp 的 PowerShell 脚本
    支持多种部署目标和环境配置

.PARAMETER Target
    部署目标：Build|Test|Package|Deploy|All

.PARAMETER Configuration
    构建配置：Debug|Release

.PARAMETER Environment
    目标环境：Development|Staging|Production

.PARAMETER OutputPath
    输出路径，默认为 ./artifacts

.PARAMETER SkipTests
    跳过测试步骤

.PARAMETER Verbose
    启用详细输出

.EXAMPLE
    ./deploy.ps1 -Target All -Configuration Release -Environment Production

.EXAMPLE
    ./deploy.ps1 -Target Build -Configuration Debug -Verbose
#>

param(
    [Parameter(Mandatory = $false)]
    [ValidateSet("Build", "Test", "Package", "Deploy", "All")]
    [string]$Target = "All",

    [Parameter(Mandatory = $false)]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [Parameter(Mandatory = $false)]
    [ValidateSet("Development", "Staging", "Production")]
    [string]$Environment = "Development",

    [Parameter(Mandatory = $false)]
    [string]$OutputPath = "./artifacts",

    [Parameter(Mandatory = $false)]
    [switch]$SkipTests,

    [Parameter(Mandatory = $false)]
    [switch]$Verbose
)

# 设置错误处理
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# 全局变量
$script:StartTime = Get-Date
$script:ProjectRoot = $PSScriptRoot
$script:Version = "1.0.0"
$script:BuildCounter = 1

# 日志函数
function Write-Log {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,

        [Parameter(Mandatory = $false)]
        [ValidateSet("Info", "Warning", "Error", "Success")]
        [string]$Level = "Info"
    )

    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $color = switch ($Level) {
        "Info" { "White" }
        "Warning" { "Yellow" }
        "Error" { "Red" }
        "Success" { "Green" }
        default { "White" }
    }

    $prefix = switch ($Level) {
        "Info" { "ℹ️" }
        "Warning" { "⚠️" }
        "Error" { "❌" }
        "Success" { "✅" }
        default { "📝" }
    }

    Write-Host "[$timestamp] $prefix $Message" -ForegroundColor $color
}

function Write-Header {
    param([string]$Title)

    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""
}

function Test-Prerequisites {
    Write-Header "检查先决条件"

    # 检查 .NET SDK
    try {
        $dotnetVersion = dotnet --version
        Write-Log "✅ .NET SDK 版本: $dotnetVersion" -Level Success
    }
    catch {
        Write-Log "❌ 未找到 .NET SDK，请安装 .NET 8 或更高版本" -Level Error
        exit 1
    }

    # 检查 Git
    try {
        $gitVersion = git --version
        Write-Log "✅ Git 版本: $gitVersion" -Level Success
    }
    catch {
        Write-Log "⚠️  未找到 Git，某些功能可能不可用" -Level Warning
    }

    # 检查项目文件
    $requiredProjects = @(
        "PulseRPC.Benchmark.Server/PulseRPC.Benchmark.Server.csproj",
        "PulseRPC.Benchmark.Client/PulseRPC.Benchmark.Client.csproj",
        "PulseRPC.Benchmark.Core/PulseRPC.Benchmark.Core.csproj",
        "PulseRPC.Benchmark.Tests/PulseRPC.Benchmark.Tests.csproj"
    )

    foreach ($project in $requiredProjects) {
        $projectPath = Join-Path $script:ProjectRoot $project
        if (Test-Path $projectPath) {
            Write-Log "✅ 找到项目: $project" -Level Success
        }
        else {
            Write-Log "❌ 缺少项目文件: $project" -Level Error
            exit 1
        }
    }
}

function Restore-Dependencies {
    Write-Header "恢复依赖包"

    Write-Log "正在恢复 NuGet 包..." -Level Info

    try {
        dotnet restore $script:ProjectRoot --verbosity $( if ($Verbose) { "normal" } else { "minimal" })
        Write-Log "✅ 依赖包恢复完成" -Level Success
    }
    catch {
        Write-Log "❌ 依赖包恢复失败: $($_.Exception.Message)" -Level Error
        exit 1
    }
}

function Build-Projects {
    Write-Header "构建项目"

    Write-Log "开始构建所有项目..." -Level Info
    Write-Log "配置: $Configuration" -Level Info
    Write-Log "环境: $Environment" -Level Info

    $buildArgs = @(
        "build"
        $script:ProjectRoot
        "--configuration", $Configuration
        "--no-restore"
        "--verbosity", $(if ($Verbose) { "normal" } else { "minimal" })
    )

    if ($Verbose) {
        $buildArgs += "--verbosity", "detailed"
    }

    try {
        dotnet @buildArgs
        Write-Log "✅ 项目构建完成" -Level Success
    }
    catch {
        Write-Log "❌ 项目构建失败: $($_.Exception.Message)" -Level Error
        exit 1
    }
}

function Run-Tests {
    if ($SkipTests) {
        Write-Log "⏭️  跳过测试步骤" -Level Warning
        return
    }

    Write-Header "运行测试"

    $testProjects = @(
        "PulseRPC.Benchmark.Tests/PulseRPC.Benchmark.Tests.csproj"
    )

    foreach ($testProject in $testProjects) {
        $testProjectPath = Join-Path $script:ProjectRoot $testProject

        if (Test-Path $testProjectPath) {
            Write-Log "运行测试项目: $testProject" -Level Info

            try {
                dotnet test $testProjectPath `
                    --configuration $Configuration `
                    --no-build `
                    --logger "console;verbosity=normal" `
                    --results-directory "$OutputPath/TestResults"

                Write-Log "✅ 测试项目 $testProject 通过" -Level Success
            }
            catch {
                Write-Log "❌ 测试项目 $testProject 失败: $($_.Exception.Message)" -Level Error
                exit 1
            }
        }
        else {
            Write-Log "⚠️  测试项目不存在: $testProject" -Level Warning
        }
    }
}

function Create-Package {
    Write-Header "创建部署包"

    # 确保输出目录存在
    if (!(Test-Path $OutputPath)) {
        New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
    }

    $publishProjects = @(
        @{
            Name = "Server"
            Project = "PulseRPC.Benchmark.Server/PulseRPC.Benchmark.Server.csproj"
            Output = "$OutputPath/Server"
        },
        @{
            Name = "Client"
            Project = "PulseRPC.Benchmark.Client/PulseRPC.Benchmark.Client.csproj"
            Output = "$OutputPath/Client"
        }
    )

    foreach ($proj in $publishProjects) {
        Write-Log "发布 $($proj.Name)..." -Level Info

        $projectPath = Join-Path $script:ProjectRoot $proj.Project

        try {
            dotnet publish $projectPath `
                --configuration $Configuration `
                --output $proj.Output `
                --no-build `
                --self-contained false `
                --verbosity $(if ($Verbose) { "normal" } else { "minimal" })

            Write-Log "✅ $($proj.Name) 发布完成: $($proj.Output)" -Level Success
        }
        catch {
            Write-Log "❌ $($proj.Name) 发布失败: $($_.Exception.Message)" -Level Error
            exit 1
        }
    }

    # 复制配置文件和脚本
    Copy-ConfigFiles
    Copy-StartupScripts
    Create-DeploymentArchive
}

function Copy-ConfigFiles {
    Write-Log "复制配置文件..." -Level Info

    $configSources = @(
        "configs/templates/"
        "configs/*.json"
    )

    $configDestination = "$OutputPath/configs"
    if (!(Test-Path $configDestination)) {
        New-Item -ItemType Directory -Path $configDestination -Force | Out-Null
    }

    foreach ($source in $configSources) {
        $sourcePath = Join-Path $script:ProjectRoot $source
        if (Test-Path $sourcePath) {
            Copy-Item -Path $sourcePath -Destination $configDestination -Recurse -Force
            Write-Log "✅ 已复制配置: $source" -Level Success
        }
    }
}

function Copy-StartupScripts {
    Write-Log "创建启动脚本..." -Level Info

    # 服务端启动脚本
    $serverScript = @"
#!/bin/bash
# PulseRPC Benchmark Server 启动脚本

echo "🚀 启动 PulseRPC Benchmark Server..."

# 设置环境变量
export DOTNET_ENVIRONMENT=$Environment
export ASPNETCORE_ENVIRONMENT=$Environment

# 启动服务端
dotnet PulseRPC.Benchmark.Server.dll `$@

echo "✅ 服务端已停止"
"@

    $serverScript | Out-File -FilePath "$OutputPath/Server/start-server.sh" -Encoding UTF8

    # 客户端启动脚本
    $clientScript = @"
#!/bin/bash
# PulseRPC Benchmark Client 启动脚本

echo "🎯 启动 PulseRPC Benchmark Client..."

# 设置环境变量
export DOTNET_ENVIRONMENT=$Environment

# 启动客户端
dotnet PulseRPC.Benchmark.Client.dll `$@

echo "✅ 客户端已完成"
"@

    $clientScript | Out-File -FilePath "$OutputPath/Client/start-client.sh" -Encoding UTF8

    # Windows 批处理文件
    $serverBat = @"
@echo off
echo 🚀 启动 PulseRPC Benchmark Server...

set DOTNET_ENVIRONMENT=$Environment
set ASPNETCORE_ENVIRONMENT=$Environment

dotnet PulseRPC.Benchmark.Server.dll %*

echo ✅ 服务端已停止
pause
"@

    $serverBat | Out-File -FilePath "$OutputPath/Server/start-server.bat" -Encoding UTF8

    $clientBat = @"
@echo off
echo 🎯 启动 PulseRPC Benchmark Client...

set DOTNET_ENVIRONMENT=$Environment

dotnet PulseRPC.Benchmark.Client.dll %*

echo ✅ 客户端已完成
pause
"@

    $clientBat | Out-File -FilePath "$OutputPath/Client/start-client.bat" -Encoding UTF8

    Write-Log "✅ 启动脚本创建完成" -Level Success
}

function Create-DeploymentArchive {
    Write-Log "创建部署压缩包..." -Level Info

    $archiveName = "PulseRPC-BenchmarkApp-$script:Version-$Configuration-$(Get-Date -Format 'yyyyMMdd-HHmmss').zip"
    $archivePath = Join-Path (Split-Path $OutputPath -Parent) $archiveName

    try {
        Compress-Archive -Path "$OutputPath/*" -DestinationPath $archivePath -Force
        Write-Log "✅ 部署包创建完成: $archivePath" -Level Success

        # 显示文件信息
        $archiveInfo = Get-Item $archivePath
        $sizeInMB = [math]::Round($archiveInfo.Length / 1MB, 2)
        Write-Log "📦 包大小: $sizeInMB MB" -Level Info
    }
    catch {
        Write-Log "❌ 创建部署包失败: $($_.Exception.Message)" -Level Error
    }
}

function Deploy-Application {
    Write-Header "部署应用程序"

    Write-Log "⚠️  部署功能需要根据具体环境配置" -Level Warning
    Write-Log "请参考部署文档进行手动部署或配置自动化脚本" -Level Info

    # 这里可以添加具体的部署逻辑，例如：
    # - 上传到服务器
    # - 容器化部署
    # - 云平台部署
    # - 等等

    Write-Log "✅ 部署准备完成" -Level Success
}

function Show-Summary {
    Write-Header "部署总结"

    $duration = (Get-Date) - $script:StartTime

    Write-Log "🎉 部署流程完成！" -Level Success
    Write-Log "⏱️  总耗时: $($duration.ToString('hh\:mm\:ss'))" -Level Info
    Write-Log "📁 输出目录: $OutputPath" -Level Info
    Write-Log "🏗️  构建配置: $Configuration" -Level Info
    Write-Log "🌍 目标环境: $Environment" -Level Info

    if (Test-Path $OutputPath) {
        $outputSize = (Get-ChildItem $OutputPath -Recurse | Measure-Object -Property Length -Sum).Sum
        $outputSizeInMB = [math]::Round($outputSize / 1MB, 2)
        Write-Log "📊 输出大小: $outputSizeInMB MB" -Level Info
    }

    Write-Host ""
    Write-Host "📋 下一步操作:" -ForegroundColor Cyan
    Write-Host "   1. 检查输出目录中的文件" -ForegroundColor White
    Write-Host "   2. 验证配置文件" -ForegroundColor White
    Write-Host "   3. 运行启动脚本测试" -ForegroundColor White
    Write-Host "   4. 部署到目标环境" -ForegroundColor White
    Write-Host ""
}

# 主执行逻辑
function Main {
    Write-Header "PulseRPC BenchmarkApp 部署脚本"

    Write-Log "开始部署流程..." -Level Info
    Write-Log "目标: $Target" -Level Info
    Write-Log "配置: $Configuration" -Level Info
    Write-Log "环境: $Environment" -Level Info
    Write-Log "输出路径: $OutputPath" -Level Info

    try {
        if ($Target -eq "All" -or $Target -eq "Build") {
            Test-Prerequisites
            Restore-Dependencies
            Build-Projects
        }

        if ($Target -eq "All" -or $Target -eq "Test") {
            Run-Tests
        }

        if ($Target -eq "All" -or $Target -eq "Package") {
            Create-Package
        }

        if ($Target -eq "All" -or $Target -eq "Deploy") {
            Deploy-Application
        }

        Show-Summary
    }
    catch {
        Write-Log "❌ 部署过程中发生错误: $($_.Exception.Message)" -Level Error
        Write-Log "📍 错误位置: $($_.InvocationInfo.ScriptLineNumber):$($_.InvocationInfo.OffsetInLine)" -Level Error
        exit 1
    }
}

# 启动主函数
Main
