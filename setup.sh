#!/usr/bin/env bash
set -euo pipefail

# ============================================================
# Paralives Multiplayer Mod — Build & Setup Script
# Builds the mod, installs BepInEx, and copies the DLL
# into your Paralives game folder for single-machine testing.
# Automatically detects Proton vs native Linux.
# ============================================================

PROJECT_DIR="$(cd "$(dirname "$0")" && pwd)"
STEAM_APPS="$HOME/.local/share/Steam/steamapps"
PARALIVES_NATIVE="$STEAM_APPS/common/Paralives"
PARALIVES_APP_ID="1118520"

BEPINEX_VER="5.4.23.5"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

DOTNET="${DOTNET:-$(which dotnet 2>/dev/null || echo "$HOME/.dotnet/dotnet")}"
if [[ ! -x "$DOTNET" ]]; then
    echo -e "${RED}[ERROR]${NC} dotnet not found. Install .NET SDK or set DOTNET env variable."
    exit 1
fi

info()  { echo -e "${GREEN}[INFO]${NC}  $*"; }
warn()  { echo -e "${YELLOW}[WARN]${NC}  $*"; }
error() { echo -e "${RED}[ERROR]${NC} $*"; }

# --- Detect Proton vs native ---
info "Detecting game runtime..."
USES_PROTON=false

if [[ -d "$STEAM_APPS/compatdata/$PARALIVES_APP_ID/pfx" ]]; then
    USES_PROTON=true
    info "Paralives runs through Proton — using Windows BepInEx."
else
    info "Paralives runs natively — using Linux BepInEx."
fi

if [[ ! -d "$PARALIVES_NATIVE" ]]; then
    error "Game directory not found at $PARALIVES_NATIVE"
    exit 1
fi

GAME_DIR="$PARALIVES_NATIVE"
BEPINEX_DIR="$GAME_DIR/BepInEx"
PLUGINS_DIR="$BEPINEX_DIR/plugins"

# --- Step 1: Build the mod ---
info "Building ParalivesMultiplayer..."
cd "$PROJECT_DIR"
$DOTNET build src/ParalivesMultiplayer/ParalivesMultiplayer.csproj -c Release

# Find the built DLL
DLL_PATH=""
for candidate in \
    "$PROJECT_DIR/src/ParalivesMultiplayer/bin/Release/netstandard2.0/ParalivesMultiplayer.dll" \
    "$PROJECT_DIR/src/ParalivesMultiplayer/bin/Release/ParalivesMultiplayer.dll"; do
    if [[ -f "$candidate" ]]; then
        DLL_PATH="$candidate"
        break
    fi
done

if [[ -z "$DLL_PATH" ]]; then
    error "Build output .dll not found. Check build logs above."
    exit 1
fi
info "Built DLL: $DLL_PATH"

# --- Step 2: Install BepInEx (if not already present) ---
if [[ -d "$PLUGINS_DIR" && -f "$BEPINEX_DIR/core/BepInEx.dll" ]]; then
    info "BepInEx already installed at $BEPINEX_DIR — skipping download."
else
    if $USES_PROTON; then
        BEPINEX_PLATFORM="win_x64"
    else
        BEPINEX_PLATFORM="linux_x64"
    fi

    BEPINEX_FILE="BepInEx_${BEPINEX_PLATFORM}_${BEPINEX_VER}.zip"
    BEPINEX_URL="https://github.com/BepInEx/BepInEx/releases/download/v${BEPINEX_VER}/${BEPINEX_FILE}"

    info "Installing BepInEx ${BEPINEX_VER} (${BEPINEX_PLATFORM})..."
    TMPDIR_SETUP=$(mktemp -d)
    ZIP_FILE="$TMPDIR_SETUP/bepinex.zip"

    info "Downloading from $BEPINEX_URL"
    curl -fsSL -o "$ZIP_FILE" "$BEPINEX_URL"

    info "Extracting to game directory..."
    unzip -qo "$ZIP_FILE" -d "$GAME_DIR"

    # Ensure required directories exist
    mkdir -p "$PLUGINS_DIR"
    mkdir -p "$BEPINEX_DIR/cache"

    rm -rf "$TMPDIR_SETUP"
    info "BepInEx installed successfully."
fi

# --- Step 3: Copy the mod DLL ---
info "Copying ParalivesMultiplayer.dll to $PLUGINS_DIR..."
cp "$DLL_PATH" "$PLUGINS_DIR/"

# --- Done ---
echo ""
info "=========================================="
info "Setup complete! Your mod is installed at:"
info "  $PLUGINS_DIR/ParalivesMultiplayer.dll"
info "=========================================="
echo ""
info "To test on a single machine:"
echo ""
info "  1. Launch Paralives from Steam (Instance 1)"
info "     Press F5 to start as HOST"
echo ""
info "  2. Launch Paralives again from Steam (Instance 2)"
info "     Press F6 to connect as CLIENT"
echo ""
info "  3. Press F7 in either instance to disconnect"
echo ""
warn "Check logs at: $BEPINEX_DIR/LogOutput.log"
warn "Config at:     $BEPINEX_DIR/config/com.paralives.multiplayer.cfg"
echo ""
