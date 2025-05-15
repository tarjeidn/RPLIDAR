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
using RPLIDAR_Mapping.Features.Map.Algorithms;
using RPLIDAR_Mapping.Providers;

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

    private Texture2D _TileTexture { get; set; }
    public Rectangle WorldRect { get; set; }
    public Rectangle ScreenRect { get; private set; }
    public Vector2 Position { get; set; }
    public Vector2 GlobalCenter { get; set; }
    public Vector2 WorldGlobalPosition { get; set; }

    public TileCluster? Cluster { get; set; } = null;
    public int FramesInCluster = 0;
    public MapPoint _lastLIDARpoint { get; set; }
    public DataPoint LastDataPoint {  get; set; }
    private readonly SpriteBatch _SpriteBatch;
    public int _tileSize {  get; set; }
    public Tile LeftAngularNeighbor { get; set; }
    public Tile RightAngularNeighbor { get; set; }
    public Tile TopAngularNeighbor { get; set; }
    public Tile BottomAngularNeighbor { get; set; }
    public int TrustDurationCounter { get; private set; }
    public float TrustedScore { get; set; }
    public int AmountofDecays { get; set; }
    private TileTrustRegulator TTR { get; set;}
    private const int PermanentTrustThreshold = 50;  // How many cycles a tile must stay trusted to be permanent
    private const float TrustDropResetThreshold = 50;  // If trust drops below this, reset counter

    public bool IsRingTile { get; set; } = false;
    public bool IsInferredRingTile { get; set; } = false;
    public MapPoint InferredBy { get; set; }
    public bool IsClusterEnd { get; set; }
    public bool IsClusterIsolated { get; set; }
    public bool IsClusterMiddle { get; set; }
    public struct Observation
    {
      public Vector2 DevicePosition;
      public float Angle;
      public float Distance;
    }
    private List<Observation> _pastObservations = new();
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

      //_tileSize = _selfGrid.tileSize;
      _tileSize = 10;
      //  Get the global position of the grid first

      //  Get the tile's position relative to the grid
      Position = new Vector2(X * _tileSize, Y * _tileSize);
      _selfGrid = grid;
      Vector2 gridGlobalPosition = _selfGrid.GridPosition;
      WorldGlobalPosition = gridGlobalPosition + Position;
      

      //  Correctly calculate GlobalCenter (adjusted for grid position)
      GlobalCenter = gridGlobalPosition + Position + new Vector2(_tileSize / 2.0f, _tileSize / 2.0f);
      WorldRect = new Rectangle((int)WorldGlobalPosition.X, (int)WorldGlobalPosition.Y, _tileSize, _tileSize);
      //  Graphics setup
      _SpriteBatch = GraphicsDeviceProvider.SpriteBatch;

      _TileTexture = ContentManagerProvider.GetTexture("tiletexture");
      TTR = AlgorithmProvider.TileTrustRegulator;
    }
    public List<Tile> GetNeighbors()
    {
      List<Tile> neighbors = new();
      if (LeftAngularNeighbor != null) neighbors.Add(LeftAngularNeighbor);
      if (RightAngularNeighbor != null) neighbors.Add(RightAngularNeighbor);
      if (TopAngularNeighbor != null) neighbors.Add(TopAngularNeighbor);
      if (BottomAngularNeighbor != null) neighbors.Add(BottomAngularNeighbor);
      return neighbors;
    }
    public void RegisterObservation(Vector2 devicePosition, float angle, float distance)
    {
      _pastObservations.Add(new Observation
      {
        DevicePosition = devicePosition,
        Angle = angle,
        Distance = distance
      });
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
        if (!(Cluster == null)) Cluster.TrustedTiles++;
        IsPermanent = true;
      }
    }
    public Vector2 GetGlobalCenter()
    {
      //  Ensure the tile position updates correctly
      Vector2 gridGlobalPosition = _selfGrid.GridPosition;
      Vector2 tileLocalPosition = new Vector2(X * _tileSize, Y * _tileSize);

      return gridGlobalPosition + tileLocalPosition + new Vector2(_tileSize / 2.0f, _tileSize / 2.0f);


      //  Vector2 tileLocalPosition = new Vector2(X * MapScaleManager.Instance.ScaledTileSizePixels, Y * MapScaleManager.Instance.ScaledTileSizePixels);

      //  return gridGlobalPosition + tileLocalPosition + new Vector2(MapScaleManager.Instance.ScaledTileSizePixels / 2.0f, MapScaleManager.Instance.ScaledTileSizePixels / 2.0f);
    }

    public void Draw(SpriteBatch spriteBatch, Vector2 screenPos, Vector2 devicePos, bool drawSightLines)
    {
      if (!IsRingTile && TrustedScore < AlgorithmProvider.TileTrustRegulator.TrustThreshold) return;

      float intensity = TrustedScore != 0
          ? MathHelper.Clamp(TrustedScore / 100f, 0f, 1f)
          : 0f;

      Color tileColor = Color.Lerp(Color.Blue, Color.Red, intensity);
      if (IsTrusted) tileColor = Color.Green;
      if (IsRingTile) tileColor = Color.Yellow;
      if (IsInferredRingTile) tileColor = new Color(80, 80, 80);

      int tileSize = MapScaleManager.Instance.ScaledTileSizePixels;

      //  DON'T cache the screen rect
      Rectangle rect = new Rectangle(
          (int)(screenPos.X - tileSize / 2f),
          (int)(screenPos.Y - tileSize / 2f),
          tileSize,
          tileSize
      );

      // Draw sight lines (device to tile or inferred)
      if (drawSightLines)
      {
        if (_lastLIDARpoint != null)
        {
          Rectangle destRect = UtilityProvider.Camera.GetDestinationRectangle();
          Vector2 sightEnd = DrawingHelperFunctions.GetScreenBorderIntersection(screenPos, destRect, _lastLIDARpoint.Radians);
          DrawingHelperFunctions.DrawLine(spriteBatch, screenPos, sightEnd, Color.Red, 2, 0.1f);
        }

        DrawingHelperFunctions.DrawLine(spriteBatch, devicePos, screenPos, Color.Green, 2, 0.1f);
      }

      spriteBatch.Draw(_TileTexture, rect, tileColor);
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
