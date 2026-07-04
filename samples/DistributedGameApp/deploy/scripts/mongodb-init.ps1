# MongoDB 索引初始化脚本（Windows PowerShell）

param(
    [Parameter(Position=0)]
    [ValidateSet("local", "docker", "verify", "help")]
    [string]$Action = "local",

    [Parameter(Position=1)]
    [string]$ContainerName = "pulserpc-mongodb"
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $ScriptDir)
$MongoDBDir = Join-Path $ProjectRoot "deploy\mongodb"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "MongoDB 索引初始化" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

# 检查 MongoDB 连接
function Test-MongoDBConnection {
    Write-Host "`n→ 检查 MongoDB 连接..." -ForegroundColor Yellow

    try {
        $result = mongosh --eval "db.adminCommand('ping')" --quiet 2>$null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  ✓ MongoDB 连接成功" -ForegroundColor Green
            return $true
        }
    } catch {
        Write-Host "  ❌ 无法连接到 MongoDB" -ForegroundColor Red
        return $false
    }

    Write-Host "  ❌ 无法连接到 MongoDB" -ForegroundColor Red
    return $false
}

# 初始化索引
function Initialize-Indexes {
    Write-Host "`n→ 执行索引初始化脚本..." -ForegroundColor Yellow

    $initScriptPath = Join-Path $MongoDBDir "init.js"

    if (Test-Path $initScriptPath) {
        Get-Content $initScriptPath | mongosh
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  ✓ 索引初始化完成" -ForegroundColor Green
        } else {
            Write-Host "  ❌ 索引初始化失败" -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "  ❌ 找不到 init.js 文件: $initScriptPath" -ForegroundColor Red
        exit 1
    }
}

# 验证索引
function Test-Indexes {
    Write-Host "`n→ 验证索引..." -ForegroundColor Yellow

    $verifyScriptPath = Join-Path $MongoDBDir "verify-indexes.js"

    if (Test-Path $verifyScriptPath) {
        Get-Content $verifyScriptPath | mongosh
    } else {
        Write-Host "  ⚠️  找不到 verify-indexes.js 文件" -ForegroundColor DarkYellow
    }
}

# Docker 环境初始化
function Initialize-DockerIndexes {
    param([string]$Container)

    Write-Host "`n→ 在 Docker 容器中初始化索引..." -ForegroundColor Yellow

    # 检查容器是否运行
    $containerRunning = docker ps --filter "name=$Container" --format "{{.Names}}"

    if ($containerRunning -eq $Container) {
        Write-Host "  → 容器 $Container 正在运行" -ForegroundColor Green

        $initScriptPath = Join-Path $MongoDBDir "init.js"

        # 复制脚本到容器
        docker cp $initScriptPath "${Container}:/tmp/init.js"

        # 在容器中执行
        Get-Content $initScriptPath | docker exec -i $Container mongosh

        if ($LASTEXITCODE -eq 0) {
            Write-Host "  ✓ Docker 容器中的索引初始化完成" -ForegroundColor Green
        } else {
            Write-Host "  ❌ 索引初始化失败" -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "  ❌ 容器 $Container 未运行" -ForegroundColor Red
        Write-Host "  提示: 使用 docker-compose up -d 启动容器" -ForegroundColor Yellow
        exit 1
    }
}

# 显示使用方法
function Show-Usage {
    Write-Host "`n用法: .\mongodb-init.ps1 [选项] [容器名称]" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "选项:" -ForegroundColor Cyan
    Write-Host "  local         初始化本地 MongoDB（默认）"
    Write-Host "  docker        初始化 Docker 容器中的 MongoDB"
    Write-Host "  verify        验证索引"
    Write-Host "  help          显示此帮助信息"
    Write-Host ""
    Write-Host "示例:" -ForegroundColor Cyan
    Write-Host "  .\mongodb-init.ps1 local           # 初始化本地 MongoDB"
    Write-Host "  .\mongodb-init.ps1 docker          # 初始化 Docker MongoDB"
    Write-Host "  .\mongodb-init.ps1 docker my-mongo # 指定容器名称"
    Write-Host "  .\mongodb-init.ps1 verify          # 验证索引"
}

# 主逻辑
switch ($Action) {
    "local" {
        if (Test-MongoDBConnection) {
            Initialize-Indexes
            Test-Indexes
        }
    }
    "docker" {
        Initialize-DockerIndexes -Container $ContainerName
    }
    "verify" {
        if (Test-MongoDBConnection) {
            Test-Indexes
        }
    }
    "help" {
        Show-Usage
    }
    default {
        Write-Host "❌ 未知选项: $Action" -ForegroundColor Red
        Show-Usage
        exit 1
    }
}

Write-Host "`n=========================================" -ForegroundColor Cyan
Write-Host "完成" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
