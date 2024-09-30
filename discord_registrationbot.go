package main

import (
	"bufio"
	"bytes"
	"fmt"
	"io/ioutil"
	"log"
	"net"
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
	token	       string
	allowlistFile	    = "allowlist.txt" // user IP allowlist
	usernamesFile	    = "usernames_ips.txt" // mapping of IPs into usernames
	alwaysMonitorFileName = "always_monitor_channel.txt" // channel to always monitor (optional)
	guildIDFileName = "guild_id.txt" // ID of the Discord server (used to fetch user nickname when they register)
	guildID = "" // retrieved from guild_id.txt
	expirationTime = 7 * 24 * 1 * time.Hour // 1 week expiration
	allowlistMutex		sync.Mutex // Mutex to protect access to the allowlist and usernames file
	prevPlayerStatus string = ""
	monitoredChannels = make(map[string]time.Time) // monitoring enabled channels by /monitor command
	mu		 sync.Mutex // mutex protecting monitoredChannels
	monitorMaxHours int = 16 // monitor for max 16 hours

	trackingFile      = "tracking.txt"
	trackingMutex      sync.Mutex
	notifiedMutex      sync.Mutex
	notifiedTrackings  = make(map[string]map[string]bool) // trackedUser -> tracker -> bool
	discordSession *discordgo.Session
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

	// Read the guild ID
	readGuildID()

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
	discordSession = dg

	// Register the messageCreate func as a callback for MessageCreate events.
	dg.AddHandler(func(s *discordgo.Session, m *discordgo.MessageCreate) {
		messageCreate(s, m, channelName)
	})
	// In this example, we only care about receiving message events.
	dg.Identify.Intents = discordgo.IntentsGuildMessages | discordgo.IntentsDirectMessages | discordgo.IntentsGuildMembers

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

	// Initialize the always monitor channel functionality
	alwaysMonitorChannel()

	log.Println("Bot is now running. Press CTRL+C to exit.")
	// Wait here until CTRL+C or other term signal is received.
	sc := make(chan os.Signal, 1)
	signal.Notify(sc, syscall.SIGINT, syscall.SIGTERM, os.Interrupt, os.Kill)
	<-sc

	// Cleanly close down the Discord session.
	dg.Close()
}

func readGuildID() {
	// Check if the guild ID file exists
	if _, err := os.Stat(guildIDFileName); os.IsNotExist(err) {
		// File does not exist, set guildID to an empty string
		guildID = ""
		return
	}

	// Read the contents of the guild ID file
	data, err := ioutil.ReadFile(guildIDFileName)
	if err != nil {
		log.Printf("Failed to read %s: %v", guildIDFileName, err)
		guildID = "" // Set to empty string on error
		return
	}

	// Trim whitespace from the read data and set guildID
	guildID = strings.TrimSpace(string(data))
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

    // Split the message into command and arguments
    parts := strings.Fields(m.Content)
    if len(parts) == 0 {
        return
    }

    command := parts[0]
    args := parts

    switch command {
    case "/register":
        handleRegisterCommand(s, m, channel, args)
    case "/state":
        handleStateCommand(s, m)
    case "/monitor":
        handleMonitorCommand(s, m)
    case "/track":
        handleTrackCommand(s, m, args)
    case "/untrack":
        handleUntrackCommand(s, m, args)
    default:
        // Respond with detailed usage info for any other message
        sendUnknownCommandResponse(s, m)
    }
}

// handleStateCommand processes the /state command.
func handleStateCommand(s *discordgo.Session, m *discordgo.MessageCreate) {
    gameStatus, err := getCurrentGameStatus()
    if err != nil {
        log.Println("Error reading game status: ", err)
        s.ChannelMessageSend(m.ChannelID, "Error retrieving game status.")
        return
    }
    s.ChannelMessageSend(m.ChannelID, gameStatus)
}

// handleRegisterCommand processes the /register <IP> command.
func handleRegisterCommand(s *discordgo.Session, m *discordgo.MessageCreate, channel *discordgo.Channel, args []string) {
    if len(args) < 2 {
        log.Println("Invalid /register command format.")
        s.ChannelMessageSend(m.ChannelID, "Invalid command or IP address format. Please use /register 123.45.67.89")
        return
    }

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

    ip := strings.TrimSpace(args[1])
    if !isValidIP(ip) {
        log.Println("Register: invalid IP: ", ip)
        s.ChannelMessageSend(m.ChannelID, "Invalid IP address format. Please use /register 123.45.67.89")
        return
    }

    if isLocalIP(ip) {
        log.Println("Register: local IP not allowed: ", ip)
        s.ChannelMessageSend(m.ChannelID, "Local IP addresses are not allowed. Please use a public IPv4 address.")
        return
    }

    //// Get the nickname used by the user on the server
    //username := getUsernameFromMember(s, m, m.Author.ID)

    // Store unique usernames in the backend, present nicknames to user in the frontend (bot status)
    username := m.Author.Username

    // Register IP in allowlist txt file and file with IP to username mapping
    err := registerIP(ip, username)
    if err != nil {
        log.Println("error: failed to register IP: ", ip)
        s.ChannelMessageSend(m.ChannelID, fmt.Sprintf("Failed to register IP"))
        return
    }

    log.Println("Registered IP: ", ip)
    s.ChannelMessageSend(m.ChannelID, fmt.Sprintf("Your IP address %s has been successfully registered/refreshed. You can now connect to the game.", ip))
}

//// getUsernameFromMember retrieves the username or nickname of a guild member.
//func getUsernameFromMember(s *discordgo.Session, m *discordgo.MessageCreate, userID string) string {
//    username := ""
//
//    // Check if the guild ID exists (i.e., this is in a server context)
//    if guildID != "" {
//        // Fetch the guild member to retrieve their guild-specific information
//        member, err := s.GuildMember(guildID, userID)
//        if err != nil {
//            // Handle the error appropriately, fall back to the author's username if we can't fetch the member
//            log.Printf("Error fetching guild member: %v", err)
//            username = m.Author.Username
//        } else {
//            // Priority 1: Check if the member has a guild-specific nickname
//            username = member.Nick
//            // Priority 2: If no nickname, check if they have a global display name (if supported by your API version)
//            if username == "" && member.User.GlobalName != "" {
//                username = member.User.GlobalName
//            }
//            // Priority 3: If neither nickname nor global display name is available, use the username
//            if username == "" {
//                username = member.User.Username
//            }
//        }
//    } else {
//        // If no guild context, just use the author's username (DMs or other cases)
//        username = m.Author.Username
//    }
//
//    return username
//}

// sendUnknownCommandResponse sends a response for unknown commands.
func sendUnknownCommandResponse(s *discordgo.Session, m *discordgo.MessageCreate) {
    url := "https://www.google.com/search?q=google+what+is+my+ip"
    text := fmt.Sprintf("Unknown command. Here are the commands you can use:\n\n" +
        "1. `/register <IP>` - Register your IP address with the VaM multiplayer server via DM to the bot. This will gain you entry to the server with 1 week expiration. If you cannot connect to the server in VaM, register again. To find your IP, visit the link below. Link:\n%s\n\n" +
        "2. `/state` - Check the current game status to see who is playing. You can also see the same info in my status on Discord updated every 20s.\n\n" +
        "3. `/monitor <hours>` - Enable monitoring for game status changes on this channel for X hours (useful for notifications)\n\n" +
        "4. `/track <username>` - Track when a user joins the game.\n" +
        "5. `/untrack <username>` - Stop tracking user.\n\n" +
        "Please use one of the above commands.\n", url)
    s.ChannelMessageSend(m.ChannelID, text)
}



// alwaysMonitorChannel checks if always_monitor_channel.txt file is present, and if so,
// reads the channel ID from the file and sets its expiry time to 999999 hours in the future.
func alwaysMonitorChannel() {
	if _, err := os.Stat(alwaysMonitorFileName); os.IsNotExist(err) {
		// File does not exist
		return
	}

	data, err := ioutil.ReadFile(alwaysMonitorFileName)
	if err != nil {
		log.Printf("Failed to read %s: %v", alwaysMonitorFileName, err)
		return
	}

	channelID := strings.TrimSpace(string(data))
	expiryTime := time.Now().Add(999999 * time.Hour)

	mu.Lock()
	monitoredChannels[channelID] = expiryTime
	mu.Unlock()

	log.Printf("Channel %s will be monitored for 999999 hours.", channelID)
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

	// no-op for now - rely on alwaysMonitorChannel

//	// Calculate the expiration time
//	expiryTime := time.Now().Add(time.Duration(hours) * time.Hour)
//
//	// Update the monitored channels map
//	mu.Lock()
//	monitoredChannels[m.ChannelID] = expiryTime
//	mu.Unlock()

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

	return fmt.Sprintf("-----------------\n%s\n%s\n\n", statusRoom1, statusRoom2), nil
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

func getUsernameFromIP(ip string) (string, error) {
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
        parts := strings.SplitN(line, " ", 2)
        if len(parts) != 2 {
            continue
        }

        if parts[0] == ip {
            uniqueUsername := strings.TrimSpace(parts[1])
            return getProcessedUsername(discordSession, guildID, uniqueUsername)
        }
    }

    if err := scanner.Err(); err != nil {
        return "", err
    }

    return "", fmt.Errorf("IP %s not found", ip)
}

func getProcessedUsername(s *discordgo.Session, guildID, uniqueUsername string) (string, error) {
    if s == nil {
        return uniqueUsername, nil
    }
    // Fetch the user by their unique username
    users, err := s.GuildMembersSearch(guildID, uniqueUsername, 1)
    if err != nil {
        return uniqueUsername, fmt.Errorf("error searching for user: %v", err)
    }

    if len(users) == 0 {
        return uniqueUsername, nil // User not found, return the unique username
    }

    member := users[0]

    // Priority 1: Check if the member has a guild-specific nickname
    if member.Nick != "" {
        return member.Nick, nil
    }

    // Priority 2: Check if they have a global display name
    if member.User.GlobalName != "" {
        return member.User.GlobalName, nil
    }

    // Priority 3: If neither nickname nor global display name is available, use the unique username
    return uniqueUsername, nil
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

func isLocalIP(ip string) bool {
    ipAddress := net.ParseIP(strings.TrimSpace(ip))
    if ipAddress == nil {
        return false
    }

    // Check for private IP ranges
    privateIPRanges := []struct {
        start net.IP
        end   net.IP
    }{
        {net.ParseIP("10.0.0.0"), net.ParseIP("10.255.255.255")},
        {net.ParseIP("172.16.0.0"), net.ParseIP("172.31.255.255")},
        {net.ParseIP("192.168.0.0"), net.ParseIP("192.168.255.255")},
        {net.ParseIP("127.0.0.0"), net.ParseIP("127.255.255.255")},
    }

    for _, r := range privateIPRanges {
        if bytes.Compare(ipAddress, r.start) >= 0 && bytes.Compare(ipAddress, r.end) <= 0 {
            return true
        }
    }

    return false
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
    if err != nil {
        log.Println("Error getting game status:", err)
        return
    }

    if gameStatus != prevPlayerStatus {
        updateMonitoredChannelsWithStatus(s, gameStatus)

        // Parse previous players from prevPlayerStatus
        previousPlayers, err := parsePlayerStatus(prevPlayerStatus)
        if err != nil {
            log.Println("Error parsing previous player status:", err)
            previousPlayers = make(map[string]struct{}) // Assume no previous players
        }

        // Update previousPlayerStatus
        prevPlayerStatus = gameStatus

        // Parse current players from gameStatus
        currentPlayers, err := parsePlayerStatus(gameStatus)
        if err != nil {
            log.Println("Error parsing player status:", err)
            return
        }

        // Determine newly joined players
        newlyJoinedPlayers := []string{}
        for player := range currentPlayers {
            if _, exists := previousPlayers[player]; !exists {
                newlyJoinedPlayers = append(newlyJoinedPlayers, player)
            }
        }

        // Determine disconnected players
        disconnectedPlayers := []string{}
        for player := range previousPlayers {
            if _, exists := currentPlayers[player]; !exists {
                disconnectedPlayers = append(disconnectedPlayers, player)
            }
        }

        // Convert newly joined players to Discord usernames
        var newlyJoinedUsernames []string
        for _, player := range newlyJoinedPlayers {
            user, err := findUserInGuild(s, guildID, player)
            if err != nil {
                log.Printf("Error finding user for player %s: %v", player, err)
                continue
            }
            newlyJoinedUsernames = append(newlyJoinedUsernames, user.Username)
        }

        // Notify trackers about newly joined players (send DMs)
        if len(newlyJoinedUsernames) > 0 {
            notifyTrackers(s, newlyJoinedUsernames)
        }

        // Reset notifiedTrackings for disconnected players
        for _, player := range disconnectedPlayers {
            user, err := findUserInGuild(s, guildID, player)
            if err != nil {
                log.Printf("Error finding user for player %s: %v", player, err)
                continue
            }
            resetNotified(user.Username)
        }

        // Discord limitation on status length
        if len(gameStatus) > 125 {
            err = s.UpdateCustomStatus("Send /state command to check the state of rooms")
        } else {
            err = s.UpdateCustomStatus(gameStatus)
        }

        if err != nil {
            log.Println("Error updating custom status:", err)
        }
    }
}

// parsePlayerStatus parses the game status string and returns a set of usernames.
func parsePlayerStatus(gameStatus string) (map[string]struct{}, error) {
    players := make(map[string]struct{})

    // Split the status by lines
    lines := strings.Split(gameStatus, "\n")
    for _, line := range lines {
        line = strings.TrimSpace(line)
        if line == "" {
            continue
        }

        // Example player line formats:
        // basketcase controls AVATAR3.
        // clyd3division controls AVATAR1.

        if strings.Contains(line, "controls") {
            parts := strings.Split(line, "controls")
            if len(parts) >= 1 {
                username := strings.TrimSpace(parts[0])
                // Remove any leading/trailing characters like bullets or numbering if present
                username = strings.Trim(username, "•- ")
                if username != "" {
                    players[username] = struct{}{}
                }
            }
        }
    }

    return players, nil
}


func notifyTrackers(s *discordgo.Session, newlyJoinedPlayers []string) {
    log.Printf("Entering notifyTrackers function with players: %v", newlyJoinedPlayers)

    // Get the current tracking data
    trackingMutex.Lock()
    trackedMap, err := getTrackedUsers()
    trackingMutex.Unlock()

    if err != nil {
        log.Printf("Error reading tracking data: %v", err)
        return
    }

    log.Printf("Current tracked map: %+v", trackedMap)

    for _, player := range newlyJoinedPlayers {
        log.Printf("Processing player: %s", player)
        trackers, exists := trackedMap[player]
        if !exists {
            log.Printf("No trackers found for player: %s", player)
            continue
        }

        log.Printf("Trackers for %s: %v", player, trackers)

        for _, tracker := range trackers {
            log.Printf("Checking if %s has been notified about %s", tracker, player)
            if !hasNotified(tracker, player) {
                log.Printf("Sending DM to %s about %s", tracker, player)
                // Send DM to tracker
                go sendDM(s, tracker, player)
                // Mark as notified
                markAsNotified(tracker, player)
            } else {
                log.Printf("%s has already been notified about %s", tracker, player)
            }
        }
    }

    log.Printf("Exiting notifyTrackers function")
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

// Track and Untrack commands: users can get private DMs when someone who they track joins game

// getTrackedUsers reads the tracking.txt file and returns a map of tracked users to their trackers.
func getTrackedUsers() (map[string][]string, error) {
    trackedMap := make(map[string][]string)

    file, err := os.Open(trackingFile)
    if err != nil {
        if os.IsNotExist(err) {
            // If the file doesn't exist, return an empty map
            return trackedMap, nil
        }
        return nil, err
    }
    defer file.Close()

    scanner := bufio.NewScanner(file)
    for scanner.Scan() {
        line := strings.TrimSpace(scanner.Text())
        if line == "" {
            continue
        }
        parts := strings.SplitN(line, " ", 2)
        if len(parts) != 2 {
            log.Printf("Invalid tracking line format: %s", line)
            continue
        }
        trackedUser := parts[0]
        trackers := strings.Split(parts[1], ",")
        for i, tracker := range trackers {
            trackers[i] = strings.TrimSpace(tracker)
        }
        trackedMap[trackedUser] = trackers
    }

    if err := scanner.Err(); err != nil {
        return nil, err
    }

    return trackedMap, nil
}

// addTracking adds a trackedUser to the tracker's tracking list in tracking.txt.
func addTracking(tracker, trackedUser string) error {
    trackingMutex.Lock()
    defer trackingMutex.Unlock()

    // Read existing tracking data
    trackedMap, err := getTrackedUsers()
    if err != nil {
        return err
    }

    // Append the tracker to the trackedUser's list of trackers in tracking.txt.
    trackedMap[trackedUser] = appendIfMissing(trackedMap[trackedUser], tracker)

    // Write back to tracking.txt
    return writeTrackingData(trackedMap)
}

// appendIfMissing appends an item to a slice if it's not already present.
func appendIfMissing(slice []string, item string) []string {
    for _, v := range slice {
        if v == item {
            return slice
        }
    }
    return append(slice, item)
}

func removeTracking(tracker, trackedUser string) error {
    trackingMutex.Lock()
    defer trackingMutex.Unlock()

    // Read existing tracking data
    trackedMap, err := getTrackedUsers()
    if err != nil {
        log.Printf("Error getting tracked users: %v", err)
        return err
    }

    // Remove the tracker from the trackedUser's tracker list
    if trackers, exists := trackedMap[trackedUser]; exists {
        updatedTrackers := removeFromSlice(trackers, tracker)
        if len(updatedTrackers) == 0 {
            delete(trackedMap, trackedUser)
        } else {
            trackedMap[trackedUser] = updatedTrackers
        }
    } else {
        return fmt.Errorf("you are not tracking %s", trackedUser)
    }

    // Write back to tracking.txt
    return writeTrackingData(trackedMap)
}

func removeFromSlice(slice []string, item string) []string {
    updated := []string{}
    for _, v := range slice {
        if v != item {
            updated = append(updated, v)
        }
    }
    return updated
}

func writeTrackingData(trackedMap map[string][]string) error {
    file, err := os.OpenFile(trackingFile, os.O_WRONLY|os.O_CREATE|os.O_TRUNC, 0644)
    if err != nil {
        log.Printf("Error opening tracking file: %v", err)
        return err
    }
    defer file.Close()

    writer := bufio.NewWriter(file)
    for trackedUser, trackers := range trackedMap {
        line := fmt.Sprintf("%s %s\n", trackedUser, strings.Join(trackers, ","))
        if _, err := writer.WriteString(line); err != nil {
            log.Printf("Error writing line: %v", err)
            return err
        }
    }

    return writer.Flush()
}

// handleTrackCommand processes the /track <username> command.
func handleTrackCommand(s *discordgo.Session, m *discordgo.MessageCreate, args []string) {
    if len(args) < 2 {
        s.ChannelMessageSend(m.ChannelID, "Usage: /track <username>")
        return
    }

    tracker := m.Author.Username
    trackedUserIdentifier := strings.TrimSpace(args[1])

    if trackedUserIdentifier == "" {
        s.ChannelMessageSend(m.ChannelID, "Please provide a valid username to track.")
        return
    }

    // Find the user in the guild
    trackedUser, err := findUserInGuild(s, guildID, trackedUserIdentifier)
    if err != nil {
        s.ChannelMessageSend(m.ChannelID, fmt.Sprintf("Error: %v", err))
        return
    }

    // Use the unique username for tracking
    err = addTracking(tracker, trackedUser.Username)
    if err != nil {
        log.Printf("Error adding tracking: %v", err)
        s.ChannelMessageSend(m.ChannelID, fmt.Sprintf("Failed to track %s. Error: %v", trackedUser.Username, err))
        return
    }

    s.ChannelMessageSend(m.ChannelID, fmt.Sprintf("You are now tracking %s.", trackedUser.Username))
}

// handleUntrackCommand processes the /untrack <username> command.
func handleUntrackCommand(s *discordgo.Session, m *discordgo.MessageCreate, args []string) {
    if len(args) < 2 {
        s.ChannelMessageSend(m.ChannelID, "Usage: /untrack <username>")
        return
    }

    tracker := m.Author.Username
    trackedUserIdentifier := strings.TrimSpace(args[1])

    if trackedUserIdentifier == "" {
        s.ChannelMessageSend(m.ChannelID, "Please provide a valid username to untrack.")
        return
    }

    // Find the user in the guild
    trackedUser, err := findUserInGuild(s, guildID, trackedUserIdentifier)
    if err != nil {
        s.ChannelMessageSend(m.ChannelID, fmt.Sprintf("Error: %v", err))
        return
    }

    // Use the unique username for untracking
    err = removeTracking(tracker, trackedUser.Username)
    if err != nil {
        log.Printf("Error removing tracking: %v", err)
        s.ChannelMessageSend(m.ChannelID, fmt.Sprintf("Failed to untrack %s. Error: %v", trackedUser.Username, err))
        return
    }

    s.ChannelMessageSend(m.ChannelID, fmt.Sprintf("You have stopped tracking %s.", trackedUser.Username))
}

func sendDM(s *discordgo.Session, tracker, trackedUser string) {
    user, err := findUserInGuild(s, guildID, tracker)
    if err != nil {
        log.Printf("Failed to find user %s: %v", tracker, err)
        return
    }

    message := fmt.Sprintf("👀 **%s** just joined! Come get'em!", trackedUser)

    channel, err := s.UserChannelCreate(user.ID)
    if err != nil {
        log.Printf("Failed to create DM channel for %s: %v", tracker, err)
        return
    }

    _, err = s.ChannelMessageSend(channel.ID, message)
    if err != nil {
        log.Printf("Failed to send DM to %s: %v", tracker, err)
        return
    }

    log.Printf("Successfully sent DM to %s about %s joining.", tracker, trackedUser)
}

// Helper function to find a user in the guild
func findUserInGuild(s *discordgo.Session, guildID string, userIdentifier string) (*discordgo.User, error) {
    members, err := s.GuildMembers(guildID, "", 1000)
    if err != nil {
        return nil, fmt.Errorf("error fetching guild members: %v", err)
    }

    for _, member := range members {
        if member.User.Username == userIdentifier ||
           member.Nick == userIdentifier ||
           member.User.GlobalName == userIdentifier {
            return member.User, nil
        }
    }

    return nil, fmt.Errorf("user not found in guild")
}

func hasNotified(tracker, trackedUser string) bool {
    notifiedMutex.Lock()
    defer notifiedMutex.Unlock()

    if trackers, exists := notifiedTrackings[trackedUser]; exists {
        if _, notified := trackers[tracker]; notified {
            return true
        }
    }

    return false
}

func markAsNotified(tracker, trackedUser string) {
    notifiedMutex.Lock()
    defer notifiedMutex.Unlock()

    if _, exists := notifiedTrackings[trackedUser]; !exists {
        notifiedTrackings[trackedUser] = make(map[string]bool)
    }

    notifiedTrackings[trackedUser][tracker] = true
}

// resetNotified removes all notification records for the trackedUser.
func resetNotified(trackedUser string) {
    notifiedMutex.Lock()
    defer notifiedMutex.Unlock()

    delete(notifiedTrackings, trackedUser)
}

