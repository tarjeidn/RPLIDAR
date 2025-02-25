using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using RPLIDAR_Mapping.Utilities;
using RPLIDAR_Mapping.Models;
using SharpDX.Direct2D1.Effects;

namespace RPLIDAR_Mapping.Features.Map.GridModel
{
  public class Tile
  {
    public int X { get; }
    public int Y { get; }
    public int pointsHitAtThisTile { get; set; }
    public bool IsDrawn { get; private set; }
    public Grid _selfGrid { get; private set; }
    public bool IsTrusted { get;  set; }
    public Color Color { get; }
    private Texture2D _GridLineTexture { get; set; }
    private Texture2D _TileTexture { get; set; }
    public Rectangle tileRect { get;  }
    public Vector2 Position { get; set; }
    public Vector2 GlobalCenter { get; set; }
    public MapPoint _representativeMapPoint { get; private set; }
    private readonly SpriteBatch _SpriteBatch;
    private int _tileSize {  get; set; }
    private int _ScaledTileSize { get; set; }
    public Tile(int x, int y, Grid grid)
    {
      // X,Y is the grid index of the tile
      X = x;
      Y = y;
      GlobalCenter = new Vector2(x, y);
      IsTrusted = false;

      pointsHitAtThisTile = 0;
      IsDrawn = false;
      _selfGrid = grid;
      _tileSize = _selfGrid.tileSize;
      Position = new Vector2(X * _tileSize, Y * _tileSize);
      GlobalCenter = _selfGrid.GridPosition + new Vector2(x * _tileSize + _tileSize / 2.0f, y * _tileSize + _tileSize / 2.0f);
      _representativeMapPoint = new MapPoint(GlobalCenter.X , GlobalCenter.Y, 0, 0, 0, 0);
      tileRect = new Rectangle((int)Position.X, (int)Position.Y, _tileSize, _tileSize);
      //_ScaledTileSize = _tileSize * AppSettings.Default.GridScaleCMtoPixels;
      _SpriteBatch = GraphicsDeviceProvider.SpriteBatch;
      //_GridLineTexture = ContentManagerProvider.LoadTexture("Sprites/Utility/border64grey");
      _GridLineTexture = ResizeTexture(ContentManagerProvider.GetTexture("Sprites/Utility/gridline10orange"), _tileSize, _tileSize);
      _TileTexture = ContentManagerProvider.GetTexture("tiletexture");
    }




    public void DrawGrid()
    {
      Vector2 position = new Vector2(X * _tileSize, Y * _tileSize);

      // Draw the gridline texture scaled to the tile size
      _SpriteBatch.Draw(
          _GridLineTexture,
          new Rectangle((int)position.X, (int)position.Y, _tileSize, _tileSize), // Scale to tile size
          Color.White
      );

    }
    public void Draw(float highAverageTileHitCount)
    {
      

      //  Define color intensity based on `pointsHitAtThisTile`
      int maxHits = (int)Math.Max(highAverageTileHitCount, 1); // Prevent division by zero
      float intensity = MathHelper.Clamp(pointsHitAtThisTile / (float)maxHits, 0f, 1f);

      //  Change colors dynamically: Low hits = Blue, High hits = Red
      Color tileColor = Color.Lerp(Color.Blue, Color.Red, intensity);

      //  Draw the tile with dynamic color
      _SpriteBatch.Draw(
          _TileTexture,
          tileRect,
          //new Rectangle((int)position.X, (int)position.Y, _tileSize, _tileSize),
          tileColor
      );
    }
    private Texture2D ResizeTexture(Texture2D texture, int width, int height)
    {
      RenderTarget2D renderTarget = new RenderTarget2D(GraphicsDeviceProvider.GraphicsDevice, width, height);
      GraphicsDeviceProvider.GraphicsDevice.SetRenderTarget(renderTarget);
      GraphicsDeviceProvider.GraphicsDevice.Clear(Color.Transparent);


      _SpriteBatch.Begin();
      _SpriteBatch.Draw(texture, new Rectangle(0, 0, width, height), Color.White);
      _SpriteBatch.End();

      GraphicsDeviceProvider.GraphicsDevice.SetRenderTarget(null);
      return renderTarget;
    }
  }
  }
