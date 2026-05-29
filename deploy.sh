#!/bin/bash
set -e

GAME_PLUGINS="/home/$USER/.local/share/Steam/steamapps/common/Paralives/BepInEx/plugins"
PROTOCOL_DIR="/home/$USER/Paramulti/src/ParalivesMultiplayer.Protocol"
MAIN_DIR="/home/$USER/Paramulti/src/ParalivesMultiplayer"

echo "=== Building ParalivesMultiplayer ==="

echo "Building Protocol..."
cd "$PROTOCOL_DIR"
dotnet build -c Release

echo "Building Main..."
cd "$MAIN_DIR"
dotnet build -c Release

echo "=== Deploying to BepInEx ==="

cp "$PROTOCOL_DIR/bin/Release/netstandard2.0/ParalivesMultiplayer.Protocol.dll" "$GAME_PLUGINS/"
cp "$MAIN_DIR/bin/Release/netstandard2.0/ParalivesMultiplayer.dll" "$GAME_PLUGINS/"

echo "Done! Deployed:"
ls -la "$GAME_PLUGINS/ParalivesMultiplayer"*.dll
