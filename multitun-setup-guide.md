**MoleTun Setup — Paralives Multiplayer**

MoleTun creates a P2P virtual LAN so you can play online like LAN. No port forwarding, no exposed IP. All traffic encrypted with ChaCha20-Poly1305. Download: https://moletun.com/en/download

**Install**
• Win/Mac: run installer from link above
• Linux (Debian/Ubuntu): `sudo apt install ./MoleTun_*.deb` then `sudo setcap cap_net_admin=ep /usr/bin/mole`
• Linux (Fedora): `sudo dnf install ./MoleTun_*.rpm` then same setcap command
• Portable Linux: `tar -xzf MoleTun_*.tar.gz && cd MoleTun && ./mole`

**HOST**
1. Open MoleTun → **Create Room** → copy room code
2. Send room code to your friend
3. Note your virtual IP shown in MoleTun (e.g. `10.x.x.x`)
4. Launch Paralives → press `F5` (hosts on port 7890)

**CLIENT**
1. Open MoleTun → **Join Room** → paste room code
2. Note the HOST's virtual IP from the room
3. Edit `BepInEx/config/ParalivesMultiplayer.cfg`:
```
ConnectAddress = 10.x.x.x
```
(replace with actual host IP)
4. Launch Paralives → press `F6` (connects to host)

**Verify** — Host log: `[Cmd] Started HOST on port 7890` | Client log: `[Net] Connected to host.`

`F5` = host | `F6` = connect | `F7` = disconnect

**Troubleshooting**
• Room won't connect: same MoleTun version? No other VPN running? Try mobile hotspot on strict networks
• Mod can't connect: check `ConnectAddress` matches host's MoleTun IP exactly. Host must press F5 before client presses F6
• Lag: you may be relayed (strict NAT). Try a different network
• Linux firewall: `sudo ufw allow from 10.0.0.0/8`

**MoleTun Plans** — Free: 3 players, 24h sessions | Pro ($2/mo): 8 players, 7 days | Elite ($7/mo): unlimited