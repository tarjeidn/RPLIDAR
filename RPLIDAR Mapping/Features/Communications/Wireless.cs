using MQTTnet;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RPLIDAR_Mapping.Interfaces;
using RPLIDAR_Mapping.Models;
using System.Globalization;
using System.Linq;
using RPLIDAR_Mapping.Utilities;
using System.IO.Ports;

namespace RPLIDAR_Mapping.Features.Communications
{
  internal class Wireless : ICommunication
  {
    public enum LiDARState
    {
      IDLE,
      RUNNING,
      PAUSED
    }

    private LiDARState _state;
    private IMqttClient _mqttClient;
    private readonly ConcurrentQueue<DataPoint> _dataQueue = new ConcurrentQueue<DataPoint>();
    public event Action<string> OnMessageReceived;
    private readonly string _brokerAddress;
    private readonly int _brokerPort;
    private readonly string _commandTopic;
    private readonly string _dataTopic;
    private readonly string _statusTopic;
    private string _lastmessageReceived;

    public bool IsConnected => _mqttClient?.IsConnected ?? false;
    private bool _isInitialized = false;
    public bool IsInitialized
    {
      get
      {       
        return _isInitialized;
      }
    }

    public Wireless(ConnectionParams conn, string commandTopic = "lidar/commands", string dataTopic = "lidar/data", string statusTopic= "lidar/status")
    {
      _brokerAddress = conn.MQTTBrokerAddress;
      _brokerPort = conn.MQTTPort;
      _commandTopic = commandTopic;
      _dataTopic = dataTopic;
      _statusTopic = statusTopic;

      //InitializeMqttClient().Wait();
      _ = InitializeMqttClient();
    }
    //  Send mode selection to Arduino via MQTT
    public void InitializeMode()
    {
      //bool messageSent = false;
      //while(!messageSent)
      //{
      //  if (_mqttClient.IsConnected)
      //  {
      //    Send("SET_MODE:WIFI");
      //    Log("Sent 'SET_MODE:WIFI' via MQTT.");
      //    messageSent = true;
      //  }
      //}

    }
    private void StartMqttLoop()
    {
      Task.Run(async () =>
      {
        while (true)
        {
          try
          {
            await Task.Delay(100); // Delay to prevent high CPU usage
          }
          catch (Exception ex)
          {
            Log($"Error in MQTT loop: {ex.Message}");
          }
        }
      });
    }

    private async Task InitializeMqttClient()
    {
      try
      {
        Log("Starting MQTT client initialization...");

        _mqttClient = new MqttClientFactory().CreateMqttClient();

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(_brokerAddress, _brokerPort)
            .WithClientId("WirelessClient_" + Guid.NewGuid()) // Unique client ID
            .Build();

        _mqttClient.ConnectedAsync += OnConnectedAsync;
        _mqttClient.DisconnectedAsync += OnDisconnectedAsync;
        _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;

        Log("Connecting to MQTT broker...");
        await _mqttClient.ConnectAsync(options);

        _isInitialized = true; // Mark as initialized
        Log("MQTT client successfully initialized.");
      }
      catch (Exception ex)
      {
        Log($"Error initializing MQTT client: {ex.Message}");
      }
      StartMqttLoop();
    }
    public void SetWiFiParameters(string ssid, string password)
    {
      int attempts = 0;
      while (attempts < 5)
      {
        if (IsConnected)
        {
          string command = $"SET_WIFI {ssid};{password}";
          Send(command);
          Log($"Sent Wi-Fi parameters: {command}");
        }
        else
        {
          Log("Cannot send Wi-Fi parameters: Not connected to MQTT broker.");
        }
      }

    }

    public void SetMQTTParameters(string broker, int port)
    {
      if (IsConnected)
      {
        string command = $"SET_MQTT {broker};{port}";
        Send(command);
        Log($"Sent MQTT parameters: {command}");
      }
      else
      {
        Log("Cannot send MQTT parameters: Not connected to MQTT broker.");
      }
    }

    public async void Connect()
    {
      if (_mqttClient != null && !_mqttClient.IsConnected)
      {
        await _mqttClient.ReconnectAsync();
      }
    }

    public async void Disconnect()
    {
      if (_mqttClient != null && _mqttClient.IsConnected)
      {
        await _mqttClient.DisconnectAsync();
      }
    }


    public void Send(string message)
    {
      Log($"Send called with message: {message}");
      if (IsConnected)
      {
        _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(_commandTopic) // Send commands to the command topic
            .WithPayload(message)
            .Build());
        Log($"Published to {_commandTopic}: {message}");
      }
      else
      {
        Log("Failed to send data: Not connected to MQTT broker.");
      }
    }

    public string Receive()
    {
      //Log("MQTT communication uses an event-based model. Callbacks handle message reception.");
      return _lastmessageReceived; // MQTT does not support synchronous receive
    }

    public List<DataPoint> DequeueAllData()
    {
      var dataList = new List<DataPoint>();
      while (_dataQueue.TryDequeue(out var data))
      {
        dataList.Add(data);
      }
      return dataList;
    }

    private async Task OnConnectedAsync(MqttClientConnectedEventArgs e)
    {
        try
        {
            Log("Connected to MQTT broker. Subscribing to topics...");

            // Subscribe to topics
            await _mqttClient.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(_commandTopic).Build());
            await _mqttClient.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(_dataTopic).Build());
            await _mqttClient.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(_statusTopic).Build());

            Log($"Subscribed to topics: {_commandTopic}, {_dataTopic}, {_statusTopic}");
    ;
        }
        catch (Exception ex)
        {
            Log($"Error in OnConnectedAsync: {ex.Message}");
        }
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
      Log($"Disconnected from the MQTT broker. Reason: {e.Exception?.Message}");
      return Task.CompletedTask;
    }

    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
      var topic = e.ApplicationMessage.Topic;
      byte[] payload = e.ApplicationMessage.Payload.ToArray();  //  Extract binary payload
      string jsonmessage = Encoding.UTF8.GetString(payload);
      if (topic == _statusTopic)
      {
          ProcessLiDARData(jsonmessage);  //  Process commands      
      }
      if (topic == _dataTopic)
      {
        if (!Utility.IsUtf8String(payload, out string message)) //  Detect binary payload
        {
          //Debug.WriteLine("Received Binary LiDAR Data via MQTT");
          Utility.ProcessLidarBatchBinary(payload, _dataQueue);  //  Process binary data
        }
        else
        {        
          _lastmessageReceived = jsonmessage;
          //Debug.WriteLine($"Received JSON/Text Data via MQTT: {jsonmessage}");
          Utility.ProcessLidarBatchJson(jsonmessage, _dataQueue);  //  Process JSON LiDAR data       
        }
      }
      return Task.CompletedTask;
    }



    private void ProcessLiDARData(string data)
    {
      try
      {
        if (data.Contains("Status"))
        {
          OnMessageReceived?.Invoke($"MQTT: {data}");
          //Log(data);
          return;
        }
        if (data.Contains("State"))
        {
          UpdateState(data);
          return;
        }
        if (data.Contains("VMDPE"))
        {
          Log($"Ignoring diagnostic data: {data}");
          return;
        }
        if (!IsValidLiDARData(data))
        {
          Log($"Invalid data format: {data}");
          return;
        }
        if (_state == LiDARState.PAUSED)
        {
          return;
        }
        else
        {
          Log($"Invalid data format: {data}");
        }
      }
      catch (Exception ex)
      {
        Log($"Error in ProcessSerialData: {ex.Message}");
      }
    }
    private void UpdateState(string msg)
    {
      if (msg.Contains("IDLE"))
      {
        _state = LiDARState.IDLE;
      }
      else if (msg.Contains("RUNNING"))
      {
        _state = LiDARState.RUNNING;
      }
      else if (msg.Contains("PAUSED"))
      {
        _state = LiDARState.PAUSED;
      }

      Log($"Updated State: {_state}");
      return;
    }

    private bool IsValidLiDARData(string data)
    {
      return data.Count(c => c == ',') == 2 &&
             char.IsDigit(data[0]);
    }
    public void Dispose()
    {
      try
      {
        if (_mqttClient != null)
        {
          if (_mqttClient.IsConnected)
          {
            _mqttClient.DisconnectAsync().Wait(); // Ensure proper disconnection
            Debug.WriteLine("[Wireless] MQTT client disconnected in Dispose().");
          }

          _mqttClient.Dispose();
          _mqttClient = null;
          Debug.WriteLine("[Wireless] MQTT client disposed.");
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"[Wireless] Error during Dispose(): {ex.Message}");
      }
    }

    void SendKeepAliveSignal()
    {
      if (IsConnected)
      {
        _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(_statusTopic) // Send commands to the command topic
            .WithPayload("keepAlive")
            .Build());
      }
    }

    private void ProcessCommand(string command)
    {
      if (command == "IDLE")
      {
        _state = LiDARState.IDLE;
      }
      else if (command == "RUNNING")
      {
        _state = LiDARState.RUNNING;
      }
      else if (command == "PAUSED")
      {
        _state = LiDARState.PAUSED;
      }
      else
      {
        Log($"Unknown command received: {command}");
        return;
      }

      Log($"Updated State: {_state}");
    }
  }
}
