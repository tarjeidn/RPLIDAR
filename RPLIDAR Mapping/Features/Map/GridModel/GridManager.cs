using RPLIDAR_Mapping.Core;
using RPLIDAR_Mapping.Features.Map.Algorithms;
using RPLIDAR_Mapping.Features.Map.Statistics;
using RPLIDAR_Mapping.Features.Map.UI;
using RPLIDAR_Mapping.Models;
using RPLIDAR_Mapping.Providers;
using SharpDX.MediaFoundation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Features.Map.GridModel
{
  public class GridManager
  {
    public Map _map;
    public int tileSize;
    private readonly float _gridSizeM;

    public int _gridSizePixels;
    private int _framUpdates;
    public GridStats GridStats { get; set; }
    public List<MapPoint> _globalTrustedMapPoints {  get; set; }
    public Dictionary<(int, int), Grid> Grids { get; set; }
    private TileTrustRegulator TTR = AlgorithmProvider.TileTrustRegulator;
    public int TotalDrawnTiles => Grids.Values.Sum(grid => grid._drawnTiles.Count);
    public float GridScaleFactor {get; set;}

    public GridManager(Map map)
    {
      _framUpdates = 0;
      _map = map;
      _globalTrustedMapPoints = new List<MapPoint>();
      Grids = new Dictionary<(int, int), Grid>();
      GridStats = new GridStats(this);
      InitializeGrids();
      Grid template = Grids[(0, 0)];
      //tileSize = MapScaleManager.Instance.ScaledTileSizePixels;
      tileSize = 10;
      _gridSizeM = MapScaleManager.Instance.GridAreaMeters;
      //_gridSizePixels = MapScaleManager.Instance.ScaledGridSizePixels;
      _gridSizePixels = 1000;

      GridScaleFactor = MapScaleManager.Instance.ScaleFactor;


    }
    private void InitializeGrids()
    {
      for (int dx = -1; dx <= 1; dx++)
      {
        for (int dy = -1; dy <= 1; dy++)
        {
          var key = (dx, dy);
          Grids[key] = new Grid(this, dx, dy);
        }
      }

    }



    public bool Update()
    {
      bool doDecay = StatisticsProvider.MapStats.AddPointUpdates % TTR.DecayFrequency == 0;
      if (doDecay) GridStats.HighTrustTilesLostLastCycle = 0;
      bool gridupdated = false;
      GridStats.Update();
      foreach (Grid grid in Grids.Values)
      {        
        if (doDecay) {
          grid.DecayTiles();
        }
        if (grid.Update()) gridupdated = true;
      }
      StatisticsProvider.GridStats.FinalizeBatch();
      return gridupdated;
    }

    private Grid GetOrCreateGrid(int gridX, int gridY, bool createIfMissing = true)
    {
      var key = (gridX, gridY);
      if (!Grids.ContainsKey(key))
      {
        Grids[key] = new Grid(this, gridX, gridY);
      }

      return Grids[key];
    }

    public void MapPointToGrid(List<MapPoint> points)
    {
      foreach (MapPoint point in points)
      {
        //  Use dynamically scaled grid size
        float scaledGridSize = 1000;
        float scaledTileSize = 10;
        //float scaledGridSize = MapScaleManager.Instance.ScaledGridSizePixels;
        //float scaledTileSize = MapScaleManager.Instance.ScaledTileSizePixels;

        //  Compute correct grid index based on global position
        int gridX = (int)Math.Floor(point.GlobalX / scaledGridSize);
        int gridY = (int)Math.Floor(point.GlobalY / scaledGridSize);

        int maxTilesPerGrid = (int)(scaledGridSize / scaledTileSize);

        int localX = Math.Clamp((int)((point.GlobalX - (gridX * scaledGridSize)) / scaledTileSize), 0, maxTilesPerGrid - 1);
        int localY = Math.Clamp((int)((point.GlobalY - (gridY * scaledGridSize)) / scaledTileSize), 0, maxTilesPerGrid - 1);



        if (localX < 0 || localX >= (int)scaledGridSize / (int)scaledTileSize ||
            localY < 0 || localY >= (int)scaledGridSize / (int)scaledTileSize)
        {
          Debug.WriteLine($"⚠ WARNING: Tile ({localX}, {localY}) out of bounds! (Grid {gridX}, {gridY})");
          continue;
        }


        //  Fetch or create the grid and add the point
        GetOrCreateGrid(gridX, gridY).AddPoint(localX, localY, point);
      }
    }
    public Tile GetTileAtGlobalCoordinates(float globalX, float globalY)
    {
      //float scaledGridSize = MapScaleManager.Instance.ScaledGridSizePixels;
      //float scaledTileSize = MapScaleManager.Instance.ScaledTileSizePixels;
      float scaledGridSize = 1000;
      float scaledTileSize = 10;
      int gridX = (int)Math.Floor(globalX / scaledGridSize);
      int gridY = (int)Math.Floor(globalY / scaledGridSize);

      int maxTilesPerGrid = (int)(scaledGridSize / scaledTileSize);

      int localX = Math.Clamp((int)((globalX - (gridX * scaledGridSize)) / scaledTileSize), 0, maxTilesPerGrid - 1);
      int localY = Math.Clamp((int)((globalY - (gridY * scaledGridSize)) / scaledTileSize), 0, maxTilesPerGrid - 1);

      // Optional: warn on out-of-bounds
      if (localX < 0 || localX >= maxTilesPerGrid || localY < 0 || localY >= maxTilesPerGrid)
      {
        Debug.WriteLine($"⚠ WARNING: Tile ({localX}, {localY}) out of bounds! (Grid {gridX}, {gridY})");
        return null;
      }

      Grid grid = GetOrCreateGrid(gridX, gridY, createIfMissing: false);
      return grid.GetTileAt(localX, localY);
    }


    public IEnumerable<Grid> GetAllGrids()
    {
      return Grids.Values;
    }
    public HashSet<Tile> GetAllTrustedTiles()
    {
      HashSet<Tile> allTrustedTiles = new();

      foreach (Grid grid in Grids.Values)
      {
        allTrustedTiles.UnionWith(grid.GetTrustedTiles());
      }

      return allTrustedTiles;
    }

    //public Dictionary<(int, int), Dictionary<(int, int), Tile>> GetAllTrustedTiles()
    //{
    //  Dictionary<(int, int), Dictionary<(int, int), Tile>> allTrustedTiles = new();

    //  foreach (var kvp in Grids) // Loop through grids
    //  {
    //    (int gridX, int gridY) = kvp.Key; // Grid index
    //    Grid grid = kvp.Value;

    //    // Get all trusted tiles for this grid
    //    Dictionary<(int, int), Tile> gridTiles = grid.GetTrustedTiles();

    //    // Store them under their respective grid coordinates
    //    allTrustedTiles.TryAdd((gridX, gridY), gridTiles);
    //  }

    //  return allTrustedTiles;
    //}





  }
}
