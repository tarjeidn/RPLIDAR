using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using RPLIDAR_Mapping.Models;
using System.Diagnostics;
using RPLIDAR_Mapping.Features.Map.Statistics;
using RPLIDAR_Mapping.Features.Map.UI;
using RPLIDAR_Mapping.Core;
using RPLIDAR_Mapping.Features.Map.Algorithms;
using RPLIDAR_Mapping.Providers;
using static RPLIDAR_Mapping.Features.Map.Map;



namespace RPLIDAR_Mapping.Features.Map.GridModel
{
  public class Grid
  {
    public int GridX { get; }
    public int GridY { get; }
    public Vector2 GridPosition { get;  set; }

    public readonly float GridScaleMmToPixels;

    public int GridSizePixels;
    public readonly float _GridSizeM;
    //private readonly Dictionary<(int, int), Tile> tiles;
    private Dictionary<int, Dictionary<int, Tile>> _tileMap = new();

    public readonly RenderTarget2D gridLinesCanvas;
    public readonly RenderTarget2D tilesCanvas;
    private readonly SpriteBatch _SpriteBatch;
    private readonly GraphicsDevice _GraphicsDevice;

    private readonly Texture2D GridLineTexture;
    public Dictionary<(int, int), Tile> _updatedTiles;
    public Dictionary<(int, int), Tile> _drawnTiles;
    public List<Tile> _drawnTilesList;
    private HashSet<Tile> _trustedTiles = new();
    public HashSet<Tile> RingTiles = new();
    public GridManager GridManager;
    public GridStats Stats;
    private TileTrustRegulator TTR =  AlgorithmProvider.TileTrustRegulator;
    public int DrawnTileCount => _drawnTiles.Count;
    float permanentDecayModifier = 0.5f;  //  Permanent tiles decay at half speed
    public float GridScaleFactor;
    public float _scaleToPixels;
    public bool IsUpdated = false;
    public float GridSizeMeters { get; private set; }
    public int tileSize { get; private set; }  // Actual tile unit size (cm)



    public Grid(GridManager gm, int gridX, int gridY)
    {
      GridManager = gm;
      Stats = gm.GridStats;
      //GridSizePixels = MapScaleManager.Instance.ScaledGridSizePixels;
      GridSizePixels = 1000;
      GridSizeMeters = MapScaleManager.Instance.GridAreaMeters;

      GridScaleFactor = MapScaleManager.Instance.ScaleFactor;

      //tileSize = MapScaleManager.Instance.ScaledTileSizePixels;
      tileSize = 10;

      _SpriteBatch = GraphicsDeviceProvider.SpriteBatch;
      _GraphicsDevice = GraphicsDeviceProvider.GraphicsDevice;

      //  Grid position in world space
      GridX = gridX;
      GridY = gridY;
      GridPosition = new Vector2(GridX * GridSizePixels, GridY * GridSizePixels);

      //  Initialize collections (before using them)
      _updatedTiles = new Dictionary<(int, int), Tile>();
      _drawnTiles = new Dictionary<(int, int), Tile>();

      _drawnTilesList = new List<Tile>();

      //  Create render targets with the scaled grid size
      gridLinesCanvas = new RenderTarget2D(_GraphicsDevice, GridSizePixels, GridSizePixels);
      tilesCanvas = new RenderTarget2D(_GraphicsDevice, GridSizePixels, GridSizePixels);
    }
    // Resets grid tiles
    public void ResetTiles()
    {      
      _drawnTiles.Clear();
      IsUpdated = true;
    }

    public bool Update()
    {
      return IsUpdated; 
    }
    public void DecayTiles()
    {
      List<(int, int)> keysToRemove = new();

      foreach (var kvp in _drawnTiles)
      {
        Tile tile = kvp.Value;
        bool permanent = false;
        //if (tile.TrustedScore > 0)
        //{
        //  tile.TrustedScore -= AppSettings.Default.TileTrustDecrement;
        //}
        if (tile.IsPermanent)
        {
          permanent = true;
          tile.TrustedScore = Math.Max(0, tile.TrustedScore - (TTR.TrustDecrement * permanentDecayModifier));

        } else tile.TrustedScore = Math.Max(0, tile.TrustedScore - TTR.TrustDecrement);
        tile.UpdateTrust();
        if (tile.TrustedScore < AlgorithmProvider.TileTrustRegulator.TrustThreshold)
        {
          tile.IsTrusted = false;
          if (tile.Cluster != null) tile.Cluster.TrustedTiles--;
          _trustedTiles.Remove(tile);
        }
        if (tile.TrustedScore <= 0)
        {
          IsUpdated= true;
          keysToRemove.Add(kvp.Key);
          tile.TrustedScore = 0;
          tile.Cluster?.RemoveTile(tile);
        }
      }

      foreach (var key in keysToRemove)
      {
        _drawnTiles.Remove(key);

      }
    }


    public void AddPoint(int X, int Y, MapPoint point)
    {
      Tile tile = GetTileAt(X, Y);
      tile.IsRingTile = point.IsRingPoint;
      tile._lastLIDARpoint = point;
      int tileSize = 10;
      //int tileSize = ridManager.GridScaleFactor;

      //  Scale values directly into pixels
      float scaledX = point.GlobalX * tileSize;
      float scaledY = point.GlobalY * tileSize;

      tile.Position = new Vector2(X * tileSize, Y * tileSize);
      //tile.Position = new Vector2(tile.GlobalCenter.X * tileSize,
      //                            tile.GlobalCenter.Y * tileSize);

      if (point.IsRingPoint)
      {
        IsUpdated = true;
        tile.TrustedScore = 100;

        if (point.IsInferredRingPoint)
        {
          tile.IsInferredRingTile = true;
          var (x, y) = point.InferredByGridIndex;
          tile.InferredBy = point.InferredBy;

          // ➕ Add to angle-indexed ring tile dictionary
          float angleDeg = MathHelper.ToDegrees(point.Radians) % 360f;
          if (angleDeg < 0) angleDeg += 360f;
          int angleKey = (int)(angleDeg * 10); // 0–3599

          var dict = UtilityProvider.Map.EstablishedRingTilesByAngle;
          if (!dict.TryGetValue(angleKey, out var tileList))
          {
            tileList = new List<Tile>();
            dict[angleKey] = tileList;
          }
          tileList.Add(tile);
        }

        RingTiles.Add(tile);
        return;
      }
      tile.TrustedScore = Math.Min(100, tile.TrustedScore + TTR.TrustIncrement);

      if (_drawnTiles.TryAdd((X, Y), tile)) GridManager._map.NewTiles.Add(tile); 

      StatisticsProvider.GridStats.RegisterPointAdded(X, Y);
      tile.UpdateTrust();
      if (tile.TrustedScore > AlgorithmProvider.TileTrustRegulator.TileTrustTreshHold)
      {
        tile.IsTrusted = true;
        _trustedTiles.Add(tile);
      }


      IsUpdated = true;
    }





    public Tile GetTileAt(int x, int y)
    {
      //  Ensure _tileMap is initialized
      if (_tileMap == null)
      {
        Debug.WriteLine("ERROR: _tileMap is NULL in GetTileAt!");
        _tileMap = new Dictionary<int, Dictionary<int, Tile>>();
      }

      //  Ensure the column exists
      if (!_tileMap.TryGetValue(x, out var column))
      {
        column = new Dictionary<int, Tile>();
        _tileMap[x] = column;
      }

      //  Ensure the tile exists in the column
      if (!column.TryGetValue(y, out var tile))
      {
        tile = new Tile(x, y, this);
        column[y] = tile;
      }

      return tile;
    }

    //public Dictionary<(int, int), Tile> GetTrustedTiles()
    //{
    //  return _drawnTiles;
    //}
    public HashSet<Tile> GetTrustedTiles()
    {
      //return new HashSet<Tile>(_drawnTiles.Values);
      return _trustedTiles;
    }



  }
}
