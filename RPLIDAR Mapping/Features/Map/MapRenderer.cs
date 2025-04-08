using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RPLIDAR_Mapping.Features.Map.GridModel;
using RPLIDAR_Mapping.Models;
using RPLIDAR_Mapping.Utilities;
using RPLIDAR_Mapping.Features;
using RPLIDAR_Mapping.Features.Communications;
using System.Diagnostics;
using System.ComponentModel;
using RPLIDAR_Mapping.Features.Map.UI;
using RPLIDAR_Mapping.Features.Map.UI.Overlays;
using RPLIDAR_Mapping.Features.Map;
using RPLIDAR_Mapping.Features.Map.Algorithms;
using RPLIDAR_Mapping.Providers;
using MathNet.Numerics.LinearAlgebra.Factorization;
using MathNet.Numerics.Random;


namespace RPLIDAR_Mapping.Features.Map
{
  public class MapRenderer
  {
    private readonly Texture2D _pointTexture;
    public readonly RenderTarget2D _mapTexture;
    public readonly GraphicsDevice _GraphicsDevice;
    private readonly SpriteBatch _SpriteBatch;
    public readonly GridManager _GridManager;
    private RenderTarget2D TilesRenderTarget_A;
    private RenderTarget2D TilesRenderTarget_B;
    private bool _activeRenderTargetA = true; // Track which target is active
    private TileMerge _TileMerge;
    public Camera _Camera;
    private UserSelection _UserSelection;
    private readonly DistanceOverlay _distanceOverlay;
    private Queue<(int, int)> _gridDrawQueue = new Queue<(int, int)>();
    private bool _isQueueInitialized = false;
    private int MaxGridsPerFrame = 5; // 🔥 Can be adjusted dynamically
    //private readonly float _scale; // Scale factor for visualization
    private Map _map;
    private int _mapTextureSize;
    private int _ScaledTileSize;
    private int _gridSize;
    public int MainScreenWidth = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Width;
    public int MainScreenHeight = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Height;
    public bool DrawCycleActive = false;
    public bool DrawSightLines { get; set; } = false;
    public Vector2 MainScreenSize;
    public Device _device;
    public Vector2 _centerOfFullMap { get; set; }

    public MapRenderer(Map map)
    {
      _Camera = UtilityProvider.Camera;
      _UserSelection = GUIProvider.UserSelection;
      MainScreenSize = new Vector2(MainScreenWidth, MainScreenHeight);
      _ScaledTileSize = AppSettings.Default.GridTileSizeCM * AppSettings.Default.GridScaleCMtoPixels;
      _GraphicsDevice = GraphicsDeviceProvider.GraphicsDevice;
      _SpriteBatch = GraphicsDeviceProvider.SpriteBatch;
      _TileMerge = AlgorithmProvider.TileMerge;
      TilesRenderTarget_A = new RenderTarget2D(_GraphicsDevice, MainScreenWidth, MainScreenHeight);
      TilesRenderTarget_B = new RenderTarget2D(_GraphicsDevice, MainScreenWidth, MainScreenHeight);
      _map = map;
      _GridManager = _map.GetDistributor()._GridManager;
      _gridSize = _GridManager._gridSizePixels;
      //_mapTexture = new RenderTarget2D(_GraphicsDevice, AppSettings.Default.MapWindowWidth, AppSettings.Default.MapWindowHeight);

      // Create a 1x1 white texture for rendering points
      _pointTexture = new Texture2D(_GraphicsDevice, 1, 1);
      _pointTexture.SetData(new[] { Color.White });
      _distanceOverlay = new DistanceOverlay(this);
      //DrawGrids();
    }
    public void Update()
    {

    }


    public void DrawGrids(Vector2 devicePosition, int gridsDrawn)
    {

      Camera camera = UtilityProvider.Camera;
      //int scaledGridSize = MapScaleManager.Instance.ScaledGridSizePixels;
      int scaledGridSize = 1000;
      Rectangle sourceBounds = camera.GetSourceRectangle();

      for (int i = 0; i < gridsDrawn; i++)
      {
        if (_gridDrawQueue.Count == 0)
        {
          //DrawCycleActive = false;
          _activeRenderTargetA = !_activeRenderTargetA;
          break; // 🛑 Avoid errors if queue is empty
        }


        var pos = _gridDrawQueue.Dequeue(); // 🟢 Get next grid to draw
        if (!_GridManager.Grids.ContainsKey(pos))
          continue; // 🛑 Skip if grid no longer exists

        Grid grid = _GridManager.Grids[pos];

        // 🟢 Compute world position of the grid (relative to device)
        Vector2 gridWorldPosition = new Vector2(
            (pos.Item1 * scaledGridSize) + devicePosition.X,
            (pos.Item2 * scaledGridSize) + devicePosition.Y
        );
        grid.GridPosition = gridWorldPosition;
        Rectangle gridBounds = new Rectangle(
            (int)gridWorldPosition.X,
            (int)gridWorldPosition.Y,
            scaledGridSize,
            scaledGridSize
        );

        // Skip if outside viewport
        if (!sourceBounds.Intersects(gridBounds))
        {
          continue; // Skip drawing this frame
        }

        // 🟢 Convert world position to screen space
        Vector2 gridScreenPos = camera.WorldToScreen(gridWorldPosition);

        // 🟢 Draw grid tiles
        Vector2 deviceScreenPos = camera.WorldToScreen(devicePosition);
        DrawTiles(_SpriteBatch, gridScreenPos, grid, deviceScreenPos);
        DrawRingTiles(_SpriteBatch, gridScreenPos, grid, deviceScreenPos);
        //  Debug visualization
        //DrawingHelperFunctions.DrawRectangleBorder(_SpriteBatch, new Rectangle((int)gridScreenPos.X, (int)gridScreenPos.Y, 15, 15), 5, Color.Green);
      }
      DrawCycleActive = _gridDrawQueue.Count > 0;
    }

    public void DrawOverlays(Vector2 deviceScreenPos)
    {
      Camera camera = UtilityProvider.Camera;
      Rectangle sourceBounds = camera.GetSourceRectangle();
      Rectangle destBounds = camera.GetDestinationRectangle();

      _centerOfFullMap = destBounds.Center.ToVector2();
      DrawingHelperFunctions.DrawGridPattern(_SpriteBatch, destBounds, deviceScreenPos, 2);
      DrawingHelperFunctions.DrawRectangleBorder(_SpriteBatch, destBounds, 5, Color.White);
      _SpriteBatch.DrawString(
          ContentManagerProvider.GetFont("DebugFont"),
          $"FPS: {UtilityProvider.FPSCounter.FPS}\nLiDAR Updates/s: {StatisticsProvider.MapStats.LiDARUpdatesPerSecond}",
          new Vector2(10, 10),
          Color.White
      );
      DrawMatchedPairs();

    }


    public void DrawMap(bool isMapUpdated)
    {
      Vector2 devicePosition = _device._devicePosition;
      Vector2 deviceScreenPos = _Camera.WorldToScreen(devicePosition);
      Rectangle sourceBounds = _Camera.GetSourceRectangle();



      if (isMapUpdated)
      {
        foreach (var gridKey in _GridManager.Grids.Keys)
        {
          Grid grid = _GridManager.Grids[gridKey];
          if (grid != null && (grid._drawnTiles.Count > 0 || grid.RingTiles.Count > 0))
          {
            _gridDrawQueue.Enqueue(gridKey);
          }
        }
        DrawTilesCanvas();
      }
      _SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null);

      _GraphicsDevice.Clear(Color.Black); // 🔥 Clear only if drawing full map
                                          // 🔥 Draw the currently active render target to the screen
      RenderTarget2D targetToDraw = _activeRenderTargetA ? TilesRenderTarget_A : TilesRenderTarget_B;
      _SpriteBatch.Draw(targetToDraw, Vector2.Zero, Color.White);
      DrawOverlays(deviceScreenPos); // 🔥 Overlays always update
      if (_TileMerge.DrawMergedLines && _TileMerge.ComputeMergedLines) DrawMergedLines(sourceBounds);
      DrawDevice(deviceScreenPos);
      DrawSelection(sourceBounds);
      if (_map.PermanentLines.Count > 0) DrawPermanentLines(sourceBounds);
      if (_TileMerge.DrawMergedTiles) DrawClusterBoundingRectangles();
      _SpriteBatch.End();
    }
    private void DrawPermanentLines(Rectangle sourceBounds)
    {
      List<LineSegment> updatedLines = new();
      foreach (var line in _map.PermanentLines)
      {
        Vector2 start = line.Start;
        Vector2 end = line.End;

        if (DrawingHelperFunctions.ClipLineToRectangle(ref start, ref end, sourceBounds))
        {
          updatedLines.Add(new LineSegment(start, end, line.AngleDegrees, line.AngleRadians, line.IsPermanent));
          
          Vector2 screenStart = _Camera.WorldToScreen(start);
          Vector2 screenEnd = _Camera.WorldToScreen(end);
          DrawingHelperFunctions.DrawLine(_SpriteBatch, screenStart, screenEnd, Color.Green, 5);
        }
      }
    }
    private void DrawDevice(Vector2 deviceScreenPos)
    {
      Rectangle deviceRect = _device.GetDeviceRectRelative(deviceScreenPos);
      // Create a slightly larger rectangle for estimated position
      int sizeIncrease = 10; // Adjust as needed

      _SpriteBatch.Draw(
          _device._deviceTexture,
          deviceRect,
          Color.Red
      );
      // Draw estimated position as a blue border
      DrawDeviceOrientationLine(deviceScreenPos);

    }
    private void DrawDeviceOrientationLine(Vector2 deviceScreenPos)
    {

      float lineLength = 50f; // Adjust as needed
      Vector2 direction = new Vector2(MathF.Cos(_device._deviceOrientation - MathF.PI / 2),
                                      MathF.Sin(_device._deviceOrientation - MathF.PI / 2));
      Vector2 endPoint = deviceScreenPos + (direction * lineLength); // Compute endpoint

      // Draw line indicating direction
      DrawingHelperFunctions.DrawLine(_SpriteBatch, deviceScreenPos, endPoint, Color.Green, 3);
      _SpriteBatch.DrawString(
          ContentManagerProvider.GetFont("DebugFont"),
          $"T: {MathHelper.ToDegrees(_device._deviceOrientation):0.00} POS: {_device._devicePosition}",
          deviceScreenPos + new Vector2(10, -20),
          Color.White
      );
    }
    private void DrawTilesCanvas()
    {
      // Swap to inactive target for rendering
      _Camera.CenterOn(_device._devicePosition);
      RenderTarget2D targetToDraw = _activeRenderTargetA ? TilesRenderTarget_B : TilesRenderTarget_A;

      _GraphicsDevice.SetRenderTarget(targetToDraw);
      _GraphicsDevice.Clear(Color.Black);

      _SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null);

      DrawGrids(_device._devicePosition, 200); // Draw tiles in batches

      _SpriteBatch.End();
      _GraphicsDevice.SetRenderTarget(null);
    }
    private void DrawSelection(Rectangle sourceBounds)
    {

      if (_UserSelection.isSelecting)
      {
        // Draw selection rectangle
        DrawingHelperFunctions.DrawRectangleBorder(_SpriteBatch, _UserSelection.selectionBox, 4, Color.Blue * 0.9f); // Semi-transparent blue
      }

      // Draw highlighted lines
      List<(Vector2 Start, Vector2 End, float AngleDegrees, float AngleRadians, bool IsPermanent)> updatedLines = new();
      foreach (var line in _UserSelection.highlightedLines)
      {
        Vector2 start = line.Start;
        Vector2 end = line.End;
        // Set color: Inferred lines are RED, regular lines are BLUE
        Color linecolor = Color.Yellow;
        if (DrawingHelperFunctions.ClipLineToRectangle(ref start, ref end, sourceBounds))
        {
          updatedLines.Add((start, end, line.AngleDegrees, line.AngleRadians, line.IsPermanent));

          Vector2 screenStart = _Camera.WorldToScreen(start);
          Vector2 screenEnd = _Camera.WorldToScreen(end);
          DrawingHelperFunctions.DrawLine(_SpriteBatch, screenStart, screenEnd, linecolor, 5);
        }
      }

    }
    private void DrawClusterBoundingRectangles()
    {
      foreach (var cluster in _TileMerge.TileClusters)
      {
        Rectangle rect = cluster.Bounds;
        Rectangle screenRect = _Camera.WorldToScreen(rect);
        DrawingHelperFunctions.DrawRectangleBorder(_SpriteBatch, screenRect, 2, Color.White);
      }
    }
    private void DrawMatchedPairs()
    {
      foreach (var (from, to) in _map.MatchedPairs)
      {
        Vector2 screenFrom = UtilityProvider.Camera.WorldToScreen(from);
        Vector2 screenTo = UtilityProvider.Camera.WorldToScreen(to);
        _SpriteBatch.DrawLine(screenFrom, screenTo, Color.Yellow, 2);  // Use your own draw line method
      }
    }
    public void DrawTiles(SpriteBatch spriteBatch, Vector2 gridOffset, Grid grid, Vector2 devicePos)
    {
      Camera camera = UtilityProvider.Camera;
      Rectangle sourceBounds = camera.GetSourceRectangle();

      foreach (Tile tile in grid._drawnTiles.Values)
      {
        tile.WorldGlobalPosition = tile._selfGrid.GridPosition + tile.Position;
        tile.GlobalCenter = tile.WorldGlobalPosition + new Vector2(tile._tileSize / 2f, tile._tileSize / 2f);

        Vector2 worldPos = tile.WorldGlobalPosition;
        Vector2 screenPos = camera.WorldToScreen(worldPos);
        Rectangle tileBounds = tile.WorldRect;

        if (!sourceBounds.Intersects(tileBounds))
        {
          continue;
        }
        tile.Draw(_SpriteBatch, screenPos, devicePos, DrawSightLines);
      }
    }
    public void DrawRingTiles(SpriteBatch spriteBatch, Vector2 gridOffset, Grid grid, Vector2 devicePos)
    {
      //Debug.WriteLine($"RingTiles count: {grid.RingTiles.Count}");
      Camera camera = UtilityProvider.Camera;
      Rectangle sourceBounds = camera.GetSourceRectangle();

      foreach (Tile tile in grid.RingTiles)
      {
        tile.WorldGlobalPosition = tile._selfGrid.GridPosition + tile.Position;
        tile.GlobalCenter = tile.WorldGlobalPosition + new Vector2(tile._tileSize / 2f, tile._tileSize / 2f);

        Vector2 worldPos = tile.WorldGlobalPosition;
        Vector2 screenPos = camera.WorldToScreen(worldPos);
        Rectangle tileBounds = tile.WorldRect;

        if (!sourceBounds.Intersects(tileBounds))
        {

          continue;
        }
        tile.Draw(_SpriteBatch, screenPos, devicePos, DrawSightLines);
      }
    }
    private void DrawMergedLines(Rectangle sourceBounds)
    {
      //if (_TileMerge._mergedLines == null || _TileMerge._mergedLines.Count == 0) return;
      if (_TileMerge.TileClusters.Count == 0) return;

      Camera camera = UtilityProvider.Camera;


      List<LineSegment> updatedLines = new();

      //foreach (var line in _TileMerge._mergedLines)
      foreach (var cluster in _TileMerge.TileClusters)
      {
        if (cluster.FeatureLine == null) continue;
        LineSegment line = cluster.FeatureLine;
        Vector2 start = line.Start;
        Vector2 end = line.End;

        if (DrawingHelperFunctions.ClipLineToRectangle(ref start, ref end, sourceBounds))
        {
          updatedLines.Add(new LineSegment(start, end, line.AngleDegrees, line.AngleRadians, line.IsPermanent));

          Vector2 screenStart = _Camera.WorldToScreen(start);
          Vector2 screenEnd = _Camera.WorldToScreen(end);
          DrawingHelperFunctions.DrawLine(_SpriteBatch, screenStart, screenEnd, Color.White, 5);
        }
      }


      _TileMerge._mergedLines = updatedLines; // Replace with the clipped lines
    }

  }
}
