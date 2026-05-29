#!/usr/bin/env bash
set -euo pipefail

# ============================================================
# ParalivesMultiplayer installer
# Builds the mod and deploys it to the Paralives BepInEx folder
# ============================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PARALIVES_DIR="${PARALIVES_DIR:-/home/meliko/.local/share/Steam/steamapps/common/Paralives}"
PLUGINS_DIR="$PARALIVES_DIR/BepInEx/plugins"
BUILD_CONFIG="${1:-Release}"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

info()  { echo -e "${GREEN}[+]${NC} $*"; }
warn()  { echo -e "${YELLOW}[!]${NC} $*"; }
fail()  { echo -e "${RED}[-]${NC} $*"; exit 1; }

# --- Pre-flight checks ---
info "ParalivesMultiplayer installer"
info "Build config: $BUILD_CONFIG"

command -v dotnet >/dev/null 2>&1 || fail "dotnet SDK not found in PATH"

if [[ ! -d "$PARALIVES_DIR" ]]; then
    fail "Paralives directory not found at $PARALIVES_DIR"
fi

if [[ ! -d "$PLUGINS_DIR" ]]; then
    warn "Plugins directory not found, creating: $PLUGINS_DIR"
    mkdir -p "$PLUGINS_DIR"
fi

# --- Run tests ---
info "Running unit tests..."
cd "$SCRIPT_DIR"
dotnet test src/ParalivesMultiplayer.Tests/ParalivesMultiplayer.Tests.csproj \
    --verbosity quiet --no-restore 2>&1 || fail "Tests failed"
info "All tests passed"

# --- Build ---
info "Building ($BUILD_CONFIG)..."
dotnet build src/ParalivesMultiplayer/ParalivesMultiplayer.csproj \
    -c "$BUILD_CONFIG" --verbosity quiet --no-restore 2>&1 || fail "Build failed"

BUILD_DIR="$SCRIPT_DIR/src/ParalivesMultiplayer/bin/$BUILD_CONFIG/netstandard2.0"

if [[ ! -f "$BUILD_DIR/ParalivesMultiplayer.dll" ]]; then
    fail "Build output not found at $BUILD_DIR"
fi

# --- Backup existing plugin ---
if [[ -f "$PLUGINS_DIR/ParalivesMultiplayer.dll" ]]; then
    BACKUP="$PLUGINS_DIR/ParalivesMultiplayer.dll.bak.$(date +%Y%m%d%H%M%S)"
    info "Backing up existing plugin to $BACKUP"
    cp "$PLUGINS_DIR/ParalivesMultiplayer.dll" "$BACKUP"
fi

# --- Deploy ---
info "Deploying to $PLUGINS_DIR"
cp "$BUILD_DIR/ParalivesMultiplayer.dll" "$PLUGINS_DIR/"
cp "$BUILD_DIR/ParalivesMultiplayer.Protocol.dll" "$PLUGINS_DIR/"

# --- Disable BepInEx console window ---
BEPINEX_CFG="$PARALIVES_DIR/BepInEx/config/BepInEx.cfg"
mkdir -p "$PARALIVES_DIR/BepInEx/config"
if [[ ! -f "$BEPINEX_CFG" ]]; then
    cat > "$BEPINEX_CFG" << 'EOF'
[Logging.Console]
Enabled = false

[Logging]
Enabled = true
EOF
    info "Created BepInEx.cfg to disable console window"
fi

if [[ -f "$BUILD_DIR/ParalivesMultiplayer.pdb" ]]; then
    cp "$BUILD_DIR/ParalivesMultiplayer.pdb" "$PLUGINS_DIR/"
fi

if [[ -f "$BUILD_DIR/ParalivesMultiplayer.Protocol.pdb" ]]; then
    cp "$BUILD_DIR/ParalivesMultiplayer.Protocol.pdb" "$PLUGINS_DIR/"
fi

info "Deployed files:"
ls -lh "$PLUGINS_DIR"/ParalivesMultiplayer*

info "Done. Launch Paralives to load the mod."
info "Check BepInEx chainloader.log for startup status."
