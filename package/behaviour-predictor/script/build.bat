@echo off

dotnet build ../embedding/Embedding.fsproj
dotnet build ../src/Predictor.fsproj
rmdir /s /q "..\bin"
mkdir "..\bin"
move "..\src\bin\Debug\netstandard2.1\behaviour-predictor.dll" "..\bin\behaviour-predictor.dll"
move "..\embedding\bin\Debug\netstandard2.1\Embedding.dll" "..\bin\TextEmbedding.dll"
move "..\embedding\bin\Debug\netstandard2.1\miragelib.dll" "..\bin\miragelib.dll"