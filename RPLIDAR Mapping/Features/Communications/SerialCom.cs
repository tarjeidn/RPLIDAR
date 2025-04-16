using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using RPLIDAR_Mapping.Interfaces;
using System.Linq;
using RPLIDAR_Mapping.Models;

using RPLIDAR_Mapping.Utilities;
using System.IO;
using System.Threading.Tasks;
using SharpDX.MediaFoundation;
using Microsoft.Xna.Framework;
using System.Threading;

namespace RPLIDAR_Mapping.Features.Communications
{
  internal class SerialCom : ICommunication
  {
    public enum LiDARState
    {
      IDLE,
      RUNNING,
      PAUSED
    }
    private LiDARState _state;
    private SerialPort _serialPort;
    private DateTime _lastStateRequest = DateTime.MinValue;
    private List<byte> _serialBuffer = new List<byte>(); // Buffer for binary data
    private ConnectionParams _connectionParams;
    private string _packetType = null;
    public event Action<string> OnMessageReceived;
    private StringBuilder _textLineBuffer = new StringBuilder();

    // Thread-safe queue to store incoming LiDAR data as DataPoint
    private ConcurrentQueue<DataPoint> _dataQueue = new ConcurrentQueue<DataPoint>();

    public bool IsConnected
    {
      get
      {
        try
        {
          return _serialPort?.IsOpen ?? false;
        }
        catch
        {
          return false; // If an error occurs, assume it's disconnected
        }
      }
    }

    private bool _isInitialized = false;
    public bool IsInitialized
    {
      get
      {
        
        return _isInitialized;
      }
    }



    public SerialCom(ConnectionParams conn, int baudRate = 500000)
    {
      _connectionParams = conn;
      Connect();
      Thread.Sleep(1000); // Give Arduino time to boot
      _isInitialized = true;
    }

    public void InitializeMode()
    {
      if (_serialPort.IsOpen)
      {
        _serialPort.WriteLine("SET_MODE:SERIAL");
        Log("Sent 'SET_MODE:SERIAL' to Arduino.");
        SendWiFiSettings(_connectionParams.WiFiSSID, _connectionParams.WiFiPW);
      }
    }

    public void Connect()
    {
      string[] availablePorts = SerialPort.GetPortNames();

      if (!availablePorts.Contains(_connectionParams.SerialPort))
      {
        Log($"{_connectionParams.SerialPort} not found. Searching for available ports...");

        foreach (string port in availablePorts)
        {
          try
          {
            using (SerialPort testPort = new SerialPort(port, 500000))
            {
              testPort.Open();
              _connectionParams.SerialPort = port;
              Log($"Found and switched to available port: {port}");
              testPort.Close();
              break;
            }
          } catch
          {
            // Ignore ports that can't be opened
          }
        }
      }

      try
      {
        // Dispose old port if it exists
        _serialPort?.Dispose();

        _serialPort = new SerialPort(_connectionParams.SerialPort, 500000)
        {
          DataBits = 8,
          Parity = Parity.None,
          StopBits = StopBits.One,
          Handshake = Handshake.None,
          NewLine = "\n",
          DtrEnable = false,   // 🔥 prevent auto-reset
          RtsEnable = false
        };

        _serialPort.DataReceived += SerialPort_DataReceived;
        _serialPort.Open();

        Log($"Serial Port {_connectionParams.SerialPort} Reconnected");
      } catch (Exception ex)
      {
        Log($"Failed to reconnect serial port: {ex.Message}");
      }
    }


    public string FindAvailablePort(string deviceNameHint = "Arduino")
    {
      foreach (string port in SerialPort.GetPortNames())
      {
        try
        {
          using (SerialPort testPort = new SerialPort(port, 500000))
          {
            testPort.ReadTimeout = 1000; //  Prevents hanging forever (1 second timeout)
            testPort.Open();
            testPort.WriteLine("PING");  //  Ask the Arduino to respond
            System.Threading.Thread.Sleep(500);  //  Give it time to respond
            // Try reading a response (but won't hang indefinitely)
            string response = testPort.ReadLine();

            if (!string.IsNullOrEmpty(response))
            {
              _connectionParams.SerialPort = port;
              //_serialPort.PortName = port;
              testPort.Close();
              Log($"Found and switched to available port: {port}");
              return port;
            }
          }
        }
        catch (TimeoutException)
        {
          Log($"Port {port} did not respond in time. Skipping...");
        }
        catch (Exception ex)
        {
          Log($"Error testing port {port}: {ex.Message}");
        }
      }
      return null;
    }



    public void Disconnect()
    {
      try
      {
        if (_serialPort != null)
        {
          if (_serialPort.IsOpen)
          {
            _serialPort.Close();
            Debug.WriteLine("[SerialCom] Serial port closed.");
          }
          _serialPort.Dispose();
          _serialPort = null;
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"[SerialCom] Error during Disconnect(): {ex.Message}");
      }
    }


    public void Send(string message)
      
    {
      Log("send " + message);
      if (IsConnected)
      {
        _serialPort.WriteLine(message);
      }
    }

    public string Receive()
    {
      try
      {
        if (IsConnected && _serialPort.IsOpen)
        {
          //  Check if data is available before reading
          if (_serialPort.BytesToRead > 0)
          {
            return _serialPort.ReadLine();
          }
        }
      }
      catch (TimeoutException)
      {
        Debug.WriteLine("Serial Read Timeout: No data received.");
      }
      catch (InvalidOperationException)
      {
        Debug.WriteLine("Serial Port is closed or disconnected.");
      }
      catch (OperationCanceledException)
      {
        Debug.WriteLine("Serial Read Operation was canceled.");
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"Serial Read Error: {ex.Message}");
      }

      return null; //  Return null if nothing was read
    }
    public void SendWiFiSettings(string ssid, string password)
    {
      string command = $"SET_WIFI:{ssid},{password}\n";
      _serialPort.Write(command);
      Debug.WriteLine($"Sent WiFi Credentials: {ssid} / {password}");
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




    // Temporary buffer for accumulating valid packets
   
    private bool _isReceivingPacket = false;




    private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
      try
      {
        int bytesToRead = _serialPort.BytesToRead;
        if (bytesToRead <= 0) return;

        byte[] buffer = new byte[bytesToRead];
        _serialPort.Read(buffer, 0, bytesToRead);

        for (int i = 0; i < buffer.Length; i++)
        {
          byte b = buffer[i];
          // 🔁 Packet handling
          if (_isReceivingPacket)
          {
            _serialBuffer.Add(b);

            if (_packetType == "lidar" &&
                _serialBuffer.Count > 4 &&
                _serialBuffer[^2] == 0xEE &&
                _serialBuffer[^1] == 0xBB)
            {
              int payloadSize = _serialBuffer.Count - 4;
              byte[] lidarPayload = new byte[payloadSize];
              _serialBuffer.CopyTo(2, lidarPayload, 0, payloadSize);

              if (payloadSize % 11 == 0)
              {
                Utility.ProcessLidarBatchBinary(lidarPayload, _dataQueue);
              } else
              {
                Debug.WriteLine($"⚠ Unexpected LiDAR payload size: {payloadSize} bytes");
              }

              _serialBuffer.Clear();
              _isReceivingPacket = false;
              _packetType = null;
            }

            continue; // ✅ Don't mix with string parsing
          }

          // 🔍 Detect new packet
          if (_serialBuffer.Count == 0 && b == 0xFF)
          {
            _serialBuffer.Add(b);
            continue;
          } else if (_serialBuffer.Count == 1 && _serialBuffer[0] == 0xFF && b == 0xAA)
          {
            _serialBuffer.Add(b);
            _isReceivingPacket = true;
            _packetType = "lidar";
            continue;
          } else
          {
            _serialBuffer.Clear(); // Not a valid packet, treat as text
          }

          // 📝 Text mode (ASCII printable or newline)
          if (b == '\n')
          {

            string msg = _textLineBuffer.ToString().Trim();
            if (!string.IsNullOrEmpty(msg))
            {
              OnMessageReceived?.Invoke($"Serial: {msg}");
            }

            _textLineBuffer.Clear();
          } else if (b >= 32 && b < 127)
          {
            _textLineBuffer.Append((char)b);
          } else
          {
            _textLineBuffer.Clear(); // Non-printable, clear it
          }
        }

      } catch (Exception ex)
      {
        OnMessageReceived?.Invoke($"Serial Error: {ex.Message}");
        Debug.WriteLine($"❌ Error in SerialPort_DataReceived: {ex.Message}");
        _serialBuffer.Clear();
        _isReceivingPacket = false;
        _packetType = null;
      }
    }






    //private void ProcessSerialData(string data)
    //{
    //  try
    //  {
    //    if (data.Contains("Status") || data.Contains("State"))
    //    {
    //      Log(data);
    //      return;
    //    }
    //    if (data.Contains("State"))
    //    {
    //      UpdateState(data);
    //      return;
    //    }
    //    if (data.Contains("VMDPE"))
    //    {
    //      Log($"Ignoring diagnostic data: {data}");
    //      return;
    //    }
    //    if (!IsValidLiDARData(data))
    //    {
    //      Log($"Invalid data format: {data}");
    //      return;
    //    }
    //    if (_state == LiDARState.PAUSED)
    //    {
    //      return;
    //    }
    //    string[] parts = data.Split(',');
    //    Log(data);
    //    if (parts.Length == 3)
    //    {
    //      float angle = float.Parse(parts[0], CultureInfo.InvariantCulture);
    //      float distance = float.Parse(parts[1], CultureInfo.InvariantCulture);
    //      byte quality = byte.Parse(parts[2], CultureInfo.InvariantCulture);

    //      // Enqueue a new DataPoint

    //      _dataQueue.Enqueue(new DataPoint(angle, distance, quality));
    //      Log($"Queue Size: {_dataQueue.Count}");
    //    }
    //    else
    //    {
    //      Log($"Invalid data format: {data}");
    //    }
    //  }
    //  catch (Exception ex)
    //  {
    //    Log($"Error in ProcessSerialData: {ex.Message}");
    //  }
    //}

    private bool IsValidLiDARData(string data)
    {
      return data.Count(c => c == ',') == 2 &&
             char.IsDigit(data[0]);
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
    public void Dispose()
    {
      try
      {
        if (_serialPort != null)
        {
          if (_serialPort.IsOpen)
          {
            _serialPort.Close();
            Debug.WriteLine("[SerialCom] Serial port closed in Dispose().");
          }
          _serialPort.Dispose();
          _serialPort = null;
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"[SerialCom] Error during Dispose(): {ex.Message}");
      }
    }


  }




}
