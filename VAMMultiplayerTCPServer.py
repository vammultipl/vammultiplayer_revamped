# VAM Multiplayer TCP Server
# vamrobotics (7-28-2021)
# https://github.com/vamrobot/vammultiplayer
# vammultipl (06-10-2024)
# https://github.com/vammultipl/vammultiplayer_revamped

import socket
import threading

class VAMMultiplayerServer:
    def __init__(self, host, port):
        self.host = host
        self.port = port
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.sock.bind((self.host, self.port))
        self.players = {}
        self.lock = threading.Lock()

    def listen(self):
        self.sock.listen(2)  # Only expecting two players.
        while True:
            client, address = self.sock.accept()
            client.settimeout(90)
            threading.Thread(target=self.client_connection, args=(client,)).start()

    def client_connection(self, client):
        try:
            while True:
                request = client.recv(65535)
                if not request:
                    break
                if request.endswith(b"|"):
                    self.handle_request(client, request[:-1])
        except Exception as e:
            print(f"Error: {e}")
        finally:
            self.handle_disconnect(client)
            client.close()

    def handle_request(self, client, request):
        parts = request.split(b";")
        if len(parts) == 1:
            self.handle_new_player(client, parts[0])
        else:
            player_name = parts[0]
            updates = parts[1:]
            self.handle_batch_update(client, player_name, updates)

    def handle_new_player(self, client, player_name):
        with self.lock:
            if player_name not in self.players:
                self.players[player_name] = {}
                print(f"Adding new player: {player_name.decode()}")
                client.send(player_name + b" added to server.")
            else:
                client.send(player_name + b" already added to server.")

    def handle_batch_update(self, client, player_name, updates):
        with self.lock:
            # Update positions and rotations for the player
            for update in updates:
                data = update.split(b",")
                if len(data) == 8:
                    target_name = data[0]
                    position_data = b",".join(data[1:])
                    if player_name not in self.players:
                        print(f"Adding new player: {player_name.decode()}")
                        self.players[player_name] = {}
                    self.players[player_name][target_name] = position_data

            # Prepare response with all other players' joint data
            response = []
            for other_player, targets in self.players.items():
                if other_player != player_name:
                    for target_name, pos_rot_data in targets.items():
                        response.append(other_player + b"," + target_name + b"," + pos_rot_data)

        if response:
            client.send(b";".join(response) + b"|")
        else:
            client.send(b"none|")

    def handle_disconnect(self, client):
        # Logic to handle player disconnects can be added here
        pass

def main():
    host = "0.0.0.0"
    port = 8888
    print("VAM Multiplayer Server running:")
    print(f"IP: {host}")
    print(f"Port: {port}")
    VAMMultiplayerServer(host, port).listen()

if __name__ == "__main__":
    main()

