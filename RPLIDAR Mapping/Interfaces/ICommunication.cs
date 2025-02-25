using RPLIDAR_Mapping.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Interfaces
{
  public interface ICommunication
  {
    void Connect();
    void Disconnect();
    bool IsConnected { get; }
    void Send(string message);
    string Receive();
    bool IsInitialized { get;  }
    List<DataPoint> DequeueAllData();
    void InitializeMode();
    event Action<string> OnMessageReceived;
  }
}
