# VAM Multiplayer #

If you ever wanted to use VAM in a multiplayer setting, it is now a reality! :)

I originally made the first proof of concept VAM Multiplayer some two years ago. Back then the server code was done in python 2.x and the plugin itself, though functional, was missing a lot of features.

Having some time on my hands I decided to resurrect my old VAM Multiplayer proof of concept code and update it for use with python 3.x and add in many of the features I had wanted to years back.

The files are:

* VAMMultiplayer.cs - Is the multiplayer plugin for VAM

* vamrobot.VAMMultiplayerTestScene.1.var - Contains a simple test scene, along with the VAM Multiplayer plugin itself

* VAMMultiplayerUDPServer.py and VAMMultiplayerTCPServer.py - Are the two python 3.x servers, UDP and TCP

The basic steps involved in running VAM Multiplayer are:

* Install python 3.x (adding python to the Window's PATH to make things easier) on a computer that will be accessible by all VAM clients

* Run either the UDP or the TCP python server through the command line

* Place vamrobot.VAMMultiplayerTestScene.1.var in the VAM's AddonPackages folder on each VAM client

* Launch VAM on each client and load the VAM Multiplayer Test Scene via VAM's scene browser (accept/allow the use of the included plugin when asked)

* Enter Edit Mode, go to the Scene Plugins screen, click on Open Custom UI, and you will be able to configure the VAM Muliplayer plugin

* When configuring the VAM Multiplayer plugin, select the desired Player (atom) you want to control with each VAM client and make sure each VAM client selects a different Player

* Ensure the server settings match (IP, port, protocol) with the python server you have running and hit Connect to server

* The VAM Multiplayer experience can now begin! :)

You can of course manually edit the VAM Multiplayer plugin to add new/different IP addresses, ports, etc.

In addition, you can edit the python server programs to change the desired IP and ports they attach to when run.

VAM Multiplayer can definitely be made much faster, in both the plugin's code, and definitely with the server's code, so consider this v1.0 a 'dip your toes in the water' type of experience! :)

I hope you all enjoy it and I welcome anyone to have their hand at modifying/extending the code for VAM Multiplayer, hence when I have it MIT licensed! :)
