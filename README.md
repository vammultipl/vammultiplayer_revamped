# VaM Multiplayer Revamped Plugin

This project expands on vamrobot's VaM Multiplayer Plugin:
[VaM Multiplayer Plugin by vamrobot](https://github.com/vamrobot/vammultiplayer)

## Overview
I have launched a centralized Linux server in Azure and added simple user registration via a Discord bot, eliminating the need for complex VPN or tunneling setups. This makes the plugin as easy to use as the MetaChat plugin.

[**TL;DR below**](https://github.com/vammultipl/vammultiplayer_revamped?tab=readme-ov-file#tldr-i-came-from-metachat---how-do-i-use-this)

### New Features
- Centralized server
- Simple user registration by IP and lobby monitoring via Discord bot
- Enhanced performance and optimization
- Improved player movement smoothness
- Faster connection and gameplay compared to MetaChat
- Simplified data syncing (only necessary data - player joints and clothing on/off)
- Spectator mode
- Clothing sync support (on/off status, no .var syncing yet)

## Technical Details
The client plugin:
- Scans available Player atoms and their names on initialization.
- Syncs the controlled player’s joints with the server.
- Receives other controlled players' joints from the server.
- Does not check or sync scene content (uncontrolled atoms, user data).

## Installation
The client plugin is a single [VAMMultiplayer.cs](https://raw.githubusercontent.com/vammultipl/vammultiplayer_revamped/main/VAMMultiplayer.cs) file which can be added to any scene. All players must be using the latest version of the plugin.

## Instructions
Copy the content of [this file](https://raw.githubusercontent.com/vammultipl/vammultiplayer_revamped/main/VAMMultiplayer.cs) and save it as VAMMultiplayer.cs somewhere in VaM folder. You will load it manually as a Scene plugin.

Open the scene you want to play. It can be any scene with Person atoms in it. Send the same scene .var file to other players.

### Connecting to the Server
1. **Open Plugin settings**
   - Go to Scene Plugins
   - Add [VAMMultiplayer.cs](https://raw.githubusercontent.com/vammultipl/vammultiplayer_revamped/main/VAMMultiplayer.cs) plugin if not already in the scene
   - Open plugin settings ("Open Custom UI")
2. **Player Selection:**
   - Choose a Player to control or select Spectator mode to watch.
   - Ensure the port (8888 or 9999) matches the room you want to join.
3. **Connecting:**
   - Click "Connect to Server"; it may take a few seconds.
   - Check player status in the plugin window or via the Discord bot. You'll see when someone joins the room.
   - If disconnected immediately, register your IP with the Discord bot. Registrations last a week.
   - Server might also disconnect you if selected Player is already controlled. Select a different one and reconnect.
4. **Settings:**
   - Avoid changing Update Frequency or Updateable Targets.

### Tips
- Read instructions in plugin window
- If you encounter issues, click Disconnect and Connect again.
- Reload the plugin if problems persist.
- Make sure to use the latest available version of the client plugin.
- Click an option in GiveMeFPS plugin (added to scene) for more FPS.
- If you encounter desync of other players (crooked limbs, whatever), apply some basic pose preset on them. It should cause a resync.

### Scenes
- **All players in the same room must use the same scene with pre-defined atoms.**
- Scene modifications (changing looks, adding atoms etc) on your end won’t sync with others. Only clothes on/off status is synced.
- The scene must be shared with other players on Discord first so that everyone sees the same things and has the same atoms. Otherwise, you can assume the default scene is used. Remember to download all dependencies for the scene.
- Plugins also don't sync if you modify them, except for plugins like AutoThruster (as it moves the atoms)

### Syncing
- Only Player joints and clothes status are synced; other elements like sex toys or UI changes are local and interaction with them is not visible to others.

## Registration and Commands
Currently [this Discord server](https://discord.gg/45dxAsGG) has the registration bot. The community also hangs out in MetaChat and VamChat (MetaChat successor) servers.

To register your IP, type `/register <your IP>` in a DM to the bot, for example: `/register 1.2.3.4`. Use a site like https://whatismyip.com to check your public IP.
The registration is active for 1 week, then you have to re-register.

Other useful commands (can be used in the bot channel):
- `/state`: Prints out the current room state - who's playing and which players are taken.
- `/monitor`: Constantly prints changes about rooms - who's connecting/disconnecting, useful for notifications when someone joins or leaves.

## Troubleshooting
If you can't connect to the server, it might be due to:
- The player you are trying to control is already being controlled
- You are not registered (register with the bot again)
- Server being down (unlikely)
- Room being full (verify with Discord bot)
- Not using latest version of the client plugin
- If all else fails - click Reload to reload plugin, reconnect

## Lobbies
- Two rooms are available, running in parallel on ports 8888 and 9999, max 6 players per room.
- The Discord bot shows which players are connected to which room.
- Registration works for both rooms.

## Security and Privacy
- No user data is stored on the server apart from registered user IPs and Discord usernames, which expire periodically.
- Discord bot only displays Discord usernames of connected players when prompted with /state or /monitor commands.
- TCP connection is not protected by SSL; data is in plaintext.
- IPs not in the allowlist managed by the Discord bot are immediately disconnected.

## Known Issues
- Cannot change clothing of another look if it is being controlled by a player

## TL;DR I came from Metachat - how do I use this?
MetaChat had web registration, lobbies with visible scenes, an in-game menu to synchronize other people's looks, and also a chat. This has none of that.
Instead:
- join [this Discord server](https://discord.gg/45dxAsGG)
- registration is via Discord bot: `/register <your_IP>`
- to check who's playing - ask Discord bot or register for notifications
- scene is pre-shared and all players in a lobby should load the same one
- ONLY player atoms joints and clothing on/off state are synced - plugins, looks, toys DO NOT SYNC. Others won't see changes.
- On the up side, no one can make your VaM freeze for 3 minutes by loading a complicated look
- to play: open plugin settings, select your Atom, click Connect :)
- chat is via Discord :)

## Additional Help
- Visit the [this Discord server](https://discord.gg/45dxAsGG) where we have a small community of players and scene makers. We also hang out in MetaChat and VamChat servers.
- Recall the great guide made by VamMoose on the old Metachat plugin:

Read section 4.5:

[VamMoose Metachat Guide](https://hub.virtamate.com/resources/metachat-toolkit.26478)

For best results - use full body tracking with Embody plugin. Otherwise, put on MetaChatReady pose from old MetaChat toolkit on your atom and move around like section 4.5 says.

## Hosting Your Own VaM Multiplayer (Optional)
If you want to host everything yourself, you can recreate the whole VaM MP setup easily:
1. Host a Linux server (e.g., free Azure tier or Oracle Linux VMs, or locally on your PC with port 8888 exposed).
2. Run the Python TCP server on your server.
3. Run the Go Discord bot in the same folder to handle user registration.
4. Add the bot to your Discord.
5. Put your Discord bot API token in token.txt
6. Put name of the channel where the bot is in `bot_discord_channel_name.txt`
6. Change the server IP in the Plugin .cs file to your server’s IP (servers.Add line).
7. You now have a VaM Multiplayer setup with full admin rights.
Consult `start_server.sh` script on how to run the servers.

## Donate
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
