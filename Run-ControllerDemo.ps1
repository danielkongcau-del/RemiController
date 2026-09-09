# Run-ControllerDemo.ps1 —— 构建并启动 Remielle 控制器演示（ZCode 工作副本）
# 用法：
#   .\Run-ControllerDemo.ps1            # 构建新 Player 并启动
#   .\Run-ControllerDemo.ps1 -NoBuild   # 跳过构建，直接启动最近一次构建的 Player
#   .\Run-ControllerDemo.ps1 -Reference # 同时提示冻结原件侧的对照 Player 路径（codex 时期产物）
param(
    [switch]$NoBuild,
    [switch]$Reference
)

$ErrorActionPreference = 'Stop'
$Unity  = 'E:\Unity\Editor\Unity 6000.3.17f1\Editor\Unity.exe'
$Project = 'E:\ZZZ\ZCode\10_Unity\Remielle_Main'
$BuildLog = 'E:\ZZZ\ZCode\90_Builds\controller-demo-build.log'
$PlayerDir = 'E:\ZZZ\ZCode\90_Builds\ControllerDependencies\Player'
$PlayerExe = Join-Path $PlayerDir 'RemielleControllerPreview.exe'
$ReferenceExe = 'E:\ZZZ\local-only\RemielleControllerDependencies\Player\RemielleControllerPreview.exe'

if (-not $NoBuild) {
    if (Test-Path $PlayerExe) {
        Remove-Item $PlayerExe -Force   # 防旧构建混淆；Player 目录其余内容由构建器重建
    }
    Write-Host "[1/2] Unity 批处理构建 Player（SourceControllerBuild.BuildPlayer，约几分钟）..." -ForegroundColor Cyan
    & $Unity -batchmode -quit -projectPath $Project -executeMethod Remielle.Controller.Editor.SourceControllerBuild.BuildPlayer -logFile $BuildLog
    if ($LASTEXITCODE -ne 0) {
        Write-Host "构建失败（退出码 $LASTEXITCODE），日志：$BuildLog" -ForegroundColor Red
        exit 1
    }
}

if (-not (Test-Path $PlayerExe)) {
    Write-Host "未找到 Player：$PlayerExe（先去掉 -NoBuild 构建一次）" -ForegroundColor Red
    exit 1
}

Write-Host "[2/2] 启动 Player（D3D11）：$PlayerExe" -ForegroundColor Cyan
if ($Reference) {
    Write-Host "对照（codex 冻结原件侧）：$ReferenceExe" -ForegroundColor Yellow
}
& $PlayerExe -force-d3d11
