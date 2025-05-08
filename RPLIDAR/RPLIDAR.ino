
#include <lds_all_models.h>
#include <LDS_CAMSENSE_X1.h>
#include <LDS_DELTA_2A_115200.h>
#include <LDS_DELTA_2A_230400.h>
#include <LDS_DELTA_2B.h>
#include <LDS_DELTA_2G.h>
#include <LDS_LDROBOT_LD14P.h>
#include <LDS_LDS02RR.h>
#include <LDS_NEATO_XV11.h>
#include <LDS_YDLIDAR_SCL.h>
#include <LDS_YDLIDAR_X2_X2L.h>
#include <LDS_YDLIDAR_X3.h>
#include <LDS_YDLIDAR_X3_PRO.h>
#include <LDS_YDLIDAR_X4.h>
#include <LDS_YDLIDAR_X4_PRO.h>
#include <PID_v1_0_0.h>
#include <LDS.h>
#include <LDS_RPLIDAR_A1.h>


#include <PubSubClient.h>
#include <ArduinoJson.h>
#include <ArduinoJson.hpp>
//#include <RPLidar.h>
#include <WiFiNINA.h>
#include <stdint.h> 
#include <Wire.h>
#include <Adafruit_Sensor.h>
#include <Adafruit_BNO055.h>
#include <utility/imumaths.h>

Adafruit_BNO055 bno = Adafruit_BNO055(55);
LDS_RPLIDAR_A1 lidar;
//RPLidar lidar;

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

struct WiFiSettings {
	char ssid[32];
	char password[64];
};
struct Vec2 {
	float x;
	float y;
};
WiFiServer server(80);

const char* apSSID = "lidar";
const char* apPassword = "123";

IPAddress localIp(192, 168, 0, 44);   // Arduino's IP
IPAddress gateway(192, 168, 0, 1);    // Your router
IPAddress subnet(255, 255, 255, 0);   // Default subnet

float _yawOffset = 0;
unsigned long lastVelocitySentTime = 0;
unsigned long lastTime = 0;
const float motionThreshold = 0.15f; // Threshold to detect real motion
static unsigned long lastMotionTime = 0;
const unsigned long velocitySendInterval = 200; // send every 200ms
Vec2 velocity = { 0.0f, 0.0f };
const int velocitySmoothingWindow = 5; // adjust for smoothness vs lag
float vxHistory[velocitySmoothingWindow] = { 0 };
float vyHistory[velocitySmoothingWindow] = { 0 };
int velocityHistoryIndex = 0;
int maxDistance = 4000;
//FlashStorage(wifiConfig, WiFiSettings);

WiFiSettings settings;
bool wifiConfigured = false;


char* ssid = "Telia-2G-B9A841";
char* password = "nyUu4MgAWhTG";

bool shouldRestart = false;

State currentState = State::IDLE; // Start in the IDLE state
bool useSerial = false;
bool useMQTT = false;
bool isReady = false;


float currentYaw = 0;
unsigned long lastMQTTCheck = 0;
unsigned long lastYawRead = 0;
const unsigned long yawInterval = 50; // ms interval
unsigned long lastStateUpdate = 0;
const unsigned long stateUpdateInterval = 1000; // 1 second

unsigned long lastKeepAliveTime = 0;
const unsigned long timeout = 5000;


const char* mqttServer = "192.168.0.147";
const int mqttPort = 1883;
const char* dataTopic = "lidar/data";       // Topic for publishing LiDAR data
const char* commandTopic = "lidar/commands"; // Topic for receiving commands
const char* statusTopic = "lidar/status";

int motorSpeed = 200;
//String dataBatch[batchSize];
String mode = "";

WiFiClient wifiClient;
PubSubClient mqttClient(wifiClient);
int batchIndex = 0;
////
//JSON data: batchsize 10
//byte data (7): batchsize 30
//byte data(5) : batchsize 45
#define MAX_BATCH_SIZE 50  //  Define max batch size to prevent overflow
#pragma pack(push, 1)  // Ensure 1-byte memory alignment

struct LidarPoint {
	uint16_t angle;      // 2 bytes
	uint16_t distance;   // 2 bytes
	byte quality;        // 1 byte
	uint16_t yaw;        // 2 bytes (0–360° → stored as degrees × 100)
	uint32_t timestamp;  // 4 bytes
};
#pragma pack(pop)
#pragma pack(push, 1)
struct IMUVelocityPacket {
	uint32_t timestamp;    // 4 bytes
	float vx;            // velocity X * 1000
	float vy;            // velocity Y * 1000
};
#pragma pack(pop)

int BATCH_SIZE = 50;  //  Tune this for better performance
LidarPoint lidarPoints[MAX_BATCH_SIZE];
byte payload[(MAX_BATCH_SIZE * sizeof(LidarPoint)) + 4];

void scanPointCallback(float angle_deg, float dist_mm, float quality, bool scan_completed) {
	if (quality < 5) return; // Simple quality filter

	unsigned long now = millis();

	lidarPoints[batchIndex].angle = (uint16_t)(angle_deg * 100);
	lidarPoints[batchIndex].quality = (uint8_t)quality;
	lidarPoints[batchIndex].timestamp = now;

	// Clamp distances outside range to form reference ring
	if (dist_mm > 160 && dist_mm < 3500)
		lidarPoints[batchIndex].distance = (uint16_t)dist_mm;
	else
		lidarPoints[batchIndex].distance = 3500;
	//lidarPoints[batchIndex].distance = (uint16_t)dist_mm;


	lidarPoints[batchIndex].yaw = (uint16_t)(currentYaw * 100);

	batchIndex++;

	// Batch ready to send?
	if (batchIndex >= BATCH_SIZE) {
		sendBatch(); // Non-blocking batch transmission
		batchIndex = 0;
	}
}

// Non-blocking serial read callback
int serialReadCallback() {
	return Serial1.available() ? Serial1.read() : -1;
}

// Efficient serial write callback
size_t serialWriteCallback(const uint8_t* buffer, size_t length) {
	return Serial1.write(buffer, length);
}

void motorPinCallback(float value, LDS::lds_pin_t pin) {
	//// Always control pin 3, no matter what the library says
	//pinMode(RPLIDAR_MOTOR, OUTPUT);

	//if (value == LDS::DIR_OUTPUT_PWM) {
	//  // Ignore – setup already handled
	//  return;
	//}

	//if (value == LDS::DIR_OUTPUT_CONST) {
	//  // Ignore – setup already handled
	//  return;
	//}

	//if (value == LDS::VALUE_LOW) {
	//  analogWrite(RPLIDAR_MOTOR, 0);  // Stop motor
	//  return;
	//}

	//if (value == LDS::VALUE_HIGH) {
	//  analogWrite(RPLIDAR_MOTOR, 255);  // Full power
	//  return;
	//}

	//if (value >= 0.0f && value <= 1.0f) {
	//  analogWrite(RPLIDAR_MOTOR, int(value * 255));
	//}
}



void sendBatch() {
	const int payloadSize = batchIndex * sizeof(LidarPoint);

	// Total payload size = markers (2 + 2) + actual data
	uint8_t payload[payloadSize + 4];

	payload[0] = 0xFF; // Start marker 1
	payload[1] = 0xAA; // Start marker 2

	memcpy(&payload[2], lidarPoints, payloadSize);

	payload[payloadSize + 2] = 0xEE; // End marker 1
	payload[payloadSize + 3] = 0xBB; // End marker 2

	if (useSerial) {
		Serial.write(payload, payloadSize + 4);
		Serial.flush(); // optional
	}
	else if (useMQTT) {
		
		bool success = mqttClient.publish(dataTopic, payload, payloadSize + 4);
		if (!success && useSerial) {
			SendStatusMessage("MQTT Publish Failed!");
		}
	}

	batchIndex = 0;
}



void handleCommand(String command, String com) {
	String message = "Received command from " + com + " protocol";
	SendStatusMessage(message.c_str());
	if (command == "keepAlive") {
		lastKeepAliveTime = millis();
	}
	if (command.startsWith("SET_MODE:WIFI") || command == "w") {
		SendStatusMessage("Received SET MODE: WIFI Command");

		if (WiFi.status() != WL_CONNECTED) {
			SendStatusMessage("WiFi not connected yet, attempting connection...");
			connectWiFi(ssid, password);
		}

		if (!mqttClient.connected()) {
			SendStatusMessage("Setting up MQTT after SET_MODE:WIFI...");
			setupMQTT();
		}

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
	if (command == "s") {  // Start clearly handled
		resetLidar();                      // Explicitly reset to known idle state
		if (StartLidar()) {                // Explicitly start LiDAR scanning
			SendStatusMessage("Running");
		}
		else {
			SendStatusMessage("Failed to start LiDAR.");
		}
		SendState();
	}

	else if (command == "p") {  // Stop clearly handled
		StopLidar();
		SendStatusMessage("Stopped");
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

		response += "Firmware: v1.0.0 | Ready for commands";

		SendStatusMessage(response.c_str());  //  Send via Serial or MQTT
	}

	else {
		SendStatusMessage("Unknown command received.");
	}
}
void startWifiCommunication() {
	SendStatusMessage("WiFi mode selected. Connecting...");
	server.begin();
	wifiConfigured = connectWiFi(ssid, password);

	if (!wifiConfigured) {
		SendStatusMessage("No valid WiFi stored. Waiting for settings over Serial...");
		waitForWiFiConfig(); // Wait for new settings
	}

	mqttClient.loop();
	useMQTT = true;
	SendStatusMessage("LiDAR set to use MQTT");
	SendStatusMessage("LiDAR set to use MQTT");
}
void connectMQTT() {
	while (!mqttClient.connected()) {
		SendStatusMessage("Attempting MQTT connection...");

		// Attempt to connect
		if (mqttClient.connect("MKR1010Client")) {
			SendStatusMessage("Connected to MQTT broker!");

			// Subscribe to the command topic
			mqttClient.subscribe(commandTopic);
			SendStatusMessage("Subscribed to command topic: " + String(commandTopic));

			useMQTT = true;
		}
		else {
			SendStatusMessage("Failed, rc=");
			SendStatusMessage(mqttClient.state()); // Print the connection error code
			SendStatusMessage(" Retrying in 5 seconds...");
			delay(5000); // Wait before retrying
		}
	}
}
void StopLidar() {
	SendStatusMessage("Stopping LiDAR...");

	lidar.stop();  // Stop scanning
	analogWrite(RPLIDAR_MOTOR, 0);  // Manually stop motor
	currentState = State::IDLE;

	SendStatusMessage("LiDAR stopped.");
}
bool StartLidar() {
	SendStatusMessage("Starting LiDAR...");

	pinMode(RPLIDAR_MOTOR, OUTPUT);
	analogWrite(RPLIDAR_MOTOR, motorSpeed);  // Manually start motor
	delay(1500);  // Let it spin up

	LDS::result_t result = lidar.start();  // Library will now succeed

	if (result == LDS::RESULT_OK) {
		currentState = State::RUNNING;
		SendStatusMessage("✅ LiDAR started successfully.");
		return true;
	}

	SendStatusMessage("❌ LiDAR failed to start. Code: " + String(result));
	SendStatusMessage("↪ " + lidar.resultCodeToString(result));
	analogWrite(RPLIDAR_MOTOR, 0);  // Stop motor if failed
	currentState = State::IDLE;
	return false;
}
void resetLidar() {
	SendStatusMessage("Resetting LiDAR...");

	lidar.stop();  // Stop scanning (library function)
	analogWrite(RPLIDAR_MOTOR, 0);  // Manually stop motor
	delay(500);

	while (RPLIDAR_SERIAL.available()) RPLIDAR_SERIAL.read();  // Clear RX buffer

	lidar.init();  // ✅ Call only once per full reset

	currentState = State::IDLE;
	SendStatusMessage("LiDAR reset complete.");
}
bool checkSerialConnection() {
	Serial.begin(500000);


	delay(1000); // Give time for Serial to initialize

	if (Serial) {  //  Checks if the Serial connection is active
		SendStatusMessage("Serial port is connected!");
		return true;
	}

	SendStatusMessage("Serial port not detected.");
	return false;
}
bool connectWiFi(const char* ssid, const char* password) {
	int attempts = 0;
	SendStatusMessage("Using WiFi mode...");
	SendStatusMessage(ssid);SendStatusMessage(password);
	WiFi.begin(ssid, password);
	while (WiFi.status() != WL_CONNECTED && attempts < 10) {
		delay(1000);
		SendStatusMessage(".");
		attempts++;
	}
	if (WiFi.status() == WL_CONNECTED) {
		SendStatusMessage("\nConnected to WiFi!");
		SendStatusMessage("IP Address: ");
		SendStatusMessage(WiFi.localIP());
		//setupMQTT();
		return true;
	}
	SendStatusMessage("\nFailed to connect.");
	return false;


}
void setupMQTT() {
	mqttClient.setServer(mqttServer, mqttPort);
	mqttClient.setCallback(MQTTcallback);
	mqttClient.setBufferSize(2048);

	connectMQTT();

	if (WiFi.status() == WL_CONNECTED && mqttClient.connected()) {

		mqttClient.loop();

		SendStatusMessage("Connected to MQTT at server: " + String(mqttServer));
	}
}
void waitForWiFiConfig() {
	String newSSID = "";
	String newPassword = "";

	SendStatusMessage("Send WiFi credentials in format: SET_WIFI:SSID,PASSWORD");
	SendStatusMessage("type c to skip and use serial");

	while (true) {
		if (Serial.available()) {
			String input = Serial.readStringUntil('\n');
			input.trim();

			if (input.startsWith("SET_WIFI:")) {
				int commaIndex = input.indexOf(",");
				if (commaIndex > 9) {
					newSSID = input.substring(9, commaIndex);
					newPassword = input.substring(commaIndex + 1);

					SendStatusMessage("Received New WiFi Credentials!");
					//saveWiFiSettings(newSSID.c_str(), newPassword.c_str());

					SendStatusMessage("Restarting to apply new settings...");
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
void checkForTimeout() {
	if (millis() - lastKeepAliveTime > timeout) {
		SendStatusMessage("MonoGame is not responding, shutting down LiDAR...");
		analogWrite(RPLIDAR_MOTOR, 0);
		resetLidar();
	}
}
void reconnectMQTT() {
	while (!mqttClient.connected()) {
		SendStatusMessage("Attempting MQTT Reconnection...");
		if (mqttClient.connect("LidarClient")) {  // Replace with a unique ID if needed
			SendStatusMessage("MQTT Reconnected!");
			mqttClient.subscribe("lidar/commands");  //Re-subscribe to any necessary topics
			mqttClient.setBufferSize(2048);
		}
		else {
			SendStatusMessage("MQTT Reconnection Failed, Error Code: ");
			SendStatusMessage(mqttClient.state());
			delay(5000);  // Wait before retrying
		}
	}
}
void setup() {
	Serial.begin(500000);
	unsigned long serialTimeout = millis() + 1000;
	while (!Serial && millis() < serialTimeout) { ; }

	if (Serial) {
	    useSerial = true;
	    useMQTT = false;
	    SendStatusMessage("Serial port detected.");
	}
	else {
	    useSerial = false;
	    useMQTT = true;
	    SendStatusMessage("No Serial detected. Switching to WiFi/MQTT.");
	}

	RPLIDAR_SERIAL.begin(115200);
	if (useSerial) SendStatusMessage("LiDAR serial initialized.");

	lidar.setScanPointCallback(scanPointCallback);
	lidar.setSerialReadCallback(serialReadCallback);
	lidar.setSerialWriteCallback(serialWriteCallback);
	lidar.setMotorPinCallback(motorPinCallback);
	lidar.setErrorCallback(handleLdsError);
	lidar.setInfoCallback(handleLdsInfo);

	if (!bno.begin()) {
		SendStatusMessage("BNO055 not detected. Check wiring!");
		while (1);
	}
	delay(100);
	bno.setExtCrystalUse(true);
	SendStatusMessage("BNO055 initialized.");

	sensors_event_t orientationData;
	bno.getEvent(&orientationData);
	_yawOffset = 0;
	SendStatusMessage("Yaw offset set.");

	pinMode(RPLIDAR_MOTOR, OUTPUT);
	analogWrite(RPLIDAR_MOTOR, 0);

	lidar.init();
	currentState = State::IDLE;

	if (useMQTT) {
		wifiConfigured = connectWiFi(ssid, password);

		if (wifiConfigured) {
			setupMQTT();   //  only after WiFi OK
		}
		else {
			SendStatusMessage("Failed to connect WiFi. Waiting for settings...");
			waitForWiFiConfig();
		}
	}

	SendStatusMessage("Setup complete, ready for commands.");
	Serial.flush();
	delay(200);
}


// Efficient IMU yaw reading function
float readYaw() {
	sensors_event_t orientationData;
	bno.getEvent(&orientationData);
	float yaw = orientationData.orientation.x; // Assuming orientation.x is yaw
	yaw -= _yawOffset;
	if (yaw < 0) yaw += 360;
	return yaw;
}
void loop() {
	unsigned long now = millis();

	// Quick Serial command handling
	if (Serial.available() > 0) {
		String command = Serial.readStringUntil('\n');
		handleCommand(command, "Serial");
	}


	if (useMQTT) {
		if (!mqttClient.connected()) {
			SendStatusMessage("MQTT Disconnected! Reconnecting...");
			reconnectMQTT();
		}
		mqttClient.loop();
	}

	// Non-blocking IMU yaw sampling at intervals (~50 ms)
	if (now - lastYawRead >= yawInterval) {
		sensors_event_t orientationData;
		bno.getEvent(&orientationData);
		currentYaw = orientationData.orientation.x - _yawOffset;
		if (currentYaw < 0) currentYaw += 360;
		lastYawRead = now;
	}

	switch (currentState) {
	case State::RUNNING:
		lidar.loop(); // Clearly call lidar loop when running
		break;

	case State::IDLE:
		// Idle state, do nothing special
		break;

	case State::PAUSED:
		// Handle paused state if needed
		break;
	}

	// Add other non-blocking tasks here as necessary
}


void sendIMUVelocityPacket(Vec2 vel, unsigned long timestamp) {
	struct __attribute__((packed)) IMUVelocityPacket {
		uint32_t timestamp;
		float vx;
		float vy;
	} pkt;

	pkt.timestamp = timestamp;
	pkt.vx = vel.x;
	pkt.vy = vel.y;

	Serial.write(0xFA); Serial.write(0xAF); // Start marker
	Serial.write((uint8_t*)&pkt, sizeof(pkt));
	Serial.write(0xEF); Serial.write(0xBE); // End marker
}
void applySettings(String json) {
	StaticJsonDocument<200> doc;
	DeserializationError error = deserializeJson(doc, json);

	if (error) {
		SendStatusMessage("Error parsing settings!");
		return;
	}

	if (doc.containsKey("BatchSize")) {
		BATCH_SIZE = doc["BatchSize"];
	}

	SendStatusMessage("Settings updated.");
}
void MQTTcallback(char* topic, byte* payload, unsigned int length) {
	// Convert the payload into a String
	String command = "";
	for (unsigned int i = 0; i < length; i++) {
		command += (char)payload[i];
	}

	String msg = "Message arrived in topic: " + String(topic) + ": " + String(command);
	SendStatusMessage(msg);

	handleCommand(command, "MQTT");


}
void SendStatusMessage(String msg) {
	SendStatusMessage(msg.c_str());  // Convert `String` to `const char*`
}
void SendStatusMessage(const char* msg) {

	String statusMessage = "Status " + String(msg);  // Append message

	if (useSerial) {
		Serial.write(msg);
		Serial.write('\n');
	}
	else if (useMQTT) {
		if (!statusMessage.startsWith("State:")) {
			mqttClient.publish(statusTopic, statusMessage.c_str());  // Send over MQTT
		}
	}
	else {
		SendStatusMessage("No mode set \n" + statusMessage);
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
void SendStatusMessage(LDS::result_t value, bool asHex = false) {
	if (asHex) {
		SendStatusMessage("0x" + String((int)value, HEX));
	}
	else {
		SendStatusMessage(lidar.resultCodeToString(value));
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
void handleLdsError(LDS::result_t code, String aux) {
	SendStatusMessage(" LDS error: " + code + aux);

}
void handleLdsInfo(LDS::info_t code, String value) {
	SendStatusMessage(" LDS info: " + lidar.infoCodeToString(code) + " = " + value);

}






