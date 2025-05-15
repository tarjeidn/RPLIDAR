using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RPLIDAR_Mapping.Features.Communications;
using RPLIDAR_Mapping.Interfaces;
using RPLIDAR_Mapping.Models;
using RPLIDAR_Mapping.Providers;

namespace RPLIDAR_Mapping.Features.Communications
{
  public class Device : IDisposable
  {
    private ICommunication _communication;
    public LidarSettings _LidarSettings;
    public ConnectionParams _ConnectionParams;
    private SerialPort _serialPort; //  Keep a direct reference to Serial
    private Thread _serialListenerThread; //  Background thread for Serial reading
    private bool _isListeningSerial = false; //  Flag to control serial listening
    public GuiManager _GuiManager;
    public Rectangle _deviceRect {  get; private set; }
    public Vector2 _devicePosition { get; set; }
    public long lastVelocityTimestamp { get; set; }
    public Vector2 lastVelocity { get; set; }
    public Texture2D _deviceTexture { get; private set; }
    public float _deviceOrientation { get; set; }
    private const int DeviceWidth = 20; 
    private const int DeviceHeight = 20;



    private readonly Queue<Vector2> _positionHistory = new();
    private const float MinDistanceThreshold = 50f; // e.g. 10mm or 1cm
    private const int MaxHistoryLength = 200;
    public IReadOnlyList<Vector2> PositionHistory => _positionHistory.ToList();
    public Device(ConnectionParams connectionParameters, GuiManager gm)
    {
      string communicationType = connectionParameters.ConnectionType;
      //AlgorithmProvider.DevicePositionEstimator._device = this;
      _ConnectionParams = connectionParameters; 
      _GuiManager = gm;
      _GuiManager.SetDevice(this);
      _devicePosition = new Vector2(0, 0);
      UpdateDeviceRect();
      _deviceTexture = new Texture2D(GraphicsDeviceProvider.GraphicsDevice, 1, 1);
      _deviceTexture.SetData(new[] { Color.White });
      try
      {
        // Setup serial port to listen when using wifi AND USB is connected
        if (communicationType.ToLower() == "wifi")
        {

          //StartSerialListener(connectionParameters.SerialPort);
        }
        _communication = communicationType.ToLower() switch
        {
          "serial" => new SerialCom(connectionParameters),
          "wifi" => new Wireless(connectionParameters), //  Default to WiFi but keep Serial active
          _ => throw new ArgumentException($"Unsupported communication type: {communicationType}")
        };
        // Always start Serial listening, even if using WiFi

      }
      catch (Exception e)
      {
        Log($"Failed to connect {communicationType}: {e.Message}");
      }

      if (_communication != null)
      {
        _communication.OnMessageReceived += _GuiManager.AddLogMessage;
        //_communication.InitializeMode();
      }

      _LidarSettings = new LidarSettings();

      _positionHistory.Enqueue(_devicePosition); // Add initial position to history

    }
    public void UpdatePositionHistory(Vector2 newPos)
    {
      // Only add to history if moved enough
      if (_positionHistory.Count == 0 || Vector2.Distance(_positionHistory.Last(), newPos) > MinDistanceThreshold)
      {
        _positionHistory.Enqueue(newPos);

        // Keep history bounded
        if (_positionHistory.Count > MaxHistoryLength)
          _positionHistory.Dequeue();
      }

      
    }

    public void SetDevicePosition(Vector2 newPos)
    {
      _devicePosition = newPos;
    }
    private void UpdateDeviceRect()
    {
      _deviceRect = new Rectangle(
          (int)(_devicePosition.X - DeviceWidth / 2),  // Center X
          (int)(_devicePosition.Y - DeviceHeight / 2), // Center Y
          DeviceWidth,
          DeviceHeight
      );
    }
    public Rectangle GetDeviceRectRelative(Vector2 screenPos)
    {
      return new Rectangle(
          (int)(screenPos.X - DeviceWidth / 2),  // Center the device at screenPos
          (int)(screenPos.Y - DeviceHeight / 2),
          DeviceWidth,
          DeviceHeight
      );
    }



    private void SendInitMode()
    {
      if (_serialPort != null && _serialPort.IsOpen)
      {
        try
        {
          Log("Attempting to send 'SET_MODE:WIFI'...");
          _serialPort.WriteLine("SET_MODE:WIFI");
          Log(" Sent 'SET_MODE:WIFI' to Arduino.");
        }
        catch (Exception ex)
        {
          Log($" Error writing to serial: {ex.Message}");
        }
      }
      else
      {
        Log(" No serial connection detected");
      }
    }

    private void StartSerialListener(string portName)
    {
      try
      {
        Log($"Attempting to open Serial Port: {portName}...");

        _serialPort = new SerialPort(portName, 500000);
        _serialPort.Open();

        Log(" Serial port opened successfully!");

        _isListeningSerial = true;

        _serialListenerThread = new Thread(SerialListenerLoop)
        {
          IsBackground = true
        };

        _serialListenerThread.Start();
        Log(" Serial listener thread started");
        // Send init mode as wifi over serial, when using wifi. 
        //SendInitMode();

        Log($"Started Serial listening on {portName}");
      }
      catch (Exception ex)
      {
        Log($" Failed to start Serial listening: {ex.Message}");
      }
    }

    private void SerialListenerLoop()
    {
      while (_isListeningSerial)
      {
        try
        {
          if (_serialPort != null && _serialPort.IsOpen && _serialPort.BytesToRead > 0)
          {
            string message = _serialPort.ReadLine().Trim();
            Log($"[Serial Listener] {message}");

            //  Forward Serial messages to the main handler
            HandleIncomingSerialMessage(message);
          }
        }
        catch (Exception ex)
        {
          Log($"Serial Listener Error: {ex.Message}");
        }

        Thread.Sleep(100); //  Prevents CPU overload
      }
    }
    private void HandleIncomingSerialMessage(string message)
    {
      _GuiManager.AddLogMessage($"{message}");

    }
    public void UpdateLidarSettings(LidarSettings newSettings)
    {
      if (newSettings == null) throw new ArgumentNullException(nameof(newSettings));

      _LidarSettings = newSettings;
      string settingsJson = newSettings.ToJson();
      Send($"SETTINGS:{settingsJson}");
    }
    private void Log(string message)
    {
      Debug.WriteLine("Log");
      _GuiManager?.AddLogMessage($"{message}");
    }
    public List<DataPoint> GetData() => _communication.DequeueAllData();

    public void Connect() => _communication.Connect();

    public void Disconnect() => _communication.Disconnect();

    public bool IsConnected => _communication.IsConnected;

    public bool IsInitialized
    {
      get
      {
        var initialized = _communication?.IsInitialized ?? false;

        return initialized;
      }
    }

    public void Send(string message) => _communication.Send(message);

    public string Receive() => _communication.Receive();



    public void Dispose()
    {
      try
      {
        _communication?.Dispose();
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"[Device] Error in Dispose(): {ex.Message}");
      }
    }
  }
  }
  