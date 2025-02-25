using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Models
{
  public class ConnectionParams
  {
    public string WiFiSSID { get; private set; }
    public string WiFiPW { get; private set; }
    public string MQTTBrokerAddress { get; private set; }
    public int MQTTPort { get; private set; }
    public string SerialPort { get; set; }
    public ConnectionParams(string serialPort = "", string wifiSSID ="", string wifiPW = "", string mqttBrokerAddress = "", int mqttPort = 1883)
    {
      SerialPort = serialPort;
      WiFiSSID = wifiSSID;
      WiFiPW = wifiPW;
      MQTTBrokerAddress = mqttBrokerAddress;
      MQTTPort = mqttPort;
    }
  }
}
