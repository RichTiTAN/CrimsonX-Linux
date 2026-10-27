<div align="center">
  <img src="Assets/CrimsonX.ico" width="128" height="128" alt="CrimsonX Logo">
  
  # CrimsonX

  **A GUI VPN client that fetches, tests and load-balances multiple xray configs suited for your network.**  
  *Automated config scraping, intelligent load-balancing, and seamless tunneling for Linux.*
</div>

---

##  Overview

**CrimsonX** is an advanced proxy/vpn client for Linux built with C# and Avalonia UI. It takes advantage of the powerful **Xray-core** and **sing-box** engines under the hood, wrapping them in a beautiful, highly animated, and user-friendly interface.

Unlike standard clients, CrimsonX features a **dynamic pipeline** that constantly pulls, tests, and caches the fastest configurations in the background. It automatically load-balances traffic across multiple nodes to ensure uninterrupted, high-speed connectivity.

##  Key Features

-  **Seamless Proxy and VPN integration:** Uses `Xray-core` for proxying and load-balancing, and `sing-box` for seamless system-wide VPN Mode (TUN).
-  **Seamless Swapping mechanism:** Actively checking and swapping dead configs for healthy ones.
-  **Intelligent Load Balancing:** Distribute traffic using multiple policies:
      - Round Robin: Evenly distributes connections across all active nodes.
      - Least Ping: Routes traffic through the node with the lowest latency.
      - Least Load: Dynamically selects the node with the fewest active connections.
      - Random: Picks a node at random for statistical distribution.
-  **Dynamic Config Pipeline:** Automatically scrapes, background-tests, and caches working proxy configurations from remote workers, prioritizing cached configs on the next startup.
-  **Advanced Split Tunneling:** Fine-tune your routing rules:
      - Exclude specific continents (Geo-IP based routing).
      - Enable Ad-Blocker to filter malicious and tracking requests.
      - Bypass proxy for specific apps or IPs (Direct UDP support).
-  **Apps & Games Routing:** A dedicated hub to manage TCP/UDP routing, adapter binding, and region matching for specific applications and games.
-  **LAN Sharing:** Share your VPN connection over the local network, with optional Username/Password authentication.
-  **DNS Control:** Built-in support for secure DNS-over-HTTPS (DoH) and customizable System DNS fallbacks. Name resolution follows the traffic path - proxied apps resolve through the node, direct/bypassed apps resolve directly, so a dead node cannot take down DNS for the traffic that was never proxied.

##  Screenshots

<img width="696" height="554" alt="Screenshot 2026-10-04 155541" src="https://github.com/user-attachments/assets/7b093fdb-5d37-4ee0-a75c-efadf7aa86b9" />


##  Installation

CrimsonX runs natively on Linux — no Wine, no Proton. The builds are self-contained, so the .NET runtime travels with the app and the only extra requirements are a few system libraries.

1. Go to the [Releases](https://github.com/RichTiTAN/CrimsonX-Linux/releases) page.
2. Extract it — the release contains one folder per architecture. Use the one for your machine:

   | Folder | Use it for |
   | --- | --- |
   | `x64` | 64-bit x86 — regular PCs, Steam Deck, most laptops |
   | `arm64` | 64-bit ARM — Raspberry Pi 4/5, ARM boards and servers |
   | `arm` | 32-bit ARMv7 — Raspberry Pi 2/3, older ARM boards |

3. Open a terminal inside its `CrimsonX` folder — or let the one-line installer do steps 2 and 3 for you.

There are three ways to install it.

###  One line

```bash
curl -fsSL https://raw.githubusercontent.com/RichTiTAN/CrimsonX-Linux/main/install/get-crimsonx.sh | sh
```

Detects your architecture, downloads the matching build, verifies its SHA-256, and runs the installer below. Knobs:

| Variable | Effect |
| --- | --- |
| `PREFIX` | install somewhere other than `~/.local` |
| `CRIMSONX_VERSION` | pin a release, e.g. `v1.2.3` |
| `CRIMSONX_BASE_URL` | download from a mirror |
| `KEEP=1` | leave the unpacked download behind |

If you would rather read it before running it: `curl -fsSL …/get-crimsonx.sh -o get-crimsonx.sh`, then `less get-crimsonx.sh`, then `sh get-crimsonx.sh`.

###  From the extracted folder

```bash
cd x64/CrimsonX       # arm64/CrimsonX or arm/CrimsonX on other machines
sh ./install.sh
```

The script checks the required libraries, copies the app to `~/.local/opt/CrimsonX`, installs the bundled fonts, adds a menu entry and a `crimsonx` command, and grants the single permission VPN Mode needs. It asks for your password once, in the terminal — after that VPN Mode never prompts again, which is what makes it work in Game Mode and VR sessions that have no password dialog.

Start it from your application menu, or type `crimsonx`.

###  Run it portable

```bash
cd x64/CrimsonX       # arm64/CrimsonX or arm/CrimsonX on other machines
chmod +x CrimsonX Data/xray/xray Data/sing_box/sing-box
./CrimsonX
```

Create an empty `portable.txt` next to `CrimsonX` to keep every setting inside the folder instead of your home directory. Portable mode cannot grant itself the VPN permission, so run this once if you want VPN Mode without a password prompt:

```bash
sudo setcap cap_net_admin,cap_net_raw+ep Data/sing_box/sing-box
```

Without it, VPN Mode asks for a password through polkit; Proxy Mode works either way, because it needs no extra privileges.

###  Checking an install

```bash
crimsonx --doctor
```

Prints what was detected: display backend, tray support, DNS tooling, both engines, the ICU library, and whether VPN Mode can start without a password. If the window comes up blank on a VM or an unusual GPU, add `--software-render`.

*Requires a 64-bit or ARMv7 system with X11 or XWayland. Tested on Ubuntu, Debian and SteamOS. ARMv6 boards such as the Raspberry Pi 1 and Zero are not supported.*

###  Updating

CrimsonX updates itself from the [CrimsonX-Linux releases](https://github.com/RichTiTAN/CrimsonX-Linux/releases). It reads `version.json`, downloads the archive for your architecture, unpacks it next to the running build, and a short shell script finishes the copy once the app has exited and starts it again. Your settings, configs and saved rules are left alone — they live outside the program folder.

The archive carries a new `sing-box`, which is the one file that holds the VPN permission. If the capability could not be put back, VPN Mode asks for a password once; re-running `install.sh` restores it for good, and `crimsonx --doctor` tells you which state you are in.

###  Building from source
Private worker URLs and a built-in encryption key live in a **gitignored** file (`Services/AppSecrets.cs`) so they never end up in the public repository. After cloning, create it from the template before building:

```
cp Services/AppSecrets.cs.example Services/AppSecrets.cs
```

Then open `Services/AppSecrets.cs` and fill in your own worker URLs plus a freshly generated 32-byte cache key and 16-byte IV.

Then build — one command does compile, tests and packaging:

```bat
build.cmd              # build, run the tests, package all three architectures (~6 min)
build.cmd -SkipPack    # build + tests only, the loop while you are coding (~40 s)
build.cmd -Quick       # compile only (~15 s)
```

The full run writes two things, both outside the repository:

| Output | What it is |
| --- | --- |
| `CrimsonXAvalonia\Linux\x64\CrimsonX\` (and `arm64\`, `arm\`) | one self-contained folder per architecture: the app, both engines, xray's geo data, the fonts, `install.sh` and the GPL-3.0 licence |
| `CrimsonXAvalonia\Linux\dist\CrimsonX-linux-*.tar.gz` + `.sha256` | the release assets the one-line installer and the in-app updater download |

`build.cmd` is a thin wrapper around `build.ps1`, because a stock Windows PowerShell refuses to run a `.ps1` that was copied onto the machine. It also refuses to package when the three version numbers disagree — `CrimsonX.csproj`, `Services/UpdateService.cs` and `version.json` — since a release where they differ either offers an update nobody receives, or keeps offering one that never changes.

At runtime the stores in `Data/cache/*.bin` are encrypted with a 32-byte key that is generated on first use and kept in `data.key` inside the app's data directory (`~/.local/share/crimsonx`, or the folder itself in portable mode), readable only by your user. The key in `AppSecrets.cs` is only used to read stores written by older builds and as the fallback when the key file cannot be written, so an upgrade keeps your settings and saved credentials.

##  Configuration & Usage

### 1. Connection Modes
- **VPN Mode:** Uses sing-box to create a virtual network interface (TUN), forcing all system traffic through the proxy.
- **Proxy Mode:** Sets the desktop proxy (GNOME via `gsettings`, KDE Plasma via `kwriteconfig`, otherwise an `environment.d` file) to route standard web traffic. Desktop proxies are advisory, so apps that ignore them bypass it — VPN Mode is the reliable option on Linux.
- **Clear Proxy Mode:** Disables the system proxy settings, allowing direct connection to the internet. while exposing the proxy to apps that support manual proxy configuration.

### 2. Custom Configs
If you don't want to rely on the automated scrapers, you can inject up to 2 of your own configs directly in the Settings tab: VLESS/VMESS/Shadowsocks/Socks share links, xray/sing-box outbound JSON, or full **OpenVPN** (`.ovpn`, with inline `<ca>`/`<tls-crypt>`/`<tls-auth>` blocks) and **WireGuard** configs (`.conf`, `wireguard://` share links, sing-box endpoint JSON or xray `wireguard` outbound JSON).

Just paste them in (**Ctrl+V**), press the **+** button to import a file, or pick one of your saved configs.
The same configs can also be used per app in the **Apps & Games** tab (custom proxy per rule). 

The **CUSTOM CONFIGS** section also carries a **PER-CONFIG ADAPTER** switch. With it on, each of the two boxes gets its own adapter picker and each config leaves through the adapter it was given.

### 3. Load Balance Policies
Head to the **Settings** tab to adjust how CrimsonX distributes connections. If you're downloading large files, **Least Ping** or **Round Robin** is recommended.  

### 4. Seamless Swap
The app constantly checks if your current configs are working and if they are not it will seamlessly replace them with working configs without disconnecting you from the internet.

### 5. Apps & Games Profile Routing
The **Apps & Games** tab provides pre-configured routing profiles for popular games (Valorant, CS2, League of Legends, etc.) and applications (Discord, Telegram). You can seamlessly route TCP and UDP traffic independently, bind them to specific network adapters, or force regional matchmaking without affecting your global system VPN.

### TROUBLESHOOTING:
- Check the [Documentation file](https://github.com/RichTiTAN/CrimsonX-Linux/blob/main/Documentation.md) for detailed instructions.

##  License

This program is free software: you can redistribute it and/or modify it under the terms of the **GNU General Public License (v3)** as published by the Free Software Foundation.

See the [LICENSE](LICENSE) file for more details.

---
<div align="center">
  <i>Developed with ❤️ by RichTiTAN</i>
</div>

# Credits and Donations  
Creator: [@itsTiTANVPN](https://t.me/itsTitanVPN)  

__Credits:__  
xray: https://github.com/xtls/xray-core  
Sing_Box: https://github.com/SagerNet/sing-box  
Avalonia: https://github.com/avaloniaui

__Donations:__ 
- If you want to support the project or me you can do so by sending your desired amount to one of these wallet addresses:

USDT (BEP20)  
`0xFc1d71C22DC2604f6C13Ca540ed842535cbE6d75`

USDT (TRC20)  
`TNMaNGDMG7BzbjkXeiguFWzDHZ4hCUU9R8`

BITCOIN  
`bc1quzdzuhrfse520r0wkqgkvsl7nv354r8sj5u9f9`

TON  
`UQCB3bvk4nXMiokNWprb7CwPjurU8WOUfVSgAVXMMke0CxdW`
