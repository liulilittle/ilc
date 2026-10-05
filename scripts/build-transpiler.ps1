param([string]$Config = "Debug")
$ErrorActionPreference = "Stop"
dotnet build "src/il2cppc/il2cppc.csproj" -c $Config
if ($LASTEXITCODE -ne 0) { throw "transpiler build failed" }
Write-Host "transpiler built OK"
