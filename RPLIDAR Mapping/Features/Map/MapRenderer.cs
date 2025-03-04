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
      float scaleFactor = MapScaleManager.Instance.ScaleFactor;
      int scaledGridSize = MapScaleManager.Instance.ScaledGridSizePixels;

      //  Get viewport bounds adjusted for scaling
      Rectangle viewportBounds = camera.GetViewportBounds(scaledGridSize);



      foreach ((int, int) pos in _GridManager.Grids.Keys)
      {
        Grid grid = _GridManager.Grids[pos];

        //  Compute **world position** correctly, applying scale
        Vector2 gridWorldPosition = new Vector2(
            pos.Item1 * scaledGridSize,
            pos.Item2 * scaledGridSize
        );

        //  Ensure the grid is within the viewport bounds
        Rectangle gridBounds = new Rectangle(
            (int)gridWorldPosition.X,
            (int)gridWorldPosition.Y,
            scaledGridSize,
            scaledGridSize
        );

        if (!viewportBounds.Intersects(gridBounds))
        {

          continue;
        }

        //  Compute **offset correctly**, applying scale factor
        Vector2 gridOffset = new Vector2(
            (_centerOfFullMap.X + (gridWorldPosition.X - devicePosition.X) * scaleFactor),
            (_centerOfFullMap.Y + (gridWorldPosition.Y - devicePosition.Y) * scaleFactor)
        );



        //  Draw grid tiles
        grid.DrawTiles(_SpriteBatch, gridOffset);

        //  Draw the grid border using scaled values
        Rectangle gridScreenRect = new Rectangle(
            (int)Math.Round(gridOffset.X),
            (int)Math.Round(gridOffset.Y),
            scaledGridSize,
            scaledGridSize
        );

        // ContentManagerProvider.DrawRectangleBorder(_SpriteBatch, gridScreenRect, 1, Color.White);
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
      //  Draw the device at the center of the screen
      _SpriteBatch.Draw(
          _device._deviceTexture,
          _device.GetDeviceRectRelative(_centerOfFullMap),
          Color.Red
      );

      //  Draw Map Border
      DrawingHelperFunctions.DrawGridPattern(_SpriteBatch, _mapTexture, 2);
      ContentManagerProvider.DrawRenderTargetBorder(_SpriteBatch, _mapTexture, 5, Color.White);

      //  Flush all queued lines in a single draw call
      ContentManagerProvider.DrawQueuedLines(_SpriteBatch);

      //_distanceOverlay.Draw();

      _SpriteBatch.End();
      _GraphicsDevice.SetRenderTarget(null);
    }



    private void DrawMergedRectangles(Vector2 devicePosition, Vector2 centerOfFullMap)
    {
      // IMPORTANT: set minsize here, do not look it up from settings in the if statement. Causes a huge drop in speed
      int minSize = AppSettings.Default.MinMergedTileSize;
      foreach (var (rect, angle, ispermanent) in _map._mergedObjects)
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










  }
}
