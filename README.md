# VaM Multiplayer Revamped Plugin

This project expands on vamrobot's VaM Multiplayer Plugin:
[VaM Multiplayer Plugin by vamrobot](https://github.com/vamrobot/vammultiplayer)

## Overview
I have launched a centralized Linux server in Azure and added simple user registration via a Discord bot, eliminating the need for complex VPN or tunneling setups. This makes the plugin as easy to use as the MetaChat plugin.

### New Features
- Centralized server with Azure
- Simple user registration via Discord bot
- Enhanced performance and optimization
- Improved player movement smoothness
- Faster connection and gameplay compared to MetaChat
- Simplified data syncing (only necessary data - player joints)

## Technical Details
The client plugin:
- Scans available Player atoms and their names on initialization.
- Syncs the controlled player’s joints with the server.
- Receives other controlled players' joints from the server.
- Does not check or sync scene content (uncontrolled atoms, user data).

### Simplified vs. MetaChat
- No user accounts; users are allowed by IP, registered via Discord bot for 24h.
- Voice/chat must be coordinated outside the plugin (e.g., Discord with OVR Toolkit in VR).

## Installation
The client plugin is a single `VAMMultiplayer.cs` file which can be added to any scene. All players must be using the same version of the plugin.

## Instructions

### Connecting to the Server
1. **Open Plugin settings**
   - Go to Scene Plugins
   - Add VAMMultiplayer.cs plugin if not already in the scene
   - Open plugin settings ("Open Custom UI")
2. **Player Selection:**
   - Choose a Player to control or select Spectator mode to watch.
   - Ensure the port (8888 or 9999) matches the room you want to join.
3. **Connecting:**
   - Click "Connect to Server"; it may take a few seconds.
   - Check player status in the plugin window or via the Discord bot.
   - If disconnected immediately, register your IP with the Discord bot. Registrations last 24h.
   - Server might also disconnect you if selected Player is already controlled. Select a different one and reconnect.
4. **Settings:**
   - Avoid changing Update Frequency or Updateable Targets. If lag occurs, try 25ms or 30ms Frequency.

### Tips
- If you encounter issues, click Disconnect and Connect again.
- Reload the plugin if problems persist.

### Scenes
- All players in the same room must use the same scene and atoms.
- Scene modifications on your end won’t sync with others.
- Scenes must be shared with players on Discord before playing. Otherwise, you can assume the default scene is used.

### Syncing
- Only Player joints are synced; other elements like sex toys or UI changes are local and not visible to others.

## Registration and Commands
Currently, MetaChat and VamChat Discords have the registration bot. You only need to register in one server.

To register your IP, type `/register <your IP>` in the bot channel, for example: `/register 1.2.3.4`. Use a site like https://whatismyip.com to check your public IP.

Other useful commands:
- `/state`: Prints out the current room state - who's playing and which players are taken.
- `/monitor`: Constantly prints changes about rooms - who's connecting/disconnecting, useful for notifications when someone joins or leaves.

## Troubleshooting
If you can't connect to the server, it might be due to:
- Server being down (unlikely)
- Room being full (verify with Discord bot)
- You are not registered (register with the bot again)
- The player you are trying to control is already being controlled

## Lobbies
- Two rooms are available, running in parallel on ports 8888 and 9999.
- The Discord bot shows which players are connected to which room.
- Registration works for both rooms.

## Hosting Your Own VaM Multiplayer
If you want to host everything yourself, you can recreate this setup easily:
1. Host a Linux server (e.g., free Azure tier or Oracle Linux VMs).
2. Run the TCP server on your server.
3. Run the Discord bot in the same folder to handle user registration.
4. Add the bot to your Discord.
5. Change the server IP in the Plugin .cs file to your server’s IP.
6. You now have a parallel VaM Multiplayer setup with full admin rights.

## Security and Privacy
- No user data is stored on the server apart from registered user IPs, which expire and are deleted every 24h.
- Position and rotation of joints are the only data transmitted.
- TCP connection is not protected by SSL; data is in plaintext.
- IPs not in the allowlist managed by the Discord bot are immediately disconnected.

## Additional Help
- Visit the old MetaChat Discord or the newer VamChat Discord (another project for a full-fledged MetaChat replacement).
