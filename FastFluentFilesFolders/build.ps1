$projectName = "FastFluentFilesFolders"
$archList = @("x64", "arm64")
$innoCompiler = "C:\Program Files\Inno Setup 7\ISCC.exe"

Write-Host "Starting build for $projectName..." -ForegroundColor Cyan

foreach ($arch in $archList) {
    Write-Host "`n===== Building for architecture: $arch =====" -ForegroundColor Yellow
    
    # 1. 发布 .NET 应用
    Write-Host "Publishing .NET app for $arch..."
    $publishArgs = @(
        "publish", "-c", "Release", "-r", "win-$arch", "--self-contained", "true",
        "-p:PublishTrimmed=false", "-p:PublishSingleFile=false",
        "-p:WindowsPackageType=None", "-o", "./publish_$arch"
    )
    $publishResult = & dotnet $publishArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] dotnet publish failed for $arch" -ForegroundColor Red
        exit 1
    }
    
    # 2. 编译 Inno Setup 安装包
    Write-Host "Compiling Inno Setup installer for $arch..."
    & $innoCompiler "setup.iss" "/DDestnationArch=$arch"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] Inno Setup compilation failed for $arch" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "Completed for $arch." -ForegroundColor Green
}

Write-Host "`n===== All builds completed successfully! =====" -ForegroundColor Cyan
Write-Host "Installers are located in the 'Output' folder."