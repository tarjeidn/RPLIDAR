using Microsoft.Xna.Framework.Graphics;
using RPLIDAR_Mapping.Utilities;
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
    public List<Tile> _trustedTiles;
    public GridManager GridManager;
    public GridStats Stats;
    private TileTrustRegulator TTR =  AlgorithmProvider.TileTrustRegulator;
    public int DrawnTileCount => _drawnTiles.Count;
    float permanentDecayModifier = 0.5f;  //  Permanent tiles decay at half speed
    public float GridScaleFactor;
    public float _scaleToPixels;




    public float GridSizeMeters { get; private set; }
    public int tileSize { get; private set; }  // Actual tile unit size (cm)



    public Grid(GridManager gm, int gridX, int gridY)
    {
      GridManager = gm;
      Stats = gm.GridStats;
      GridSizePixels = MapScaleManager.Instance.ScaledGridSizePixels;
      GridSizeMeters = MapScaleManager.Instance.GridAreaMeters;
      //GridSizePixels = AppSettings.Default.GridPixels; // 3000px
      //GridSizeMeters = AppSettings.Default.GridSizeM; // 10m



      // 🔥 Use GridScaleFactor to determine world unit per tile
      GridScaleFactor = MapScaleManager.Instance.ScaleFactor;


      //  Compute tile size (scaled to cm)
      tileSize = MapScaleManager.Instance.ScaledTileSizePixels;
      //tileSize = (int)(AppSettings.Default.GridTileSizeCM * GridScaleFactor);
      //if (tileSize < 1) 
      //{ 
      //  tileSize = 1;
      //}



      _SpriteBatch = GraphicsDeviceProvider.SpriteBatch;
      _GraphicsDevice = GraphicsDeviceProvider.GraphicsDevice;

      //  Grid position in world space
      GridX = gridX;
      GridY = gridY;
      GridPosition = new Vector2(GridX * GridSizePixels, GridY * GridSizePixels);

      //  Initialize collections (before using them)
      _updatedTiles = new Dictionary<(int, int), Tile>();
      _drawnTiles = new Dictionary<(int, int), Tile>();
      _trustedTiles = new List<Tile>();
      _drawnTilesList = new List<Tile>();

      //  Create render targets with the scaled grid size
      gridLinesCanvas = new RenderTarget2D(_GraphicsDevice, GridSizePixels, GridSizePixels);
      tilesCanvas = new RenderTarget2D(_GraphicsDevice, GridSizePixels, GridSizePixels);
    }
    // Resets grid tiles
    public void ResetTiles()
    {      
      _drawnTiles.Clear();
    }



    public bool Update()
    {
      bool gridupdated = false;

      return gridupdated; 
    }
    public void DecayTiles()
    {
      List<(int, int)> keysToRemove = new();

      foreach (var kvp in _drawnTiles)
      {
        Tile tile = kvp.Value;

        //if (tile.TrustedScore > 0)
        //{
        //  tile.TrustedScore -= AppSettings.Default.TileTrustDecrement;
        //}
        if (tile.IsPermanent)
        {
          tile.TrustedScore = Math.Max(0, tile.TrustedScore - (TTR.TrustDecrement * permanentDecayModifier));
          if (tile.TrustedScore < TTR.TrustThreshold) Stats.HighTrustTilesLostLastCycle++;
        } else tile.TrustedScore = Math.Max(0, tile.TrustedScore - TTR.TrustDecrement);




        if (tile.TrustedScore <= 0)
        {
          keysToRemove.Add(kvp.Key);
          tile.TrustedScore = 0;
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

      _drawnTiles.TryAdd((X, Y), tile);
      StatisticsProvider.GridStats.RegisterPointAdded(X, Y);
      tile.UpdateTrust();
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

    public List<Tile> GetDrawnTiles()
    {
      _drawnTilesList.Clear();
      foreach(Tile tile in  _drawnTiles.Values)
      {
        _drawnTilesList.Add(tile);
      }
      return _drawnTilesList;
    }




  }
}
