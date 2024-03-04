@echo off

dotnet build ../src/Whisper.fsproj
rmdir /s /q "..\bin"
mkdir "..\bin"
move "..\src\bin\Debug\netstandard2.1\openai-whisper.dll" "..\bin\openai-whisper.dll"
move "..\src\bin\Debug\netstandard2.1\whisper-cpp.dll" "..\bin\whisper-cpp.dll"
move "..\src\bin\Debug\netstandard2.1\cublas64_12.dll" "..\bin\cublas64_12.dll"