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



namespace RPLIDAR_Mapping.Features.Map
{
  public class MapRenderer
  {
    private readonly Texture2D _pointTexture;
    public readonly RenderTarget2D _mapTexture;
    public readonly GraphicsDevice _GraphicsDevice;
    private readonly SpriteBatch _SpriteBatch;
    private readonly GridManager _GridManager;
    //private readonly float _scale; // Scale factor for visualization
    private Map _map;
    private int _mapTextureSize;
    private int _ScaledTileSize;
    public Device _device;

    public MapRenderer(Map map)
    {
      _ScaledTileSize = AppSettings.Default.GridTileSizeCM * AppSettings.Default.GridScaleCMtoPixels;
      _GraphicsDevice = GraphicsDeviceProvider.GraphicsDevice;
      _SpriteBatch = GraphicsDeviceProvider.SpriteBatch;
      

      _map = map;
      _GridManager = _map.GetDistributor()._GridManager;


      _mapTexture = new RenderTarget2D(_GraphicsDevice, 2500, 2500);
      // Create a 1x1 white texture for rendering points
      _pointTexture = new Texture2D(_GraphicsDevice, 1, 1);
      _pointTexture.SetData(new[] { Color.White });
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
        Vector2 screenPoint = center + new Vector2(point.X, point.Y) * AppSettings.Default.MapScale;

        // Draw the point
        spriteBatch.Draw(_pointTexture, screenPoint, null, Color.Red, 0, Vector2.Zero, 2f, SpriteEffects.None, 0);
      }
    }
    public void DrawMap(Vector2 target)
    {

      DrawTiles();
      DrawGrids();


    }
    private void DrawTiles()
    {
      var font = ContentManagerProvider.GetFont("DebugFont");


      foreach ((int, int) pos in _GridManager.Grids.Keys)
      {
        Grid grid = _GridManager.Grids[pos];
        // Draw new tiles
        if (grid._updatedTiles.Any())
        {
          grid.DrawTiles();
        }

      }
    }

    private void DrawGrids()
    {
      int gridSize = _GridManager._gridSizePixels;
      Vector2 centerOfFullMap = new Vector2(_mapTexture.Width / 2, _mapTexture.Height / 2);      
      var font = ContentManagerProvider.GetFont("DebugFont");

      _GraphicsDevice.SetRenderTarget(_mapTexture);
      _GraphicsDevice.Clear(Color.Transparent);
      _SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
      foreach ((int, int) pos in _GridManager.Grids.Keys)
      {
        Grid grid = _GridManager.Grids[pos];        
        //  Step 3: Calculate the correct position for each grid
        Vector2 gridOffset = new Vector2(
            centerOfFullMap.X + (pos.Item1 * gridSize),
            centerOfFullMap.Y + (pos.Item2 * gridSize)
        );

        //  Step 4: Draw Grid Lines 
        if (AppSettings.Default.DrawGrids)
        {
          _SpriteBatch.Draw(
              grid.gridLinesCanvas, //  Draw directly into _mapTexture
              new Rectangle((int)gridOffset.X, (int)gridOffset.Y, gridSize, gridSize),
              Color.White
          );
        }

        //  Step 5: Draw LiDAR Hit Tiles (ONLY to `_mapTexture`)
        _SpriteBatch.Draw(
            grid.tilesCanvas, //  Ensure this is drawn onto _mapTexture
            //new Rectangle(0,0, gridSize, gridSize),
            new Rectangle((int)gridOffset.X, (int)gridOffset.Y, gridSize, gridSize),
            Color.White
        );
        // Get the relative position of the device, and draw it
        _SpriteBatch.Draw(
          _device._deviceTexture,
          _device.GetDeviceRectRelative(centerOfFullMap),
          Color.Red
          );
      }
      _SpriteBatch.End();
      _GraphicsDevice.SetRenderTarget(null);
    }
  }
}
