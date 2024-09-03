import socket
import threading
import sys
import struct
import time
import logging
from abc import ABC, abstractmethod

MAGIC_NUMBER = b'INITFRAME'
SERVER_MAJOR_VERSION = 1  # Server version of data protocol
SERVER_MINOR_VERSION = 0
SERVER_PATCH_VERSION = 0

PLAYER_LIMIT = 8 # Max number of controlled players at a time (does not include spectators)
USERS_LIMIT = 10 # Max users connected (controlled players and potential spectators)

SPECTATOR_PLAYER_NAME = b"@SPECTATOR@" # special player name for spectator

class VAMMultiplayerTCP(ABC):
    def __init__(self, host, port):
        self.host = host
        self.port = port
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        # Set TCP_NODELAY to disable Nagle's algorithm
        self.sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.sock.bind((self.host, self.port))
        self.players = {} # all controlled players and their current targets (node positions)
        self.users = {} # all connected users - keys are IP:PORT and values are controlled player names
        self.usersLastClothesUpdate = {}
        self.usersScenes = {} # scene names that users are using (key is IP:PORT, value is scene name)
        self.player_to_user = {} # not including spectators
        self.lock = threading.RLock() # recursive lock


    def listen(self):
        logging.info("Listening on %s:%d", self.host, self.port)
        self.on_user_change()
        self.sock.listen(USERS_LIMIT)  # Limit number of users connected
        while True:
            client, address = self.sock.accept()
            logging.info(f"New connection from {address[0]}:{address[1]}")
            # Load IP allowlist fresh
            allowlist = self.load_allowlist('allowlist.txt')
            if allowlist is not None and address[0] not in allowlist:
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
                    # we got one or more messages, first one is potentially a partial message
                    messages = request.split(b"|")
                    messages[0] = saved_partial_message + messages[0]
                    saved_partial_message = b'' # stored partial message was incorporated - clear it
                    if not request.endswith(b"|"):
                        # request ends with partial message - save it
                        saved_partial_message = messages[-1]
                        messages = messages[:-1]
                    # process all messages - there could be a clothes update in any of them
                    for msg in messages:
                        if len(msg) > 0:
                            self.handle_request(client, msg, address)

        except Exception as e:
            logging.info(f"Error from {address[0]}:{address[1]} :{e}")
        finally:
            self.handle_disconnect(client, address)
            client.close()

    def parse_initial_frame(self, data):
        # Extract version information
        major, minor, patch = struct.unpack('BBB', data[len(MAGIC_NUMBER):len(MAGIC_NUMBER)+3])

        # Check version compatibility
        if (major, minor, patch) != (SERVER_MAJOR_VERSION, SERVER_MINOR_VERSION, SERVER_PATCH_VERSION):
            return None, f"Version mismatch. Please update your client."

        # Extract scene name
        scene_data = data[len(MAGIC_NUMBER)+3:]
        try:
            scene_name = scene_data.decode('utf-8').split('|')[0]
        except UnicodeDecodeError:
            logging.error(f"Invalid scene name encoding")
            return None, "Invalid scene name encoding"

        return scene_name, None

    def handle_request(self, client, request, address):
        # key for hashmaps involving user (IP:port)
        key = f"{address[0]}:{address[1]}"

        # Check if it is initial frame
        if len(request) >= len(MAGIC_NUMBER) + 3: # 3 bytes for version
            # Check magic number
            if request[:len(MAGIC_NUMBER)] == MAGIC_NUMBER:
                # Handle initial frame and return
                scene_name, err_str = self.parse_initial_frame(request)
                if err_str:
                    client.sendall(b"{err_str}|")
                else:
                    # Check if other users have different scenes
                    with self.lock:
                        other_scenes = set(self.usersScenes.values()) - {scene_name}
                    if other_scenes:
                        warning = "WARNING: You are using a different scene than others on the server."
                        scene_list = f"Others are using: {', '.join(other_scenes)}."
                        client.sendall(f"{warning}\n{scene_list}\nclient_version: OK|".encode())
                    else:
                        client.sendall(b"client_version: OK|")
                # Remember scene name loaded by user
                if scene_name:
                    with self.lock:
                        self.usersScenes[key] = scene_name
                return

        parts = request.split(b";")

        if len(parts) > 2: # assume more than one joint status is sent
            # joints were sent - it is not spectator but controlled player
            player_name = parts[0]
            updates = parts[1:]

            with self.lock:
                same_user_same_player = False
                # 1) check if player is trying to control a player already controlled by another user
                if player_name in self.player_to_user:
                    if self.player_to_user[player_name] != key:
                        logging.info(f"Disconnected user {key} for trying to control already controlled player {player_name.decode()}")
                        self.handle_disconnect(client, address)
                        client.close()
                        return
                    else:
                        same_user_same_player = True # most common case - this user is controlling same player as in previous request

                if not same_user_same_player:
                    # user or player change
                    is_new_user = False
                    user_was_spectator = False
                    if key in self.users:
                        if self.users[key] == SPECTATOR_PLAYER_NAME:
                            user_was_spectator = True
                    else:
                        is_new_user = True

                    # 2) if new user or user was spectator before - check if new player exceeds player limit
                    if is_new_user or user_was_spectator:
                        if len(self.players) >= PLAYER_LIMIT:
                            logging.error(f"Error: exceeding limit of {PLAYER_LIMIT} players (players:{len(self.players)}) when trying to add Player with name: {player_name.decode()}")
                            logging.error(f"Disconnected user {key} for exceeding player limit")
                            self.handle_disconnect(client, address)
                            client.close()
                            return

                    if not is_new_user:
                        # old user that has switched controlled player or was spectator before
                        old_playername = self.users[key]
                        # mark previous player controlled by user as free to use
                        if old_playername in self.player_to_user:
                            del self.player_to_user[old_playername]
                        if old_playername in self.players:
                            del self.players[old_playername]
                        # log if a player was released
                        if old_playername != SPECTATOR_PLAYER_NAME:
                            logging.info(f"User {key} stopped controlling {old_playername.decode()}")
                        else:
                            logging.info(f"User {key} is no longer SPECTATOR")

                    # set new player for user
                    self.users[key] = player_name
                    self.player_to_user[player_name] = key
                    self.players[player_name] = {}
                    logging.info(f"{key} now controls player {player_name.decode()}")
                    self.on_user_change()

            # parse updates from client and send response to client
            self.handle_batch_update(key, client, False, player_name, updates)

        else:
            if request == b"S":
                # spectator mode
                with self.lock:
                    player_name = SPECTATOR_PLAYER_NAME # can be multiple spectators
                    # if user not exists or exists but was controlling player before
                    if key not in self.users or self.users[key] != player_name:
                        if key in self.users:
                            # user was controlling player before - mark previous player as free to use
                            old_playername = self.users[key]
                            # mark previous player controlled by user as free to use
                            if old_playername in self.player_to_user:
                                del self.player_to_user[old_playername]
                            if old_playername in self.players:
                                del self.players[old_playername]
                            # log that a player was released
                            logging.info(f"User {key} stopped controlling {old_playername.decode()}")

                        self.users[key] = player_name
                        logging.info(f"{key} is now a SPECTATOR")
                        self.on_user_change()

                # send response to client
                self.handle_batch_update(key, client, True, None, None)
            else:
                logging.error(f"Error: got malformed input: {request.decode()}")

    def handle_batch_update(self, user, client, is_spectator, player_name, updates):
        with self.lock:
            if not is_spectator:
                # Update positions and rotations for the player (also CLOTHES, which is a separate optional target)
                for update in updates:
                    data = update.split(b",")
                    target_name = data[0]
                    target_data = b",".join(data[1:])
                    self.players[player_name][target_name] = target_data

            # Prepare response with all other players' joint data
            response = []
            # Do not send clothes update unless enough time has passed - to save traffic
            send_clothes = False
            current_time = time.monotonic()
            last_sent = self.usersLastClothesUpdate.get(user, 0)
            if current_time - last_sent >= 2.0: #send clothes updates every 2seconds per player
                self.usersLastClothesUpdate[user] = current_time
                send_clothes = True

            for other_player, targets in self.players.items():
                if player_name is None or other_player != player_name:
                    for target_name, pos_rot_data in targets.items():
                        if target_name == b"CLOTHES":
                            if send_clothes:
                                response.append(other_player + b"," + target_name + b"," + pos_rot_data)
                        else:
                            response.append(other_player + b"," + target_name + b"," + pos_rot_data)

        if response:
            client.sendall(b";".join(response) + b"|")
        else:
            client.sendall(b"none|")

    def handle_disconnect(self, client, address):
        # Log disconnect details
        key = f"{address[0]}:{address[1]}"
        logging.info(f"Client disconnected from {key}")

        with self.lock:
            # Get player name if it exists
            player_name = self.users.get(key)

            if player_name:
                logging.info(f"Client {key} stopped controlling {player_name.decode()}")

                # Clean up player-related dictionaries
                self.players.pop(player_name, None)
                self.player_to_user.pop(player_name, None)

            # Clean up user-related dictionaries
            self.users.pop(key, None)
            self.usersLastClothesUpdate.pop(key, None)
            self.usersScenes.pop(key, None)

            # Call on_user_change only if the key was in self.users
            if player_name:
                self.on_user_change()

    @abstractmethod
    def on_user_change(self):
        pass

    @abstractmethod
    def load_allowlist(self, filename):
        pass
