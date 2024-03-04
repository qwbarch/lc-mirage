@echo off

dotnet build ../src/Utilities.fsproj
rmdir /s /q "..\bin"
mkdir "..\bin"
move "..\src\bin\Debug\netstandard2.1\mirage-utilities.dll" "..\bin\mirage-utilities.dll"