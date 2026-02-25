#!/usr/bin/env nix-shell
#!nix-shell -i bash -p umu-launcher
set -euo pipefail

# Download dotnet SDK if not present
if [ ! -f dotnet-sdk-9.0.310-win-x64.exe ]; then
	wget https://builds.dotnet.microsoft.com/dotnet/Sdk/9.0.310/dotnet-sdk-9.0.310-win-x64.exe
fi

# Download Starcraft.zip if not present
if [ ! -f Starcraft.zip ]; then
	wget -O Starcraft.zip "https://snow0-my.sharepoint.com/:u:/g/personal/alex_mickelson_snow_edu1/IQDss8Kj45-XRrhHa0wSm9OdAcdmTZTAzXHzVoUBATpH4nM?e=nPYQfx&download=1"
fi

# Unzip Starcraft.zip to Starcraft directory if not already unzipped
if [ ! -d Starcraft ]; then
	unzip Starcraft.zip
fi

umu-run ./dotnet-sdk-9.0.310-win-x64.exe

# run with:
# nix-shell -p umu-launcher --run 'umu-run dotnet watch --project Web'
# killall dotnet.exe