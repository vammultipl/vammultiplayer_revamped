# VAM Multiplayer TCP Server
# vamrobotics (7-28-2021)
# https://github.com/vamrobot/vammultiplayer
# vammultipl (06-10-2024)
# https://github.com/vammultipl/vammultiplayer_revamped

import socket
import threading

players = {}
lock = threading.Lock()

class VAMMultiplayerServer:
    def __init__(self, host, port):
        self.host = host
        self.port = port
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.sock.bind((self.host, self.port))

    def listen(self):
        self.sock.listen(2)  # Only expecting two players.
        while True:
            client, address = self.sock.accept()
            client.settimeout(90)
            threading.Thread(target=self.clientConnection, args=(client,)).start()

    def clientConnection(self, client):
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
            client.close()

    def handle_request(self, client, request):
        parts = request.split(b",")
        if len(parts) == 1:
            self.handle_new_player(client, parts[0])
        elif len(parts) == 2:
            self.handle_position_query(client, *parts)
        elif len(parts) == 9:
            self.handle_position_update(client, parts)

    def handle_new_player(self, client, playerName):
        with lock:
            if playerName not in players:
                players[playerName] = {}
                print(f"Adding new player: {playerName.decode()}")
                client.send(playerName + b" added to server.")
            else:
                client.send(playerName + b" already added to server.")

    def handle_position_query(self, client, playerName, targetName):
        with lock:
            target_data = players.get(playerName, {}).get(targetName, b"none|")
        if target_data == b"none|":
            client.send(target_data)
        else:
            client.send(playerName + b"," + targetName + b"," + target_data)

    def handle_position_update(self, client, data):
        playerName, targetName = data[0], data[1]
        position_data = b",".join(data[2:])
        with lock:
            if playerName not in players:
                players[playerName] = {}
            players[playerName][targetName] = position_data
        client.send(b"Target data recorded|")

def main():
    host = "0.0.0.0"
    port = 8888
    print("VAM Multiplayer Server running:")
    print(f"IP: {host}")
    print(f"Port: {port}")
    VAMMultiplayerServer(host, port).listen()

if __name__ == "__main__":
    main()
