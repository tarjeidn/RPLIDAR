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

public class GuiManager
{
  private List<string> _logMessages = new();
  private ImGuiRenderer _guiRenderer;
  private bool _showLogWindow = true;
  private List<string> _logBuffer;
  public System.Numerics.Vector4 _colorV4;
  private ImFontPtr _loggerFont;
  private Map _Map;

  public GuiManager(Game game, Map map)
  {
    _guiRenderer = new ImGuiRenderer(game);

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

    //  Begin Tab Bar
    if (ImGui.BeginTabBar("MainTabs"))
    {
      //  Tab: Log Monitor
      if (ImGui.BeginTabItem("Log Monitor"))
      {
        DrawLogWindow();
        ImGui.EndTabItem();
      }

      //  Tab: Statistics
      if (ImGui.BeginTabItem("Statistics"))
      {
        DrawStatisticsWindow();
        ImGui.EndTabItem();
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
  }

  //  Statistics Tab
  private void DrawStatisticsWindow()
  {
    ImGui.TextColored(new System.Numerics.Vector4(0, 1, 1, 1), "Statistics"); // Cyan title

    ImGui.Text($"FPS: {StatisticsProvider.MapStats.FPS}");
    ImGui.Text($"Points Per Second: {StatisticsProvider.MapStats.PointsPerSecond}");
    ImGui.Text($"Data batch size: {StatisticsProvider.MapStats.CurrentPacketSize}");

    ImGui.Separator(); // Adds a visual break

    var samples = StatisticsProvider.MapStats._pointHistory.ToArray();
    if (samples.Length > 0)
    {
      float[] floatSamples = Array.ConvertAll(samples, x => (float)x);
      ImGui.PlotLines("Points Handled Per Frame", ref floatSamples[0], floatSamples.Length);
    }
    ImGui.Separator(); // Adds a visual break
    ImGui.Text($"Total grids: {StatisticsProvider.GridStats.TotalGrids}");
    ImGui.Text($"Average hits on tiles: {StatisticsProvider.GridStats.averageTileHitCount}");
    ImGui.Text($"Most hits on tile: {StatisticsProvider.GridStats.higestTileHitCount}");
    ImGui.Text($"Total hits: {StatisticsProvider.GridStats.totalHitCount}");
    ImGui.Text($"Total hit tiles: {StatisticsProvider.GridStats.totalHitTiles}");
    ImGui.Text($"Average hitcount of tiles above average hits: {StatisticsProvider.GridStats.highAverageTileHitCount}");
    ImGui.Text($"Total hits on tiles with above average hits: {StatisticsProvider.GridStats.highTotalHitCount}");
    ImGui.Text($"Amount of tiles with above average hit count: {StatisticsProvider.GridStats.highTotalHitTiles}");

  }

  //private void DrawLogWindow()
  //{
  //  ImGui.Begin("Serial/WiFi Log Monitor", ref _showLogWindow, ImGuiWindowFlags.MenuBar);

  //  if (ImGui.BeginMenuBar())
  //  {
  //    if (ImGui.BeginMenu("File"))
  //    {
  //      if (ImGui.MenuItem("Open..", "Ctrl+O")) { /* Do stuff */ }
  //      if (ImGui.MenuItem("Save", "Ctrl+S")) { /* Do stuff */ }
  //      if (ImGui.MenuItem("Close", "Ctrl+W")) { _showLogWindow = false; }
  //      ImGui.EndMenu();
  //    }
  //    ImGui.EndMenuBar();

  //    //  New Section: Statistics
  //    ImGui.Separator(); // Adds a visual break line
  //    ImGui.TextColored(new System.Numerics.Vector4(0, 1, 1, 1), "Statistics"); // Cyan title

  //    ImGui.Text($"FPS: {StatisticsProvider.MapStats.FPS}");
  //    ImGui.Text($"Points Per Second: {StatisticsProvider.MapStats.PointsPerSecond}");
  //    ImGui.Text($"Data batch size: {StatisticsProvider.MapStats.CurrentPacketSize}");

  //    ImGui.Separator(); // Adds another break before the next section

  //    var samples = StatisticsProvider.MapStats._pointHistory.ToArray();
  //    if (samples.Length > 0)
  //    {
  //      float[] floatSamples = Array.ConvertAll(samples, x => (float)x);
  //      ImGui.PlotLines("Points Handled Per Frame", ref floatSamples[0], floatSamples.Length);
  //    }


  //    // Display contents in a scrolling region
  //    ImGui.TextColored(new System.Numerics.Vector4(1, 1, 0, 1), "Device log");
  //    ImGui.PushFont(_loggerFont);
  //    ImGui.BeginChild("Scrolling", new System.Numerics.Vector2(0));
  //    for (var n = 0; n < _logMessages.Count; n++)
  //    {
  //      ImGui.Text(_logMessages[n]);
  //    }

  //    //  Auto-scroll to the bottom (if new log entries exist)
  //    if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
  //    {
  //      ImGui.SetScrollHereY(1.0f);
  //    }
  //  }

  //  ImGui.End();
  //}
}
