@echo off
REM Lance le Dynamic Island (compile au besoin)
cd /d "%~dp0"
dotnet run -c Release --project "DynamicIsland.csproj"
