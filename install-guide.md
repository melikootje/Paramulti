# Install ParalivesMultiplayer Mod — Windows

Repo: https://github.com/melikootje/Paramulti

## 1. Install BepInEx

- Download **BepInEx Unity x86_64** from [BepInEx Releases](https://github.com/BepInEx/BepInEx/releases) (latest `.zip`)
- Extract the entire contents into your Paralives game folder (where `Paralives.exe` lives)
- You should now see a `BepInEx/` folder there

## 2. Get the mod DLL

Go to [Releases](https://github.com/melikootje/Paramulti/releases) → download the latest `ParalivesMultiplayer.zip` → extract it.

## 3. Install the mod

Copy **all** `.dll` files from the archive into:

```
Paralives\BepInEx\plugins\
```

## 4. Configure (client only)

Edit `BepInEx\config\com.paralives.multiplayer.cfg`:

```ini
[Network]
ListenPort=7890
ConnectAddress=127.0.0.1
```

Change `ConnectAddress` to your host IP (or MoleTun virtual IP for online play).

## 5. Launch Paralives

Start the game — the mod loads automatically.

**F5** = host | **F6** = connect | **F7** = disconnect

Check `BepInEx\LogOutput.log` if something goes wrong.
