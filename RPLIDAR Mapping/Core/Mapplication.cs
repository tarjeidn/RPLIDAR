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
    private Camera _camera;
    private Rectangle _view;
    private float _mapScale = 1.0f;
    private Vector2 _mapDrawingPosition = new Vector2(100, 100);




    public Mapplication()
    {

      _graphics = new GraphicsDeviceManager(this);
      _graphics.IsFullScreen = false;
      _graphics.PreferredBackBufferWidth = AppSettings.Default.WindowWidth;
      _graphics.PreferredBackBufferHeight = AppSettings.Default.WindowHeight;


      Content.RootDirectory = "Content";
      IsMouseVisible = true;

      IsFixedTimeStep = false; // Let the game run as fast as possible
      _graphics.SynchronizeWithVerticalRetrace = false; // Disable VSync

    }

    protected override void Initialize()
    {
      AlgorithmProvider.Initialize();
      
      GraphicsDeviceProvider.Initialize(GraphicsDevice, UtilityProvider.FPSCounter);
      UtilityProvider.Initialize(GraphicsDeviceProvider.GraphicsDevice);
      ContentManagerProvider.Initialize(Content);
      _camera = UtilityProvider.Camera;
      _guiManager = new GuiManager(this, _map);
      _fpsCounter = UtilityProvider.FPSCounter;
      _GraphicsDevice = GraphicsDeviceProvider.GraphicsDevice;
      // TODO: Add your initialization logic here
      // Initialize the communication via the Device class
      //_device = new Device("serial", "COM4");
      
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
        float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds; // Get time since last frame

        _inputManager.Update(gameTime);

        _camera.SetZoom(AppSettings.Default.MapZoom);
        //  Get the device's relative position on _mapTexture
        Vector2 centerOfFullMap = new Vector2(_mapRenderer._mapTexture.Width / 2, _mapRenderer._mapTexture.Height / 2);
        Rectangle deviceRect = _device.GetDeviceRectRelative(centerOfFullMap);
        Vector2 deviceRelativePosition = new Vector2(deviceRect.X + deviceRect.Width / 2, deviceRect.Y + deviceRect.Height / 2);
        _camera.CenterOn(deviceRelativePosition);  //  Center on the correct relative position

        // Process LiDAR data
        var lidarDataList = _device.GetData();        
        _map.Update(lidarDataList, deltaTime);

        base.Update(gameTime);
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"EXCEPTION in Update(): {ex.Message}");
      }
    }

protected override void Draw(GameTime gameTime)
{
    _GraphicsDevice.Clear(Color.Black);

      // ✅ Get the corrected source rectangle from Camera
      //Rectangle sourceRect = _camera.GetSourceRectangle();
      Rectangle destRect = _camera.GetDestinationRectangle();
      Rectangle sourceRect = _camera.GetSourceRectangle();

      _mapRenderer.DrawMap();
      _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

    _spriteBatch.Draw(
        _mapRenderer._mapTexture,
        destRect,
        sourceRect,
        Color.White
    );

    _spriteBatch.DrawString(
        ContentManagerProvider.GetFont("DebugFont"),
        $"FPS: {_fpsCounter.FPS}\nLiDAR Updates/s: {StatisticsProvider.MapStats.LiDARUpdatesPerSecond}",
        new Vector2(10, 10),
        Color.White
    );

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
