#include <FlashAsEEPROM.h>
#include <FlashStorage.h>
#include <PubSubClient.h>
#include <ArduinoJson.h>
#include <ArduinoJson.hpp>
#include <RPLidar.h>
#include <WiFiNINA.h>
#include <stdint.h> 

#define EEPROM_SSID_ADDR 0          //  Start address for SSID
#define EEPROM_PASS_ADDR 32         //  Start address for Password
#define MAX_SSID_LENGTH 31          //  31 + 1 for null terminator
#define MAX_PASS_LENGTH 63          //  63 + 1 for null terminator


#define RPLIDAR_MOTOR 3 // Pin connected to the RPLIDAR motor control (blue wire)
// Define the hardware serial port for the LiDAR
#define RPLIDAR_SERIAL Serial1
enum class State {
  IDLE,    // Waiting for command
  RUNNING, // LiDAR active
  PAUSED   // LiDAR paused
};
#define MAX_BATCH_SIZE 100  //  Define max batch size to prevent overflow
struct WiFiSettings {
  char ssid[32];
  char password[64];
};


WiFiServer server(80);
FlashStorage(wifiConfig, WiFiSettings);

WiFiSettings settings;
bool wifiConfigured = false;

char* ssid = "";
char* password = "";
//char* ssid = "Telia-2G-B9A841";
//char* password = "nyUu4MgAWhTG";

bool shouldRestart = false;

State currentState = State::IDLE; // Start in the IDLE state
bool useSerial = false;
bool useMQTT = false;
bool isReady = false;

RPLidar lidar;
unsigned long lastStateUpdate = 0;
const unsigned long stateUpdateInterval = 1000; // 1 second

unsigned long lastKeepAliveTime = 0;
const unsigned long timeout = 5000;


const char* mqttServer = "192.168.0.44"; // Public MQTT broker
const int mqttPort = 1883;
const char* dataTopic = "lidar/data";       // Topic for publishing LiDAR data
const char* commandTopic = "lidar/commands"; // Topic for receiving commands
const char* statusTopic = "lidar/status";
int batchSize = 45;  //  Tune this for better performance
//String dataBatch[batchSize];
String mode = "";

WiFiClient wifiClient;
PubSubClient mqttClient(wifiClient);
int batchIndex = 0;
////
//JSON data: batchsize 10
//byte data (7): batchsize 30
//byte data(5) : batchsize 45

#pragma pack(push, 1)  //  Ensure 1-byte memory alignment
struct LidarPoint {
  uint16_t angle;    //  2 bytes (was 4 as float)
  uint16_t distance; //  2 bytes (was 4 as int)
  byte quality;     //  1 byte
};
#pragma pack(pop)
LidarPoint lidarPoints[MAX_BATCH_SIZE];

void handleCommand(String command, String com) {
  String message = "Received command from " + com + " protocol";
  SendStatusMessage(message.c_str());
  if (command == "keepAlive") {
    lastKeepAliveTime = millis();
  }
  if (command.startsWith("SET_MODE:WIFI") || command == "w") {
    SendStatusMessage("Received SET MODE: WIFI Command ");
    startWifiCommunication();
    mode = "WIFI";
    useSerial = false;
    useMQTT = true;
  }
  if (command.startsWith("SET_MODE:SERIAL") || command == "q") {
    SendStatusMessage("Received SET MODE: Serial Command ");

    mode = "SERIAL";
    useSerial = true;
    useMQTT = false;
  }
  if (command.startsWith("SETTINGS:")) {
    String jsonSettings = command.substring(9);  // Extract JSON part
    applySettings(jsonSettings);
  }
  if (command.startsWith("M")) {
    int pwmValue = command.substring(1).toInt(); // Extract PWM value
    pwmValue = constrain(pwmValue, 0, 255);     // Clamp to valid range
    analogWrite(RPLIDAR_MOTOR, pwmValue);       // Set motor speed
    delay(100);                                 // Allow motor to stabilize
    SendStatusMessage(("Motor speed set to PWM: " + String(pwmValue)).c_str());
  }
  else if (command == "s") { // Start command
    resetLidar();
    analogWrite(RPLIDAR_MOTOR, 200); // 5 RPM
    delay(1000);                    // Allow motor to stabilize
    u_result result = lidar.startScan();
    if (IS_OK(result)) {
      currentState = State::RUNNING;
      SendStatusMessage("Running");

    }
    else {
      SendStatusMessage(" Failed to start LiDAR. Error code: ");
      SendStatusMessage(result, HEX);
      currentState = State::IDLE;
      analogWrite(RPLIDAR_MOTOR, 0);
      SendStatusMessage("Failed to start LiDAR.");
    }
    SendState();
  }
  else if (command == "p") { // Pause command
    currentState = State::PAUSED;
    analogWrite(RPLIDAR_MOTOR, 0); // Stop motor
    lidar.stop();
    SendStatusMessage("Paused");
    SendState();
  }
  else if (command == "STATE") { // Request state command
    SendState();
  }
  else if (command == "PING") {
    String response = "Arduino Online! ";
    response += "Mode: " + String(useSerial ? "Serial" : "WiFi") + " | ";

    if (useMQTT) {
      response += "WiFi: " + String(WiFi.SSID()) + " | ";
      response += "IP: " + WiFi.localIP().toString() + " | ";
      response += "MQTT: " + String(mqttClient.connected() ? "Connected" : "Disconnected") + " | ";
    }
    else {
      response += "WiFi: Not Connected | ";
    }

    response += "Firmware: v1.0.0 | Ready for commands.";

    SendStatusMessage(response.c_str());  //  Send via Serial or MQTT
  }

  else {
    SendStatusMessage("Unknown command received.");
  }
}
void applySettings(String json) {
  StaticJsonDocument<200> doc;
  DeserializationError error = deserializeJson(doc, json);

  if (error) {
    Serial.println("Error parsing settings!");
    return;
  }

  if (doc.containsKey("BatchSize")) {
    batchSize = doc["BatchSize"];
  }
  //if (doc.containsKey("ScanSpeed")) {
  //  scanSpeed = doc["ScanSpeed"];
  //}
  //if (doc.containsKey("QualityThreshold")) {
  //  qualityThreshold = doc["QualityThreshold"];
  //}

  SendStatusMessage("Settings updated.");
}
void callback(char* topic, byte* payload, unsigned int length) {

  // Convert the payload into a String
  String command = "";
  for (unsigned int i = 0; i < length; i++) {
    command += (char)payload[i];
  }

  String msg = "Message arrived in topic: " + String(topic) + ": " +String(command);
  SendStatusMessage(msg);

  handleCommand(command, "MQTT");


}
void SendStatusMessage(String msg) {
  SendStatusMessage(msg.c_str());  // Convert `String` to `const char*`
}
void SendStatusMessage(const char* msg) {

  String statusMessage = "Status " + String(msg);  // Append message

  if (useSerial) {
    Serial.println(statusMessage);  // Send over Serial
  }
  else if (useMQTT) {
    if (!statusMessage.startsWith("State:")) {
      mqttClient.publish(statusTopic, statusMessage.c_str());  // Send over MQTT
    }
  }
  else {
    Serial.println("No mode set \n" + statusMessage);
  }
}
// Overload for numbers (int, float, double)
void SendStatusMessage(int value) {
  SendStatusMessage(String(value));  // Convert `int` to `String`
}
void SendStatusMessage(float value) {
  SendStatusMessage(String(value, 2));  // Convert `float` to `String` (2 decimal places)
}
void SendStatusMessage(double value) {
  SendStatusMessage(String(value, 2));  // Convert `double` to `String` (2 decimal places)
}
// Overload for boolean values
void SendStatusMessage(bool value) {
  SendStatusMessage(value ? "true" : "false");  // Convert `bool` to `String`
}
//  Overload for IPAddress
void SendStatusMessage(IPAddress ip) {
  String ipString = String(ip[0]) + "." +
    String(ip[1]) + "." +
    String(ip[2]) + "." +
    String(ip[3]);  // Convert IPAddress to readable string

  SendStatusMessage(ipString);  // Pass it to the existing function
}
//  Overload to send numerical values in HEX format
void SendStatusMessage(u_result value, int format) {
  if (format == HEX) {
    String hexString = "0x" + String(value, HEX);  // Convert number to hex
    SendStatusMessage(hexString.c_str());         // Call existing function
  }
  else {
    SendStatusMessage(String(value).c_str());     // Convert normally
  }
}
void SendState() {
  switch (currentState) {
  case State::IDLE:
    SendStatusMessage("State: IDLE");
    break;
  case State::RUNNING:
    SendStatusMessage("State: RUNNING");
    break;
  case State::PAUSED:
    SendStatusMessage("State: PAUSED");
    break;
  }
}
void startWifiCommunication() {
  Serial.println("WiFi mode selected. Connecting...");
  server.begin();
  wifiConfigured = connectWiFi(ssid, password);

  if (!wifiConfigured) {
    Serial.println("No valid WiFi stored. Waiting for settings over Serial...");
    waitForWiFiConfig(); // Wait for new settings
  }

  mqttClient.loop();
  useMQTT = true;
  SendStatusMessage("LiDAR set to use MQTT");
  Serial.println("LiDAR set to use MQTT");
}
void connectMQTT() {
  while (!mqttClient.connected()) {
    Serial.print("Attempting MQTT connection...");

    // Attempt to connect
    if (mqttClient.connect("MKR1010Client")) { 
      Serial.println("Connected to MQTT broker!");

      // Subscribe to the command topic
      mqttClient.subscribe(commandTopic);
      Serial.println("Subscribed to command topic: " + String(commandTopic));

      useMQTT = true;
    }
    else {
      Serial.print("Failed, rc=");
      Serial.print(mqttClient.state()); // Print the connection error code
      Serial.println(" Retrying in 5 seconds...");
      delay(5000); // Wait before retrying
    }
  }
}
void resetLidar() {
  Serial.println("Resetting LiDAR...");
  lidar.stop(); // Stop any previous operation
  delay(2000);   // Allow some time for the LiDAR to stabilize
}
bool checkSerialConnection() {
  Serial.begin(115200);
  RPLIDAR_SERIAL.begin(115200);

  delay(500); // Give time for Serial to initialize

  if (Serial) {  //  Checks if the Serial connection is active
    Serial.println("Serial port is connected!");
    return true;
  }

  Serial.println("Serial port not detected.");
  return false;
}
bool connectWiFi(const char* ssid, const char* password) {
  int attempts = 0;
  Serial.println("Using WiFi mode...");
  Serial.println(ssid);Serial.println(password);
  WiFi.begin(ssid, password);
  while (WiFi.status() != WL_CONNECTED && attempts < 10) {
    delay(1000);
    Serial.print(".");
    attempts++;
  }
  if (WiFi.status() == WL_CONNECTED) {
    SendStatusMessage("\nConnected to WiFi!");
    SendStatusMessage("IP Address: ");
    SendStatusMessage(WiFi.localIP());
    setupMQTT();
    return true;
  }
  SendStatusMessage("\nFailed to connect.");
  return false;


}
void setupMQTT() {
  mqttClient.setServer(mqttServer, mqttPort);
  mqttClient.setCallback(callback);
  connectMQTT();

  if (WiFi.status() == WL_CONNECTED && mqttClient.connected()) {
    mqttClient.setBufferSize(256);
    mqttClient.loop();
   
    SendStatusMessage("Connected to MQTT at server: " +  String(mqttServer));
  }
}
void waitForWiFiConfig() {
  String newSSID = "";
  String newPassword = "";

  Serial.println("Send WiFi credentials in format: SET_WIFI:SSID,PASSWORD");
  Serial.println("type c to skip and use serial");

  while (true) {
    if (Serial.available()) {
      String input = Serial.readStringUntil('\n');
      input.trim();

      if (input.startsWith("SET_WIFI:")) {
        int commaIndex = input.indexOf(",");
        if (commaIndex > 9) {
          newSSID = input.substring(9, commaIndex);
          newPassword = input.substring(commaIndex + 1);

          Serial.println("Received New WiFi Credentials!");
          saveWiFiSettings(newSSID.c_str(), newPassword.c_str());

          Serial.println("Restarting to apply new settings...");
          delay(2000);
          NVIC_SystemReset(); // Restart the board
        }
      }
      if (input == "c") {
        mode = "serial";
        useSerial = true;
        break;
      }
    }
    delay(100);
  }
}
void saveWiFiSettings(const char* newSSID, const char* newPassword) {
  Serial.println("Saving WiFi credentials to EEPROM...");

  //  Write SSID
  for (int i = 0; i < MAX_SSID_LENGTH; i++) {
    EEPROM.write(EEPROM_SSID_ADDR + i, newSSID[i]);
    if (newSSID[i] == '\0') break;  // Stop at null terminator
  }

  //  Write Password
  for (int i = 0; i < MAX_PASS_LENGTH; i++) {
    EEPROM.write(EEPROM_PASS_ADDR + i, newPassword[i]);
    if (newPassword[i] == '\0') break;  // Stop at null terminator
  }

  EEPROM.commit();  //  Ensure data is written
  Serial.println("Restarting to apply new settings...");
  delay(2000);
  NVIC_SystemReset();  //  Restart board
}
void loadWiFiSettings(char* ssid, char* password) {
  Serial.println(" Loading stored WiFi credentials...");

  //  Read SSID
  for (int i = 0; i < MAX_SSID_LENGTH; i++) {
    ssid[i] = EEPROM.read(EEPROM_SSID_ADDR + i);
    if (ssid[i] == '\0') break;  // Stop at null terminator
  }

  //  Read Password
  for (int i = 0; i < MAX_PASS_LENGTH; i++) {
    password[i] = EEPROM.read(EEPROM_PASS_ADDR + i);
    if (password[i] == '\0') break;  // Stop at null terminator
  }

  Serial.print(" Stored SSID: ");
  Serial.println(ssid);
  Serial.print(" Stored Password: ");
  Serial.println(password);
}
void checkForTimeout() {
  if (millis() - lastKeepAliveTime > timeout) {
    Serial.println("MonoGame is not responding, shutting down LiDAR...");
    analogWrite(RPLIDAR_MOTOR, 0);
    resetLidar();
  }
}
void reconnectMQTT() {
  while (!mqttClient.connected()) {
    Serial.println("Attempting MQTT Reconnection...");
    if (mqttClient.connect("LidarClient")) {  // Replace with a unique ID if needed
      Serial.println("MQTT Reconnected!");
      mqttClient.subscribe("lidar/commands");  //Re-subscribe to any necessary topics
      mqttClient.setBufferSize(512);
    }
    else {
      Serial.print("MQTT Reconnection Failed, Error Code: ");
      Serial.println(mqttClient.state());
      delay(5000);  // Wait before retrying
    }
  }
}
void setup() {
  SendState();
  pinMode(RPLIDAR_MOTOR, OUTPUT);
  analogWrite(RPLIDAR_MOTOR, 0);
  resetLidar();
  lidar.begin(RPLIDAR_SERIAL);
  useSerial = false;
  useMQTT = false;
  //  Check if LiDAR is connected via Serial
  bool serialConnected = checkSerialConnection();

  if (serialConnected) {
    Serial.println("LiDAR detected over Serial.");
    Serial.println("Input 'q' for serial mode, 'w' for WiFi mode.");

    while (!useSerial && !useMQTT) {
      if (Serial.available() > 0) {

        String command = Serial.readStringUntil('\n');
        command.trim();
        handleCommand(command, "Serial");
        break;
      }

      Serial.print(".");
      delay(2000);
    }
  }
  else {
    Serial.println("No serial connection detected. Trying WiFi mode...");

    wifiConfigured = connectWiFi(ssid, password);
    if (!wifiConfigured) {
      Serial.println("No valid WiFi stored. Waiting for settings over Serial...");
      waitForWiFiConfig(); // Wait for WiFi settings over Serial
    }
    mqttClient.loop();
    useMQTT = true;
    SendStatusMessage("LiDAR set to use MQTT with no serial connected");
  }
  Serial.println("Setup complete");
}
void loop() {


  // Check for c serial ommands from C# program

  if (Serial.available() > 0) {
    String command = Serial.readStringUntil('\n');
    handleCommand(command, "Serial");
  }


  if (useMQTT) {
    // mqttloop checks for callback commands over mqtt

    if (!mqttClient.connected()) {
      Serial.println("MQTT Disconnected! Reconnecting...");
      reconnectMQTT();
    }
    mqttClient.loop();
  }
  if (millis() - lastStateUpdate >= stateUpdateInterval) {
    lastStateUpdate = millis();
  }
  // Read and store LiDAR data in batches
  if (currentState == State::RUNNING) {
    if (lidar.waitPoint() == RESULT_OK) {
      float rawDistance = lidar.getCurrentPoint().distance;
      float rawAngle = lidar.getCurrentPoint().angle;
      byte quality = lidar.getCurrentPoint().quality;

      

      if (rawDistance > 200 && quality > 0 ) {
        uint16_t angle = (uint16_t)(rawAngle * 100);  // Store angle as int16_t (2 bytes)
        uint16_t distance = (uint16_t)rawDistance;  // Store distance as int16_t (2 bytes)

        // Store in batch array
        lidarPoints[batchIndex].angle = angle;
        lidarPoints[batchIndex].quality = quality;
        lidarPoints[batchIndex].distance = distance;

        batchIndex++;

        // Send batch when full
        if (batchIndex >= batchSize) {
          sendBatch();
          batchIndex = 0; // Reset batch index
        }
      }
    }
    else if (lidar.waitPoint() == RESULT_OPERATION_FAIL) {
      SendStatusMessage("LiDAR data read failed.");
    }

    delay(0);
  }

  if (currentState == State::PAUSED) {
    delay(50); // Allow some delay when paused
  }
}
void sendBatch() {
  byte payload[batchSize * sizeof(LidarPoint)];  //  Define payload buffer inside sendBatch()
  int index = 0; //  Keep track of byte position

  for (int i = 0; i < batchIndex; i++) {
    LidarPoint lidarPoint;
    lidarPoint.angle = lidarPoints[i].angle;
    lidarPoint.quality = lidarPoints[i].quality;
    lidarPoint.distance = lidarPoints[i].distance;

    //  Copy each LidarPoint struct into the payload buffer
    memcpy(&payload[index], &lidarPoint, sizeof(LidarPoint));
    index += sizeof(LidarPoint);
  }

  //  Send the payload efficiently
  if (useSerial) {
    //  Create full payload with markers
    byte fullPayload[index + 4];
    fullPayload[0] = 0xFF; // Start Marker 1
    fullPayload[1] = 0xAA; // Start Marker 2
    memcpy(&fullPayload[2], payload, index);  //  Copy LiDAR data into payload
    fullPayload[index + 2] = 0xEE; // End Marker 1
    fullPayload[index + 3] = 0xBB; // End Marker 2

    //  Send full payload at once
    Serial.write(fullPayload, index + 4);
    Serial.flush();  //  Ensure data is fully sent before continuing
  }
  else if (useMQTT) {
    //  MQTT does not need markers, send the raw payload
    bool success = mqttClient.publish(dataTopic, payload, index);
    if (!success) {
      Serial.println(" MQTT Publish Failed!");
    }
  }

  batchIndex = 0; //  Reset batch index after sending
}
void handleWebClient(WiFiClient client) {
  Serial.println("Client Connected!");

  String request = "";
  while (client.connected()) {
    if (client.available()) {
      char c = client.read();
      request += c;
      if (c == '\n') break;
    }
  }

  Serial.print("📥 Received Request: ");
  Serial.println(request);

  //  Handle WiFi Change
  if (request.indexOf("GET /SET_WIFI?ssid=") != -1) {
    int ssidStart = request.indexOf("ssid=") + 5;
    int passStart = request.indexOf("&pass=") + 6;
    int endIndex = request.indexOf(" ", passStart);

    String newSSID = request.substring(ssidStart, passStart - 6);
    String newPassword = request.substring(passStart, endIndex);

    newSSID.trim();
    newPassword.trim();

    Serial.print(" New WiFi: ");
    Serial.println(newSSID);
    Serial.print(" New Password: ");
    Serial.println(newPassword);

    newSSID.toCharArray(ssid, sizeof(ssid));
    newPassword.toCharArray(password, sizeof(password));

    WiFi.disconnect();
    WiFi.begin(ssid, password);

    client.println("HTTP/1.1 200 OK");
    client.println("Content-Type: text/html");
    client.println();
    client.println("<h2> WiFi Updated!</h2>");
    delay(1000);
    NVIC_SystemReset(); // Restart to apply new credentials
  }

  //  Send Web UI
  client.println("HTTP/1.1 200 OK");
  client.println("Content-Type: text/html");
  client.println();
  client.println("<html><head><title>WiFi Config</title></head><body>");
  client.println("<h2>WiFi Configuration</h2>");
  client.println("<form action='/SET_WIFI'>");
  client.println("SSID: <input type='text' name='ssid'><br>");
  client.println("Password: <input type='text' name='pass'><br>");
  client.println("<input type='submit' value='Save & Reconnect'>");
  client.println("</form>");
  client.println("</body></html>");

  client.stop();
}


