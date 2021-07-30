# VAM Multiplayer UDP Server
# vamrobotics (7-28-2021)
# https://github.com/vamrobot/vammultiplayer

import socket
import threading

players = {}

class VAMMultiplayerServer():
	def __init__(self, host, port):
		self.host = host
		self.port = port
		self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
		#self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
		self.sock.bind((self.host, self.port))

	def listen(self):
		#self.sock.listen(5)
		while True:
			request, address = self.sock.recvfrom(65535)
			#client.settimeout(90)
			threading.Thread(target = self.clientConnection, args = (request, address)).start()

	def clientConnection(self, request, address):
		try:
			#request = self.sock.recvfrom(65535)
			#print(request)
			if request.endswith(b"|"):
				request = request[:-1]
				tmp = request.split(b",")
				global players
				if len(tmp) == 1:
					playerName = tmp[0]
					if playerName not in players:
						players[playerName] = {}
						print(b"Adding new player: " + playerName)
						self.sock.sendto(playerName + b" added to server.", address)
					else:
						self.sock.sendto(playerName + b" already added to server.", address)
				if len(tmp) >= 2:
					playerName = tmp[0]
					if len(tmp) == 2:
						targetName = tmp[1]
						if targetName in players[playerName]:
							self.sock.sendto(playerName + b"," + targetName + b"," + players[playerName][targetName], address)
							#print(playerName + b"," + targetName + b"," + players[playerName][targetName])
						else:
							self.sock.sendto(b"none|", address)
							#print(b"none|")
					if len(tmp) == 9:
						targetName = tmp[1]
						xPos = tmp[2]
						yPos = tmp[3]
						zPos = tmp[4]
						wRot = tmp[5]
						xRot = tmp[6]
						yRot = tmp[7]
						zRot = tmp[8]
						#print(b"Player: " + playerName)
						#print(b"Target Name: " + targetName)
						#print(b"X Position: " + xPos)
						#print(b"Y Position: " + yPos)
						#print(b"Z Position: " + zPos)
						#print(b"W Rotation: " + wRot)
						#print(b"X Rotation: " + xRot)
						#print(b"Y Rotation: " + yRot)
						#print(b"Z Rotation: " + zRot)
						players[playerName][targetName] = xPos + b"," + yPos + b"," + zPos + b"," + wRot + b"," + xRot + b"," + yRot + b"," + zRot
						self.sock.sendto(b"Target data recorded|", address)
					
		except Exception as e:
			print(e)
		finally:
			pass

def main():
	host = "0.0.0.0"
	port = 8888
	print("VAM Multiplayer Server running:")
	print("IP: " + host)
	print("Port: " + str(port))
	VAMMultiplayerServer(host, port).listen()

if __name__ == "__main__":
	main()
