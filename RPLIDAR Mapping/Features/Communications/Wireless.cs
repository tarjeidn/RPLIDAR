using MQTTnet;

using RPLIDAR_Mapping.Interfaces;
using RPLIDAR_Mapping.Models;
using RPLIDAR_Mapping.Utilities;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Features.Communications
{
  internal class Wireless : ICommunication
  {
    private IMqttClient _mqttClient;
    private readonly ConcurrentQueue<DataPoint> _dataQueue = new ConcurrentQueue<DataPoint>();

    private readonly string _brokerAddress;
    private readonly int _brokerPort;
    private readonly string _commandTopic;
    private readonly string _dataTopic;
    private readonly string _statusTopic;

    private bool _connecting = false;
    private bool _shouldReconnect = true;

    public bool IsConnected => _mqttClient?.IsConnected ?? false;
    public bool IsInitialized { get; private set; } = false;
    public event Action<string> OnMessageReceived;

    public Wireless(ConnectionParams conn, string commandTopic = "lidar/commands", string dataTopic = "lidar/data", string statusTopic = "lidar/status")
    {
      _brokerAddress = conn.MQTTBrokerAddress;
      _brokerPort = conn.MQTTPort;
      _commandTopic = commandTopic;
      _dataTopic = dataTopic;
      _statusTopic = statusTopic;

      Debug.WriteLine($"[Wireless] Created. Broker={_brokerAddress}:{_brokerPort}");
      Debug.WriteLine("[Wireless] Starting internal MQTT broker...");
      Task.Run(async () =>
      {
        try
        {
          await InternalMqttServer.StartAsync();
          Debug.WriteLine("[Wireless] Internal MQTT server started.");
          // Wait a little to ensure server fully binds socket
          await Task.Delay(1000);

          // Now that server is started, initialize the MQTT client
          await InitializeMqttClient();
          _ = InitializeMqttClient();
        }
        catch (Exception ex)
        {
          Debug.WriteLine($"[Wireless] Failed to start internal MQTT server: {ex.Message}");
        }
      });


    }

    private async Task InitializeMqttClient()
    {
      try
      {
        Debug.WriteLine("[Wireless] Initializing MQTT client...");

        var factory = new MqttClientFactory();
        _mqttClient = factory.CreateMqttClient();

        _mqttClient.ApplicationMessageReceivedAsync += e => HandleIncomingMessage(e);

        _mqttClient.DisconnectedAsync += async e =>
        {
          Debug.WriteLine("[Wireless] Disconnected from MQTT broker.");
          IsInitialized = false;
          _connecting = false;

          if (_shouldReconnect)
          {
            Debug.WriteLine("[Wireless] Waiting 2s then reconnecting...");
            await Task.Delay(2000);
            Connect();
          }
        };

        _mqttClient.ConnectedAsync += async e =>
        {
          Debug.WriteLine("[Wireless] Connected to MQTT broker!");
          await _mqttClient.SubscribeAsync(_commandTopic);
          await _mqttClient.SubscribeAsync(_dataTopic);
          await _mqttClient.SubscribeAsync(_statusTopic);
          Debug.WriteLine("[Wireless] Subscribed to topics.");

          //  Send SET_MODE:WIFI immediately after subscribing
          await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
              .WithTopic(_commandTopic)
              .WithPayload("SET_MODE:WIFI")
              .Build());

          Debug.WriteLine("[Wireless] Sent SET_MODE:WIFI command after connection.");

          IsInitialized = true;
          _connecting = false;
        };

        Connect();
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"[Wireless] InitializeMqttClient error: {ex.Message}");
      }
    }

    private Task HandleIncomingMessage(MqttApplicationMessageReceivedEventArgs e)
    {
      var topic = e.ApplicationMessage.Topic;
      var payloadSequence = e.ApplicationMessage.Payload; // ReadOnlySequence<byte>

      Debug.WriteLine($"[Wireless] Incoming message on topic: {topic}");

      // Convert to byte[]
      byte[] payload = payloadSequence.ToArray();

      Debug.WriteLine($"[Wireless] Payload Length: {payload.Length}");

      if (payload.Length > 0)
      {
        // Print first few bytes as hex to check content
        string hexPreview = BitConverter.ToString(payload.Take(Math.Min(20, payload.Length)).ToArray());
        Debug.WriteLine($"[Wireless] Payload (first bytes): {hexPreview}");
      }
      else
      {
        Debug.WriteLine($"[Wireless] Payload is empty.");
      }

      if (topic == _dataTopic)
      {
        if (!Utility.IsUtf8String(payload, out string _))
        {
          if (payload.Length > 4 && payload[0] == 0xFF && payload[1] == 0xAA &&
              payload[payload.Length - 2] == 0xEE && payload[payload.Length - 1] == 0xBB)
          {
            // Skip start (2 bytes) and end markers (2 bytes)
            byte[] dataWithoutMarkers = new byte[payload.Length - 4];
            Array.Copy(payload, 2, dataWithoutMarkers, 0, dataWithoutMarkers.Length);

            Utility.ProcessLidarBatchBinary(dataWithoutMarkers, _dataQueue);
          }
          else
          {
            Debug.WriteLine("[Wireless] Invalid packet markers!");
          }
        }
      }
      else if (topic == _statusTopic)
      {
        string message = Encoding.UTF8.GetString(payload);
        Debug.WriteLine($"[Wireless] Status message: {message}");
        OnMessageReceived?.Invoke($"Status: {message}");
      }
      else if (topic == _commandTopic)
      {
        string message = Encoding.UTF8.GetString(payload);
        Debug.WriteLine($"[Wireless] Command message: {message}");
        OnMessageReceived?.Invoke($"Command: {message}");
      }

      return Task.CompletedTask;
    }



    public void Connect()
    {
      if (_mqttClient == null)
      {
        Debug.WriteLine("[Wireless] Connect() skipped, no mqttClient");
        return;
      }

      if (_mqttClient.IsConnected)
      {
        Debug.WriteLine("[Wireless] Already connected, skipping Connect()");
        return;
      }

      if (_connecting)
      {
        Debug.WriteLine("[Wireless] Already connecting, skipping Connect()");
        return;
      }

      _connecting = true;

      Task.Run(async () =>  // <<== 🔥 MOVE CONNECT LOGIC TO A BACKGROUND TASK
      {
        int attempt = 0;
        const int maxAttempts = 99999;
        const int retryDelayMs = 2000;

        while (_mqttClient != null && !_mqttClient.IsConnected && attempt < maxAttempts)
        {
          try
          {
            Debug.WriteLine($"[Wireless] Attempting MQTT connect... (attempt {attempt + 1})");

            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(_brokerAddress, _brokerPort)
                .WithClientId("MappingClient_" + Guid.NewGuid())
                .Build();

            await _mqttClient.ConnectAsync(options, CancellationToken.None);

            if (_mqttClient.IsConnected)
            {
              Debug.WriteLine("[Wireless] MQTT connect successful!");
              break;
            }
          }
          catch (ObjectDisposedException)
          {
            Debug.WriteLine("[Wireless] MQTT client disposed during connect, stopping retries.");
            break;
          }
          catch (OperationCanceledException)
          {
            Debug.WriteLine("[Wireless] MQTT connect cancelled, stopping retries.");
            break;
          }
          catch (Exception ex)
          {
            Debug.WriteLine($"[Wireless] MQTT connect attempt {attempt + 1} failed: {ex.Message}");

            await Task.Delay(retryDelayMs);
          }

          attempt++;
        }

        _connecting = false;
      });
    }


    public void Disconnect()
    {
      _shouldReconnect = false;
      _mqttClient?.DisconnectAsync();
      Debug.WriteLine("[Wireless] Disconnect called.");
    }

    public void Send(string message)
    {
      if (IsConnected)
      {
        Debug.WriteLine($"[Wireless] Publishing: {message}");
        _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(_commandTopic)
            .WithPayload(message)
            .Build());
      }
      else
      {
        Debug.WriteLine("[Wireless] Cannot Send(), not connected.");
      }
    }

    public string Receive()
    {
      return null;
    }

    public List<DataPoint> DequeueAllData()
    {
      var list = new List<DataPoint>();
      while (_dataQueue.TryDequeue(out var point))
      {
        list.Add(point);
      }
      return list;
    }

    public void InitializeMode()
    {
      Debug.WriteLine("[Wireless] InitializeMode() called (noop).");
      // No-op. Connection already started.
    }

    public void Dispose()
    {
      try
      {
        _shouldReconnect = false;
        _mqttClient?.Dispose();
        Debug.WriteLine("[Wireless] Disposed.");
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"[Wireless] Error during Dispose(): {ex.Message}");
      }
    }
  }
}
