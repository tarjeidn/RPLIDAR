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

namespace RPLIDAR_Mapping.Core
{
  /// <summary>
  /// The main application class handling initialization, content loading, game loop updates, and rendering.
  /// </summary>
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

    /// <summary>
    /// Indicates whether the map was updated during the current frame.
    /// </summary>
    public bool MapUpdated = false;

    /// <summary>
    /// Constructor initializes graphics settings and content root.
    /// </summary>
    public Mapplication()
    {
      _graphics = new GraphicsDeviceManager(this)
      {
        IsFullScreen = false,
        PreferredBackBufferWidth = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Width,
        PreferredBackBufferHeight = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Height
      };

      Content.RootDirectory = "Content";
      IsMouseVisible = true;
      IsFixedTimeStep = false;
      _graphics.SynchronizeWithVerticalRetrace = false;
    }

    /// <summary>
    /// Sets up services and components on startup.
    /// </summary>
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
          AppSettings.Default.CommunicationProtocol);

      _LidarSettings = new LidarSettings
      {
        BatchSize = AppSettings.Default.LIDARDataBatchSize
      };

      Window.AllowUserResizing = AppSettings.Default.AllowResizing;
      _graphics.ApplyChanges();

      base.Initialize();
    }

    /// <summary>
    /// Loads map, GUI, device, and other content dependencies.
    /// </summary>
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

      _mapRenderer = new MapRenderer(_map)
      {
        _device = _device
      };
      UtilityProvider.MapRenderer = _mapRenderer;

      GUIProvider.UserSelection._map = _map;
      _guiManager._map = _map;
      _camera._device = _device;

      StatisticsProvider.Initialize(_map.GetDistributor()._GridManager.GridStats, _map._MapStats);

      Task.Run(() =>
      {
        while (!_device.IsInitialized)
          Thread.Sleep(1000);

        Log("Device initialized!");
        // _device.UpdateLidarSettings(_LidarSettings);
      });
    }

    /// <summary>
    /// Updates the application state, including device connection and map processing.
    /// </summary>
    /// <param name="gameTime">Provides a snapshot of timing values.</param>
    protected override void Update(GameTime gameTime)
    {
      try
      {
        _fpsCounter.Update(gameTime);

        if (!_device.IsInitialized)
          return;

        if (!_device.IsConnected)
        {
          Log($"No connection to {_connectionParams.SerialPort}");
          _device.Connect();
        }

        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed ||
            Keyboard.GetState().IsKeyDown(Keys.Escape))
          Exit();

        List<DataPoint> lidarDataList = _device.GetData();
        MapUpdated = _map.Update(lidarDataList, gameTime);

        base.Update(gameTime);
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"EXCEPTION in Update(): {ex.Message}");
      }
    }

    /// <summary>
    /// Draws the map and GUI.
    /// </summary>
    /// <param name="gameTime">Provides a snapshot of timing values.</param>
    protected override void Draw(GameTime gameTime)
    {
      if (_map == null) return;
      _mapRenderer.DrawMap(MapUpdated);
      _guiManager.Draw(gameTime);
      base.Draw(gameTime);
    }

    /// <summary>
    /// Called when the application is unloading content.
    /// </summary>
    protected override void UnloadContent()
    {
      DisposeDevice();
      base.UnloadContent();
    }

    /// <summary>
    /// Handles safe shutdown of the device.
    /// </summary>
    private void DisposeDevice()
    {
      try
      {
        if (_device != null)
        {
          _device.Send("P");
          Log("Sent STOP command to LiDAR.");
          Thread.Sleep(500);
          _device.Dispose();
          Log("LiDAR stopped and device disposed.");
        }
      }
      catch (Exception ex)
      {
        Log($"Error during device disposal: {ex.Message}");
      }
    }

    /// <summary>
    /// Called on application exit to dispose resources.
    /// </summary>
    protected override void OnExiting(object sender, EventArgs args)
    {
      DisposeDevice();
      base.OnExiting(sender, args);
    }

    /// <summary>
    /// Ensures all resources are properly disposed.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
      if (disposing)
        DisposeDevice();
      base.Dispose(disposing);
    }

    /// <summary>
    /// Reloads the device and all dependent components.
    /// </summary>
    /// <param name="newDevice">The new device instance to use.</param>
    public void ReloadDevice(Device newDevice)
    {
      if (_device != null)
      {
        _device.Dispose();
        _device = null;
      }

      _device = newDevice;
      _inputManager = new InputManager(newDevice);
      _map = new Map(newDevice, _inputManager);
      _mapRenderer = new MapRenderer(_map)
      {
        _device = newDevice
      };

      StatisticsProvider.Initialize(_map.GetDistributor()._GridManager.GridStats, _map._MapStats);

      Task.Run(() =>
      {
        while (!_device.IsInitialized)
          Thread.Sleep(1000);

        Log("Device initialized!");
        _device.UpdateLidarSettings(_LidarSettings);
      });

      Console.WriteLine("Device and dependent components reloaded successfully!");
    }
  }
}
