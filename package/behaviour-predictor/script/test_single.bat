@echo off
rem Check if an argument is provided
if "%1"=="" (
    echo Usage: %0 [Filter]
    exit /b 1
)

rem Run the dotnet test command with the provided filter argument
dotnet test ../test/Test.fsproj --filter %1
