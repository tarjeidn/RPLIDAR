using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Models
{
  public class ConnectionParams
  {
    public string ConnectionType { get; set; } = "";
    public string WiFiSSID { get; set; } = "";
    public string WiFiPW { get; set; } = "";
    public string MQTTBrokerAddress { get; set; } = "192.168.0.147";
    public int MQTTPort { get; set; } = 1883;
    public string SerialPort { get; set; } = "";
    public ConnectionParams(string serialPort = "", string wifiSSID = "", string wifiPW = "", string mqttBrokerAddress = "192.168.0.147", int mqttPort = 1883, string connectionType = "")
    {
      SerialPort = serialPort;
      WiFiSSID = wifiSSID;
      WiFiPW = wifiPW;
      MQTTBrokerAddress = mqttBrokerAddress;
      MQTTPort = mqttPort;
      ConnectionType = connectionType;
    }
  }
}
