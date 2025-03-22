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
using SharpDX.Direct2D1.Effects;
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
      GridSizePixels = MapScaleManager.Instance.ScaledGridSizePixels;
      GridSizeMeters = MapScaleManager.Instance.GridAreaMeters;

      GridScaleFactor = MapScaleManager.Instance.ScaleFactor;

      tileSize = MapScaleManager.Instance.ScaledTileSizePixels;

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
        if (permanent && !tile.IsPermanent)
        {
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
      tile._lastLIDARpoint = point;

      //  Scale cm values directly into pixels
      float scaledX = point.GlobalX * GridManager.GridScaleFactor;
      float scaledY = point.GlobalY * GridManager.GridScaleFactor; ;

      //tile.Position = new Vector2(X * tileSize, Y * tileSize);
      tile.Position = new Vector2(tile.GlobalCenter.X * GridManager.GridScaleFactor,
                                  tile.GlobalCenter.Y * GridManager.GridScaleFactor);


      tile.TrustedScore = Math.Min(100, tile.TrustedScore + TTR.TrustIncrement);

      if (_drawnTiles.TryAdd((X, Y), tile)) GridManager._map.NewTiles.Add(tile); 

      StatisticsProvider.GridStats.RegisterPointAdded(X, Y);
      tile.UpdateTrust();
      if (tile.TrustedScore > 50)
      {
        _trustedTiles.Add(tile);
      }
      // ✅ Register observation and add to lookup
      Vector2 devicePosition = GridManager._map._device._devicePosition;
      tile.RegisterObservation(devicePosition, point.Angle, point.Distance);

      var key = new ObservationKey(devicePosition, point.Angle, point.Distance);
      if (Map.ObservationLookup.TryGetValue(key, out Tile oldTile))
      {
        GridManager._map.MatchedTileCount++;
        Tile currentTile = GetTileAt(X, Y);

        int oldX = (int)(oldTile.GlobalCenter.X / MapScaleManager.Instance.ScaledTileSizePixels);
        int oldY = (int)(oldTile.GlobalCenter.Y / MapScaleManager.Instance.ScaledTileSizePixels);

        int newX = (int)(currentTile.GlobalCenter.X / MapScaleManager.Instance.ScaledTileSizePixels);
        int newY = (int)(currentTile.GlobalCenter.Y / MapScaleManager.Instance.ScaledTileSizePixels);

        int dx = Math.Abs(oldX - newX);
        int dy = Math.Abs(oldY - newY);

        if (dx + dy > 1 )  // use (dx > 0 || dy > 0) for 0 tolerance or use dx + dy > 1 to allow 1-tile tolerance
        {
          GridManager._map.MismatchedTileCount++;
          //Debug.WriteLine($"⚠️ MISMATCH: Expected tile ({oldX},{oldY}), got ({newX},{newY})");
        }
      }
      if (!Map.ObservationLookup.ContainsKey(key))
      {
        Map.ObservationLookup.Add(key, tile);
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
      return new HashSet<Tile>(_drawnTiles.Values);
    }



  }
}
