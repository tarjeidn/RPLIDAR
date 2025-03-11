using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Xna.Framework;
using MonoGame.Framework.Utilities.Deflate;
using RPLIDAR_Mapping.Core;
using RPLIDAR_Mapping.Features.Communications;
using RPLIDAR_Mapping.Features.Map.Algorithms;
using RPLIDAR_Mapping.Features.Map.GridModel;
using RPLIDAR_Mapping.Features.Map.Statistics;
using RPLIDAR_Mapping.Features.Map.UI;
using RPLIDAR_Mapping.Models;
using RPLIDAR_Mapping.Utilities;




namespace RPLIDAR_Mapping.Features.Map
{
  public class Map
  {
    private int MaxPoints = AppSettings.Default.MaxPoints; // Maximum points to store
    private readonly List<MapPoint> _points; // List of LiDAR points
    private ScanMatcher _matcher;
    //private MapDictionary _mapDictionary;
    private Distributor _Distributor { get; set; }
    public MapStats _MapStats { get; set; }
    public Device _device { get; set; }
    private List<DataPoint> _pointsBuffer;
    private FPSCounter _fpsCounter;
    public TileTrustRegulator _tileTrustRegulator;
    public TileMerge _tileMerge {  get; private set; }
    public bool _pointsAdded {  get; private set; }
    //public List<Rectangle> _mergedObjects {  get; private set; }

    //public List<(Rectangle, float)> _mergedObjects { get; private set; }
    private int _mergeTilesFrameCounter;
    public GridManager _gridManager { get; set; }

    private float _gridScaleFactor = 1.0f;  // 🔥 Centralized here!

    public float GridScaleFactor { get; set; }
    public bool MapUpdated = false;
    public int _minPointQuality;
    public float _minPointDistance;


    public Map(Device device)
    {
      _device = device;
      MapScaleManager.Instance._map = this;
      _tileTrustRegulator = AlgorithmProvider.TileTrustRegulator;
      _tileMerge = AlgorithmProvider.TileMerge;
      _minPointDistance = _tileMerge.MinPointDistance;
      _minPointQuality = _tileMerge.MinPointQuality;
      _mergeTilesFrameCounter = 0;
      _fpsCounter = UtilityProvider.FPSCounter;
      _MapStats = new MapStats();
      _points = new List<MapPoint>();
      _Distributor = new Distributor(this);
      _pointsBuffer = new List<DataPoint>();
      //_mergedObjects = new List<Rectangle>();
      //_mergedObjects = new List<(Rectangle, float, bool)>();
      _matcher = new ScanMatcher(_Distributor._GridManager._globalTrustedMapPoints);
      _gridManager = _Distributor._GridManager;
      GridScaleFactor = MapScaleManager.Instance.ScaleFactor;

    }

    public bool Update(List<DataPoint> dplist, float deltaTime)
    {
      bool updated = false;
      updated = _Distributor.Update();
      if (_tileTrustRegulator.RegulatorEnabled)
      {
        _tileTrustRegulator.Update(deltaTime);
      }

      if (dplist.Count > 0)
      {
        StatisticsProvider.MapStats.AddPointUpdates++;
        StatisticsProvider.MapStats.UpdatesSincePointsAdded = 0;
        _fpsCounter.IncrementLiDARUpdate();
        AddPoints(dplist);
        updated = true;

        //_mergeTilesFrameCounter = 0; // Reset counter since we received new points
      }
      else if (_mergeTilesFrameCounter >= _tileMerge.MergeFrequency && _tileMerge.DrawMergedTiles)
      {
        //Stopwatch stopwatch = Stopwatch.StartNew();
        _tileMerge.MergeTilesGlobal(AppSettings.Default.TilemergeThreshold, GetDistributor()._GridManager.GetAllDrawnTiles());
        //stopwatch.Stop();
        // Debug.WriteLine($"MergeTilesGlobal Execution Time: {stopwatch.ElapsedMilliseconds} ms");

        _mergeTilesFrameCounter = 0; // Reset counter after merging
        updated = true;
      }
      else if (_tileMerge.DrawMergedTiles)
      {
        _mergeTilesFrameCounter++; // Increment counter if no new points
      }

      _MapStats.AddToPointHistory(dplist.Count);
      return updated;
    }
     // Clears all added tiles
    public void ResetAllTiles()
    {
      foreach (var grid in _Distributor._GridManager.Grids.Values)
      {
        grid.ResetTiles();
      }
      _tileMerge.ClearMergedObjects();
    }
    public void resetAllGrids()
    {
      _gridManager.Grids.Clear();
    }

    public Distributor GetDistributor()
    {
      return _Distributor;
    }
    public void AddPoints(List<DataPoint> dplist)
    {
      List<MapPoint> addedPoints = new List<MapPoint>(dplist.Count);

      foreach (DataPoint dp in dplist)
      {
        if (dp.Quality >= _minPointQuality && dp.Distance >= _minPointDistance)
        {
          float radians = MathHelper.ToRadians(dp.Angle);
          float distance = dp.Distance;


          // Relative positions, from the viewpoint of the device. 
          float relativeX = distance * (float)Math.Cos(radians);
          float relativeY = distance * (float)Math.Sin(radians);

          // Global coordinates 
          float globalX = (_device._devicePosition.X) + relativeX;
          float globalY = (_device._devicePosition.Y) + relativeY;

          //  Store only scaled values in MapPoint
          MapPoint mapPoint = new MapPoint(relativeX, relativeY, dp.Angle, distance, radians, dp.Quality, globalX, globalY);
          addedPoints.Add(mapPoint);
        }
      }

      _Distributor.Distribute(addedPoints);
    }





    public List<MapPoint> GetPoints()
    {
      // Return a copy of the points list to avoid modification outside
      return new List<MapPoint>(_points);
    }


  }
}