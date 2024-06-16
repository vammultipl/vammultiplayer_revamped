# VaM Multiplayer Revamped Plugin

This project expands on vamrobot's VaM Multiplayer Plugin:
[VaM Multiplayer Plugin by vamrobot](https://github.com/vamrobot/vammultiplayer)

## Overview
I have launched a centralized Linux server in Azure and added simple user registration via a Discord bot, eliminating the need for complex VPN or tunneling setups. This makes the plugin as easy to use as the MetaChat plugin.

### New Features
- Centralized server
- Simple user registration and lobby monitoring via Discord bot
- Enhanced performance and optimization
- Improved player movement smoothness
- Faster connection and gameplay compared to MetaChat
- Simplified data syncing (only necessary data - player joints)
- Spectator mode

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
The client plugin is a single `VAMMultiplayer.cs` file which can be added to any scene. All players must be using the latest version of the plugin.

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
   - Avoid changing Update Frequency or Updateable Targets. If FPS drops severely after connecting, try switching to 15Hz Frequency.

### Tips
- If you encounter issues, click Disconnect and Connect again.
- Reload the plugin if problems persist.
- Make sure to use the latest available version of the client plugin

### Scenes
- All players in the same room must use the same scene with pre-defined atoms.
- Scene modifications (changing looks, clothes, etc) on your end won’t sync with others.
- The scene must be shared with other players on Discord first so that everyone sees the same things and has the same atoms. Otherwise, you can assume the default scene is used. Remember to download all dependencies for the scene.
- Plugins also don't sync if you modify them, except for plugins like AutoThruster (as it moves the atoms)

### Syncing
- Only Player joints are synced; other elements like sex toys or UI changes are local and interaction with them is not visible to others.

## Registration and Commands
Currently, MetaChat and VamChat Discords have the registration bot. You only need to register in one server.

To register your IP, type `/register <your IP>` in a DM to the bot, for example: `/register 1.2.3.4`. Use a site like https://whatismyip.com to check your public IP.
The registration is active for the next 24h.

Other useful commands (can be used in the bot channel):
- `/state`: Prints out the current room state - who's playing and which players are taken.
- `/monitor`: Constantly prints changes about rooms - who's connecting/disconnecting, useful for notifications when someone joins or leaves.

## Troubleshooting
If you can't connect to the server, it might be due to:
- Server being down (unlikely)
- Room being full (verify with Discord bot)
- You are not registered (register with the bot again)
- The player you are trying to control is already being controlled
- Not using latest version of the client plugin

## Lobbies
- Two rooms are available, running in parallel on ports 8888 and 9999, max 4 players per room.
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
- No user data is stored on the server apart from registered user IPs and Discord usernames, which expire and are deleted every 24h.
- Discord bot only displays Discord usernames of connected players when prompted with /state or /monitor commands.
- Position and rotation of joints are the only data transmitted.
- TCP connection is not protected by SSL; data is in plaintext.
- IPs not in the allowlist managed by the Discord bot are immediately disconnected.

## Known Issues
- "Player connected/disconnected" messages in plugin window are wonky and not always correct. Discord bot statuses are always correct, updated every 20s.
- If you add more plugins to the scene or atoms, FPS might drop a lot because the multiplayer plugin blocks on TCP connection in FixedUpdate() which may degrade performance of other scripts. This performance degradation might be even worse if you are far from the server (currently EU based). This issue will be fixed in another release.

## TL;DR I came from Metachat - how do I use this?
MetaChat had web registration, lobbies with visible scenes, an in-game menu to synchronize other people's looks, and also a chat. This has none of that.
Instead:
- registration is via Discord bot: `/register <your_IP>`
- to check who's playing - ask Discord bot or register for notifications
- scene is pre-shared and all players in a lobby should load the same one
- ONLY players joints are synced - plugins, looks, clothing, toys DO NOT SYNC. Others won't see changes.
- On the up side, no one can make your VaM freeze for 3 minutes by loading a complicated look
- to play: open plugin settings, select your Atom, click Connect :)
- chat is via Discord :)

## Additional Help
- Visit the old MetaChat Discord or the newer VamChat Discord (another project for a full-fledged MetaChat replacement).

## Tips
https://ko-fi.com/vammultipl
Thanks!

## Credits
Original plugin:
[VaM Multiplayer Plugin by vamrobot](https://github.com/vamrobot/vammultiplayer)

Sample scene:
```
AcidBubbles.ColliderEditor.36                 By: AcidBubbles          License: CC BY-SA        Link: https://github.com/acidbubbles/vam-collider-editor
AcidBubbles.Embody.58                         By: AcidBubbles          License: CC BY-SA        Link: https://github.com/acidbubbles/vam-embody
AcidBubbles.Embody.60                         By: AcidBubbles          License: CC BY-SA        Link: https://github.com/acidbubbles/vam-embody
AcidBubbles.Timeline.283                      By: AcidBubbles          License: CC BY-SA        Link: https://github.com/acidbubbles/vam-timeline
ascorad.asco_Expressions.latest               By: ascorad              License: CC BY           Link: https://www.patreon.com/ascorad
Blaspheratus.Vr_sex_Cowgirl.latest            By: Blaspheratus         License: CC BY          
DasBoot.Futa_EyeShadow_and_Liner.latest       By: DasBoot              License: CC BY          
everlaster.FloatParamRandomizerEE.6           By: everlaster           License: CC BY-SA        Link: https://patreon.com/everlaster
everlaster.Lumination.1                       By: everlaster           License: CC BY-SA        Link: https://github.com/everlasterVR/Lumination
hazmhox.vammoan.22                            By: hazmhox              License: CC BY-SA       
Hunting-Succubus.AutomaticBodySmoother.7      By: Hunting-Succubus     License: CC BY-NC-ND     Link: https://www.patreon.com/HunTingSuccuBus
Hunting-Succubus.Enhanced_Eyes.latest         By: Hunting-Succubus     License: CC BY-NC        Link: https://www.patreon.com/HunTingSuccuBus
incuboy.Default_Male.latest                   By: incuboy              License: CC BY          
KyraAngel.Kyra_Tzimisce.latest                By: KyraAngel            License: CC BY          
MacGruber.Life.10                             By: MacGruber            License: CC BY-SA        Link: https://hub.virtamate.com/resources/life.165/
MacGruber.Life.13                             By: MacGruber            License: CC BY-SA        Link: https://hub.virtamate.com/resources/life.165/
Redeyes.GiveMeFPS.25                          By: Redeyes              License: CC BY          
Riddler.Eyes.latest                           By: Riddler              License: CC BY          
Roac.Daisy.latest                             By: Roac                 License: CC BY           Link: patreon.com/Roac
SupaRioAmateur.Basic_Earrings.latest          By: SupaRioAmateur       License: CC BY-NC-SA     Link: https://www.patreon.com/suparioamateur
SupaRioAmateur.Layered_Nip_8K.latest          By: SupaRioAmateur       License: CC BY-NC-SA     Link: https://www.patreon.com/SupaRioAmateur
SupaRioAmateur.Nails_as_Cloth.latest          By: SupaRioAmateur       License: CC BY-NC-SA     Link: http://patreon.com/SupaRioAmateur
ToumeiHitsuji.DiviningRod.4                   By: ToumeiHitsuji        License: CC BY-SA       
UrukYay.SupplementaryColliders.2              By: UrukYay              License: CC BY          
VAMJFD.FullMouthTexturePack.latest            By: VAMJFD               License: CC BY           Link: https://www.patreon.com/vamjfd
Vinput.AutoThruster.17                        By: Vinput               License: CC BY-SA       
WeebU.Ange_Futa.1                             By: WeebU                License: CC BY-NC        Link: https://www.patreon.com/WeebUVR
WeebU.Mira_Futa_Texture.latest                By: WeebU                License: CC BY-NC       
WeebU.My_morphs.latest                        By: WeebU                License: FC              Link: https://www.patreon.com/WeebUVR
WeebU.Sweat_gloss_maps.latest                 By: WeebU                License: CC BY-NC        Link: https://www.patreon.com/WeebUVR
WeebU.W-Open_Heels1.latest                    By: WeebU                License: CC BY-NC        Link: https://www.patreon.com/WeebUVR
```
