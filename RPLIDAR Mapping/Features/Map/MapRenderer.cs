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




namespace RPLIDAR_Mapping.Features.Map
{
  public class MapRenderer
  {
    private readonly Texture2D _pointTexture;
    public readonly RenderTarget2D _mapTexture;
    public readonly GraphicsDevice _GraphicsDevice;
    private readonly SpriteBatch _SpriteBatch;
    public readonly GridManager _GridManager;
    private readonly DistanceOverlay _distanceOverlay;
    //private readonly float _scale; // Scale factor for visualization
    private Map _map;
    private int _mapTextureSize;
    private int _ScaledTileSize;
    private int _gridSize;
    public Device _device;
    private Vector2 _centerOfFullMap;

    public MapRenderer(Map map)
    {
      _ScaledTileSize = AppSettings.Default.GridTileSizeCM * AppSettings.Default.GridScaleCMtoPixels;
      _GraphicsDevice = GraphicsDeviceProvider.GraphicsDevice;
      _SpriteBatch = GraphicsDeviceProvider.SpriteBatch;
      _map = map;
      _GridManager = _map.GetDistributor()._GridManager;
      _gridSize = _GridManager._gridSizePixels;
      _mapTexture = new RenderTarget2D(_GraphicsDevice, AppSettings.Default.MapWindowWidth, AppSettings.Default.MapWindowHeight);
      _centerOfFullMap = new Vector2(_mapTexture.Width / 2, _mapTexture.Height / 2);
      // Create a 1x1 white texture for rendering points
      _pointTexture = new Texture2D(_GraphicsDevice, 1, 1);
      _pointTexture.SetData(new[] { Color.White });
      _distanceOverlay = new DistanceOverlay(this);
      //DrawGrids();
    }
    public void Update()
    {

    }

    public void DrawRawPoint(SpriteBatch spriteBatch, List<MapPoint> points, Vector2 center)
    {
      foreach (var point in points)
      {
        // Transform the point to screen coordinates
        Vector2 screenPoint = center + new Vector2(point.X, point.Y) * AppSettings.Default.MapZoom;

        // Draw the point
        spriteBatch.Draw(_pointTexture, screenPoint, null, Color.Red, 0, Vector2.Zero, 2f, SpriteEffects.None, 0);
      }
    }
    public void DrawGrids(Vector2 devicePosition)
    {
      Camera camera = UtilityProvider.Camera;
      Rectangle viewportBounds = camera.GetViewportBounds(_gridSize);

      foreach ((int, int) pos in _GridManager.Grids.Keys)
      {
        Grid grid = _GridManager.Grids[pos];

        // ✅ Compute world position correctly, handling negative grids
        Vector2 gridWorldPosition = new Vector2(pos.Item1 * _gridSize, pos.Item2 * _gridSize);

        Rectangle gridBounds = new Rectangle(
            (int)gridWorldPosition.X,
            (int)gridWorldPosition.Y,
            _gridSize,
            _gridSize
        );

        // ✅ FIX: Draw the grid as long as any part of it intersects the viewport
        if (!viewportBounds.Intersects(gridBounds))
        {
          continue; // Skip only if completely outside
        }

        // ✅ Calculate grid offset for proper screen positioning
        Vector2 gridOffset = new Vector2(
            _centerOfFullMap.X + gridWorldPosition.X - devicePosition.X,
            _centerOfFullMap.Y + gridWorldPosition.Y - devicePosition.Y
        );

        // ✅ Draw the grid tiles
        grid.DrawTiles(_SpriteBatch, gridOffset);

        // ✅ Draw the grid border
        Rectangle gridScreenRect = new Rectangle(
            (int)gridOffset.X,
            (int)gridOffset.Y,
            _gridSize,
            _gridSize
        );
        ContentManagerProvider.DrawRectangleBorder(_SpriteBatch, gridScreenRect, 1, Color.White);
      }
    }



    public void DrawMap()
    {

      Vector2 devicePosition = _device._devicePosition;

      _GraphicsDevice.SetRenderTarget(_mapTexture);
      _GraphicsDevice.Clear(Color.Transparent);


      _SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

      DrawGrids(devicePosition);

      if (AppSettings.Default.DrawMergedTiles)
      {
        DrawMergedRectangles(devicePosition, _centerOfFullMap);
      }
      // ✅ Draw the device at the center of the screen
      _SpriteBatch.Draw(
          _device._deviceTexture,
          _device.GetDeviceRectRelative(_centerOfFullMap),
          Color.Red
      );

      // ✅ Draw Map Border
      ContentManagerProvider.DrawRenderTargetBorder(_SpriteBatch, _mapTexture, 5, Color.White);

      // ✅ Flush all queued lines in a single draw call
      ContentManagerProvider.DrawQueuedLines(_SpriteBatch);

      //_distanceOverlay.Draw();

      _SpriteBatch.End();
      _GraphicsDevice.SetRenderTarget(null);
    }



    //private void DrawGrids()
    //{
    //  int gridSize = _GridManager._gridSizePixels;
    //  Vector2 centerOfFullMap = new Vector2(_mapTexture.Width / 2, _mapTexture.Height / 2);
    //  Vector2 devicePosition = _device._devicePosition; // The current device position in world space

    //  _GraphicsDevice.SetRenderTarget(_mapTexture);
    //  _GraphicsDevice.Clear(Color.Transparent);

    //  _SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

    //  foreach ((int, int) pos in _GridManager.Grids.Keys)
    //  {
    //    Grid grid = _GridManager.Grids[pos];

    //    //  Step 1: Offset each grid so it's centered around the device
    //    Vector2 gridOffset = new Vector2(
    //        centerOfFullMap.X + (pos.Item1 * gridSize) - devicePosition.X,
    //        centerOfFullMap.Y + (pos.Item2 * gridSize) - devicePosition.Y
    //    );

    //    //  Step 2: Draw Grid Lines 
    //    if (AppSettings.Default.DrawGrids)
    //    {
    //      _SpriteBatch.Draw(
    //          grid.gridLinesCanvas,
    //          new Rectangle((int)gridOffset.X, (int)gridOffset.Y, gridSize, gridSize),
    //          Color.White
    //      );
    //    }

    //    //  Step 3: Draw LiDAR Hit Tiles
    //    _SpriteBatch.Draw(
    //        grid.tilesCanvas,
    //        new Rectangle((int)gridOffset.X, (int)gridOffset.Y, gridSize, gridSize),
    //        Color.White
    //    );
    //    if (AppSettings.Default.DrawMergedTiles)
    //    {
    //      DrawMergedRectangles(devicePosition, centerOfFullMap);
    //    }



    //    //  Step 5: Draw the device at the center of the screen
    //    _SpriteBatch.Draw(
    //        _device._deviceTexture,
    //        _device.GetDeviceRectRelative(centerOfFullMap),
    //        Color.Red
    //    );

    //    //  Step 6: Draw Map Border
    //    ContentManagerProvider.DrawRenderTargetBorder(_SpriteBatch, _mapTexture, 5, Color.White);
    //  }
    //  //  NOW flush all queued lines in a single draw call
    //  ContentManagerProvider.DrawQueuedLines(_SpriteBatch);
    //  _SpriteBatch.End();
    //  _GraphicsDevice.SetRenderTarget(null);
    //}

    private void DrawMergedRectangles(Vector2 devicePosition, Vector2 centerOfFullMap)
    {
      // IMPORTANT: set minsize here, do not look it up from settings in the if statement. Causes a huge drop in speed
      int minSize = AppSettings.Default.MinMergedTileSize;
      foreach (var (rect, angle, ispermanent) in _map._mergedObjects)
      {
        if(rect.Width < minSize && rect.Height < minSize)
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










  }
}
