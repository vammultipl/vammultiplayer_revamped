//// VAM Multiplayer
// vamrobotics (7-28-2021)
// https://github.com/vamrobot/vammultiplayer
// vammultipl (06-15-2024)
// https://github.com/vammultipl/vammultiplayer_revamped

using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using SimpleJSON;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Linq;
using System.Diagnostics;

namespace vamrobotics
{
    class VAMMultiplayer : MVRScript
    {
        private Socket client;
        // timestamps of when we sent requests
        // used also to track requests in flight
        // receive path pops from this queue
        private Queue<long> sendTimes = new Queue<long>();
        // TODO rework latency matching with stopwatch and milliseconds, not Update() ticks
        private long lastSentTimestamp = 0;
        private long ticksSinceLastSend = 0;
        private long sendIntervalTicks = 6; // Initial interval in ticks
        private long latencyTicks = 10; // Initial latency guess
        private long fixedUpdateCounter = 0;

        // debug statistics
        private double averageLatency = 30.0;
        private long summedLatencies = 0;
        private long latenciesCount = 0;
        private double averageInFlightRequests = 1.0;
        private long summedInFlightRequests = 0;
        private long inFlightRequestsCount = 0;
        private long timeoutsSinceLastReceive = 0;
        private long successfulReceivesCount = 0;
        private long totalTimeouts = 0;
        private double averageReceiveTimeouts = 1.0;
        private long partialMessages = 0;
        private long sendTimeouts = 0;
        private long ioPendingExceptions = 0;

        private byte[] receiveBuffer = new byte[65535];
        private StringBuilder responseBuilder = new StringBuilder();

        // Network background thread
        private Thread thread;
        // Latest request message containing player quaternions
        private StringBuilder requestGlobal = new StringBuilder();
        private Mutex requestMtx; // locks requestGlobal
        // Latest received response message containing other players quaternions
        private string responseGlobal;
        private Mutex responseMtx; // locks responseGlobal
        private bool isLooping = true;
        private int networkLoopSleepValue = 5;

        protected JSONStorableStringChooser playerChooser;
        protected JSONStorableStringChooser serverChooser;
        protected JSONStorableStringChooser portChooser;
        protected JSONStorableStringChooser protocolChooser;
        protected JSONStorableStringChooser updateFrequencyChooser;
        protected JSONStorableBool spectatorModeBool;
        protected JSONStorableBool positionsBool;
        protected JSONStorableBool rotationsBool;
        protected JSONStorableBool controlBool;
        protected JSONStorableBool hipControlBool;
        protected JSONStorableBool pelvisControlBool;
        protected JSONStorableBool chestControlBool;
        protected JSONStorableBool headControlBool;
        protected JSONStorableBool rHandControlBool;
        protected JSONStorableBool lHandControlBool;
        protected JSONStorableBool rFootControlBool;
        protected JSONStorableBool lFootControlBool;
        protected JSONStorableBool neckControlBool;
        protected JSONStorableBool eyeTargetControlBool;
        protected JSONStorableBool rNippleControlBool;
        protected JSONStorableBool lNippleControlBool;
        protected JSONStorableBool testesControlBool;
        protected JSONStorableBool penisBaseControlBool;
        protected JSONStorableBool penisMidControlBool;
        protected JSONStorableBool penisTipControlBool;
        protected JSONStorableBool rElbowControlBool;
        protected JSONStorableBool lElbowControlBool;
        protected JSONStorableBool rKneeControlBool;
        protected JSONStorableBool lKneeControlBool;
        protected JSONStorableBool rToeControlBool;
        protected JSONStorableBool lToeControlBool;
        protected JSONStorableBool abdomenControlBool;
        protected JSONStorableBool abdomen2ControlBool;
        protected JSONStorableBool rThighControlBool;
        protected JSONStorableBool lThighControlBool;
        protected JSONStorableBool rArmControlBool;
        protected JSONStorableBool lArmControlBool;
        protected JSONStorableBool rShoulderControlBool;
        protected JSONStorableBool lShoulderControlBool;
        protected UIDynamicButton connectToServer;
        protected UIDynamicButton disconnectFromServer;
        protected UIDynamicButton checkAll;
        protected UIDynamicButton unCheckAll;
        protected JSONStorableString diagnostics;
        protected UIDynamicTextField diagnosticsTextField;
        protected JSONStorableString instructions;
        protected UIDynamicTextField instructionsTextField;
        protected JSONStorableString debugStats;
        protected UIDynamicTextField debugStatsTextField;
        private List<string> playerList;
        private List<string> onlinePlayers;
        private List<Player> players;
        private string lastSentClothesUpdate = ""; //copy of last sent clothes update for current player

            private static string[] shortTargetNames = new string[] {
        "c", "Hc", "pc", "cc", "hc", "rh", "lh", "rf", "lf", "nc", "et", "rn", "ln",
        "tc", "pb", "pm", "pt", "re", "le", "rk", "lk", "Rt", "Lt", "ac", "a2",
        "rt", "lt", "ra", "la", "rs", "ls"
            };

            private static string[] longTargetNames = new string[] {
                "control", "hipControl", "pelvisControl", "chestControl", "headControl",
                "rHandControl", "lHandControl", "rFootControl", "lFootControl", "neckControl",
                "eyeTargetControl", "rNippleControl", "lNippleControl", "testesControl",
                "penisBaseControl", "penisMidControl", "penisTipControl", "rElbowControl",
                "lElbowControl", "rKneeControl", "lKneeControl", "rToeControl", "lToeControl",
                "abdomenControl", "abdomen2Control", "rThighControl", "lThighControl",
                "rArmControl", "lArmControl", "rShoulderControl", "lShoulderControl"
            };

        public override void Init()
        {
            try
            {
                pluginLabelJSON.val = "VAM Multiplayer v1.0";

                // Find all 'Person' Atoms currently in the scene
                Atom tempAtom;
                playerList = new List<string>();
                onlinePlayers = new List<string>();
                players = new List<Player>();
                foreach (string atomUID in SuperController.singleton.GetAtomUIDs())
                {
                    tempAtom = SuperController.singleton.GetAtomByUid(atomUID);
                    if (tempAtom.type == "Person")
                    {
                        // Add new Player/'Person' Atom to playerList
                        playerList.Add(atomUID);

                        //clothing example from metachat:
                        //(where selector = geometry)
                            //  foreach (DAZDynamicItem dazDynamicItem in ((IEnumerable<DAZClothingItem>) this.selector.clothingItems).Where<DAZClothingItem>((Func<DAZClothingItem, bool>) (m => ((DAZDynamicItem) m).active)))
                            //  {
                            //   string uid = dazDynamicItem.uid;
                                //

                        // Create new Player and add Player's Atom's targets to Player's object
                        FreeControllerV3[] targets = tempAtom.freeControllers;
                        Player tempPlayer = new Player(atomUID);
                        foreach (FreeControllerV3 target in targets)
                        {
                            tempPlayer.addTarget(target.name, target.transform.position, target.transform.position, target.transform.rotation, target.transform.rotation);
                        }

                        // Store clothing
                        //note DAZClothingItem has: name, uid, containingAtom

// example:
//SuperController.LogMessage("ATOM UID: " + atomUID + "\n");
//SuperController.LogMessage(item.name + ":" + item.uid + ":" + item.containingAtom.uid);
//ATOM UID: Man
//AIPants:AI Pants:Man
//AIShirt:AI Shirt:Man
//AIShoes:AI Shoes:Man
//ATOM UID: Woman
//ErrandsShoes:Errands Shoes:Woman
//CustomClothingItem:PornPlayer.LBD.2:/Custom/Clothing/Female/PornPlayer/LBD/LBD.vam:Woman

                        //DAZClothingItem[] clothes2 = GameObject.FindObjectsOfType< DAZClothingItem >();
                        tempPlayer.geometry = tempAtom.GetStorableByID("geometry") as DAZCharacterSelector;
                        // two overloads:
                        //public void SetActiveClothingItem(DAZClothingItem item, bool active, bool fromRestore = false)
                        //public void SetActiveClothingItem(string itemId, bool active, bool fromRestore = false)
                        tempPlayer.activeClothesUids = tempPlayer.geometry.clothingItems.Where(c => c.isActiveAndEnabled).Select(c => c.uid).ToList();
                        SuperController.LogMessage("ATOM UID: " + atomUID + "\n");
                        foreach (string uid in tempPlayer.activeClothesUids)
                        {
                            SuperController.LogMessage(uid);
                        }
                        SuperController.LogMessage("\n");
                        players.Add(tempPlayer);
                    }
                }
                SuperController.LogMessage("Done displaying init clothing info.\n");

                // Setup player selector
                playerChooser = new JSONStorableStringChooser("Player Chooser", playerList, null, "Select Player", PlayerChooserCallback);
                RegisterStringChooser(playerChooser);
                CreatePopup(playerChooser);

                // Setup update frequency selector
                List<string> updateFrequencies = new List<string>();
                updateFrequencies.Add("5.0");
                updateFrequencies.Add("10.0");
                updateFrequencies.Add("15.0");
                updateFrequencies.Add("20.0");
                updateFrequencies.Add("25.0");
                updateFrequencies.Add("30.0");
                updateFrequencies.Add("40.0");
                updateFrequencies.Add("50.0");
                updateFrequencies.Add("60.0");
                updateFrequencies.Add("75.0");
                //updateFrequencies.Add("500.0");
                updateFrequencyChooser = new JSONStorableStringChooser("Update Frequency Chooser", updateFrequencies, updateFrequencies[5], "Update Frequency", UpdateFrequencyChooserCallback);
                RegisterStringChooser(updateFrequencyChooser);
                CreatePopup(updateFrequencyChooser);

                // Setup server selector
                List<string> servers = new List<string>();
                // Add new 'servers.Add("NEW SERVER IP");' to add new servers to the list
                //servers.Add("127.0.0.1");
                //servers.Add("192.168.1.1");
                servers.Add("20.79.154.48");
                serverChooser = new JSONStorableStringChooser("Server Chooser", servers, servers[0], "Select Server", ServerChooserCallback);
                RegisterStringChooser(serverChooser);
                CreatePopup(serverChooser, true);

                // Setup server selector
                List<string> ports = new List<string>();
                // Add new 'ports.Add("NEW PORT");' to add new ports to the list
                ports.Add("8888");
                ports.Add("9999");
                //ports.Add("80");
                //ports.Add("443");
                portChooser = new JSONStorableStringChooser("Port Chooser", ports, ports[0], "Select Port", PortChooserCallback);
                RegisterStringChooser(portChooser);
                CreatePopup(portChooser, true);

                // Setup network protocol selector
                List<string> protocols = new List<string>();
                //protocols.Add("UDP");
                protocols.Add("TCP");
                protocolChooser = new JSONStorableStringChooser("Protocol Chooser", protocols, protocols[0], "Select Net Protocol", ProtocolChooserCallback);
                RegisterStringChooser(protocolChooser);
                CreatePopup(protocolChooser, true);

                // Spectator mode toggle
                spectatorModeBool = new JSONStorableBool("Spectator Mode", false);
                CreateToggle(spectatorModeBool);

                // Setup connect to server button
                connectToServer = CreateButton("Connect to server", true);
                connectToServer.button.onClick.AddListener(ConnectToServerCallback);

                // Setup disconnect from server button
                disconnectFromServer = CreateButton("Disconnect from server", true);
                disconnectFromServer.button.onClick.AddListener(DisconnectFromServerCallback);

                // Setup a text field for diagnostics
                diagnostics = new JSONStorableString("Diagnostics", "Diagnostics:\n");
                diagnosticsTextField = CreateTextField(diagnostics, true);
                diagnosticsTextField.height = 600f;

                // Setup positions and rotations bools
                positionsBool = new JSONStorableBool("Update Positions", true);
                CreateToggle(positionsBool);
                rotationsBool = new JSONStorableBool("Update Rotations", true);
                CreateToggle(rotationsBool);

                // Setup a text fields for targets
                UIDynamicTextField targetsTextField = CreateTextField(new JSONStorableString("Targets1", "Select Updateable Targets Below:"));
                targetsTextField.height = 40;
                targetsTextField.UItext.fontSize = 40;

                // Setup uncheck and check all buttons
                unCheckAll = CreateButton("Uncheck All");
                unCheckAll.button.onClick.AddListener(UncheckAllCallback);
                checkAll = CreateButton("Check All");
                checkAll.button.onClick.AddListener(CheckAllCallback);

                // Setup player's target bools
                controlBool = new JSONStorableBool("control", true);
                CreateToggle(controlBool);
                hipControlBool = new JSONStorableBool("hipControl", true);
                CreateToggle(hipControlBool);
                pelvisControlBool = new JSONStorableBool("pelvisControl", true);
                CreateToggle(pelvisControlBool);
                chestControlBool = new JSONStorableBool("chestControl", true);
                CreateToggle(chestControlBool);
                headControlBool = new JSONStorableBool("headControl", true);
                CreateToggle(headControlBool);
                rHandControlBool = new JSONStorableBool("rHandControl", true);
                CreateToggle(rHandControlBool);
                lHandControlBool = new JSONStorableBool("lHandControl", true);
                CreateToggle(lHandControlBool);
                rFootControlBool = new JSONStorableBool("rFootControl", true);
                CreateToggle(rFootControlBool);
                lFootControlBool = new JSONStorableBool("lFootControl", true);
                CreateToggle(lFootControlBool);
                neckControlBool = new JSONStorableBool("neckControl", false);
                CreateToggle(neckControlBool);
                eyeTargetControlBool = new JSONStorableBool("eyeTargetControl", false);
                CreateToggle(eyeTargetControlBool);
                rNippleControlBool = new JSONStorableBool("rNippleControl", false);
                CreateToggle(rNippleControlBool);
                lNippleControlBool = new JSONStorableBool("lNippleControl", false);
                CreateToggle(lNippleControlBool);
                testesControlBool = new JSONStorableBool("testesControl", false);
                CreateToggle(testesControlBool);
                penisBaseControlBool = new JSONStorableBool("penisBaseControl", false);
                CreateToggle(penisBaseControlBool);
                penisMidControlBool = new JSONStorableBool("penisMidControl", false);
                CreateToggle(penisMidControlBool);
                penisTipControlBool = new JSONStorableBool("penisTipControl", false);
                CreateToggle(penisTipControlBool);
                rElbowControlBool = new JSONStorableBool("rElbowControl", true);
                CreateToggle(rElbowControlBool);
                lElbowControlBool = new JSONStorableBool("lElbowControl", true);
                CreateToggle(lElbowControlBool);
                rKneeControlBool = new JSONStorableBool("rKneeControl", true);
                CreateToggle(rKneeControlBool);
                lKneeControlBool = new JSONStorableBool("lKneeControl", true);
                CreateToggle(lKneeControlBool);
                rToeControlBool = new JSONStorableBool("rToeControl", false);
                CreateToggle(rToeControlBool);
                lToeControlBool = new JSONStorableBool("lToeControl", false);
                CreateToggle(lToeControlBool);
                abdomenControlBool = new JSONStorableBool("abdomenControl", false);
                CreateToggle(abdomenControlBool);
                abdomen2ControlBool = new JSONStorableBool("abdomen2Control", false);
                CreateToggle(abdomen2ControlBool);
                rThighControlBool = new JSONStorableBool("rThighControl", true);
                CreateToggle(rThighControlBool);
                lThighControlBool = new JSONStorableBool("lThighControl", true);
                CreateToggle(lThighControlBool);
                rArmControlBool = new JSONStorableBool("rArmControl", true);
                CreateToggle(rArmControlBool);
                lArmControlBool = new JSONStorableBool("lArmControl", true);
                CreateToggle(lArmControlBool);
                rShoulderControlBool = new JSONStorableBool("rShoulderControl", false);
                CreateToggle(rShoulderControlBool);
                lShoulderControlBool = new JSONStorableBool("lShoulderControl", false);
                CreateToggle(lShoulderControlBool);

                string instructionsStr = @"
1. Select a Player to control or choose Spectator mode to watch.
2. Ensure the port (8888 or 9999) matches the room you want to join.
3. Click 'Connect to server', it may take a few seconds.
4. Check player status in the plugin window or via the Discord bot.
5. If disconnected immediately, register your IP with the Discord bot. Registrations last 24h.
6. You also get disconnected if selected Player is already controlled. Select a different one and reconnect.
7. Avoid changing Update Frequency or Updateable Targets. If FPS drops severely after connecting, try switching to 15Hz Frequency.

Tips:
- If you encounter issues, click Disconnect and Connect again.
- Reload the plugin if problems persist.

Scenes:
- All players in the same room must use the same scene and atoms.
- Scene modifications on your end won’t sync with others.
Syncing:
- Only Player joints are synced; moving other elements like sex toys or UI changes are local and not visible to others.";

                instructions = new JSONStorableString("Instructions", "Instructions:\n");
                instructionsTextField = CreateTextField(instructions, true);
                instructionsTextField.height = 1200f;
                instructionsTextField.text += instructionsStr;

                debugStats = new JSONStorableString("Debug stats", "Debug stats:\n");
                debugStatsTextField = CreateTextField(debugStats, true);
                debugStatsTextField.height = 300f;
            }
            catch (Exception e)
            {
                SuperController.LogError("Exception caught: " + e);
            }
        }

            public static string TargetShortToLongName(string shortName)
            {
                int index = Array.IndexOf(shortTargetNames, shortName);
                if (index == -1)
                {
                    throw new ArgumentException("Short name does not exist.");
                }
                return longTargetNames[index];
            }

            public static string TargetLongToShortName(string longName)
            {
            int index = Array.IndexOf(longTargetNames, longName);
            if (index == -1)
            {
                throw new ArgumentException("Long name does not exist.");
            }
            return shortTargetNames[index];
            }

        // to be called on main Unity thread - gathers player quaternions into request message
        protected StringBuilder PrepareRequest()
        {
                // Prepare batched message for sending updates
                StringBuilder batchedMessage = new StringBuilder(playerChooser.val + ";");
                string initialMessage = batchedMessage.ToString();

                // Prepare message with controlled players joints, unless spectator mode is on
                if (!spectatorModeBool.val)
                {
                    // Collecting updates to send
                    Atom playerAtom = SuperController.singleton.GetAtomByUid(playerChooser.val);

                    // Find correct player in the List
                    int playerIndex = players.FindIndex(p => p.playerName == playerChooser.val);
                    if (playerIndex != -1)
                    {
                        Player player = players[playerIndex];

                        // Update only changed target positions and rotations for the main player
                        foreach (Player.TargetData target in player.playerTargets)
                        {
                            if (CheckIfTargetIsUpdateable(target.targetName))
                            {
                                FreeControllerV3 targetObject = playerAtom.GetStorableByID(target.targetName) as FreeControllerV3;

                                if (targetObject != null)
                                {
                                //if (targetObject.transform.position != target.positionOld || targetObject.transform.rotation != target.rotationOld)
                                {
                                    // Append main player's target position and rotation data to the batched message
                                    // TODO: if value < 0.00001, round down to 0 to save space

                                    // Optimize transfer - use shortened targetname
                                    string shortTargetName = "";
                                    try
                                    {
                                        shortTargetName = TargetLongToShortName(target.targetName);
                                    }
                                    catch (Exception ex)
                                    {
                                        SuperController.LogError("Exception caught: " + ex.Message);
                                    }
                                    batchedMessage.Append($"{shortTargetName},{targetObject.transform.position.x},{targetObject.transform.position.y},{targetObject.transform.position.z},{targetObject.transform.rotation.w},{targetObject.transform.rotation.x},{targetObject.transform.rotation.y},{targetObject.transform.rotation.z};");

                                    // Update the 'Old' position and rotation data
                                    if (positionsBool.val)
                                    {
                                    target.positionOld = targetObject.transform.position;
                                    }

                                    if (rotationsBool.val)
                                    {
                                    target.rotationOld = targetObject.transform.rotation;
                                    }
                                }
                                } else {
                                    ;//SuperController.LogError("TARGETOBJECT NULL 302");
                                }
                            }
                        }

                        // always include clothes in batchedMessage
                        // as an optimization, sender thread might choose to remove clothes part before sending batchedMessage
                        // if it knows that the same clothes were already sent to server before
                        var _currentActiveClothes = player.geometry.clothingItems.Where(c => c.isActiveAndEnabled).ToList();
                        var _currentActiveClothesUids = new HashSet<string>(_currentActiveClothes.Select(c => c.uid));
                        string _clothesString = string.Join(",", _currentActiveClothesUids.ToArray());
                        batchedMessage.Append("CLOTHES,")
                                    .Append(_clothesString)
                                    .Append(";");
                        // update saved list of clothing
                        player.activeClothesUids = _currentActiveClothes.Select(c => c.uid).ToList();

                        // old code - remove
                        if (false) //(forceSendClothes || checkClothes)
                        {
                            // Now check if clothing for current player changed
                            var currentActiveClothes = player.geometry.clothingItems.Where(c => c.isActiveAndEnabled).ToList();

                            // Extract UIDs from currentActiveClothes
                            var currentActiveClothesUids = new HashSet<string>(currentActiveClothes.Select(c => c.uid));

                            // Extract UIDs from the saved list
                            var savedClothesUids = new HashSet<string>(player.activeClothesUids);
                            SuperController.LogMessage("Checking clothes. Saved clothes uids:");
                            foreach (var uid in player.activeClothesUids)
                            {
                                SuperController.LogMessage(uid);
                            }
                                SuperController.LogMessage("\n");
                            SuperController.LogMessage("Current clothes uids:");
                            foreach (var c in currentActiveClothes)
                            {
                                SuperController.LogMessage(c.uid);
                            }
                                SuperController.LogMessage("\n");


                            // Check if both sets contain the same UIDs
                            bool areSameClothes = currentActiveClothesUids.SetEquals(savedClothesUids);
                            //if (forceSendClothes || !areSameClothes)
                            if (!areSameClothes)
                            {
                                SuperController.LogMessage("On send - clothes difference detected\n");
                                // clothes changed since last time, append active clothes list (uids) to request
                                // format: just add a joint/targetname called CLOTHES and list out the clothes separated by ','
                                string clothesString = string.Join(",", currentActiveClothesUids.ToArray());
                                batchedMessage.Append("CLOTHES,")
                                            .Append(clothesString)
                                            .Append(";");
                                // update saved list of clothing
                                player.activeClothesUids = currentActiveClothes.Select(c => c.uid).ToList();
                            }
                        }
                    } else
                    {
                        ;//SuperController.LogError("PLAYER NOT FOUND");
                    }
                } else
                {
                    // message indicating spectator mode
                    // no data is sent in request, we just want the response with all the other players data
                    batchedMessage.Length = 0; // clear string
                    batchedMessage.Append("S");
                }

                if (batchedMessage.ToString() == initialMessage)
                {
                    batchedMessage.Length = 0; // clear string
                }
                return batchedMessage;
        }
        // apply any processing to request right before sending
        // called in network thread
        protected string PreprocessRequestBeforeSending(string request)
        {
            // as an optimization, remove clothes update if it is the same as sent last time
            if (!request.Contains("CLOTHES"))
            {
                return request;
            }
            string[] parts = request.Split(';');
            string lastPart = parts[parts.Length - 1];
            if (!lastPart.Contains("CLOTHES"))
            {
                SuperController.LogError("Error: Clothes update missing in last part of request before processing");
                return request;
            }
            if (lastPart == lastSentClothesUpdate)
            {
                // clothes update same as last time, no need to send it again, let's remove it from the request
                var requestArr = parts.Take(parts.Length - 1).ToArray();
                return string.Join(";", requestArr);
            }
            else
            {
                // new clothes update - send it as is and remember what was sent
                lastSentClothesUpdate = lastPart;
                return request;
            }
            return request;
        }
        protected bool SendRequestToServer(Mutex reqMtx)
        {
            // Gather quaternions of player (or empty spectator request) and assemble the message
            StringBuilder batchedMessage;
            bool gotMutex = reqMtx.WaitOne(1000); // try to get mutex for 1s
            if (!gotMutex)
            {
                return false;
            }
            // grab the latest prepared request
            batchedMessage = requestGlobal;
            reqMtx.ReleaseMutex();

            // Send the batched message if there are updates
            if (batchedMessage.Length <= 0 || client == null)
            {
                return false;
            }

            try
            {
                // Remove the last character (trailing semicolon)
                if (batchedMessage[batchedMessage.Length - 1] == ';')
                {
                    batchedMessage.Remove(batchedMessage.Length - 1, 1);
                }
                String request = batchedMessage.ToString();
                // do any last-minute processing on the request
                request = PreprocessRequestBeforeSending(request);
                // add terminator
                request = request + "|";
                byte[] data = Encoding.ASCII.GetBytes(request);

                // start point for RTT latency calculation
                // we use fixed counter for time measurement instead of timers or stopwatches
                //sendTimes.Enqueue(fixedUpdateCounter);
                //stream.BeginWrite(data, 0, data.Length, new AsyncCallback(OnSend), null);
                bool sendSucceeded = false;
                // best effort send: we just skip the send if it WOULDBLOCK
                                int totalBytesSent = 0;
                                int bytesLeft = data.Length;
                try
                {
                                        // send all data
                                        while (bytesLeft > 0)
                                        {
                                                List<Socket> checkWrite = new List<Socket> { client };
                                                List<Socket> checkError = new List<Socket> { client };
                                                Socket.Select(null, checkWrite, checkError, 300 * 1000); // 300ms timeout for send
                                                if (checkWrite.Contains(client))
                                                {
                                                        int bytesSent = client.Send(data, totalBytesSent, bytesLeft, SocketFlags.None);
                                                        totalBytesSent += bytesSent;
                                                        bytesLeft -= bytesSent;

                                                        if (bytesLeft == 0)
                                                        {
                                                                sendSucceeded = true;
                                                        }
                                                }
                                                else if (checkError.Contains(client))
                                                {
                                                        diagnosticsTextField.text += "sendfailed Error: socket error.\n";
                                                        break;
                                                }
                                                else
                                                {
                                                        // send timeout - bail
                                                        // XXX: what if we sent part of message in this loop - we still return to caller like we didn't
                                                        sendTimeouts++;
                                                        break;
                                                }
                                        }
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode != SocketError.WouldBlock)
                    {
                        if (ex.SocketErrorCode == SocketError.TimedOut)
                        {
                            diagnosticsTextField.text += "sendfailed Error: SEND TIMEOUT\n";
                        }
                                                else if (ex.SocketErrorCode == SocketError.IOPending)
                                                {
                                                        ioPendingExceptions++;
                                                }
                                                else if (ex.SocketErrorCode == SocketError.NoBufferSpaceAvailable)
                                                {
                                                        ; // bail
                                                }
                        else
                        {
                            SuperController.LogError("SocketException caught: " + ex.Message);
                            diagnosticsTextField.text += "sendfailed Error: server disconnected. Try to re-register via Discord bot. Or did you try controlling an already controlled look?\n";
                            client.Close();
                            client = null;
                            ClearState();
                        }
                    }
                    else
                    {
                                                ; // WOULDBLOCK - bail
                    }
                }
                return sendSucceeded;
            }
            catch (Exception ex)
            {
                // Handle other exceptions
                SuperController.LogError("Exception caught: " + ex.Message);
            }
            return false;
        }
        protected void ReceiveResponse(Mutex respMtx, Stopwatch sw)
        {
            if (client == null) //&& stream.DataAvailable)
            {
                return;
            }

            //stream.BeginRead(receiveBuffer, 0, receiveBuffer.Length, OnReceiveComplete, null);
            DoReceive(respMtx, sw);
        }
        private void DoReceive(Mutex respMtx, Stopwatch sw)
        {
            int bytesRead = 0;
            try
            {
                                List<Socket> checkRead = new List<Socket> { client };
                                List<Socket> checkError = new List<Socket> { client };
                                // Check if the socket is ready for reading within 5ms
                                Socket.Select(checkRead, null, checkError, 5000);

                                if (checkRead.Contains(client))
                                {
                                        bytesRead = client.Receive(receiveBuffer);
                                        if (bytesRead == 0)
                                        {
                                            SuperController.LogError("receive socket error! server disconnected");
                                            diagnosticsTextField.text += "receivefail Error: server disconnected. Try to re-register via Discord bot. Or did you try controlling an already controlled look?\n";
                                            client.Close();
                                            client = null;
                                            ClearState();
                                        }
                                }
                                else if (checkError.Contains(client))
                                {
                                        SuperController.LogError("receive socket error! disconnected");
                                        diagnosticsTextField.text += "receivefail Error: server disconnected. Try to re-register via Discord bot. Or did you try controlling an already controlled look?\n";
                                        client.Close();
                                        client = null;
                                        ClearState();
                                }
                                else
                                {
                                        // timeout happened, it's okay, we'll try next time
                                        timeoutsSinceLastReceive++;
                                        return;
                                }
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode != SocketError.WouldBlock)
                {
                    if (ex.SocketErrorCode == SocketError.TimedOut)
                    {
                        // timeout happened, it's okay, we'll try next time
                        timeoutsSinceLastReceive++;
                        return;
                    }
                                    else if (ex.SocketErrorCode == SocketError.IOPending)
                                    {
                                        ioPendingExceptions++;
                                                return;
                                    }
                                        else if (ex.SocketErrorCode == SocketError.NoBufferSpaceAvailable)
                                        {
                                                ; // bail
                                        }
                    SuperController.LogError("Receive Exception SocketException caught: " + ex.Message);
                    diagnosticsTextField.text += "\n" + "socketerrorcode on receive="  + ex.SocketErrorCode + "\n";
                    diagnosticsTextField.text += "receivefail Error: server disconnected. Try to re-register via Discord bot. Or did you try controlling an already controlled look?\n";
                    client.Close();
                    client = null;
                    ClearState();
                }
                else
                {
                    return; // WOULDBLOCK, try again later
                }
            }
            catch (Exception ex)
            {
                SuperController.LogError("Receive error - exception: " + ex.Message);
                diagnosticsTextField.text += "receivefail general Error: server disconnected. Try to re-register via Discord bot. Or did you try controlling an already controlled look?\n";
                client.Close();
                client = null;
                ClearState();
            }

            if (bytesRead <= 0)
            {
                diagnosticsTextField.text += "bytesRead:" + bytesRead + "\n";
                return; // no data, try again later
            }

            // update debug statistics
            successfulReceivesCount++;
            if (timeoutsSinceLastReceive > 0)
            {
                totalTimeouts += timeoutsSinceLastReceive;
                timeoutsSinceLastReceive = 0; // Reset timeouts count
            }

            // handle received data
            byte[] receivedBytes = new byte[bytesRead];
            Array.Copy(receiveBuffer, receivedBytes, bytesRead);

            responseBuilder.Append(Encoding.UTF8.GetString(receivedBytes));
            string responseStr = responseBuilder.ToString();
            if (!responseStr.Contains("|"))
            {
                // have not assembled full message yet
                return;
            }
            // we have a termination symbol "|" in message
            responseBuilder.Length = 0; // clear responseBuilder
            string partialMsg = string.Empty;
            int endIndex = responseStr.LastIndexOf("|");
            if ((endIndex + 1) != responseStr.Length)
            {
                // we have "|" in message but last character is not "|", save the partial message
                partialMsg = responseStr.Substring(endIndex + 1);
                                partialMessages++;
            }
            responseBuilder.Append(partialMsg);

            // Split the string by the "|" terminator
            // if there was a partial msg at the end - we skip it
            string[] messages = responseStr.Substring(0, endIndex).Split('|');

            // if there were multiple messages received, process only the last one
            // we need to dequeue from sendTimes for all the ignored messages
            if (messages.Length > 0)
            {
               // for (int i = 0; i < messages.Length - 1; i++)
               // {
               //     // dequeue everything except last one
               //     if (sendTimes.Count > 0)
               //     {
               //         long foo = sendTimes.Dequeue();
               //     }
               // }

               // // Process last message as it has the latest update
               // if (!string.IsNullOrEmpty(messages[messages.Length - 1]))
               // {
               //     string resp = ProcessResponse(messages[messages.Length - 1] + "|", sw);

               //     bool gotMutex = respMtx.WaitOne(1000);
               //     if (!gotMutex)
               //     {
               //         return;
               //     }
               //     // put the response we got into a global to be applied in FixedUpdate
               //     responseGlobal = resp;
               //     respMtx.ReleaseMutex();
               // }

               // Process each message (we could just read the last one but we could miss the periodic clothes update)
                foreach (string msg in messages)
                {
                    if (!string.IsNullOrEmpty(msg))
                    {
                        string resp = ProcessResponse(msg + "|", sw);

                        bool gotMutex = respMtx.WaitOne(1000);
                        if (!gotMutex)
                        {
                            return;
                        }
                        // put the response we got into a global to be applied in FixedUpdate
                        // we chould be updating responseGlobal many times before FixedUpdate gets to run and process it - this is fine
                        // as only latest response is relevant, previous can be ignored.
                        // the exception from that is clothing updates - see below
                        responseGlobal = resp;

                        // if this msg has a clothing update - store it in Player object and make sure it gets received and applied
                        if (msg.Contains("CLOTHES"))
                        {
                            string[] responses = msg.Split(';');
                            if (responses.Length - 1 < 0)
                            {
                                SuperController.LogError("Error: unexpected format for clothes update");
                            }
                            string clothesUpdate = responses[responses.Length - 1];
                            if (!clothesUpdate.Contains("CLOTHES"))
                            {
                                SuperController.LogError("Error: unexpected format for clothes update - no CLOTHES");
                            }
                            string[] targetData = clothesUpdate.Split(',');
                            string playerName = targetData[0];
                            // store latest clothes update in Player object
                            int playerIndex = players.FindIndex(p => p.playerName == playerName);
                            if (playerIndex != -1)
                            {
                                Player player = players[playerIndex];
                                // lastClothingUpdate is protected by respMtx mutex
                                player.lastClothingUpdate = clothesUpdate;
                            }
                        }

                        respMtx.ReleaseMutex();
                    }
                }
            }
        }
        protected string ProcessResponse(string response, Stopwatch sw)
        {
            long currentTimestamp = 0;
            if (sw != null)
            {
                currentTimestamp = sw.ElapsedMilliseconds;
            }
            if (sendTimes.Count > 0)
            {
                // get timestamp of when request was sent
                long sentTimestamp = sendTimes.Dequeue();
                // calculate latency in ms between request and response
                long responseLatency = currentTimestamp - sentTimestamp;

                // update debug statistics
                summedLatencies += responseLatency;
                latenciesCount++;
                summedInFlightRequests += (sendTimes.Count + 1);
                inFlightRequestsCount++;
                // update send interval to half of the latency
                // TODO, dividing by 2 for now, later change divisor dynamically according to absolute latency value
                // for users with high latency, divisor should be a higher number
                //sendIntervalTicks = latencyTicks / 2;
                //sendIntervalTicks = latencyTicks * 2;
                 //sendIntervalTicks = latencyTicks / 2;
//                sendIntervalTicks = 100;
                //sendIntervalTicks = 6;
                //
            }
            else
            {
                diagnosticsTextField.text += "Unexpected: sendTimes empty when processing response\n";
            }
//            // just queue up the response to be processed by main thread in FixedUpdate()
//            lastResponse = response;
            return response;
        }

        // called in FixedUpdate()
        // check if updates from server on clothes of players differ from our state
        // if there is a difference - apply new clothes
        protected void ApplyLatestClothesUpdates()
        {
            // targetData[1] is "CLOTHES"
            // targetData[2] and higher contain clothes UIDs
            foreach (var player in players)
            {
                // don't apply update for our own player
                if (player.playerName == playerChooser.val && !spectatorModeBool.val)
                {
                    continue;
                }

                // get latest clothing update for player
                string lastClothingUpdate = "";
                bool gotMutex = responseMtx.WaitOne(200);
                if (!gotMutex)
                {
                    return;
                }
                lastClothingUpdate = player.lastClothingUpdate;
                responseMtx.ReleaseMutex();

                if (lastClothingUpdate == "")
                {
                    continue;
                }

                string[] targetData = lastClothingUpdate.Split(',');
                string playerName = targetData[0];

                // Now check if clothing for this player changed vs what we currently have
                var localActiveClothes = player.geometry.clothingItems.Where(c => c.isActiveAndEnabled).ToList();

                // Extract UIDs from currentActiveClothes
                var localActiveClothesUids = new HashSet<string>(localActiveClothes.Select(c => c.uid));

                // Make hashset out of clothes from server response
                HashSet<string> responseActiveClothesUids = new HashSet<string>();
                if (targetData.Length > 2 && !string.IsNullOrEmpty(targetData[2]))
                {
                    responseActiveClothesUids = new HashSet<string>(targetData.Skip(2));
                }

                // Check if both sets contain the same UIDs
                bool areSameClothes = localActiveClothesUids.SetEquals(responseActiveClothesUids);
                if (!areSameClothes)
                {
                    // remote on/off state of clothes for this player differs from local state
                    // sync it:
                    //  - strip what was stripped
                    //  - apply new active clothes from response

                    // Determine which clothes need to be removed and which need to be added
                    var clothesToRemove = localActiveClothesUids.Except(responseActiveClothesUids);
                    var clothesToAdd = responseActiveClothesUids.Except(localActiveClothesUids);

                    foreach (var clothingUid in clothesToRemove)
                    {
                        var clothing = player.geometry.clothingItems.Where(c => c.uid == clothingUid).FirstOrDefault();
                        if (clothing != null)
                        {
                            player.geometry.SetActiveClothingItem(clothing, active: false, fromRestore: true);
                        }
                    }
                    foreach (var clothingUid in clothesToAdd)
                    {
                        var clothing = player.geometry.clothingItems.Where(c => c.uid == clothingUid).FirstOrDefault();
                        if (clothing != null)
                        {
                            player.geometry.SetActiveClothingItem(clothing, active: true, fromRestore: true);
                        }
                    }

                   // // Strip player:
                   // player.geometry.clothingItems.Where(c => c.isActiveAndEnabled).ToList().ForEach((clothing) =>
                   // {
                   //     player.geometry.SetActiveClothingItem(clothing, active: false);
                   // });
                   // // Put on clothes from response:
                   // var clothesToPutOn = player.geometry.clothingItems
                   // .Where(c => responseActiveClothesUids.Contains(c.uid))
                   // .ToList();
                   // foreach (var clothing in clothesToPutOn)
                   // {
                   //     player.geometry.SetActiveClothingItem(clothing, active: true);
                   // }
                   
                    // this comes useful when current user switches between players - after switch the clothes will remain as they were left before
                    player.activeClothesUids = player.geometry.clothingItems.Where(c => c.isActiveAndEnabled).Select(c => c.uid).ToList(); 
                }
            }
        }

        // parse response and apply quaternions from other players
        // to be called in Unity main thread
        protected void ActuallyProcessResponse(String response)
        {
            if (response.Length == 0)
            {
                return;
            }

            try
            {
                // Parse the batched response
                string[] responses = response.Split(';');
                List<string> latestOnlinePlayers = new List<string>();
                foreach (string res in responses)
                {
                    if (!string.IsNullOrEmpty(res) && res != "none|")
                    {
                        // Truncate trailing "|" if there is one
                        string trimmedRes = res.TrimEnd('|');
                        string[] targetData = trimmedRes.Split(',');

                        // Make sure we have that player first
                        int playerIdx = players.FindIndex(p => p.playerName == targetData[0]);
                        if (playerIdx != -1)
                        {
                            // Update list of active players if needed
                            // Display in diag window if there is any new player
                            if (!latestOnlinePlayers.Contains(targetData[0]))
                            {
                                latestOnlinePlayers.Add(targetData[0]);
                            }
                            if (!onlinePlayers.Contains(targetData[0]))
                            {
                                onlinePlayers.Add(targetData[0]);
                                diagnosticsTextField.text += targetData[0] + " joined." + "\n";
                            }
                            Atom otherPlayerAtom = SuperController.singleton.GetAtomByUid(targetData[0]);
                            if (targetData[1] == "CLOTHES")
                            {
                                // skip CLOTHES update for now, we apply latest CLOTHES update later in FixedUpdate() parsed out from a separate variable
                                continue;
                            }

                            // restore original target name
                            string longTargetName = "";
                            try
                            {
                                longTargetName = TargetShortToLongName(targetData[1]);
                            }
                            catch (Exception ex)
                            {
                                SuperController.LogError("Exception caught: " + ex.Message);
                            }
                            FreeControllerV3 targetObject = otherPlayerAtom.GetStorableByID(longTargetName) as FreeControllerV3;

                            if (targetObject != null)
                            {
                                if (positionsBool.val)
                                {
                                    Vector3 tempPosition = targetObject.transform.position;
                                    tempPosition.x = float.Parse(targetData[2]);
                                    tempPosition.y = float.Parse(targetData[3]);
                                    tempPosition.z = float.Parse(targetData[4]);

                                    targetObject.transform.position = tempPosition;
                                }

                                if (rotationsBool.val)
                                {
                                    Quaternion tempRotation = targetObject.transform.rotation;
                                    tempRotation.w = float.Parse(targetData[5]);
                                    tempRotation.x = float.Parse(targetData[6]);
                                    tempRotation.y = float.Parse(targetData[7]);
                                    tempRotation.z = float.Parse(targetData[8]);

                                    targetObject.transform.rotation = tempRotation;
                                }
                            }
                            else {
                                ;//SuperController.LogError("TARGET OBJECT NULL");
                            }
                        }
                        else {
                            ;//SuperController.LogError("PLAYER NOT FOUND AGAIN" + targetData[0]);
                        }
                    } else {
                            ;//SuperController.LogError("NONE RESPONSE");
                    }
                }
                // Check if anyone disconnected since last tick
                foreach (string player in onlinePlayers)
                {
                    if (!latestOnlinePlayers.Contains(player))
                    {
                        diagnosticsTextField.text += player + " disconnected." + "\n";
                    }
                }
                onlinePlayers.Clear();
                onlinePlayers.AddRange(latestOnlinePlayers);
            }
            catch (SocketException ex)
            {
                // Handle the socket exception
                SuperController.LogError("SocketException caught: " + ex.Message);
            }
            catch (Exception ex)
            {
                // Handle other exceptions
                SuperController.LogError("Exception caught: " + ex.Message);
            }
        }

        // Background thread loop doing networking
        private void NetworkLoop(Mutex reqMtx, Mutex respMtx)
        {
            // Start stopwatch that will go continuously, never stopping (until we disconnect)
            Stopwatch sw = Stopwatch.StartNew();
            while (isLooping)
            {
                if (client != null)
                {
                    // Send request and/or receive response
                    SendReceiveUpdate(sw, reqMtx, respMtx);
                }
                Thread.Sleep(networkLoopSleepValue);
            }
        }
        protected void SendReceiveUpdate(Stopwatch sw, Mutex reqMtx, Mutex respMtx)
        {
           // if ((fixedUpdateCounter % 200) == 0)
           // {
           //     diagnosticsTextField.text += "sendIntervalTicks=" + sendIntervalTicks + ", latency=" + latencyTicks + ", queue=" + sendTimes.Count() + "\n";
           // }

            // update debug stats once in a while
            if ((sw.ElapsedMilliseconds % 2000) == 0)
            {
                if (successfulReceivesCount > 0)
                {
                    averageReceiveTimeouts = (double)totalTimeouts / successfulReceivesCount;
                }
                if (latenciesCount > 0)
                {
                    averageLatency = (double)summedLatencies / latenciesCount;
                }
                if (inFlightRequestsCount > 0)
                {
                    averageInFlightRequests = (double)summedInFlightRequests / inFlightRequestsCount;
                }
                debugStatsTextField.text = "Avg latency=" + averageLatency + "ms\n" +
                                           "Avg in-flight requests=" + averageInFlightRequests + "\n" +
                                           "Avg cycles to recv=" + averageReceiveTimeouts + "\n" +
                                                                                   "Partial msgs=" + partialMessages + "\n" +
                                                                                   "Send timeouts=" + sendTimeouts + "\n" +
                                                                                   "IOpending exceptions=" + ioPendingExceptions;
                                // clear
                                averageLatency = 30.0;
                                summedLatencies = 0;
                                latenciesCount = 0;
                                averageInFlightRequests = 1.0;
                                summedInFlightRequests = 0;
                                inFlightRequestsCount = 0;
                                timeoutsSinceLastReceive = 0;
                                successfulReceivesCount = 0;
                                totalTimeouts = 0;
                                averageReceiveTimeouts = 1.0;
                                // do not clear partial msgs
            }

            try
            {
                //ticksSinceLastSend++;
                // Get ms elapsed since last time we sent a request
                long msElapsed = sw.ElapsedMilliseconds - lastSentTimestamp;

                // If the ms elapsed is greater than the period based on the update frequency then
                // send a request
                if ((float)msElapsed >= (1000.0 / float.Parse(updateFrequencyChooser.val)))
                {
                    //if (ticksSinceLastSend >= sendIntervalTicks)
                    // limit in-flight requests
                    if (sendTimes.Count() <= 12)
                    {
                        // our send interval elapsed - time to send a request
                        // note there might be multiple requests in flight at any time
                        if (client != null)
                        {
                            // TODO if its nonblocking - check that it actually sent
                            bool succeeded = SendRequestToServer(reqMtx);
                            if (succeeded)
                            {
                                lastSentTimestamp = sw.ElapsedMilliseconds;
                                sendTimes.Enqueue(lastSentTimestamp);
                            }
                        }
                    }
                }
                // if any requests are in flight - try to receive
                // note that updateFrequency does not affect this, we try to receive every iteration
                if (sendTimes.Count != 0)
                {
                    ReceiveResponse(respMtx, sw);
                }
            }
            catch (Exception e)
            {
                SuperController.LogError("Exception caught: " + e);
            }

            //fixedUpdateCounter++;
        }
        // called by Unity on main thread, used for physics updates has Physics Rate frequency (usually 90hz so 11.1ms)
        protected void FixedUpdate()
        {
            bool gotMutex = responseMtx.WaitOne(200);
            if (!gotMutex)
            {
                return;
            }
            // get latest response that the networking thread received from server
            string latestResp = responseGlobal;
            responseMtx.ReleaseMutex();

            // apply quaternions from response
            ActuallyProcessResponse(latestResp);

            // check for clothes change of local player every ~1s
            if ((fixedUpdateCounter % 100) == 0)
            {
                // apply latest clothes update (must be stored and retrieved separately as it doesn't come in every message)
                ApplyLatestClothesUpdates();
            }
            // put local player quaternions into message
            //StringBuilder batchedMessage = PrepareRequest(checkClothes);
            StringBuilder batchedMessage = PrepareRequest();
            gotMutex = requestMtx.WaitOne(200); // try to get mutex for 0.2ms
            if (!gotMutex)
            {
                return;
            }
            // put message in a global so networking thread can send it
            requestGlobal = batchedMessage;
            requestMtx.ReleaseMutex();

            // NOTE: UNCOMMENT FOR LOCAL TESTING WITH TWO VAM WINDOWS
            // VAM SLOWS DOWN WHEN UNFOCUSED SO WE NEED TO "KICK" VAM
            // BY SLEEPING A BIT IN FIXEDUPDATE
            // OTHERWISE FPS TANK TO <10
            //Thread.Sleep(5);

            fixedUpdateCounter++;
        }

        protected bool CheckIfTargetIsUpdateable(string targetName)
        {
            if (targetName == "control")
            {
                return controlBool.val;
            }
            else if (targetName == "hipControl")
            {
                return hipControlBool.val;
            }
            else if (targetName == "pelvisControl")
            {
                return pelvisControlBool.val;
            }
            else if (targetName == "chestControl")
            {
                return chestControlBool.val;
            }
            else if (targetName == "headControl")
            {
                return headControlBool.val;
            }
            else if (targetName == "rHandControl")
            {
                return rHandControlBool.val;
            }
            else if (targetName == "lHandControl")
            {
                return lHandControlBool.val;
            }
            else if (targetName == "rFootControl")
            {
                return rFootControlBool.val;
            }
            else if (targetName == "lFootControl")
            {
                return lFootControlBool.val;
            }
            else if (targetName == "neckControl")
            {
                return neckControlBool.val;
            }
            else if (targetName == "eyeTargetControl")
            {
                return eyeTargetControlBool.val;
            }
            else if (targetName == "rNippleControl")
            {
                return rNippleControlBool.val;
            }
            else if (targetName == "lNippleControl")
            {
                return lNippleControlBool.val;
            }
            else if (targetName == "testesControl")
            {
                return testesControlBool.val;
            }
            else if (targetName == "penisBaseControl")
            {
                return penisBaseControlBool.val;
            }
            else if (targetName == "penisMidControl")
            {
                return penisMidControlBool.val;
            }
            else if (targetName == "penisTipControl")
            {
                return penisTipControlBool.val;
            }
            else if (targetName == "rElbowControl")
            {
                return rElbowControlBool.val;
            }
            else if (targetName == "lElbowControl")
            {
                return lElbowControlBool.val;
            }
            else if (targetName == "rKneeControl")
            {
                return rKneeControlBool.val;
            }
            else if (targetName == "lKneeControl")
            {
                return lKneeControlBool.val;
            }
            else if (targetName == "rToeControl")
            {
                return rToeControlBool.val;
            }
            else if (targetName == "lToeControl")
            {
                return lToeControlBool.val;
            }
            else if (targetName == "abdomenControl")
            {
                return abdomenControlBool.val;
            }
            else if (targetName == "abdomen2Control")
            {
                return abdomen2ControlBool.val;
            }
            else if (targetName == "rThighControl")
            {
                return rThighControlBool.val;
            }
            else if (targetName == "lThighControl")
            {
                return lThighControlBool.val;
            }
            else if (targetName == "rArmControl")
            {
                return rArmControlBool.val;
            }
            else if (targetName == "lArmControl")
            {
                return lArmControlBool.val;
            }
            else if (targetName == "rShoulderControl")
            {
                return rShoulderControlBool.val;
            }
            else if (targetName == "lShoulderControl")
            {
                return lShoulderControlBool.val;
            }

            return false;
        }

        protected void UpdateFrequencyChooserCallback(string updateFrequency)
        {
            SuperController.LogMessage("Update frequency " + updateFrequency + " selected.");
        }

        protected void UncheckAllCallback()
        {
            controlBool.SetVal(false);
            hipControlBool.SetVal(false);
            pelvisControlBool.SetVal(false);
            chestControlBool.SetVal(false);
            headControlBool.SetVal(false);
            rHandControlBool.SetVal(false);
            lHandControlBool.SetVal(false);
            rFootControlBool.SetVal(false);
            lFootControlBool.SetVal(false);
            neckControlBool.SetVal(false);
            eyeTargetControlBool.SetVal(false);
            rNippleControlBool.SetVal(false);
            lNippleControlBool.SetVal(false);
            testesControlBool.SetVal(false);
            penisBaseControlBool.SetVal(false);
            penisMidControlBool.SetVal(false);
            penisTipControlBool.SetVal(false);
            rElbowControlBool.SetVal(false);
            lElbowControlBool.SetVal(false);
            rKneeControlBool.SetVal(false);
            lKneeControlBool.SetVal(false);
            rToeControlBool.SetVal(false);
            lToeControlBool.SetVal(false);
            abdomenControlBool.SetVal(false);
            abdomen2ControlBool.SetVal(false);
            rThighControlBool.SetVal(false);
            lThighControlBool.SetVal(false);
            rArmControlBool.SetVal(false);
            lArmControlBool.SetVal(false);
            rShoulderControlBool.SetVal(false);
            lShoulderControlBool.SetVal(false);

            SuperController.LogMessage("All targets unchecked.");
        }

        protected void CheckAllCallback()
        {
            controlBool.SetVal(true);
            hipControlBool.SetVal(true);
            pelvisControlBool.SetVal(true);
            chestControlBool.SetVal(true);
            headControlBool.SetVal(true);
            rHandControlBool.SetVal(true);
            lHandControlBool.SetVal(true);
            rFootControlBool.SetVal(true);
            lFootControlBool.SetVal(true);
            neckControlBool.SetVal(true);
            eyeTargetControlBool.SetVal(true);
            rNippleControlBool.SetVal(true);
            lNippleControlBool.SetVal(true);
            testesControlBool.SetVal(true);
            penisBaseControlBool.SetVal(true);
            penisMidControlBool.SetVal(true);
            penisTipControlBool.SetVal(true);
            rElbowControlBool.SetVal(true);
            lElbowControlBool.SetVal(true);
            rKneeControlBool.SetVal(true);
            lKneeControlBool.SetVal(true);
            rToeControlBool.SetVal(true);
            lToeControlBool.SetVal(true);
            abdomenControlBool.SetVal(true);
            abdomen2ControlBool.SetVal(true);
            rThighControlBool.SetVal(true);
            lThighControlBool.SetVal(true);
            rArmControlBool.SetVal(true);
            lArmControlBool.SetVal(true);
            rShoulderControlBool.SetVal(true);
            lShoulderControlBool.SetVal(true);

            SuperController.LogMessage("All targets checked.");
        }

        protected void PlayerChooserCallback(string player)
        {
            SuperController.LogMessage("Player " + player + " selected.");
        }

        protected void ServerChooserCallback(string server)
        {
            SuperController.LogMessage("Server " + server + " selected.");
        }

        protected void PortChooserCallback(string port)
        {
            SuperController.LogMessage("Port " + port + " selected.");
        }

        protected void ProtocolChooserCallback(string protocol)
        {
            SuperController.LogMessage("Protocol " + protocol + " selected.");
        }

        protected void ConnectToServerCallback()
        {
            //  Ignore if already connected
            if (client != null && client.Connected)
            {
                diagnosticsTextField.text += "error: already connected." + "\n";
                SuperController.LogMessage("Already connected to server.");
                return;
            }

            try
            {
                // Connects to the UI selected server:port with the corresponding selected protocol
                IPHostEntry ipHostEntry = Dns.GetHostEntry(serverChooser.val);
                IPAddress ipAddress = Array.Find(ipHostEntry.AddressList, ip => ip.AddressFamily == AddressFamily.InterNetwork);
                SuperController.LogMessage(ipHostEntry.AddressList[0].ToString());
                IPEndPoint ipEndPoint = new IPEndPoint(ipAddress, int.Parse(portChooser.val));

                if (protocolChooser.val == "TCP")
                {
                    client = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    // Set the TCP_NODELAY flag to disable the Nagle Algorithm
                    client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);

                    // client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                    // XXX irrelevant if Blocking is false
                    client.SendTimeout = 5000;    // 5 seconds timeout for send operations
                    //client.ReceiveTimeout = 5000; // 5 seconds timeout for receive operations
                    client.ReceiveTimeout = 5; // 2ms timeout
                }

                client.Connect(ipEndPoint);
                // Set as non-blocking from now on
                client.Blocking = false;
                                // Make sure socket is writable after connecting
                                List<Socket> checkWrite = new List<Socket> { client };
                                Socket.Select(null, checkWrite, null, 15000 * 1000); // 15-second timeout
                                if (!checkWrite.Contains(client))
                                {
                                    diagnosticsTextField.text += "Err: socket not writable after connect!\n";
                                        client.Close();
                                        client = null;
                                        ClearState();
                                        SuperController.LogError("Socket not writable after connect!");
                                    return;
                                }

                // Clear any state from previous connection
                ClearState();

                diagnosticsTextField.text += "Connected to server: " + serverChooser.val + ":" + portChooser.val + "\n";
                //diagnosticsTextField.text += "Connecting..\n";

                SuperController.LogMessage("Connected to server: " + serverChooser.val + ":" + portChooser.val);

                requestMtx = new Mutex();
                responseMtx = new Mutex();
                thread = new Thread(() => NetworkLoop(requestMtx, responseMtx));
                thread.IsBackground = true;
                thread.Start();
            }
            catch (Exception e)
            {
                SuperController.LogError("Exception caught: " + e);
                diagnosticsTextField.text += "Exception caught: " + e.Message + "\n";
            }
        }
        private void ClearState()
        {
            // Clear all state except for state that is initialized in Init()
            onlinePlayers.Clear();
            sendTimes.Clear();
            lastSentTimestamp = 0;
            averageLatency = 30.0;
            summedLatencies = 0;
            latenciesCount = 0;
            ticksSinceLastSend = 0;
            averageInFlightRequests = 1.0;
            summedInFlightRequests = 0;
            inFlightRequestsCount = 0;
            timeoutsSinceLastReceive = 0;
            successfulReceivesCount = 0;
            partialMessages = 0;
            sendTimeouts = 0;
            totalTimeouts = 0;
            averageReceiveTimeouts = 1.0;
            sendIntervalTicks = 6; // Initial interval in ticks
            latencyTicks = 10; // Initial latency guess
            responseBuilder.Length = 0;
            requestGlobal.Length = 0;
            responseGlobal = "";
            lastSentClothesUpdate = "";
        }
        protected void DisconnectFromServerCallback()
        {
            // Check if the client is not null and is connected
            if (client != null)
            {
                try
                {
                    // Shutdown both send and receive operations
                    if (client.Connected)
                    {
                        if (thread != null)
                        {
                            thread.Abort();
                            thread = null;
                            requestMtx = null;
                            responseMtx = null;
                        }
                        client.Shutdown(SocketShutdown.Both);
                    }
                }
                catch (SocketException ex)
                {
                    // Log any socket exceptions that occur during shutdown
                    SuperController.LogMessage($"SocketException during shutdown: {ex.Message}");
                }
                catch (ObjectDisposedException ex)
                {
                    // Handle the case where the socket is already disposed
                    SuperController.LogMessage($"ObjectDisposedException during shutdown: {ex.Message}");
                }
                finally
                {
                    try
                    {
                        // Close the socket connection
                        client.Close();
                    }
                    catch (SocketException ex)
                    {
                        // Log any socket exceptions that occur during close
                        SuperController.LogMessage($"SocketException during close: {ex.Message}");
                    }
                    catch (ObjectDisposedException ex)
                    {
                        // Handle the case where the socket is already disposed
                        SuperController.LogMessage($"ObjectDisposedException during close: {ex.Message}");
                    }
                    finally
                    {
                        // Set the client to null to indicate it is disconnected
                        client = null;

                        // Update UI and log the disconnection
                        diagnosticsTextField.text += "Disconnected from server.\n";
                        SuperController.LogMessage("Disconnected from server.");
                    }
                }
            }
        }

        protected void OnDestroy()
        {
            if (client != null)
            {
                client.Close();
                client = null;
            }
            if (thread != null)
            {
                thread.Abort();
                thread = null;
            }
            isLooping = false;
        }
    }

    // Class for the players
    public class Player
    {
        public string playerName;
        public List<TargetData> playerTargets;
        public DAZCharacterSelector geometry;
        public List<string> activeClothesUids; // used mostly in sending so that we can determine if local player's clothing has changed - then send update
        public string lastClothingUpdate = ""; // store last clothing update for this player, parsed out of server response

        public Player(string name)
        {
            playerName = name;

            playerTargets = new List<TargetData>();
            activeClothesUids = new List<string>();
        }

        public void addTarget(string name, Vector3 pos, Vector3 posOld, Quaternion rot, Quaternion rotOld)
        {
            playerTargets.Add(new TargetData { targetName = name, position = pos, positionOld = posOld, rotation = rot, rotationOld = rot });
        }

        // Using a class to hold the various players target data since Tuples are not as supported in Unity
        public class TargetData
        {
            public string targetName;
            public Vector3 position;
            public Vector3 positionOld;
            public Quaternion rotation;
            public Quaternion rotationOld;

            public string TargetName { get; set; }
            public string Position { get; set; }
            public string PositionOld { get; set; }
            public string Rotation { get; set; }
            public string RotationOld { get; set; }
        }
    }
}

