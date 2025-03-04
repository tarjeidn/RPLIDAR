using System;
using System.Collections.Generic;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.ImGuiNet;
//using SharpDX;
using System.Numerics;
using System.Diagnostics;
using System.IO;
using RPLIDAR_Mapping.Features.Map;
using RPLIDAR_Mapping.Utilities;
using RPLIDAR_Mapping.Features.Map.Algorithms;
using RPLIDAR_Mapping;
using RPLIDAR_Mapping.Features.Map.UI;

public class GuiManager
{
  private List<string> _logMessages = new();
  private ImGuiRenderer _guiRenderer;
  private bool _showLogWindow = true;
  private List<string> _logBuffer;
  public System.Numerics.Vector4 _colorV4;
  private ImFontPtr _loggerFont;
  private Map _Map;
  private TileTrustRegulator _tileTrustRegulator;

  public GuiManager(Game game, Map map)
  {
    _guiRenderer = new ImGuiRenderer(game);
    _tileTrustRegulator = AlgorithmProvider.TileTrustRegulator;
    _guiRenderer.RebuildFontAtlas();
    ImGui.GetIO().FontGlobalScale = 1.5f;
    _colorV4 = Color.CornflowerBlue.ToVector4().ToNumerics();
    ImGui.GetIO().DisplayFramebufferScale = new System.Numerics.Vector2(1.5f, 1.5f);
  }

  public void AddLogMessage(string message)
  {
    Debug.WriteLine(message);
    if (_logMessages.Count > 1000)
      _logMessages.RemoveAt(0);

    _logMessages.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
  }

  //public void Update(GameTime gameTime)
  //{
  //  _guiRenderer.Update(gameTime);
  //}

  public void Draw(GameTime gametime)
  {
    _guiRenderer.BeginLayout(gametime);

    if (_showLogWindow)
      DrawMainWindow();

    _guiRenderer.EndLayout();
  }
  private void DrawMainWindow()
  {
    ImGui.SetNextWindowSize(new System.Numerics.Vector2(800, 600), ImGuiCond.FirstUseEver);
    ImGui.Begin("Main Window", ref _showLogWindow, ImGuiWindowFlags.MenuBar);


    if (ImGui.BeginMenuBar())
    {
      if (ImGui.BeginMenu("File"))
      {
        if (ImGui.MenuItem("Open..", "Ctrl+O")) { /* Do stuff */ }
        if (ImGui.MenuItem("Save", "Ctrl+S")) { /* Do stuff */ }
        if (ImGui.MenuItem("Close", "Ctrl+W")) { _showLogWindow = false; }
        ImGui.EndMenu();
      }
      ImGui.EndMenuBar();
    }
    float zoom = AppSettings.Default.MapZoom;
    float gridScaleFactor = MapScaleManager.Instance.ScaleFactor;
    if (ImGui.SliderFloat("Grid Scale", ref gridScaleFactor, 0.1f, 10.0f, "%.2f"))
      MapScaleManager.Instance.SetScaleFactor(gridScaleFactor);



    if (ImGui.SliderFloat("Zoom", ref zoom, 0.05f, 1))
      AppSettings.Default.MapZoom = zoom;
    //  Begin Tab Bar
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

        ImGui.EndTabBar();
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

    ImGui.Separator(); // 🔹 Visual break

    //  Graph for points handled per frame
    var samples = StatisticsProvider.MapStats._pointHistory.ToArray();
    if (samples.Length > 0)
    {
      float[] floatSamples = Array.ConvertAll(samples, x => (float)x);
      ImGui.PlotLines("Points Handled Per Frame", ref floatSamples[0], floatSamples.Length);
    }

    ImGui.Separator(); // 🔹 Visual break

    //  Updated Grid Statistics
    var gridStats = StatisticsProvider.GridStats;

    ImGui.Text($"Total Grids: {gridStats.TotalGrids}");
    ImGui.Text($"Total Active Tiles: {gridStats.TotalHitTiles}");
    ImGui.Text($"Total Points Processed: {gridStats.TotalPointsHandled}");

    ImGui.Separator(); // 🔹 Tile statistics

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

    //  Sliders show real-time values from AppSettings
    bool regulatorEnabled = TileRegulatorSettings.Default.RegulatorEnabled;
    bool drawMergedTiles = AppSettings.Default.DrawMergedTiles;
    float trustIncrement = AppSettings.Default.TileTrustIncrement;
    float trustDecrement = AppSettings.Default.TileTrustDecrement;
    float tileMergeTreshold = AppSettings.Default.TilemergeThreshold;
    int decayFrequency = AppSettings.Default.TileDecayRate;
    int trustThreshold = AppSettings.Default.TileTrustThreshold;
    int mergeFrequency = AppSettings.Default.MergeTilesFrequency;
    int minmergedTileSize = AppSettings.Default.MinMergedTileSize;


    //  Sliders allow manual adjustments (values are updated in AppSettings)
    if (ImGui.Checkbox("Draw merged tiles", ref drawMergedTiles))
    {
      AppSettings.Default.DrawMergedTiles = drawMergedTiles;
    }
    if (ImGui.SliderInt("Minimum merged til size (pixels)", ref minmergedTileSize, 0, 100))
      AppSettings.Default.MinMergedTileSize = minmergedTileSize;

    if (ImGui.SliderInt("Tile merge frequency (updates)", ref mergeFrequency, 0, 100))
      AppSettings.Default.MergeTilesFrequency = mergeFrequency;

    if (ImGui.SliderFloat("Tile merge treshold (pixels)", ref tileMergeTreshold, 0 , 100))
      AppSettings.Default.TilemergeThreshold = tileMergeTreshold;
    
    //regulator section
    if (ImGui.Checkbox("Enable Regulator", ref regulatorEnabled))
    {
      TileRegulatorSettings.Default.RegulatorEnabled = regulatorEnabled;
    }

    if (ImGui.SliderFloat("Tile Trust Increment", ref trustIncrement, 0.1f, TileRegulatorSettings.Default.MaxTileTrustIncrement, "%.2f"))
      AppSettings.Default.TileTrustIncrement = trustIncrement;

    if (ImGui.SliderFloat("Tile Trust Decrement", ref trustDecrement, 0.1f, TileRegulatorSettings.Default.MaxTileTrustDecrement, "%.2f"))
      AppSettings.Default.TileTrustDecrement = trustDecrement;

    if (ImGui.SliderInt("Decay Frequency (updates)", ref decayFrequency, 1, TileRegulatorSettings.Default.MaxTileDecayRate))
      AppSettings.Default.TileDecayRate = decayFrequency;

    if (ImGui.SliderInt("Trust Threshold for Drawing", ref trustThreshold, TileRegulatorSettings.Default.MinTileTrustThreshold, TileRegulatorSettings.Default.MaxTileTrustThreshold))
      AppSettings.Default.TileTrustThreshold = trustThreshold;

    ImGui.EndTabItem();
  }


}



