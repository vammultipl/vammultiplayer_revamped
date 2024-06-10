// VAM Multiplayer
// vamrobotics (7-28-2021)
// https://github.com/vamrobot/vammultiplayer

using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using SimpleJSON;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Diagnostics;

namespace vamrobotics
{
    class VAMMultiplayer : MVRScript
    {
        private Socket client;

        protected JSONStorableStringChooser playerChooser;
        protected JSONStorableStringChooser serverChooser;
        protected JSONStorableStringChooser portChooser;
        protected JSONStorableStringChooser protocolChooser;
        protected JSONStorableStringChooser updateFrequencyChooser;
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
        private List<string> playerList;
        private List<Player> players;
        private Stopwatch sw = Stopwatch.StartNew();

        public override void Init()
        {
            try
            {
                pluginLabelJSON.val = "VAM Multiplayer v1.0";

                // Find all 'Person' Atoms currently in the scene
                Atom tempAtom;
                playerList = new List<string>();
                players = new List<Player>();
                foreach (string atomUID in SuperController.singleton.GetAtomUIDs())
                {
                    tempAtom = SuperController.singleton.GetAtomByUid(atomUID);
                    if (tempAtom.type == "Person")
                    {
                        // Add new Player/'Person' Atom to playerList
                        playerList.Add(atomUID);

                        // Create new Player and add Player's Atom's targets to Player's object
                        FreeControllerV3[] targets = tempAtom.freeControllers;
                        Player tempPlayer = new Player(atomUID);
                        foreach (FreeControllerV3 target in targets)
                        {
                            tempPlayer.addTarget(target.name, target.transform.position, target.transform.position, target.transform.rotation, target.transform.rotation);
                            //SuperController.LogMessage(atomUID);
                            //SuperController.LogMessage(target.name);
                            //SuperController.LogMessage(target.transform.position.x.ToString() + "," + target.transform.position.y.ToString()+ "," + target.transform.position.z.ToString());
                            //SuperController.LogMessage(target.transform.rotation.w.ToString() + "," + target.transform.rotation.x.ToString() + "," + target.transform.rotation.y.ToString() + "," + target.transform.rotation.z.ToString());
                        }
                        players.Add(tempPlayer);
                    }
                }

                // Setup player selector
                playerChooser = new JSONStorableStringChooser("Player Chooser", playerList, null, "Select Player", PlayerChooserCallback);
                RegisterStringChooser(playerChooser);
                CreatePopup(playerChooser);

                // Setup update frequency selector
                List<string> updateFrequencies = new List<string>();
                updateFrequencies.Add("1.0");
                updateFrequencies.Add("5.0");
                updateFrequencies.Add("10.0");
                updateFrequencies.Add("20.0");
                updateFrequencies.Add("30.0");
                updateFrequencies.Add("40.0");
                updateFrequencies.Add("50.0");
                updateFrequencies.Add("60.0");
                updateFrequencyChooser = new JSONStorableStringChooser("Update Frequency Chooser", updateFrequencies, updateFrequencies[2], "Update Frequency", UpdateFrequencyChooserCallback);
                RegisterStringChooser(updateFrequencyChooser);
                CreatePopup(updateFrequencyChooser);

                // Setup server selector
                List<string> servers = new List<string>();
                // Add new 'servers.Add("NEW SERVER IP");' to add new servers to the list
                servers.Add("127.0.0.1");
                servers.Add("192.168.1.1");
                serverChooser = new JSONStorableStringChooser("Server Chooser", servers, servers[0], "Select Server", ServerChooserCallback);
                RegisterStringChooser(serverChooser);
                CreatePopup(serverChooser, true);

                // Setup server selector
                List<string> ports = new List<string>();
                // Add new 'ports.Add("NEW PORT");' to add new ports to the list
                ports.Add("8888");
                ports.Add("80");
                ports.Add("443");
                portChooser = new JSONStorableStringChooser("Port Chooser", ports, ports[0], "Select Port", PortChooserCallback);
                RegisterStringChooser(portChooser);
                CreatePopup(portChooser, true);

                // Setup network protocol selector
                List<string> protocols = new List<string>();
                protocols.Add("UDP");
                protocols.Add("TCP");
                protocolChooser = new JSONStorableStringChooser("Protocol Chooser", protocols, protocols[0], "Select Net Protocol", ProtocolChooserCallback);
                RegisterStringChooser(protocolChooser);
                CreatePopup(protocolChooser, true);

                // Setup connect to server button
                connectToServer = CreateButton("Connect to server", true);
                connectToServer.button.onClick.AddListener(ConnectToServerCallback);

                // Setup disconnect from server button
                disconnectFromServer = CreateButton("Disconnect from server", true);
                disconnectFromServer.button.onClick.AddListener(DisconnectFromServerCallback);

                // Setup a text field for diagnostics
                diagnostics = new JSONStorableString("Diagnostics", "Diagnostics:\n");
                diagnosticsTextField = CreateTextField(diagnostics, true);

                // Setup positions and rotations bools
                positionsBool = new JSONStorableBool("Update Positions", true);
                CreateToggle(positionsBool);
                rotationsBool = new JSONStorableBool("Update Rotations", false);
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
                controlBool = new JSONStorableBool("control", false);
                CreateToggle(controlBool);
                hipControlBool = new JSONStorableBool("hipControl", false);
                CreateToggle(hipControlBool);
                pelvisControlBool = new JSONStorableBool("pelvisControl", false);
                CreateToggle(pelvisControlBool);
                chestControlBool = new JSONStorableBool("chestControl", false);
                CreateToggle(chestControlBool);
                headControlBool = new JSONStorableBool("headControl", true);
                CreateToggle(headControlBool);
                rHandControlBool = new JSONStorableBool("rHandControl", true);
                CreateToggle(rHandControlBool);
                lHandControlBool = new JSONStorableBool("lHandControl", true);
                CreateToggle(lHandControlBool);
                rFootControlBool = new JSONStorableBool("rFootControl", false);
                CreateToggle(rFootControlBool);
                lFootControlBool = new JSONStorableBool("lFootControl", false);
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
                rElbowControlBool = new JSONStorableBool("rElbowControl", false);
                CreateToggle(rElbowControlBool);
                lElbowControlBool = new JSONStorableBool("lElbowControl", false);
                CreateToggle(lElbowControlBool);
                rKneeControlBool = new JSONStorableBool("rKneeControl", false);
                CreateToggle(rKneeControlBool);
                lKneeControlBool = new JSONStorableBool("lKneeControl", false);
                CreateToggle(lKneeControlBool);
                rToeControlBool = new JSONStorableBool("rToeControl", false);
                CreateToggle(rToeControlBool);
                lToeControlBool = new JSONStorableBool("lToeControl", false);
                CreateToggle(lToeControlBool);
                abdomenControlBool = new JSONStorableBool("abdomenControl", false);
                CreateToggle(abdomenControlBool);
                abdomen2ControlBool = new JSONStorableBool("abdomen2Control", false);
                CreateToggle(abdomen2ControlBool);
                rThighControlBool = new JSONStorableBool("rThighControl", false);
                CreateToggle(rThighControlBool);
                lThighControlBool = new JSONStorableBool("lThighControl", false);
                CreateToggle(lThighControlBool);
                rArmControlBool = new JSONStorableBool("rArmControl", false);
                CreateToggle(rArmControlBool);
                lArmControlBool = new JSONStorableBool("lArmControl", false);
                CreateToggle(lArmControlBool);
                rShoulderControlBool = new JSONStorableBool("rShoulderControl", false);
                CreateToggle(rShoulderControlBool);
                lShoulderControlBool = new JSONStorableBool("lShoulderControl", false);
                CreateToggle(lShoulderControlBool);
            }
            catch (Exception e)
            {
                SuperController.LogError("Exception caught: " + e);
            }
        }

        protected void FixedUpdate()
        {
            try
            {
                // Get ms elapsed since current stopwatch interval
                float msElapsed = sw.ElapsedMilliseconds;

                // If the ms elapsed is greater than the period based on the update frequency then
                // stop the stopwatch, call the update function, and restart the stopwatch
                if (msElapsed >= (1000.0 / float.Parse(updateFrequencyChooser.val)))
                {
                    sw.Stop();

		    // Prepare batched message for sending updates
		    StringBuilder batchedMessage = new StringBuilder(playerChooser.val + ";");
		    string initialMessage = batchedMessage.ToString();

		    // Collecting updates to send
		    Atom playerAtom = SuperController.singleton.GetAtomByUid(playerChooser.val);

		    // Find correct player in the List
		    int playerIndex = players.FindIndex(p => p.playerName == playerChooser.val);
		    if (playerIndex != -1 && client != null)
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
					if (targetObject.transform.position != target.positionOld || targetObject.transform.rotation != target.rotationOld)
					{
					    // Append main player's target position and rotation data to the batched message
					    batchedMessage.Append($"{target.targetName},{targetObject.transform.position.x},{targetObject.transform.position.y},{targetObject.transform.position.z},{targetObject.transform.rotation.w},{targetObject.transform.rotation.x},{targetObject.transform.rotation.y},{targetObject.transform.rotation.z};");

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
				    }
				}
			    }
		    }

		    // Send the batched message if there are updates
		    if (batchedMessage.Length > 0 && batchedMessage != initialMessage && client != null)
		    {
			string response = SendToServer(batchedMessage.ToString() + "|");
			// Parse the batched response
			string[] responses = response.Split(';');
			foreach (string res in responses)
			{
			    if (!string.IsNullOrEmpty(res) && res != "none|")
			    {
				// Truncate trailing "|" if there is one
				string trimmedRes = res.TrimEnd('|');
				string[] targetData = trimmedRes.Split(',');

				if (targetData.Length == 9)
				{
				    // Make sure we have that player first
				    int playerIdx = players.FindIndex(p => p.playerName == targetData[0]);
				    if (playerIdx != -1)
			            {
					    Atom otherPlayerAtom = SuperController.singleton.GetAtomByUid(targetData[0]);
					    FreeControllerV3 targetObject = otherPlayerAtom.GetStorableByID(targetData[1]) as FreeControllerV3;

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
				    }
				}
				else
				{
				    SuperController.LogError("Malformed server response: " + res);
				}
			    }
			}
		    }

		    sw = Stopwatch.StartNew();
		}
	    }
	    catch (Exception e)
	    {
		SuperController.LogError("Exception caught: " + e);
	    }
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
            //  Close any established socket server connection
            if (client != null)
            {
                DisconnectFromServerCallback();
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

                    client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                }
                else if (protocolChooser.val == "UDP")
                {
                    client = new Socket(ipAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                }

                client.Connect(ipEndPoint);

                diagnosticsTextField.text += "Connected to server: " + serverChooser.val + ":" + portChooser.val + "\n";

                SuperController.LogMessage("Connected to server: " + serverChooser.val + ":" + portChooser.val);
            }
            catch (Exception e)
            {
                SuperController.LogError("Exception caught: " + e);
            }
        }

        protected string SendToServer(string message)
        {
            // Sends data to server over existing socket connection
            if (client != null)
            {
                byte[] messageBytes = Encoding.ASCII.GetBytes(message);

                int bytesSent = client.Send(messageBytes);

                byte[] responseBytes = new byte[65535];

                int bytesReceived = client.Receive(responseBytes, 0, responseBytes.Length, 0);

                return Encoding.UTF8.GetString(responseBytes, 0, bytesReceived);
            }
            else
            {
                SuperController.LogError("Tried to send but not connected to any server.");

                return "Not Connected.";
            }
        }

        protected void DisconnectFromServerCallback()
        {
            // Closes the current socket connection to the server
            if (client != null)
            {
                client.Shutdown(SocketShutdown.Both);
                client.Close();
                client = null;

                diagnosticsTextField.text += "Disconnected from server.\n";

                SuperController.LogMessage("Disconnected from server.");
            }
        }

        protected void OnDestroy()
        {

        }
    }

    // Class for the players
    public class Player
    {
        public string playerName;
        public List<TargetData> playerTargets;

        public Player(string name)
        {
            playerName = name;

            playerTargets = new List<TargetData>();
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
