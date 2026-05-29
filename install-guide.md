# Install ParalivesMultiplayer Mod — Windows

## Quick Install (Automated)

1. Clone or download this repository to any folder on your machine.
2. Run `ParamultiInstaller.exe` (pre-built, see [Releases](https://github.com/melikootje/Paramulti/releases) or build below).
3. The installer will:
   - Detect or prompt for your Paralives game directory
   - Install .NET SDK if missing
   - Build the mod from source
   - Install BepInEx automatically
   - Deploy the DLLs into `BepInEx/plugins/`

## Manual Install

### 1. Install BepInEx

- Download **BepInEx Unity x86_64** from [BepInEx Releases](https://github.com/BepInEx/BepInEx/releases) (latest `.zip`)
- Extract the entire contents into your Paralives game folder (where `Paralives.exe` lives)
- You should now see a `BepInEx/` folder there

### 2. Build the mod

Requires [.NET SDK 6.0+](https://dotnet.microsoft.com/download/dotnet/6.0).

```bash
dotnet build src/ParalivesMultiplayer/ParalivesMultiplayer.csproj -c Release
```

Output: `src/ParalivesMultiplayer/bin/Release/netstandard2.0/ParalivesMultiplayer.dll`

### 3. Install the mod

Copy all `.dll` files from the build output into:

```
Paralives\BepInEx\plugins\
```

### 4. Configure (client only)

Edit `BepInEx\config\com.paralives.multiplayer.cfg`:

```ini
[Network]
ListenPort=7890
ConnectAddress=127.0.0.1
```

Change `ConnectAddress` to your host IP (or MoleTun virtual IP for online play).

### 5. Launch Paralives

Start the game — the mod loads automatically.

**F5** = host | **F6** = connect | **F7** = disconnect

Check `BepInEx\LogOutput.log` if something goes wrong.

## Building the Installer (Developer)

To build a self-contained Windows installer executable:

```bash
dotnet publish src/ParalivesMultiplayer.Installer/ParalivesMultiplayer.Installer.csproj ^
    -c Release -r win-x64 --self-contained true -o ./installer-output
```

Output: `installer-output/ParamultiInstaller.exe` (standalone, no .NET required to run).
