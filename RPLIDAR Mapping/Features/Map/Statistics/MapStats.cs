using Microsoft.Xna.Framework;
using RPLIDAR_Mapping.Features.Map.GridModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Features.Map.Statistics
{
  public class MapStats
  {
    public Queue<int> _pointHistory = new Queue<int>();
    public const int MAX_ENTRIES = 100;
    public int TotalPointsHandledThisFrame {  get; set; }
    public float PointsPerSecond {  get; set; }
    public float FPS {  get; set; }
    public float CurrentPacketSize { get; set; }
    public int FrameUpdates {  get; set; }
    public int AddPointUpdates { get; set; }
    public int LiDARUpdatesPerSecond { get; set; }

    public MapStats()
    {
      TotalPointsHandledThisFrame = 0;
      FrameUpdates = 0;
      AddPointUpdates = 0;
    }
    public void AddToPointHistory(int count)
    {
      if (count > 0) {
        CurrentPacketSize = count;
      }
      TotalPointsHandledThisFrame = count;
      if (_pointHistory.Count >= MAX_ENTRIES)
      {
        _pointHistory.Dequeue(); // Remove the oldest entry
      }
      _pointHistory.Enqueue(count); // Add the new entry
    }



  }
}
