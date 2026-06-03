#!/bin/bash
set -e

# Default: deploy to the current user's Steam install
TARGET_USER="${1:-$USER}"
WORK_TARGET="${2:-}"

GAME_PLUGINS="/home/$TARGET_USER/.local/share/Steam/steamapps/common/Paralives/BepInEx/plugins"
PROTOCOL_DIR="/home/$USER/Paramulti/src/ParalivesMultiplayer.Protocol"
MAIN_DIR="/home/$USER/Paramulti/src/ParalivesMultiplayer"

# work user deploys require sudo (luna can't write to /home/work)
SUDO=""
if [ "$TARGET_USER" != "$USER" ]; then
    if command -v sudo >/dev/null 2>&1; then
        SUDO="sudo"
    else
        echo "ERROR: target user '$TARGET_USER' != current user '$USER' and sudo is not available."
        echo "Run this script as the target user, or install sudo."
        exit 1
    fi
fi

echo "=== Building ParalivesMultiplayer ==="

echo "Building Protocol..."
cd "$PROTOCOL_DIR"
dotnet build -c Release

echo "Building Main..."
cd "$MAIN_DIR"
dotnet build -c Release

echo "=== Deploying to $TARGET_USER's BepInEx ==="
$SUDO cp "$PROTOCOL_DIR/bin/Release/netstandard2.0/ParalivesMultiplayer.Protocol.dll" "$GAME_PLUGINS/"
$SUDO cp "$MAIN_DIR/bin/Release/netstandard2.0/ParalivesMultiplayer.dll" "$GAME_PLUGINS/"
$SUDO cp "$MAIN_DIR/bin/Release/netstandard2.0/LiteNetLib.dll" "$GAME_PLUGINS/"

echo "Done! Deployed to $GAME_PLUGINS:"
$SUDO ls -la "$GAME_PLUGINS/ParalivesMultiplayer"*.dll "$GAME_PLUGINS/LiteNetLib.dll" 2>&1

# Optional second target (e.g., "deploy.sh luna work" deploys to both)
if [ -n "$WORK_TARGET" ] && [ "$WORK_TARGET" != "$TARGET_USER" ]; then
    echo ""
    echo "=== Deploying to $WORK_TARGET's BepInEx ==="
    WORK_PLUGINS="/home/$WORK_TARGET/.local/share/Steam/steamapps/common/Paralives/BepInEx/plugins"
    if [ -d "/home/$WORK_TARGET" ]; then
        $SUDO cp "$PROTOCOL_DIR/bin/Release/netstandard2.0/ParalivesMultiplayer.Protocol.dll" "$WORK_PLUGINS/"
        $SUDO cp "$MAIN_DIR/bin/Release/netstandard2.0/ParalivesMultiplayer.dll" "$WORK_PLUGINS/"
        $SUDO cp "$MAIN_DIR/bin/Release/netstandard2.0/LiteNetLib.dll" "$WORK_PLUGINS/"
        echo "Done! Deployed to $WORK_PLUGINS:"
        $SUDO ls -la "$WORK_PLUGINS/ParalivesMultiplayer"*.dll "$WORK_PLUGINS/LiteNetLib.dll" 2>&1
    else
        echo "Skipping $WORK_TARGET: /home/$WORK_TARGET not accessible"
    fi
fi
