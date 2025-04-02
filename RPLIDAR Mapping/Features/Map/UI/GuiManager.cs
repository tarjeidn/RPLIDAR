using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.ImGuiNet;
using RPLIDAR_Mapping.Features.Map;
using RPLIDAR_Mapping.Features.Map.Algorithms;
using RPLIDAR_Mapping.Features.Map.UI;
using RPLIDAR_Mapping.Features.Communications;
using System;
using System.Collections.Generic;
//using SharpDX;
using System.Diagnostics;
using RPLIDAR_Mapping.Models;
using RPLIDAR_Mapping.Core;
using RPLIDAR_Mapping.Providers;

public class GuiManager
{
  private List<string> _logMessages = new();
  private ImGuiRenderer _guiRenderer;
  private GraphicsDevice _graphicsDevice;
  private Device Device;
  private ConnectionParams _tempConnectionParams;
  private LidarSettings _tempLidarSettings;
  private Mapplication _MainApplication;
  private UserSelection UserSelection;
  public Map _map { get; set; }
  private System.Numerics.Vector2 GuiSize;
  private System.Numerics.Vector2 GuiPosition;
  private bool _showLogWindow = true;
  private List<string> _logBuffer;
  private ImFontPtr _loggerFont;
  private float _fontScale = 2.0f;
  private TileTrustRegulator _tileTrustRegulator;
  private TileMerge _tileMerge;
  private int ScreenWidth;
  private int ScreenHeight;
  private bool _showLidarSettings = false;

  public GuiManager(Mapplication mainapp)  {
    _MainApplication = mainapp;    
    _guiRenderer = new ImGuiRenderer(mainapp);
    _graphicsDevice = GraphicsDeviceProvider.GraphicsDevice;
    _tileTrustRegulator = AlgorithmProvider.TileTrustRegulator;
    _tileMerge = AlgorithmProvider.TileMerge;
    UserSelection = GUIProvider.UserSelection;
    ScreenWidth = _graphicsDevice.Viewport.Width;
    ScreenHeight = _graphicsDevice.Viewport.Height;
    GuiSize = new System.Numerics.Vector2((int)(ScreenWidth * 0.3), (int)(ScreenHeight * 0.8));
    GuiPosition = new System.Numerics.Vector2((int)(ScreenWidth * 0.66), (int)(ScreenHeight * 0.05));
    _guiRenderer.RebuildFontAtlas();
    ImGui.GetIO().FontGlobalScale = _fontScale;
    ImGui.GetIO().DisplayFramebufferScale = new System.Numerics.Vector2(_fontScale, _fontScale);
  }

  public void AddLogMessage(string message)
  {
    Debug.WriteLine(message);
    if (_logMessages.Count > 1000)
      _logMessages.RemoveAt(0);

    _logMessages.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
  }
  public void SetDevice(Device device)
  {
    Device = device;
    _tempConnectionParams = device._ConnectionParams;
  }
  public void DrawLidarSettingsWindow()
  {
    ImGui.SetNextWindowSize(new System.Numerics.Vector2(400, 400), ImGuiCond.FirstUseEver);
    ImGui.Begin("LiDAR Settings", ref _showLidarSettings, ImGuiWindowFlags.AlwaysAutoResize);

    // LiDAR Configuration Section


    ImGui.Separator(); // Adds a visual separator

    // Use local variables for input fields
    string serialPort = _tempConnectionParams.SerialPort;
    string wifiSSID = _tempConnectionParams.WiFiSSID;
    string wifiPassword = _tempConnectionParams.WiFiPW;
    string mqttServer = _tempConnectionParams.MQTTBrokerAddress;
    int mqttPort = _tempConnectionParams.MQTTPort;

    // Device Connection Settings Section
    ImGui.Text("Connection Parameters:");
    if (ImGui.InputText("Serial Port", ref serialPort, 64))
      _tempConnectionParams.SerialPort = serialPort;

    if (ImGui.InputText("WiFi SSID", ref wifiSSID, 64))
      _tempConnectionParams.WiFiSSID = wifiSSID;

    if (ImGui.InputText("WiFi Password", ref wifiPassword, 64))
      _tempConnectionParams.WiFiPW = wifiPassword;

    if (ImGui.InputText("MQTT Server", ref mqttServer, 64))
      _tempConnectionParams.MQTTBrokerAddress = mqttServer;

    if (ImGui.InputInt("MQTT Port", ref mqttPort))
      _tempConnectionParams.MQTTPort = mqttPort;

    ImGui.Separator();

    // Apply Button
    if (ImGui.Button("Apply Settings"))
    {
      ApplyLidarSettings();
    }

    ImGui.End();
  }

  // Function to Apply Settings
  private void ApplyLidarSettings()
  {
    // Send updated settings to the LiDAR
    Device.UpdateLidarSettings(_tempLidarSettings);

    // Update Connection Parameters
    Device = new Device(_tempConnectionParams, this);

    // Optionally, log the update
    Console.WriteLine("Updated LiDAR settings: " + _tempLidarSettings.ToJson());
    _MainApplication.ReloadDevice(Device);
  }

  public void Draw(GameTime gametime)
  {
    _guiRenderer.BeginLayout(gametime);

    if (_showLogWindow )
    {
      if (Device.IsInitialized) DrawMainWindow();
      else
      {
        _showLidarSettings = true;
        DrawLidarSettingsWindow();
      }
    }
    _guiRenderer.EndLayout();
  }
  private void DrawMainWindow()
  {
    ImGui.SetNextWindowPos(
      GuiPosition,
      ImGuiCond.Always
      );
    ImGui.SetNextWindowSize(
      GuiSize,
      ImGuiCond.Always
      );
    // Remove decorations
    ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;

    //ImGui.Begin("Main Window", ref _showLogWindow, ImGuiWindowFlags.MenuBar);
    ImGui.Begin("Main Window", ref _showLogWindow, windowFlags);
    // **Merge Highlighted Lines Button**
    if (UserSelection.highlightedLines.Count > 0) // Only show if lines are selected
    {
      if (ImGui.Button("Infer missing lines"))
      {
        UserSelection.InferLinesBetweenSelected();
      }
      if (ImGui.Button("Delete selected lines"))
      {
        UserSelection.DeleteSelectedLines();
      }
    }
    if (UserSelection.highlightedLines.Count > 0 ) // Only show if lines are selected
    {
      ImGui.SameLine();
      if (ImGui.Button("Merge Selected Lines"))
      {
        UserSelection.MergeSelectedLinesIntoWalls();
      }
      if (ImGui.Button("Smooth Premanent Lines"))
      {
        UserSelection.SmoothPermanentLines();
      }
    }

    float zoom = MapScaleManager.Instance.MapZoomFactor;
    float gridScaleFactor = MapScaleManager.Instance.ScaleFactor;
    float minPointDistance = _map._minPointDistance;
    int maxPointDistance = _map.MaxAcceptableDistance;
    int minPointQuality = _map._minPointQuality;
    int MinTileBufferSizeToAdd = _map.MinTileBufferSizeToAdd;
    bool drawSightlines = UtilityProvider.MapRenderer.DrawSightLines;

    if (ImGui.SliderFloat("Grid Scale", ref gridScaleFactor, 0.1f, 8.0f, "%.2f"))
    {
      MapScaleManager.Instance.SetScaleFactor(gridScaleFactor);
      _MainApplication.MapUpdated = true;
    }
    if (ImGui.SliderFloat("Zoom", ref zoom, 0.05f, 1))
      MapScaleManager.Instance.SetMapZoomFactor(zoom);
    if (ImGui.SliderInt("Minimum point quality", ref minPointQuality, 0, 100))
      _map._minPointQuality = minPointQuality;
    if (ImGui.SliderFloat("Minimum Point Distance", ref minPointDistance, 0, 200))
      _map._minPointDistance = minPointDistance;
    if (ImGui.SliderInt("MinimumPointDistance", ref maxPointDistance, 200, 6000))
      _map.MaxAcceptableDistance = maxPointDistance;
    if (ImGui.SliderInt("Minimum tiles to add", ref MinTileBufferSizeToAdd, 0, 500))
      _map.MinTileBufferSizeToAdd = MinTileBufferSizeToAdd;
    if (ImGui.SliderFloat("Font Scale", ref _fontScale, 0.5f, 4.0f, "%.2f"))
    {
      ImGui.GetIO().FontGlobalScale = _fontScale;
      ImGui.GetIO().DisplayFramebufferScale = new System.Numerics.Vector2(_fontScale, _fontScale);
    }
    if (ImGui.Checkbox("Draw sightlines", ref drawSightlines))
    {
      UtilityProvider.MapRenderer.DrawSightLines = drawSightlines;
    }
    //  Begin Tabs
    if (ImGui.BeginTabBar("MainTabs"))
    {
      //  Tab: Log Monitor
      if (ImGui.BeginTabItem("Log Monitor"))
      {
        DrawLogWindow();
      }
      //  Tab: Statistics
      if (ImGui.BeginTabItem("Statistics"))
      {
        DrawStatisticsWindow();

      }
      // Regulator Settings Tab
      if (ImGui.BeginTabItem("Regulator Settings"))
      {
        DrawTileRegulatorSettings();
      }
      if (ImGui.BeginTabItem("Map scale Settings"))
      {
        DrawScalingSettings();
      }

      ImGui.EndTabBar();
    }
    if (ImGui.Button("Start LiDAR"))
    {
      Device.Send("s");
    }
    if (ImGui.Button("Stop LiDAR"))
    {
      Device.Send("p"); // Example: Replace with the correct stop command
    }
    ImGui.End();
  }

  //  Log Monitor Tab
  private void DrawLogWindow()
  {
    // Display contents in a scrolling region
    ImGui.TextColored(new System.Numerics.Vector4(1, 1, 0, 1), "Device log");
    ImGui.PushFont(_loggerFont);

    ImGui.BeginChild("Scrolling", new System.Numerics.Vector2(0, 300));

    for (var n = 0; n < _logMessages.Count; n++)
    {
      ImGui.Text(_logMessages[n]);
    }

    //  Auto-scroll to the bottom
    if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
    {
      ImGui.SetScrollHereY(1.0f);
    }

    ImGui.EndChild();
    ImGui.EndTabItem();
  }

  //  Statistics Tab
  private void DrawStatisticsWindow()
  {
    ImGui.TextColored(new System.Numerics.Vector4(0, 1, 1, 1), "Statistics"); // Cyan title

    //  General performance stats
    ImGui.Text($"FPS: {StatisticsProvider.MapStats.FPS}");
    ImGui.Text($"LiDAR Updates Per Second: {StatisticsProvider.MapStats.PointsPerSecond}");
    ImGui.Text($"Data Batch Size: {StatisticsProvider.MapStats.CurrentPacketSize}");

    ImGui.Separator(); //  Visual break

    //  Graph for points handled per frame
    var samples = StatisticsProvider.MapStats._pointHistory.ToArray();
    if (samples.Length > 0)
    {
      float[] floatSamples = Array.ConvertAll(samples, x => (float)x);
      ImGui.PlotLines("Points Handled Per Frame", ref floatSamples[0], floatSamples.Length);
    }

    ImGui.Separator(); //  Visual break

    //  Updated Grid Statistics
    var gridStats = StatisticsProvider.GridStats;

    ImGui.Text($"Total Grids: {gridStats.TotalGrids}");
    ImGui.Text($"Total Active Tiles: {gridStats.TotalHitTiles}");
    ImGui.Text($"Total Points Processed: {gridStats.TotalPointsHandled}");

    ImGui.Separator(); //  Tile statistics

    ImGui.Text($"Highest Hits on a Single Tile: {gridStats.HighestTileHitCount}");
    ImGui.Text($"Average Tile Hit Count: {gridStats.AverageTileHitCount}");
    ImGui.Text($"Tiles Above Average Hits: {gridStats.HighTotalHitTiles}");
    ImGui.Text($"Total Hits on High-Intensity Tiles: {gridStats.HighTotalHitCount}");
    ImGui.Text($"Avg. Hits of High-Intensity Tiles: {gridStats.HighAverageTileHitCount}");

    ImGui.EndTabItem();
  }

  private void DrawTileRegulatorSettings()
  {

    //  Display dynamically updating values
    ImGui.Text($"Current Tile Count: {StatisticsProvider.GridStats.TotalHitTiles}");
    ImGui.Text($"Points Per Second: {StatisticsProvider.MapStats.PointsPerSecond}");



    //  Read values from the regulator
    bool regulatorEnabled = _tileTrustRegulator.RegulatorEnabled;
    bool drawMergedTiles = _tileMerge.DrawMergedTiles;
    bool drawMergedLines = _tileMerge.DrawMergedLines;
    bool computeMerged = _tileMerge.ComputeMergedTiles;
    float trustIncrement = _tileTrustRegulator.TrustIncrement;
    float trustDecrement = _tileTrustRegulator.TrustDecrement;
    float tileMergeThreshold = _tileMerge.TileMergeThreshold;
    int decayFrequency = _tileTrustRegulator.DecayFrequency;
    int trustThreshold = _tileTrustRegulator.TrustThreshold;
    int mergeFrequency = _tileMerge.MergeFrequency;
    int MinLargeFeaturesLineLength = _tileMerge.MinLargeFeaturesLineLength;
    int minTileClusterSize = _tileMerge.MinTileClusterSize;
    int mergeTileRadius = _tileMerge.mergeTileRadius;

    //  Sliders allow manual adjustments (values are updated in TileTrustRegulator)
    if (ImGui.Checkbox("Compute merged points", ref computeMerged))
    {
      _tileMerge.ComputeMergedTiles = computeMerged;

      // If ComputeMerged is disabled, also disable the other two checkboxes
      if (!computeMerged)
      {
        drawMergedTiles = false;
        drawMergedLines = false;
        _tileMerge.DrawMergedTiles = false;
        _tileMerge.DrawMergedLines = false;
      }
    }

    ImGui.SameLine();

    // Disable the checkboxes if computeMerged is false
    ImGui.BeginDisabled(!computeMerged);
    if (ImGui.Checkbox("Draw merged tiles", ref drawMergedTiles))
    {
      _tileMerge.DrawMergedTiles = drawMergedTiles;
    }
    ImGui.SameLine();

    if (ImGui.Checkbox("Draw merged lines", ref drawMergedLines))
    {
      _tileMerge.DrawMergedLines = drawMergedLines;
    }
    ImGui.EndDisabled();
    if (ImGui.SliderInt("Point Cluster Merge Radius", ref mergeTileRadius, 0, 50))
      _tileMerge.mergeTileRadius = mergeTileRadius; ;
    if (ImGui.SliderInt("Minimum merged tile cluster size", ref minTileClusterSize, 2, 100))
    {
      _tileMerge.MinTileClusterSize = minTileClusterSize;
    }
    if (ImGui.SliderInt("Minimum large feature line length", ref MinLargeFeaturesLineLength, 10, 1000))
    {
      _tileMerge.MinLargeFeaturesLineLength = MinLargeFeaturesLineLength;
    }

    if (ImGui.SliderInt("Tile merge frequency (updates)", ref mergeFrequency, 0, 100))
    {
      _tileMerge.MergeFrequency = mergeFrequency;
    }

    if (ImGui.SliderFloat("Tile merge threshold (pixels)", ref tileMergeThreshold, 0, 100))
    {
      _tileMerge.TileMergeThreshold = tileMergeThreshold;
    }

    //  Regulator section
    if (ImGui.Checkbox("Enable Regulator", ref regulatorEnabled))
    {
      _tileTrustRegulator.RegulatorEnabled = regulatorEnabled;
    }

    if (ImGui.SliderFloat("Tile Trust Increment", ref trustIncrement, 0.0f, 100, "%.2f")) // 100 is a placeholder max value
    {
      _tileTrustRegulator.TrustIncrement = trustIncrement;
    }

    if (ImGui.SliderFloat("Tile Trust Decrement", ref trustDecrement, 0.0f, 100, "%.2f")) // 100 is a placeholder max value
    {
      _tileTrustRegulator.TrustDecrement = trustDecrement;
    }

    if (ImGui.SliderInt("Decay Frequency (updates)", ref decayFrequency, 0, 20)) // 100 is a placeholder max value
    {
      _tileTrustRegulator.DecayFrequency = decayFrequency;
    }

    if (ImGui.SliderInt("Trust Threshold for Drawing", ref trustThreshold, 0, 100)) // 0-100 as example range
    {
      _tileTrustRegulator.TrustThreshold = trustThreshold;
    }

    ImGui.EndTabItem();

  }
  private void DrawScalingSettings()
  {




    float gridAreaMeters = AppSettings.Default.TileTrustIncrement;

    if (ImGui.SliderFloat("Grid size (meters)", ref gridAreaMeters, 0.1f, 5.0f, "%.2f"))
      MapScaleManager.Instance.SetGridAreaMeters(gridAreaMeters);


    ImGui.Text($"Tile pixel size: {MapScaleManager.Instance.ScaledTileSizePixels}");

    ImGui.EndTabItem();
  }


}






