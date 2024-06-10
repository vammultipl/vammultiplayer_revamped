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
        parts = request.split(b",")
        if len(parts) == 1:
            self.handle_new_player(client, parts[0])
        elif len(parts) == 2:
            self.handle_position_query(client, *parts)
        elif len(parts) == 9:
            self.handle_position_update(client, parts)

    def handle_new_player(self, client, player_name):
        with self.lock:
            if player_name not in self.players:
                self.players[player_name] = {}
                print(f"Adding new player: {player_name.decode()}")
                client.send(player_name + b" added to server.")
            else:
                client.send(player_name + b" already added to server.")

    def handle_position_query(self, client, player_name, target_name):
        with self.lock:
            target_data = self.players.get(player_name, {}).get(target_name, b"none|")
        if target_data == b"none|":
            client.send(target_data)
        else:
            client.send(player_name + b"," + target_name + b"," + target_data)

    def handle_position_update(self, client, data):
        player_name = data[0]
        target_name = data[1]
        position_data = b",".join(data[2:])
        with self.lock:
            if player_name not in self.players:
                self.players[player_name] = {}
            self.players[player_name][target_name] = position_data
        client.send(b"Target data recorded|")

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

