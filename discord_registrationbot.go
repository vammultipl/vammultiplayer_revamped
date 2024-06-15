package main

import (
	"bufio"
	"fmt"
	"os"
	"os/signal"
	"regexp"
	"strconv"
	"strings"
	"sync"
	"syscall"
	"time"

	"github.com/bwmarrin/discordgo"
)

var (
	token          string
	filePath       = "allowlist.txt" // user IP allowlist
	expirationTime = 24 * 14 * time.Hour // 2 weeks expiration
	mutex          sync.Mutex // Mutex to protect access to the allowlist file
	prevPlayerStatus string = ""
)

func main() {
	// Read the bot token from a file
	tokenFile, err := os.Open("token.txt")
	if err != nil {
		fmt.Println("error opening token file,", err)
		return
	}
	defer tokenFile.Close()

	scanner := bufio.NewScanner(tokenFile)
	if scanner.Scan() {
		token = scanner.Text()
	}
	if err := scanner.Err(); err != nil {
		fmt.Println("error reading token file,", err)
		return
	}
	trimmedToken := strings.TrimSpace(token)

	// Create a new Discord session using the provided bot token.
	dg, err := discordgo.New("Bot " + trimmedToken)
	if err != nil {
		fmt.Println("error creating Discord session,", err)
		return
	}

	// Register the messageCreate func as a callback for MessageCreate events.
	dg.AddHandler(messageCreate)
	// In this example, we only care about receiving message events.
	dg.Identify.Intents = discordgo.IntentsGuildMessages

	// Open a websocket connection to Discord and begin listening.
	err = dg.Open()
	if err != nil {
		fmt.Println("error opening connection,", err)
		return
	}

	// Start the periodic cleanup in a separate goroutine
	go startCleanupTimer()
	// Goroutine to poll for player state changes and update status
	go startPlayerStateMonitor(dg)

	fmt.Println("Bot is now running. Press CTRL+C to exit.")
	// Wait here until CTRL+C or other term signal is received.
	sc := make(chan os.Signal, 1)
	signal.Notify(sc, syscall.SIGINT, syscall.SIGTERM, os.Interrupt, os.Kill)
	<-sc

	// Cleanly close down the Discord session.
	dg.Close()
}

func messageCreate(s *discordgo.Session, m *discordgo.MessageCreate) {
	// Ignore all messages created by the bot itself
	if m.Author.ID == s.State.User.ID {
		return
	}
	fmt.Println("Got message: ", m.Content)

	// Check if the message starts with "/register"
	if strings.HasPrefix(m.Content, "/register") {
		parts := strings.Split(m.Content, " ")
		if len(parts) == 2 {
			if (isValidIP(parts[1])) {
				ip := strings.TrimSpace(parts[1])
				registerIP(ip)
				fmt.Println("Registered IP: ", ip)
				s.ChannelMessageSend(m.ChannelID, fmt.Sprintf("Your IP address %s has been successfully registered/refreshed. You can now connect to the game.", ip))
			} else {
				fmt.Println("Register: invalid IP: ", parts[1])
				s.ChannelMessageSend(m.ChannelID, "Invalid IP address format. Please use /register 123.45.67.89")
			}
		} else {
			fmt.Println("Invalid command: ", m.Content)
			s.ChannelMessageSend(m.ChannelID, "Invalid command or IP address format. Please use /register 123.45.67.89")
		}
	} else if strings.HasPrefix(m.Content, "/state") {
		gameStatus, err := getCurrentGameStatus()
		if err != nil {
			fmt.Println("Error reading game status: ", err)
			s.ChannelMessageSend(m.ChannelID, "Error retrieving game status.")
			return
		}
		s.ChannelMessageSend(m.ChannelID, gameStatus)
	} else {
		// Respond with detailed usage info for any other message
		url := "https://www.google.com/search?q=google+what+is+my+ip"
		text := fmt.Sprintf("Unknown command. Here are the commands you can use:\n\n" +
		"1. `/register <IP>` - Register your IP address with the VaM multiplayer server. This will gain you entry to the server with about 2 weeks expiration. If you cannot connect to the server in VaM, register again. To find your IP, visit the link below. Link:\n%s\n\n" +
		    "2. `/state` - Check the current game status to see who is playing. You can also see the same info in my status on Discord updated every 30s.\n\n" +
		    "Please use one of the above commands.\n", url)
		s.ChannelMessageSend(m.ChannelID, text)
	}
}

// getCurrentGameStatus reads the last line of the file to get the current game status
// it does that for both files (for both rooms)
func getCurrentGameStatus() (string, error) {
	filenameRoom1 := "current_players_port8888.txt"
	filenameRoom2 := "current_players_port9999.txt"

	statusRoom1, err := getRoomStatus(filenameRoom1, "ROOM1")
	if err != nil {
		return "", err
	}

	statusRoom2, err := getRoomStatus(filenameRoom2, "ROOM2")
	if err != nil {
		return "", err
	}

	return fmt.Sprintf("%s\n%s", statusRoom1, statusRoom2), nil
}

func getRoomStatus(filePath, roomLabel string) (string, error) {
	file, err := os.Open(filePath)
	if err != nil {
		return "", err
	}
	defer file.Close()

	var lastLine string
	scanner := bufio.NewScanner(file)
	fileEmpty := true  // Flag to check if the file is empty
	for scanner.Scan() {
		lastLine = scanner.Text()
		fileEmpty = false // File has at least one line
	}

	// if file is empty - just say the room is not running
	if fileEmpty {
		return fmt.Sprintf("%s:\n%s", roomLabel, "Not running."), nil
	}

	if err := scanner.Err(); err != nil {
		return "", err
	}

	// Parsing the last line to extract game status
	parts := strings.SplitN(lastLine, ";", 2)
	if len(parts) != 2 {
		return fmt.Sprintf("%s: Invalid game status format in file.", roomLabel), nil
	}

	timestamp, state := parts[0], parts[1]

	// Convert timestamp to human-readable format
	timestampInt, err := strconv.ParseInt(timestamp, 10, 64)
	if err != nil {
		return fmt.Sprintf("%s: Error parsing timestamp.", roomLabel), nil
	}
	timestampStr := time.Unix(timestampInt, 0).Format(time.RFC1123)

	// Build the player details string
	playerDetails, err := getPlayerDetails(state, timestampStr)
	if err != nil {
		return "", err
	}

	return fmt.Sprintf("%s:\n%s", roomLabel, playerDetails), nil
}

func getPlayerDetails(state, timestampStr string) (string, error) {
	if state == "" {
		return fmt.Sprintf("%s: Empty.", timestampStr), nil
	}

	playerInfo := strings.Split(state, ",")
	playerDetails := ""
	for _, info := range playerInfo {
		playerParts := strings.Split(info, ":")
		if len(playerParts) == 3 {
			if "@SPECTATOR@" == playerParts[2] {
				playerDetails += fmt.Sprintf("IP: %s is SPECTATOR.\n", playerParts[0])
			} else {
				playerDetails += fmt.Sprintf("IP: %s controls %s.\n", playerParts[0], playerParts[2])
			}
		}
	}

	return fmt.Sprintf("%s:\n%s", timestampStr, playerDetails), nil
}

func isValidIP(ip string) bool {
	ipTrimmed := strings.TrimSpace(ip)
	re := regexp.MustCompile(`^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$`)
	return re.MatchString(ipTrimmed)
}

func startCleanupTimer() {
	ticker := time.NewTicker(6 * time.Hour)
	for {
		select {
		case <-ticker.C:
			cleanupExpiredIPs()
		}
	}
}

func startPlayerStateMonitor(s *discordgo.Session) {
	ticker := time.NewTicker(20* time.Second)
	for {
		select {
		case <-ticker.C:
			updatePlayerStatus(s)
		}
	}
}

func updatePlayerStatus(s *discordgo.Session) {
	gameStatus, err := getCurrentGameStatus()
	if err == nil {
		if len(gameStatus) > 125 {
			gameStatus = "Send /state command to check the state of rooms"
		}
		if gameStatus != prevPlayerStatus {
			err := s.UpdateCustomStatus(gameStatus)
			if err != nil {
				fmt.Println("error updating custom status", err)
			}
			prevPlayerStatus = gameStatus
		}
	} else {
		fmt.Println("error getting game status", err)
	}
}

func registerIP(ip string) {
	mutex.Lock()
	defer mutex.Unlock()

	currentTime := time.Now().Unix()
	var updatedLines []string
	ipExists := false

	file, err := os.OpenFile(filePath, os.O_RDWR|os.O_CREATE, 0644)
	if err != nil {
		fmt.Println("error opening allowlist file,", err)
		return
	}
	defer file.Close()

	scanner := bufio.NewScanner(file)
	for scanner.Scan() {
		line := scanner.Text()
		parts := strings.Split(line, " ")
		if len(parts) != 2 {
			continue
		}
		existingIP := parts[0]
		timestamp := parts[1]

		if existingIP == ip {
			updatedLines = append(updatedLines, fmt.Sprintf("%s %d", ip, currentTime))
			ipExists = true
		} else {
			updatedLines = append(updatedLines, fmt.Sprintf("%s %s", existingIP, timestamp))
		}
	}

	if !ipExists {
		updatedLines = append(updatedLines, fmt.Sprintf("%s %d", ip, currentTime))
	}

	if err := scanner.Err(); err != nil {
		fmt.Println("error reading allowlist file,", err)
		return
	}

	file.Seek(0, 0)
	file.Truncate(0)

	for _, line := range updatedLines {
		_, err := file.WriteString(line + "\n")
		if err != nil {
			fmt.Println("error writing to allowlist file,", err)
		}
	}
}

func cleanupExpiredIPs() {
	fmt.Println("Cleaning up expired IPs")
	mutex.Lock()
	defer mutex.Unlock()

	currentTime := time.Now().Unix()
	var updatedLines []string

	file, err := os.OpenFile(filePath, os.O_RDWR|os.O_CREATE, 0644)
	if err != nil {
		fmt.Println("error opening allowlist file,", err)
		return
	}
	defer file.Close()

	scanner := bufio.NewScanner(file)
	for scanner.Scan() {
		line := scanner.Text()
		parts := strings.Split(line, " ")
		if len(parts) != 2 {
			continue
		}
		ip := parts[0]
		timestamp, err := strconv.ParseInt(parts[1], 10, 64)
		if err != nil {
			continue
		}

		if currentTime-timestamp <= int64(expirationTime.Seconds()) {
			updatedLines = append(updatedLines, line)
		} else {
			fmt.Printf("Expired IP removed: %s\n", ip)
		}
	}

	if err := scanner.Err(); err != nil {
		fmt.Println("error reading allowlist file,", err)
		return
	}

	file.Seek(0, 0)
	file.Truncate(0)

	for _, line := range updatedLines {
		_, err := file.WriteString(line + "\n")
		if err != nil {
			fmt.Println("error writing to allowlist file,", err)
		}
	}
}

