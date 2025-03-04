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
using SharpDX.DirectWrite;
using System.Diagnostics;
using RPLIDAR_Mapping.Features.Map.UI;

namespace RPLIDAR_Mapping.Features.Map.GridModel
{
  public class Tile
  {
    public int X { get; }
    public int Y { get; }
    public int pointsHitAtThisTile { get; set; }
    public bool IsDrawn { get; private set; }
    public bool IsPermanent { get; private set; }
    public Grid _selfGrid { get; private set; }
    public bool IsTrusted { get;  set; }
    public Color Color { get; }
    private Texture2D _GridLineTexture { get; set; }
    private Texture2D _TileTexture { get; set; }
    public Rectangle tileRect { get; private set; }
    public Vector2 Position { get; set; }
    public Vector2 GlobalCenter { get; set; }
    public MapPoint _representativeMapPoint { get; private set; }
    public MapPoint _lastLIDARpoint { get; set; }
    private readonly SpriteBatch _SpriteBatch;
    public int _tileSize {  get; set; }
    private int _ScaledTileSize { get; set; }
    public int TrustDurationCounter { get; private set; }
    public float TrustedScore { get; set; }
    public int AmountofDecays { get; set; }
    private const int PermanentTrustThreshold = 50;  // How many cycles a tile must stay trusted to be permanent
    private const float TrustDropResetThreshold = 70;  // If trust drops below this, reset counter


    public Tile(int x, int y, Grid grid)
    {
      // Tile index within the grid
      X = x;
      Y = y;
      AmountofDecays = 0;
      IsTrusted = false;
      TrustedScore = 0;
      pointsHitAtThisTile = 0;
      IsDrawn = false;
      _selfGrid = grid;
      _tileSize = _selfGrid.tileSize;

      //  Get the global position of the grid first
      Vector2 gridGlobalPosition = _selfGrid.GridPosition;

      //  Get the tile's position relative to the grid
      Position = new Vector2(X * _tileSize, Y * _tileSize);

      //  Correctly calculate GlobalCenter (adjusted for grid position)
      GlobalCenter = gridGlobalPosition + Position + new Vector2(_tileSize / 2.0f, _tileSize / 2.0f);

      //  Create representative MapPoint
      //_representativeMapPoint = new MapPoint(GlobalCenter.X, GlobalCenter.Y, 0, 0, 0, 0);

      //  Set tile rectangle for rendering
      //tileRect = new Rectangle((int)GlobalCenter.X - _tileSize / 2, (int)GlobalCenter.Y - _tileSize / 2, _tileSize, _tileSize);
      tileRect = new Rectangle((int)Position.X, (int)Position.Y, _tileSize, _tileSize);

      //  Graphics setup
      _SpriteBatch = GraphicsDeviceProvider.SpriteBatch;
      _GridLineTexture = ResizeTexture(ContentManagerProvider.GetTexture("Sprites/Utility/gridline10orange"), _tileSize, _tileSize);
      _TileTexture = ContentManagerProvider.GetTexture("tiletexture");
    }
    public void UpdateTrust()
    {
      //  If trust is above threshold, count towards permanence
      if (TrustedScore >= TrustDropResetThreshold)
      {
        TrustDurationCounter++;
      }
      else
      {
        TrustDurationCounter = 0;  //  Reset if trust drops too much
      }

      // 🏗 Mark tile as permanent if it stays trusted long enough
      if (TrustDurationCounter >= PermanentTrustThreshold)
      {
        IsPermanent = true;
      }
    }



    //public void Draw(SpriteBatch spriteBatch, Vector2 gridOffset, float scaleFactor)
    //{
    //  const float TrustDropBuffer = 5;  // 🟢 NEW: Buffer to prevent flickering

    //  if (IsDrawn)
    //  {
    //    // 🛑 Only remove tile if trust drops significantly below threshold
    //    if (TrustedScore < AppSettings.Default.TileTrustThreshold - TrustDropBuffer)
    //    {
    //      IsDrawn = false;
    //      return;
    //    }
    //  }
    //  else
    //  {
    //    //  Only allow drawing when trust is fully above threshold
    //    if (TrustedScore >= AppSettings.Default.TileTrustThreshold)
    //    {
    //      IsDrawn = true;
    //    }
    //    else
    //    {
    //      return;  //  Skip drawing if still below threshold
    //    }
    //  }

    //  // 🔥 Define color intensity based on TrustedScore
    //  float intensity = 0;
    //  if (TrustedScore != 0)
    //  {
    //    intensity = MathHelper.Clamp(
    //        (TrustedScore - AppSettings.Default.TileTrustThreshold) / (100 - AppSettings.Default.TileTrustThreshold),
    //        0f, 1f
    //    );
    //  }

    //  // 🔥 Change colors dynamically: Low trust = Blue, High trust = Red
    //  Color tileColor = Color.Lerp(Color.Blue, Color.Red, intensity);
    //  Position = new Vector2(X * _tileSize, Y * _tileSize) + gridOffset;



    //  //Vector2 position = Position + gridOffset;  //  Apply grid offset
    //  // 🔥 Draw the tile with dynamic color
    //  _SpriteBatch.Draw(
    //      _TileTexture,
    //      new Rectangle((int)Position.X, (int)Position.Y, _tileSize, _tileSize),
    //      tileColor
    //  );
    //}

    public void Draw(SpriteBatch spriteBatch, Vector2 drawPos)
    {
      if (TrustedScore < AppSettings.Default.TileTrustThreshold)
        return;

      float scaledTileSize = MapScaleManager.Instance.ScaledTileSizePixels;
      Color tileColor = Color.Lerp(Color.Blue, Color.Red, TrustedScore / 100f);

      spriteBatch.Draw(
        _TileTexture,
          new Rectangle((int)drawPos.X, (int)drawPos.Y, (int)scaledTileSize, (int)scaledTileSize),
          tileColor);
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
