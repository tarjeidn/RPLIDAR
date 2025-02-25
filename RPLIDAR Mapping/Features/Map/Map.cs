using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Xna.Framework;
using RPLIDAR_Mapping.Features.Communications;
using RPLIDAR_Mapping.Features.Map.Statistics;
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
    

    public Map()
    {
      
      _MapStats = new MapStats();
      _points = new List<MapPoint>();
      _Distributor = new Distributor();
      _matcher = new ScanMatcher(_Distributor._GridManager._globalTrustedMapPoints);


    }
    public void Update(List<DataPoint> dplist)
    {

      AddPoints(dplist);
      _MapStats.AddToPointHistory(dplist.Count);
      _Distributor.Update();
    }

    public Distributor GetDistributor()
    {
      return _Distributor;
    }

    public void AddPoints(List<DataPoint> dplist) 
    {
      // Ignore points with low quality
      List<MapPoint> mplist= new List<MapPoint>();
      foreach (DataPoint dp in dplist) {
        if (dp.Quality > AppSettings.Default.MinPointQuality)
        {
          if (dp.Distance < 1.0f) // Consider ignoring small distances
          {
            Debug.WriteLine($"⚠ WARNING: Small Distance Detected! → Angle: {dp.Angle}, Distance: {dp.Distance}");
            return;
          }
          // Convert polar to Cartesian
          dp.Angle = (dp.Angle + 360) % 360;
          float radians = MathHelper.ToRadians(dp.Angle);
          float x = dp.Distance * (float)Math.Cos(radians);
          float y = dp.Distance * (float)Math.Sin(radians);
          if ((x == 0 && y == 0) || dp.Angle > 360)
          {
            return;
          }
          // Add point and distribute it
          MapPoint point = new MapPoint(x, y, dp.Angle, dp.Distance, radians, dp.Quality);
          mplist.Add(point);
          _Distributor.Distribute(point);
          //_points.Add(point);
          //Debug.WriteLine($"📡 LiDAR Data → Angle: {dp.Angle}, Distance: {dp.Distance}, Quality: {dp.Quality}");
          //Debug.WriteLine($"🌎 Converted to Cartesian → X: {x}, Y: {y}");
          // Remove oldest points if over capacity
          if (_points.Count > MaxPoints)
          {
            _points.RemoveAt(0);
          }
        }
      }
      // Run scan matching to compute the transformation between scans
      //if ((StatisticsProvider.MapStats.FrameUpdates % AppSettings.Default.TileDecayRate == 0) && mplist.Count > 0)
      //{
      //  Transformation icpTransform = _matcher.ProcessScan(mplist);
      //  _device.UpdatePositionFromScan(icpTransform);
      //}

    }


    public List<MapPoint> GetPoints()
    {
      // Return a copy of the points list to avoid modification outside
      return new List<MapPoint>(_points);
    }

    public void Clear()
    {
      _points.Clear();
    }
  }
}