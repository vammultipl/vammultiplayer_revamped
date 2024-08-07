package main

import (
	"bufio"
	"fmt"
	"io/ioutil"
	"log"
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
	allowlistFile       = "allowlist.txt" // user IP allowlist
	usernamesFile       = "usernames_ips.txt" // mapping of IPs into usernames
	expirationTime = 7 * 24 * 1 * time.Hour // 1 week expiration
	allowlistMutex          sync.Mutex // Mutex to protect access to the allowlist and usernames file
	prevPlayerStatus string = ""
	monitoredChannels = make(map[string]time.Time) // monitoring enabled channels by /monitor command
	mu               sync.Mutex // mutex protecting monitoredChannels
	monitorMaxHours int = 16 // monitor for max 16 hours
)

func main() {
	// Read the bot token from a file
	tokenFile, err := os.Open("token.txt")
	if err != nil {
		log.Println("error opening token file,", err)
		return
	}
	defer tokenFile.Close()

        // Read the channel name from the file
        channelName, err := readChannelNameFromFile("bot_discord_channel_name.txt")
        if err != nil {
            log.Println("Error reading channel name:", err)
            log.Println("Quitting..")
            return
        }

	scanner := bufio.NewScanner(tokenFile)
	if scanner.Scan() {
		token = scanner.Text()
	}
	if err := scanner.Err(); err != nil {
		log.Println("error reading token file,", err)
		return
	}
	trimmedToken := strings.TrimSpace(token)

	// Create a new Discord session using the provided bot token.
	dg, err := discordgo.New("Bot " + trimmedToken)
	if err != nil {
		log.Println("error creating Discord session,", err)
		return
	}

	// Register the messageCreate func as a callback for MessageCreate events.
        dg.AddHandler(func(s *discordgo.Session, m *discordgo.MessageCreate) {
            messageCreate(s, m, channelName)
        })
	// In this example, we only care about receiving message events.
	dg.Identify.Intents = discordgo.IntentsGuildMessages | discordgo.IntentsDirectMessages

	// Open a websocket connection to Discord and begin listening.
	err = dg.Open()
	if err != nil {
		log.Println("error opening connection,", err)
		return
	}

	// Start the periodic cleanup in a separate goroutine
	go startCleanupTimer()
	// Goroutine to poll for player state changes and update status
	go startPlayerStateMonitor(dg)

	log.Println("Bot is now running. Press CTRL+C to exit.")
	// Wait here until CTRL+C or other term signal is received.
	sc := make(chan os.Signal, 1)
	signal.Notify(sc, syscall.SIGINT, syscall.SIGTERM, os.Interrupt, os.Kill)
	<-sc

	// Cleanly close down the Discord session.
	dg.Close()
}

func readChannelNameFromFile(filename string) (string, error) {
    content, err := ioutil.ReadFile(filename)
    if err != nil {
        return "", err
    }

    lines := strings.Split(string(content), "\n")
    for _, line := range lines {
        trimmedLine := strings.TrimSpace(line)
        if trimmedLine != "" && !strings.HasPrefix(trimmedLine, "#") {
            return trimmedLine, nil
        }
    }

    return "", fmt.Errorf("no valid channel name found in the file")
}

func messageCreate(s *discordgo.Session, m *discordgo.MessageCreate, allowedChannelName string) {
	// Ignore all messages created by the bot itself
	if m.Author.ID == s.State.User.ID {
		return
	}

	// Retrieve channel information
	channel, err := s.Channel(m.ChannelID)
	if err != nil {
		log.Println("Error getting channel info: ", err)
		return
	}

        // Only respond to messages in the allowed channel or DMs
        if channel.Type != discordgo.ChannelTypeDM && channel.Name != allowedChannelName {
            return
        }

	log.Println("Got message: ", m.Content)

	// Check if the message starts with "/register"
	if strings.HasPrefix(m.Content, "/register") {
		if channel.Type != discordgo.ChannelTypeDM {
			log.Println("/register command sent not in DM - deleting msg and warning user")
		        // Delete the user's message
		        err := s.ChannelMessageDelete(m.ChannelID, m.ID)
		        if err != nil {
		            log.Println("Error deleting message:", err)
		        }
			s.ChannelMessageSend(m.ChannelID, "Send /register commands via DM only.")
			return
		}

		// Command /register received via DM
		parts := strings.Split(m.Content, " ")
		if len(parts) == 2 {
			if (isValidIP(parts[1])) {
				ip := strings.TrimSpace(parts[1])
				// register IP in allowlist txt file and file with IP to username mapping
				err := registerIP(ip, m.Author.Username)
				if err != nil {
					log.Println("error: failed to register IP: ", ip)
					s.ChannelMessageSend(m.ChannelID, fmt.Sprintf("Failed to register IP"))
					return
				}
				log.Println("Registered IP: ", ip)
				s.ChannelMessageSend(m.ChannelID, fmt.Sprintf("Your IP address %s has been successfully registered/refreshed. You can now connect to the game.", ip))
			} else {
				log.Println("Register: invalid IP: ", parts[1])
				s.ChannelMessageSend(m.ChannelID, "Invalid IP address format. Please use /register 123.45.67.89")
			}
		} else {
			log.Println("Invalid command: ", m.Content)
			s.ChannelMessageSend(m.ChannelID, "Invalid command or IP address format. Please use /register 123.45.67.89")
		}
	} else if strings.HasPrefix(m.Content, "/state") {
		gameStatus, err := getCurrentGameStatus()
		if err != nil {
			log.Println("Error reading game status: ", err)
			s.ChannelMessageSend(m.ChannelID, "Error retrieving game status.")
			return
		}
		s.ChannelMessageSend(m.ChannelID, gameStatus)
	} else if strings.HasPrefix(m.Content, "/monitor") {
		handleMonitorCommand(s, m)
		// print state as well
		gameStatus, err := getCurrentGameStatus()
		if err != nil {
			log.Println("Error reading game status: ", err)
			s.ChannelMessageSend(m.ChannelID, "Error retrieving game status.")
			return
		}
		s.ChannelMessageSend(m.ChannelID, gameStatus)
	} else {
		// Respond with detailed usage info for any other message
		url := "https://www.google.com/search?q=google+what+is+my+ip"
		text := fmt.Sprintf("Unknown command. Here are the commands you can use:\n\n" +
		"1. `/register <IP>` - Register your IP address with the VaM multiplayer server via DM to the bot. This will gain you entry to the server with 1 week expiration. If you cannot connect to the server in VaM, register again. To find your IP, visit the link below. Link:\n%s\n\n" +
		    "2. `/state` - Check the current game status to see who is playing. You can also see the same info in my status on Discord updated every 20s.\n\n" +
		    "3. `/monitor <hours>` - Enable monitoring for game status changes on this channel for X hours (useful for notifications)\n\n" +
		    "Please use one of the above commands.\n", url)
		s.ChannelMessageSend(m.ChannelID, text)
	}
}

// handleMonitorCommand handles the /monitor <hours> command
func handleMonitorCommand(s *discordgo.Session, m *discordgo.MessageCreate) {
	st, err := s.Channel(m.ChannelID)
	if err != nil {
		log.Println("Error retrieving Channel type")
		return
	}

	if st.Type == discordgo.ChannelTypeDM {
		log.Println("/monitor command sent in DM - ignoring")
		s.ChannelMessageSend(m.ChannelID, "The /monitor command can only be used in a server channel.")
		return
	}

	// Parse the command
	var hours int
	_, err = fmt.Sscanf(m.Content, "/monitor %d", &hours)
	if err != nil || hours <= 0 {
		s.ChannelMessageSend(m.ChannelID, "Usage: /monitor <hours>")
		return
	}
	if hours > monitorMaxHours {
		text := fmt.Sprintf("You can only monitor for maximum %d hours.", monitorMaxHours)
		s.ChannelMessageSend(m.ChannelID, text)
		return
	}

	// Calculate the expiration time
	expiryTime := time.Now().Add(time.Duration(hours) * time.Hour)

	// Update the monitored channels map
	mu.Lock()
	monitoredChannels[m.ChannelID] = expiryTime
	mu.Unlock()

	s.ChannelMessageSend(m.ChannelID, fmt.Sprintf("Monitoring this channel for %d hours.", hours))
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

	return fmt.Sprintf("%s\n%s\n\n", statusRoom1, statusRoom2), nil
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

// Fetch username from mapping file based on IP
func getUsernameFromIP(ip string) (string, error) {
	// lock the files mutex
	allowlistMutex.Lock()
	defer allowlistMutex.Unlock()

	file, err := os.Open(usernamesFile)
	if err != nil {
		return "", err
	}
	defer file.Close()

	scanner := bufio.NewScanner(file)
	for scanner.Scan() {
		line := scanner.Text()
		parts := strings.Split(line, " ")
		if len(parts) != 2 {
			continue // Skip lines that don't match the expected format
		}

		if parts[0] == ip {
			return parts[1], nil
		}
	}

	if err := scanner.Err(); err != nil {
		return "", err
	}

	return "", fmt.Errorf("IP %s not found", ip)
}

func getPlayerDetails(state, timestampStr string) (string, error) {
	if state == "" {
		return fmt.Sprintf("%s: Empty.", timestampStr), nil
	}

	playerInfo := strings.Split(state, ",")
	playerDetails := ""
	sceneNames := make(map[string]int)
	lastScene := ""
	for _, info := range playerInfo {
		playerParts := strings.Split(info, ":")
		if len(playerParts) < 3 || len(playerParts) > 4 {
		    continue // Skip invalid entries
		}
		// get username from mapping file based on IP
		// we want to avoid showing user IPs
		username, err := getUsernameFromIP(playerParts[0])
		if err != nil {
			username = "unknown"
		}
		characterName := playerParts[2]
		sceneName := ""
		if len(playerParts) == 4 {
		    sceneName = playerParts[3]
		    sceneNames[sceneName]++
		    lastScene = sceneName
		}
		if "@SPECTATOR@" == characterName {
			playerDetails += fmt.Sprintf("%s is SPECTATOR.\n", username)
		} else {
			playerDetails += fmt.Sprintf("%s controls %s.\n", username, characterName)
		}
		if sceneName != "" {
		    playerDetails += fmt.Sprintf("%s is on %s\n", username, sceneName)
		}
	}
	// check if all players are on same scene
	if len(sceneNames) == 1 && sceneNames[lastScene] == len(playerInfo) {
		// they are - run the same loop again but print scene only at the end (TODO optimize this)
		playerDetails = ""
		for _, info := range playerInfo {
			playerParts := strings.Split(info, ":")
			if len(playerParts) < 3 || len(playerParts) > 4 {
			    continue // Skip invalid entries
			}
			// get username from mapping file based on IP
			// we want to avoid showing user IPs
			username, err := getUsernameFromIP(playerParts[0])
			if err != nil {
				username = "unknown"
			}
			characterName := playerParts[2]
			if "@SPECTATOR@" == characterName {
				playerDetails += fmt.Sprintf("%s is SPECTATOR.\n", username)
			} else {
				playerDetails += fmt.Sprintf("%s controls %s.\n", username, characterName)
			}
		}
		playerDetails += fmt.Sprintf("Players running scene: %s\n", lastScene)
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
		if gameStatus != prevPlayerStatus {
			updateMonitoredChannelsWithStatus(s, gameStatus)
			prevPlayerStatus = gameStatus
			// Discord limitation on status length
			if len(gameStatus) > 125 {
				s.UpdateCustomStatus("Send /state command to check the state of rooms")
				return
			}
			err := s.UpdateCustomStatus(gameStatus)
			if err != nil {
				log.Println("error updating custom status", err)
			}
		}
	} else {
		log.Println("error getting game status", err)
	}
}

func updateMonitoredChannelsWithStatus(dg *discordgo.Session, currentState string) {
	mu.Lock()
	defer mu.Unlock()

	now := time.Now()
	for channelID, expiry := range monitoredChannels {
		if now.Before(expiry) {
			dg.ChannelMessageSend(channelID, currentState)
		} else {
			delete(monitoredChannels, channelID)
		}
	}
}

func registerIP(ip string, username string) error {
	allowlistMutex.Lock()
	defer allowlistMutex.Unlock()

	// Add IP-username mapping in file
	fileUsernames, err := os.OpenFile(usernamesFile, os.O_RDWR|os.O_CREATE, 0644)
	if err != nil {
		log.Println("error opening usernames file,", err)
		return err
	}
	defer fileUsernames.Close()

	var updatedLinesUsernames []string
	userExists := false
	scanner := bufio.NewScanner(fileUsernames)
	for scanner.Scan() {
		line := scanner.Text()
		parts := strings.Split(line, " ")
		if len(parts) != 2 {
			continue
		}
		existingIP := parts[0]
		existingUser := parts[1]
		if existingUser == username {
			// update IP for user
			updatedLinesUsernames = append(updatedLinesUsernames, fmt.Sprintf("%s %s", ip, username))
			userExists = true
		} else {
			updatedLinesUsernames = append(updatedLinesUsernames, fmt.Sprintf("%s %s", existingIP, existingUser))
		}
	}

	if !userExists {
		updatedLinesUsernames = append(updatedLinesUsernames, fmt.Sprintf("%s %s", ip, username))
	}

	if err := scanner.Err(); err != nil {
		log.Println("error reading usernames file,", err)
		return err
	}

	fileUsernames.Seek(0, 0)
	fileUsernames.Truncate(0)

	for _, line := range updatedLinesUsernames {
		_, err := fileUsernames.WriteString(line + "\n")
		if err != nil {
			log.Println("error writing to usernames file,", err)
			return err
		}
	}

	// Now update allowlist text file
	currentTime := time.Now().Unix()
	var updatedLinesAllowlist []string
	ipExists := false

	fileAllowlist, err := os.OpenFile(allowlistFile, os.O_RDWR|os.O_CREATE, 0644)
	if err != nil {
		log.Println("error opening allowlist file,", err)
		return err
	}
	defer fileAllowlist.Close()

	scanner = bufio.NewScanner(fileAllowlist)
	for scanner.Scan() {
		line := scanner.Text()
		parts := strings.Split(line, " ")
		if len(parts) != 2 {
			continue
		}
		existingIP := parts[0]
		timestamp := parts[1]

		if existingIP == ip {
			updatedLinesAllowlist = append(updatedLinesAllowlist, fmt.Sprintf("%s %d", ip, currentTime))
			ipExists = true
		} else {
			updatedLinesAllowlist = append(updatedLinesAllowlist, fmt.Sprintf("%s %s", existingIP, timestamp))
		}
	}

	if !ipExists {
		updatedLinesAllowlist = append(updatedLinesAllowlist, fmt.Sprintf("%s %d", ip, currentTime))
	}

	if err := scanner.Err(); err != nil {
		log.Println("error reading allowlist file,", err)
		return err
	}

	fileAllowlist.Seek(0, 0)
	fileAllowlist.Truncate(0)

	for _, line := range updatedLinesAllowlist {
		_, err := fileAllowlist.WriteString(line + "\n")
		if err != nil {
			log.Println("error writing to allowlist file,", err)
			return err
		}
	}

	return nil
}

func cleanupExpiredIPs() {
	log.Println("Cleaning up expired IPs")
	allowlistMutex.Lock()
	defer allowlistMutex.Unlock()

	expiredIPs := make(map[string]struct{})

	currentTime := time.Now().Unix()
	var updatedLines []string

	file, err := os.OpenFile(allowlistFile, os.O_RDWR|os.O_CREATE, 0644)
	if err != nil {
		log.Println("error opening allowlist file,", err)
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
			log.Printf("Expired IP removed: %s\n", ip)
			expiredIPs[ip] = struct{}{}
		}
	}

	if err := scanner.Err(); err != nil {
		log.Println("error reading allowlist file,", err)
		return
	}

	file.Seek(0, 0)
	file.Truncate(0)

	for _, line := range updatedLines {
		_, err := file.WriteString(line + "\n")
		if err != nil {
			log.Println("error writing to allowlist file,", err)
		}
	}

	// No need to proceed if no IPs were expired
	if len(expiredIPs) == 0 {
		return
	}

	// Now clear the expired IPs from usernames mapping file
	var updatedLinesUsernames []string
	fileUsernames, err := os.OpenFile(usernamesFile, os.O_RDWR|os.O_CREATE, 0644)
	if err != nil {
		log.Println("error opening usernames file,", err)
		return
	}
	defer fileUsernames.Close()

	scanner = bufio.NewScanner(fileUsernames)
	for scanner.Scan() {
		line := scanner.Text()
		parts := strings.Split(line, " ")
		if len(parts) != 2 {
			continue
		}
		existingIP := parts[0]
		existingUser:= parts[1]
		// skip lines with expired IPs
		if _, exists := expiredIPs[existingIP]; !exists {
			updatedLinesUsernames = append(updatedLinesUsernames, fmt.Sprintf("%s %s", existingIP, existingUser))
		}
	}

	if err := scanner.Err(); err != nil {
		log.Println("error reading usernames file,", err)
		return
	}

	fileUsernames.Seek(0, 0)
	fileUsernames.Truncate(0)

	for _, line := range updatedLinesUsernames {
		_, err := fileUsernames.WriteString(line + "\n")
		if err != nil {
			log.Println("error writing to usernames file,", err)
			return
		}
	}
}

