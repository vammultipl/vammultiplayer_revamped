# VAM Multiplayer TCP Server
# vamrobotics (7-28-2021)
# https://github.com/vamrobot/vammultiplayer
# vammultipl (06-10-2024)
# https://github.com/vammultipl/vammultiplayer_revamped

import socket
import threading
import sys
import time
import logging

class VAMMultiplayerServer:
    def __init__(self, host, port):
        self.host = host
        self.port = port
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        # Set TCP_NODELAY to disable Nagle's algorithm
        self.sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.sock.bind((self.host, self.port))
        self.players = {}
        self.users = {}
        self.player_to_user = {} # not including spectators
        self.lock = threading.Lock()

    def listen(self):
        self.sock.listen(8)  # Only expecting up to eight users.
        while True:
            client, address = self.sock.accept()
            logging.info(f"New connection from {address[0]}:{address[1]}")
            # Load IP allowlist fresh
            allowlist = self.load_allowlist('allowlist.txt')
            if address[0] not in allowlist:
                logging.info(f"Connection from {address[0]}:{address[1]} rejected: IP not in allowlist")
                client.close()
                continue
            client.settimeout(90)
            threading.Thread(target=self.client_connection, args=(client, address)).start()

    def client_connection(self, client, address):
        key = f"{address[0]}:{address[1]}"
        # Last saved partial message (when recv() didnt get the whole message)
        saved_partial_message = b''
        try:
            while True:
                request = client.recv(65535)
                if not request:
                    break

                # Count the number of "|" terminators in the request
                terminator_count = request.count(b"|")

                if terminator_count == 0:
                    saved_partial_message += request
                    # sanity check
                    if len(saved_partial_message) > 20000:
                        # partial message unexpectedly big, something is wrong, terminate connection
                        logging.error(f"Error: partial message for {key} grew beyond 20k, disconnecting user")
                        break
                    continue
                elif terminator_count >= 1:
                    # we got multiple messages at once, first one is potentially a partial message
                    messages = request.split(b"|")
                    messages[0] = saved_partial_message + messages[0]
                    saved_partial_message = b'' # stored partial message was incorporated - clear it
                    if not request.endswith(b"|"):
                        # request ends with partial message - save it
                        saved_partial_message = messages[-1]
                        messages = messages[:-1]
                    # XXX: should we process only last message? safer to process all for now
                    for msg in messages:
                        if len(msg) > 0:
                            self.handle_request(client, msg, address)

        except Exception as e:
            logging.info(f"Error from {address[0]}:{address[1]} :{e}")
        finally:
            self.handle_disconnect(client, address)
            client.close()

    def load_allowlist(self, filename):
        allowlist = set()
        try:
            with open(filename, 'r') as file:
                for line in file:
                    parts = line.strip().split()
                    if len(parts) == 2:
                        allowlist.add(parts[0])
        except FileNotFoundError:
            logging.error(f"Allowlist file {filename} not found.")
        return allowlist

    def handle_request(self, client, request, address):
        parts = request.split(b";")
        key = f"{address[0]}:{address[1]}"
        if len(parts) > 2: #assume more than one joint status is sent
#            self.handle_new_player(client, parts[0])
#        else:
            player_name = parts[0]
            updates = parts[1:]
            if player_name in self.player_to_user:
                if self.player_to_user[player_name] != key:
                    logging.info(f"Disconnected user {key} for trying to control already controlled player {player_name.decode()}")
                    client.close()
                    self.handle_disconnect(client, address)
                    return

            self.handle_batch_update(client, False, player_name, updates)

            # Log IP and player_name changes
            with self.lock:
                if key not in self.users or self.users[key] != player_name:
                    if key in self.users:
                        old_playername = self.users[key]
                        if old_playername in self.player_to_user:
                            # Mark previous player controlled by user as free to use
                            del self.player_to_user[old_playername]
                    self.users[key] = player_name
                    logging.info(f"{key} now controls player {player_name.decode()}")
                    self.player_to_user[player_name] = key
                    self.on_user_change()
        else:
            if request == b"S":
                # spectator mode
                self.handle_batch_update(client, True, None, None)
                # Log IP for spectator
                with self.lock:
                    player_name = b"@SPECTATOR@" # can be multiple spectators
                    if key not in self.users or self.users[key] != player_name:
                        if key in self.users:
                            old_playername = self.users[key]
                            if old_playername in self.player_to_user:
                                # Mark previous player controlled by user as free to use
                                del self.player_to_user[old_playername]
                        self.users[key] = player_name
                        logging.info(f"{key} is now a SPECTATOR")
                        self.on_user_change()
            else:
                logging.error(f"Error: got malformed input: {request.decode()}")

#    def handle_new_player(self, client, player_name):
#        with self.lock:
#            if player_name not in self.players:
#                self.players[player_name] = {}
#                print(f"Adding new player: {player_name.decode()}")
#                client.send(player_name + b" added to server.")
#            else:
#                client.send(player_name + b" already added to server.")

    def handle_batch_update(self, client, is_spectator, player_name, updates):
        with self.lock:
            if not is_spectator:
                # Ensure player exists
                if player_name not in self.players:
                    if len(self.players) > 5:
                        logging.error(f"Error: already more than 5 players when trying to add Player with name: {player_name.decode()}")
                        return
                    logging.info(f"Adding new player: {player_name.decode()}")
                    self.players[player_name] = {}
                # Update positions and rotations for the player (also CLOTHES, which is a separate optional target)
                for update in updates:
                    data = update.split(b",")
                    target_name = data[0]
                    target_data = b",".join(data[1:])
                    self.players[player_name][target_name] = target_data

            # Prepare response with all other players' joint data
            response = []
            for other_player, targets in self.players.items():
                if player_name is None or other_player != player_name:
                    for target_name, pos_rot_data in targets.items():
                        response.append(other_player + b"," + target_name + b"," + pos_rot_data)
                        # TODO if target_name == CLOTHES, only send it once every X requests
                        # store a per player request counter for that

        if response:
            client.sendall(b";".join(response) + b"|")
        else:
            client.sendall(b"none|")

    def handle_disconnect(self, client, address):
        # Log disconnect details
        key = f"{address[0]}:{address[1]}"
        logging.info(f"Client disconnected from {key}")
        with self.lock:
            if key in self.users:
                logging.info(f"Client {key} stopped controlling {self.users[key].decode()}")
                player_name = self.users[key]
                if player_name in self.players:
                    del self.players[player_name]
                if player_name in self.player_to_user:
                    del self.player_to_user[player_name]
                del self.users[key]
                self.on_user_change()

    def on_user_change(self):
        filename = f'current_players_port{self.port}.txt'
        with open(filename, 'a') as f:
            timestamp = int(time.time())
            state = ",".join(f"{ip_port}:{player_name.decode()}" for ip_port, player_name in self.users.items())
            f.write(f"{timestamp};{state}\n")

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
    VAMMultiplayerServer(host, port).listen()

if __name__ == "__main__":
    main()
