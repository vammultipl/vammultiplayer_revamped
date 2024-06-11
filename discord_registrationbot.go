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
			}
		} else {
			fmt.Println("Invalid command: ", m.Content)
			s.ChannelMessageSend(m.ChannelID, "Invalid command or IP address format. Please use /register 123.45.67.89")
		}
	}
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

