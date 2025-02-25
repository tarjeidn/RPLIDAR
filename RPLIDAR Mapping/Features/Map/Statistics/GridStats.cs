using RPLIDAR_Mapping.Features.Map.GridModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Features.Map.Statistics
{
  public class GridStats
  {
    public Dictionary<string, object> Statistics { get; set; }
    public int higestTileHitCount { get; set; }
    public int totalHitTiles { get; set; }
    public int totalHitCount { get; set; }
    public float averageTileHitCount { get; set; }
    public float highAverageTileHitCount { get; set; }
    public int highTotalHitTiles { get; set; }
    public int highTotalHitCount { get; set; }
    public int TotalGrids { get; set; }

    public GridStats() 
    {
      Statistics = new Dictionary<string, object>();
      higestTileHitCount = 0;
      totalHitTiles = 0;
      totalHitCount = 0;
      averageTileHitCount = 0;
      highAverageTileHitCount = 0;
      highTotalHitTiles = 0;
      highTotalHitCount = 0;

    }
    public void AddStat(string key, object value)
    {
      Statistics[key] = value;
    }
    public void Update(GridManager gridManager) 
    {
      TotalGrids = gridManager.Grids.Count;
      
    }

  }
}
