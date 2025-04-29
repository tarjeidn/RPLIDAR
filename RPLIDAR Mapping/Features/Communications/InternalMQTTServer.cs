using System;
using System.Threading.Tasks;
using System.Text;

using MQTTnet;
using MQTTnet.Server;

namespace RPLIDAR_Mapping.Features.Communications
{
  public static class InternalMqttServer
  {
    private static MqttServer _mqttServer;

    public static async Task StartAsync()
    {
      if (_mqttServer != null)
      {
        Console.WriteLine("[InternalMqttServer] Warning: MQTT server already started.");
        return;
      }

      var options = new MqttServerOptionsBuilder()
          .WithDefaultEndpoint()
          .WithDefaultEndpointPort(1883)
          .Build();

      var factory = new MqttServerFactory();
      _mqttServer = factory.CreateMqttServer(options);

      // Optional: Attach basic event handlers (for debugging / future expansion)
      _mqttServer.ClientConnectedAsync += async e =>
      {
        Console.WriteLine($"[InternalMqttServer] Client connected: {e.ClientId}");
        await Task.CompletedTask;
      };

      _mqttServer.ClientDisconnectedAsync += async e =>
      {
        Console.WriteLine($"[InternalMqttServer] Client disconnected: {e.ClientId}");
        await Task.CompletedTask;
      };

      _mqttServer.ApplicationMessageNotConsumedAsync += async e =>
      {
        Console.WriteLine($"[InternalMqttServer] Message not consumed: {e.ApplicationMessage?.Topic}");
        await Task.CompletedTask;
      };

      await _mqttServer.StartAsync();
      Console.WriteLine("[InternalMqttServer] Internal MQTT server started.");
    }

    public static async Task StopAsync()
    {
      if (_mqttServer == null)
      {
        Console.WriteLine("[InternalMqttServer] Warning: MQTT server not running.");
        return;
      }

      await _mqttServer.StopAsync();
      Console.WriteLine("[InternalMqttServer] Internal MQTT server stopped.");

      _mqttServer = null; // Reset so we can safely restart later if needed
    }
  }
}



namespace RPLIDAR_Mapping.Features.Communications
{
  public static class MqttTestClient
  {
    private static IMqttClient _client;

    public static async Task ConnectAndTestAsync(string serverAddress = "127.0.0.1", int port = 1883)
    {
      var factory = new MqttClientFactory();
      _client = factory.CreateMqttClient();

      var options = new MqttClientOptionsBuilder()
          .WithTcpServer(serverAddress, port)
          .WithClientId("TestClient_" + Guid.NewGuid())
          .Build();

      _client.ApplicationMessageReceivedAsync += e =>
      {
        string payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
        Console.WriteLine($"[MqttTestClient] Message received: Topic={e.ApplicationMessage.Topic}, Payload={payload}");
        return Task.CompletedTask;
      };

      _client.ConnectedAsync += e =>
      {
        Console.WriteLine("[MqttTestClient] Connected to MQTT server!");
        return Task.CompletedTask;
      };

      _client.DisconnectedAsync += e =>
      {
        Console.WriteLine("[MqttTestClient] Disconnected from MQTT server.");
        return Task.CompletedTask;
      };

      Console.WriteLine("[MqttTestClient] Connecting...");
      await _client.ConnectAsync(options);

      Console.WriteLine("[MqttTestClient] Subscribing to 'test/topic'...");
      await _client.SubscribeAsync("test/topic");

      Console.WriteLine("[MqttTestClient] Publishing message...");
      var message = new MqttApplicationMessageBuilder()
          .WithTopic("test/topic")
          .WithPayload("Hello MQTT World!")
          .Build();

      await _client.PublishAsync(message);
    }

    public static async Task DisconnectAsync()
    {
      if (_client != null && _client.IsConnected)
      {
        await _client.DisconnectAsync();
        Console.WriteLine("[MqttTestClient] Disconnected cleanly.");
      }
    }
  }
}
