@echo off

call ./build.bat
mkdir "../bin/BepInEx/plugins"
powershell Move-Item -Path "../bin/Mirage.dll" -Destination "../bin/BepInEx/plugins/Mirage.dll"
powershell Compress-Archive^
    -Force^
    -Path "../bin/BepInEx/plugins",^
          "../manifest.json",^
          "../icon.png",^
          "../../../README.md",^
          "../../../CHANGELOG.md",^
          "../../../LICENSE"^
    -DestinationPath "../bin/mirage.zip"
