using RPLIDAR_Mapping.Models;
using System;
using System.Collections.Generic;

namespace RPLIDAR_Mapping.Interfaces
{
  public interface ICommunication : IDisposable  //  Add IDisposable
  {
    void Connect();
    void Disconnect();
    bool IsConnected { get; }
    void Send(string message);
    string Receive();
    bool IsInitialized { get; }
    List<DataPoint> DequeueAllData();
    void InitializeMode();
    event Action<string> OnMessageReceived;
  }
}

