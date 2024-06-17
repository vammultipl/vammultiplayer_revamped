go run discord_registrationbot.go >> /var/log/vammultiplayer/discord_registrationbot.log 2>&1 &
python3 VAMMultiplayerTCPServer.py 8888 >> /var/log/vammultiplayer/vammpserver_port8888.log 2>&1 &
python3 VAMMultiplayerTCPServer.py 9999 >> /var/log/vammultiplayer/vammpserver_port9999.log 2>&1 &
