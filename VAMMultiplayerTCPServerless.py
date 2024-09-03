# VAM Multiplayer TCP Server
# vamrobotics (7-28-2021)
# https://github.com/vamrobot/vammultiplayer
# vammultipl (06-10-2024)
# https://github.com/vammultipl/vammultiplayer_revamped

import socket
import threading
import sys
import struct
import time
import logging
from VamMultiplayerTCP import VAMMultiplayerTCP

class VAMMultiplayerServerless(VAMMultiplayerTCP):
    def __init__(self, host, port):
        super().__init__(host, port)
        self.stateLogger = logging.getLogger('ServerState')

    def load_allowlist(self, filename):
        return None


    def on_user_change(self):
        filename = f'current_players_port{self.port}.txt'
        timestamp = int(time.time())

        def format_user_data(ip_port, player_name):
            scene_name = self.usersScenes.get(ip_port)
            if not scene_name:
                scene_name = "None"
            base_info = f"{{\"IP\":\"{ip_port}\",\"playerName\":\"{player_name.decode()}\",\"scene\": {scene_name}}}"
            return base_info

        user_data = [format_user_data(ip_port, player_name) for ip_port, player_name in self.users.items()]
        if len(user_data) == 0:
            state = "Empty Server"
        else:
            state = ",".join(user_data)
        self.stateLogger.info("USERSTATECHANGE - " + state)

def main():
    host = "0.0.0.0"
    port = 8888  # Default port
    logging.basicConfig(level=logging.DEBUG,
                        format='%(asctime)s - %(levelname)s - %(message)s',
                        datefmt='%Y-%m-%d %H:%M:%S')

    # Check for command line arguments for port
    if len(sys.argv) > 1:
        try:
            port = int(sys.argv[1])
        except ValueError:
            logging.error("Invalid port number. Using default port 8888.")

    logging.info("VAM Multiplayer Server running:")
    logging.info(f"IP: {host}")
    logging.info(f"Port: {port}")
    VAMMultiplayerServerless(host, port).listen()

if __name__ == "__main__":
    main()
