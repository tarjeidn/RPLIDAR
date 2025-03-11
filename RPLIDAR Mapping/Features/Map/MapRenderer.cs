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
using SharpDX.DirectWrite;





namespace RPLIDAR_Mapping.Features.Map
{
  public class MapRenderer
  {
    private readonly Texture2D _pointTexture;
    public readonly RenderTarget2D _mapTexture;
    public readonly GraphicsDevice _GraphicsDevice;
    private readonly SpriteBatch _SpriteBatch;
    public readonly GridManager _GridManager;
    public Camera _Camera;
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
    public Vector2 MainScreenSize;
    public Device _device;
    public Vector2 _centerOfFullMap {  get; set; }

    public MapRenderer(Map map)
    {
      _Camera = UtilityProvider.Camera;

      MainScreenSize = new Vector2(MainScreenWidth, MainScreenHeight);
      _ScaledTileSize = AppSettings.Default.GridTileSizeCM * AppSettings.Default.GridScaleCMtoPixels;
      _GraphicsDevice = GraphicsDeviceProvider.GraphicsDevice;
      _SpriteBatch = GraphicsDeviceProvider.SpriteBatch;
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
      int scaledGridSize = MapScaleManager.Instance.ScaledGridSizePixels;
      Rectangle sourceBounds = camera.GetSourceRectangle();

      for (int i = 0; i < gridsDrawn; i++)
      {
        if (_gridDrawQueue.Count == 0)
        {
          //DrawCycleActive = false;
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

        //  Debug visualization
        //DrawingHelperFunctions.DrawRectangleBorder(_SpriteBatch, new Rectangle((int)gridScreenPos.X, (int)gridScreenPos.Y, 15, 15), 5, Color.Green);
      }
      DrawCycleActive = _gridDrawQueue.Count > 0;
    }

    

    private void RefillGridQueue()
{
    _gridDrawQueue.Clear();

    foreach (var gridKey in _GridManager.Grids.Keys)
    {
        _gridDrawQueue.Enqueue(gridKey);
    }

    _SpriteBatch.GraphicsDevice.Clear(Color.Black); //  Ensure screen resets when starting a new cycle
}
    public void DrawOverlays()
    {
      Camera camera = UtilityProvider.Camera;
      Rectangle sourceBounds = camera.GetSourceRectangle();
      Rectangle destBounds = camera.GetDestinationRectangle();
      Vector2 devicePosition = _device._devicePosition;
      Vector2 deviceScreenPos = _Camera.WorldToScreen(devicePosition);
      _centerOfFullMap = destBounds.Center.ToVector2();
      DrawingHelperFunctions.DrawGridPattern(_SpriteBatch, destBounds, deviceScreenPos, 2);
      DrawingHelperFunctions.DrawRectangleBorder(_SpriteBatch, destBounds, 5, Color.White);
      //DrawingHelperFunctions.DrawRectangleBorder(_SpriteBatch, sourceBounds, 5, Color.Red);
      //DrawingHelperFunctions.DrawRectangleBorder(_SpriteBatch, viewPort, 5, Color.Blue);

      

      _SpriteBatch.DrawString(
          ContentManagerProvider.GetFont("DebugFont"),
          $"FPS: {UtilityProvider.FPSCounter.FPS}\nLiDAR Updates/s: {StatisticsProvider.MapStats.LiDARUpdatesPerSecond}",
          new Vector2(10, 10),
          Color.White
      );

      //  Draw device last (so it's always on top)
      _SpriteBatch.Draw(
          _device._deviceTexture,
          _device.GetDeviceRectRelative(deviceScreenPos),
          Color.Red
      );
    }
    private int _framesSinceLastClear = 0; //  Count frames before clearing

    public void DrawMap()
    {
      Vector2 devicePosition = _device._devicePosition;
      //  If the queue is empty but DrawCycleActive is still true, it means all grids were drawn last frame.
      if (_gridDrawQueue.Count == 0 && DrawCycleActive)
      {
        DrawCycleActive = false;
      }

      //  If the cycle is inactive, reset everything **only once**
      if (!DrawCycleActive)
      {
        _GraphicsDevice.Clear(Color.Black);
        DrawOverlays();
        foreach (var gridKey in _GridManager.Grids.Keys)
        {
          if (_GridManager.Grids[gridKey] != null && _GridManager.Grids[gridKey]._drawnTiles.Count > 0)
          {
            _gridDrawQueue.Enqueue(gridKey);
          }
        }
        DrawCycleActive = _gridDrawQueue.Count > 0; //  Start new cycle
      }

      if (DrawCycleActive) DrawGrids(devicePosition, 100);

    }

    private void DrawMergedRectangles(Vector2 devicePosition, Vector2 centerOfFullMap)
    {
      // IMPORTANT: set minsize here, do not look it up from settings in the if statement. Causes a huge drop in speed
      int minSize = _map._tileMerge.MinMergedTileSize;
      foreach (var (rect, angle, ispermanent) in _map._tileMerge._mergedObjects)
      {
        if (rect.Width < minSize && rect.Height < minSize)
        {
          continue;
        }
        Color color = ispermanent ? Color.Green : Color.White;
        // Convert world coordinates to screen coordinates
        Vector2 screenPos = new Vector2(
            rect.X - devicePosition.X + centerOfFullMap.X,
            rect.Y - devicePosition.Y + centerOfFullMap.Y
        );

        // The rotation pivot must be the rectangle center
        Vector2 rectCenter = screenPos + new Vector2(rect.Width / 2, rect.Height / 2);

        //  Queue lines instead of drawing immediately
        ContentManagerProvider.QueueRotatedRectangleBorder(
            rectCenter,   // Center of rotation
            rect.Width,   // Rectangle width
            rect.Height,  // Rectangle height
            angle,        // Rotation angle in radians
            color   // Border color
        );
      }
    }
    public void DrawTiles(SpriteBatch spriteBatch, Vector2 gridOffset, Grid grid, Vector2 devicePos)
    {
      Camera camera = UtilityProvider.Camera;
      Rectangle sourceBounds = camera.GetSourceRectangle();

      foreach (Tile tile in grid._drawnTiles.Values)
      {
        if (tile.TrustedScore < AlgorithmProvider.TileTrustRegulator.TileTrustTreshHold)
        {
          continue; //  Skip untrusted tiles
        }

        Vector2 worldPos = tile.WorldGlobalPosition;
        Vector2 screenPos = camera.WorldToScreen(worldPos);
        Rectangle tileBounds = tile.WorldRect;

        if (!sourceBounds.Intersects(tileBounds))
        {

          continue;
        }
        tile.Draw(spriteBatch, screenPos, devicePos);
      }
    }

    //public void DrawTiles(SpriteBatch spriteBatch, Vector2 gridOffset, Grid grid, Vector2 devicePos)
    //{
    //  Camera camera = UtilityProvider.Camera;
    //  Rectangle sourceBounds = camera.GetSourceRectangle();



    //  foreach (Tile tile in grid._drawnTiles.Values)
    //  {
    //    //  LOG: Check if tile passes trust threshold
    //    if (tile.TrustedScore < AlgorithmProvider.TileTrustRegulator.TileTrustTreshHold)
    //    {
    //      continue;
    //    }

    //    //  Convert tile position to world space
    //    Vector2 worldPos = tile.WorldGlobalPosition;
    //    Vector2 screenPos = camera.WorldToScreen(worldPos);
    //    Rectangle tileBounds = tile.WorldRect;
    //    //  LOG: Check if tile is visible
    //    if (!sourceBounds.Intersects(tileBounds))
    //    {
    //      continue;
    //    }

    //    tile.Draw(spriteBatch, screenPos, devicePos);
    //  }
    //}




  }
}
