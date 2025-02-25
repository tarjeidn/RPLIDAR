using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.ImGuiNet;
using MQTTnet;
using RPLIDAR_Mapping.Features;
using RPLIDAR_Mapping.Features.Communications;
using RPLIDAR_Mapping.Features.Map;
using RPLIDAR_Mapping.Interfaces;
using RPLIDAR_Mapping.Models;
using RPLIDAR_Mapping.Utilities;



using System;
using System.Diagnostics;
using System.Linq;
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

    private float _mapScale = 1.0f;



    public Mapplication()
    {

      _graphics = new GraphicsDeviceManager(this);
      _graphics.IsFullScreen = false;
      _graphics.PreferredBackBufferWidth = AppSettings.Default.WindowWidth;
      _graphics.PreferredBackBufferHeight = AppSettings.Default.WindowHeight;


      Content.RootDirectory = "Content";
      IsMouseVisible = true;
      
    }

    protected override void Initialize()
    {
      ContentManagerProvider.Initialize(Content);
      GraphicsDeviceProvider.Initialize(GraphicsDevice);

      _guiManager = new GuiManager(this, _map);

      _GraphicsDevice = GraphicsDeviceProvider.GraphicsDevice;
      // TODO: Add your initialization logic here
      // Initialize the communication via the Device class
      //_device = new Device("serial", "COM4");
      _fpsCounter = new FPSCounter();
      _connectionParams = new ConnectionParams(
        AppSettings.Default.SerialPort,
        AppSettings.Default.WiFiSSID,
        AppSettings.Default.WiFiPassword,
        AppSettings.Default.mqttServer,
        AppSettings.Default.mqttPort
        );

      _LidarSettings = new LidarSettings();
      _LidarSettings.BatchSize = AppSettings.Default.LIDARDataBatchSize;

      // Apply resizing setting
      Window.AllowUserResizing =AppSettings.Default.AllowResizing;
      _graphics.ApplyChanges();
      _device = new Device(AppSettings.Default.CommunicationProtocol, _connectionParams, _guiManager);

      _inputManager = new InputManager(_device);

      // Start device initialization in a background task
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


      base.Initialize();
    }

    protected override void LoadContent()
    {
      ContentManagerProvider.LoadFont("DebugFont", "Fonts/Debug");
      _spriteBatch = GraphicsDeviceProvider.SpriteBatch;
      _map = new Map();
      _map._device = _device;
      _mapRenderer = new MapRenderer(_map);
      _mapRenderer._device = _device;
      StatisticsProvider.Initialize(_map.GetDistributor()._GridManager.GridStats, _map._MapStats);
    }

    protected override void Update(GameTime gameTime)
    {
      try
        {

        _fpsCounter.Update(gameTime);
        if (!_device.IsInitialized)
        {

          return; // Skip update logic until the device is ready
        }
        if (!_device.IsConnected)
        {
          Log($"No connection to {_connectionParams.SerialPort}");
          _device.Connect();
        }
        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || Keyboard.GetState().IsKeyDown(Keys.Escape))
          Exit();

        _inputManager.Update(gameTime);

        // Process LiDAR data
        var lidarDataList = _device.GetData();        
        _map.Update(lidarDataList);

        base.Update(gameTime);
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"EXCEPTION in Update(): {ex.Message}");
      }
    }

    protected override void Draw(GameTime gameTime)
    {
      _GraphicsDevice.Clear(new Color(_guiManager._colorV4));

      // Draw the map
      _mapRenderer.DrawMap(new Vector2(200, 200));

      _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
      _spriteBatch.Draw(_mapRenderer._mapTexture, new Vector2(200, 200), Color.White);
      _spriteBatch.DrawString(ContentManagerProvider.GetFont("DebugFont"), $"FPS: {_fpsCounter.FPS}", new Vector2(10, 10), Color.White);
      _spriteBatch.End();
      base.Draw(gameTime);
      _guiManager.Draw(gameTime);
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
        _device?.Dispose();
        Log("LiDAR stopped and device disposed.");
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



  }
}
