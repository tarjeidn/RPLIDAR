To get the program running, connect the USB cable from the Arduino to a USB port on a computer. After that, Arduino drivers needs to be installed. 
These can be be found by downloading the Arduino IDE from https://www.arduino.cc/en/software/. Opening this program should autodetect the connected Arduino, and autoinstall 
all the needed drivers. To check if the Arduino is detected by the computer, open the Device Manager (on Windows), and look for an entry named PORTS (COM & LPT).
The Arduino, if detected with the correct drivers, should be listed under here as Arduino MKR WiFi1010 (COMX), where X is the COM port its been assigned to, eg COM3.

The application should now be runnable over serial. Open the Appsettings file (Appsettings.settings), and set the SerialPort entry to the COM port listed in device manager, COMX (eg COM3).
Set the CommunicationProtocol entry to serial. Compile and run the program. This should start the program. Try clicking the start LIDAR button, or the spacebar. 
If everything works, some logs should be printed in the GUI log window, and the device should start. Stop the device by clicking the Stop LiDAR button, 
or the right arrow key.

The WiFi functionality of the device needs a valid WiFi password and ssid, on a 2.4GHz connection. The password and ssid is, as of now, hardcoded into the device, and thus needs 
the Arduino code to be adjusted and reuploaded to work on a new network. New code can be reuploaded using the Arduino IDE, or if using Visual Studio, by an extension called 
Visual Micro. Whatever is used, RPLIDAR.ino is the Arduino sketch with the software running. In RPLIDAR.ino, change the values of these two variables: 

char* ssid = "xxxxx";
char* password = "xxxxx";

Reupload the code to the Arduino. Set Appsettings.settings CommunicationProtocol to wifi, and run the program. 


Troubleshooting:
If the LiDAR doesnt start, try clicking the reset button on the Arduino, or unplug and replug it. If that doesnt work, try reuploading the .ino file to the Arduino. 
These problems are usually caused by the COM port beeing blocked, or the .ino setup not setting its communication mode properly.