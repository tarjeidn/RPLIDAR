using RPLIDAR_Mapping.Features.Map.Statistics;
using RPLIDAR_Mapping.Models;
using RPLIDAR_Mapping.Utilities;
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
    private readonly int tileSize;
    private readonly float _gridSizeM;
    private int _numberOfTiles;
    public int _gridSizePixels;
    private int _framUpdates;
    public GridStats GridStats { get; set; }
    public List<MapPoint> _globalTrustedMapPoints {  get; set; }
    public Dictionary<(int, int), Grid> Grids { get; set; } 

    public GridManager()
    {
      _framUpdates = 0;
      _globalTrustedMapPoints = new List<MapPoint>();
      Grids = new Dictionary<(int, int), Grid>();
      GridStats = new GridStats();
      InitializeGrids();
      Grid template = Grids[(0, 0)];
      tileSize = template.tileSize;
      _gridSizeM = template._GridSizeM;
      _gridSizePixels = template.GridSizePixels;
      _numberOfTiles = template._numberOfTiles;

    }
    private void InitializeGrids()
    {
      for (int dx = -1; dx <= 1; dx++)
      {
        for (int dy = -1; dy <= 1; dy++)
        {
          var key = (dx, dy);
          Grids[key] = new Grid(this);
        }
      }

    }
    public void Update()
    {

      GridStats.Update(this);
      foreach (Grid grid in Grids.Values)
      {
        grid.Update();
        //if (StatisticsProvider.MapStats.AddPointUpdates % AppSettings.Default.TileDecayRate == 0)
        //{
        //  grid.DecayTiles();
        //}
      }

    }

    private Grid GetOrCreateGrid(int gridX, int gridY)
    {
      var key = (gridX, gridY);
      if (!Grids.ContainsKey(key))
      {
        Grids[key] = new Grid(this);
      }
      return Grids[key];
    }
    // Helper method to extract trusted tile points from your grids.
    //public List<MapPoint> GetTrustedTilePoints()
    //{
    //  List<MapPoint> trustedPoints = new List<MapPoint>();
    //  foreach (Grid grid in Grids.Values)
    //  {
    //    foreach (Tile tile in grid._trustedTiles.Values)  // Assume each grid stores its trusted tiles.
    //    {
          
    //    }
    //  }
    //  return trustedPoints;
    //}

    public void MapPointToGrid(MapPoint point)
    {
      StatisticsProvider.MapStats.AddPointUpdates++;
      // Get the coordinates of the specific grid (GridX, GriY) of the GRID the point belongs to

      int gridX = (int)Math.Floor(point.X /(_gridSizeM* _gridSizePixels));
      int gridY = (int)Math.Floor(point.Y / (_gridSizeM * _gridSizePixels));
      // Coordinates of the point on its particular grid
      int localX = ((int)point.X % _gridSizePixels + _gridSizePixels) % _gridSizePixels / tileSize;
      int localY = ((int)point.Y % _gridSizePixels + _gridSizePixels) % _gridSizePixels / tileSize;
      if (localX < 0 || localX >= _gridSizePixels / tileSize ||
          localY < 0 || localY >= _gridSizePixels / tileSize)
            {
              Debug.WriteLine($"⚠ WARNING: TileX {localX} or TileY {localY} out of bounds!");
            }


      var grid = GetOrCreateGrid(gridX, gridY);
      grid.AddPoint(localX, localY, point);


    }

    public IEnumerable<Grid> GetAllGrids()
    {
      return Grids.Values;
    }


  }
}
