using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.ImGuiNet;
using MQTTnet;
using RPLIDAR_Mapping.Features;
using RPLIDAR_Mapping.Features.Communications;
using RPLIDAR_Mapping.Features.Map;
using RPLIDAR_Mapping.Features.Map.UI;
using RPLIDAR_Mapping.Interfaces;
using RPLIDAR_Mapping.Models;
using RPLIDAR_Mapping.Providers;
using RPLIDAR_Mapping.Utilities;



using System;
using System.Collections.Generic;
using System.Diagnostics;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;

//using System.Windows.Forms;



namespace RPLIDAR_Mapping.Core
{
  public class Mapplication : Game
  {
    private InputManager _inputManager;
    private GraphicsDeviceManager _graphics;
    private GraphicsDevice _GraphicsDevice;
    private SpriteBatch _spriteBatch;
    private SerialCom _serialCom;
    private Map _map;
    private MapRenderer _mapRenderer;
    private Device _device;
    private ICommunication _communication;
    private ConnectionParams _connectionParams;
    private SpriteFont _debugFont;
    private FPSCounter _fpsCounter;
    private LidarSettings _LidarSettings;
    private GuiManager _guiManager;
    private Camera _camera;
    private Rectangle _view;
    private float _mapScale = 1.0f;
    private Vector2 _mapDrawingPosition = new Vector2(100, 100);
    public bool MapUpdated = false;




    public Mapplication()
    {
      _graphics = new GraphicsDeviceManager(this);
      _graphics.IsFullScreen = false;
      _graphics.PreferredBackBufferWidth = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Width;
      _graphics.PreferredBackBufferHeight = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Height;

      Content.RootDirectory = "Content";
      IsMouseVisible = true;

      IsFixedTimeStep = false; // Let the game run as fast as possible
      _graphics.SynchronizeWithVerticalRetrace = false; // Disable VSync

    }
    protected override void Initialize()
    {
      AppDomain.CurrentDomain.ProcessExit += (s, e) => DisposeDevice();
      AppDomain.CurrentDomain.UnhandledException += (s, e) => DisposeDevice();
      AlgorithmProvider.Initialize();
      GUIProvider.Initialize();
      GraphicsDeviceProvider.Initialize(GraphicsDevice, UtilityProvider.FPSCounter);
      UtilityProvider.Initialize(GraphicsDeviceProvider.GraphicsDevice);
      ContentManagerProvider.Initialize(Content);
      _camera = UtilityProvider.Camera;

      _fpsCounter = UtilityProvider.FPSCounter;
      _GraphicsDevice = GraphicsDeviceProvider.GraphicsDevice;
      _connectionParams = new ConnectionParams(
        AppSettings.Default.SerialPort,
        AppSettings.Default.WiFiSSID,
        AppSettings.Default.WiFiPassword,
        AppSettings.Default.mqttServer,
        AppSettings.Default.mqttPort,
        AppSettings.Default.CommunicationProtocol
        );

      _LidarSettings = new LidarSettings();
      _LidarSettings.BatchSize = AppSettings.Default.LIDARDataBatchSize;

      // Apply resizing setting
      Window.AllowUserResizing = AppSettings.Default.AllowResizing;
      _graphics.ApplyChanges();





      // Start device initialization in a background task



      base.Initialize();
    }
    protected override void LoadContent()
    {
      ContentManagerProvider.LoadFont("DebugFont", "Fonts/Debug");
      _guiManager = new GuiManager(this);
      _device = new Device(_connectionParams, _guiManager);
      UtilityProvider.Device = _device;
      AlgorithmProvider.TileMerge._device = _device;
      _inputManager = new InputManager(_device);
      _spriteBatch = GraphicsDeviceProvider.SpriteBatch;
      _map = new Map(_device, _inputManager);
      UtilityProvider.Map = _map;
      //AlgorithmProvider.DevicePositionEstimator._map = _map;
      //AlgorithmProvider.DevicePositionEstimator._device = _device;
      _mapRenderer = new MapRenderer(_map);
      _mapRenderer._device = _device;
      UtilityProvider.MapRenderer = _mapRenderer;
      GUIProvider.UserSelection._map = _map;
      _guiManager._map = _map;
      _camera._device = _device;
      StatisticsProvider.Initialize(_map.GetDistributor()._GridManager.GridStats, _map._MapStats);
      Task.Run(() =>
      {
        while (!_device.IsInitialized)
        {
          //Log("Waiting for device to initialize...");
          System.Threading.Thread.Sleep(1000);
        }

        Log("Device initialized!");
        //_device.UpdateLidarSettings(_LidarSettings);
      });
    }

    protected override void Update(GameTime gameTime)
    {
      try
      {
        _fpsCounter.Update(gameTime);

        if (!_device.IsInitialized)
        {
          return;
        }

        if (!_device.IsConnected)
        {
          Log($"No connection to {_connectionParams.SerialPort}");
          _device.Connect();
        }

        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || Keyboard.GetState().IsKeyDown(Keys.Escape))
          Exit();

        //float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

        //_inputManager.Update(gameTime);


        UtilityProvider.Camera.CenterOn(Vector2.Zero);

        // Process LiDAR data
        List<DataPoint> lidarDataList = _device.GetData();

        MapUpdated = _map.Update(lidarDataList, gameTime);


        base.Update(gameTime);
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"EXCEPTION in Update(): {ex.Message}");
      }
    }



    protected override void Draw(GameTime gameTime)
    {
      if (_map == null) return;
      // 

      _mapRenderer.DrawMap(MapUpdated);

      _guiManager.Draw(gameTime);
      base.Draw(gameTime);

    }

    protected override void UnloadContent()
    {
      DisposeDevice();
      base.UnloadContent();
    }

    private void DisposeDevice()
    {
      try
      {
        if (_device != null)
        {
          _device.Send("P"); //  Send stop command before disposal
          Log("Sent STOP command to LiDAR.");
          Thread.Sleep(500); // Small delay to ensure command is sent

          _device.Dispose();
          Log("LiDAR stopped and device disposed.");
        }
      }
      catch (Exception ex)
      {
        Log($"Error during device disposal: {ex.Message}");
      }
    }

    protected override void OnExiting(object sender, EventArgs args)
    {
      DisposeDevice();
      base.OnExiting(sender, args);
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        DisposeDevice();
      }
      base.Dispose(disposing);
    }
    public void ReloadDevice(Device newDevice)
    {
      // Dispose of the old device
      if (_device != null)
      {
        _device.Dispose();
        _device = null;
      }

      // Reinitialize the device and dependencies
      _device = newDevice;
      _inputManager = new InputManager(newDevice);
      _map = new Map(newDevice, _inputManager);
      _mapRenderer = new MapRenderer(_map);
      _mapRenderer._device = newDevice;

      // Reinitialize statistics provider
      StatisticsProvider.Initialize(_map.GetDistributor()._GridManager.GridStats, _map._MapStats);

      // Start device initialization
      Task.Run(() =>
      {
        while (!_device.IsInitialized)
        {
          Log("Waiting for device to initialize...");
          System.Threading.Thread.Sleep(1000);
        }

        Log("Device initialized!");
        _device.UpdateLidarSettings(_LidarSettings);
      });

      Console.WriteLine("Device and dependent components reloaded successfully!");
    }



  }
}
