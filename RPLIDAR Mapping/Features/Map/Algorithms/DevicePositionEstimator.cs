using Microsoft.Xna.Framework;
using RPLIDAR_Mapping.Features.Communications;
using RPLIDAR_Mapping.Features.Map.GridModel;
using RPLIDAR_Mapping.Features.Map.UI;
using RPLIDAR_Mapping.Models;
using RPLIDAR_Mapping.Providers;
using RPLIDAR_Mapping.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static RPLIDAR_Mapping.Features.Map.GridModel.Tile;
using static RPLIDAR_Mapping.Features.Map.Map;


namespace RPLIDAR_Mapping.Features.Map.Algorithms
{
  public class MotionEstimator
  {
    private readonly Dictionary<int, DataPoint> _lastKnownPoints = new();
    private const float MaxDeltaThreshold = 20f; // max mm change allowed per beam
    private const float MinValidMatches = 0.8f;
    private const float MinOffsetMagnitude = 2.0f;
    private Vector2 _lastOffset = Vector2.Zero;
    private const float LerpFactor = 0.2f;

    private readonly Queue<(Vector2 position, float time)> _positionHistory = new();
    private const int PositionHistoryLength = 10;

    public Vector2 EstimateDeviceOffsetFromAngleHistory(List<DataPoint> currentBatch)
    {
      List<Vector2> deltas = new();
      float latestTimestamp = 0;

      foreach (var point in currentBatch)
      {
        float adjustedAngleDeg = point.Angle + point.Yaw;
        int angleKey = (int)(adjustedAngleDeg * 10f);


        // ✅ Protect from Overflow (clamp or wrap keys)
        if (angleKey == int.MinValue)
          angleKey = int.MinValue + 1;
        int angleKeyWindow = 2;

        bool matchFound = false;

        for (int i = -angleKeyWindow; i <= angleKeyWindow; i++) // 👈 check angleKey ±1
        {
          int testKey = angleKey + i;

          if (_lastKnownPoints.TryGetValue(testKey, out var previousPoint))
          {
            Vector2 oldRel = previousPoint.GlobalPosition - previousPoint.DevicePositionAtHit;
            Vector2 newRel = point.GlobalPosition - point.DevicePositionAtHit;

            Vector2 delta = oldRel - newRel;


            if (delta.Length() < MaxDeltaThreshold)
            {
              deltas.Add(delta);
              latestTimestamp = Math.Max(latestTimestamp, point.TimeStamp);
              matchFound = true;
              break; // ✅ use first valid match
            }
          }
        }


        _lastKnownPoints[angleKey] = point;
      }

      if (deltas.Count < MinValidMatches)
        return Vector2.Zero;

      // Step 1: Outlier filtering
      deltas = FilterOutliersMAD(deltas);
      if (deltas.Count < MinValidMatches)
        return Vector2.Zero;

      // Step 2: Compute offset
      Vector2 offset = ComputeMedian(deltas);
      if (offset.Length() < MinOffsetMagnitude)
        return Vector2.Zero;



      // Final smoothing
      _lastOffset = Vector2.Lerp(_lastOffset, offset, LerpFactor);
      return _lastOffset;

    }

    private List<Vector2> FilterOutliersMAD(List<Vector2> deltas, float madMultiplier = 2.5f)
    {
      if (deltas.Count == 0) return deltas;

      Vector2 median = ComputeMedian(deltas);
      var distances = deltas.Select(d => Vector2.Distance(d, median)).ToList();

      float mad = ComputeMedian(distances);
      float threshold = mad * madMultiplier;

      return deltas.Where(d => Vector2.Distance(d, median) < threshold).ToList();
    }

    private float ComputeMedian(List<float> values)
    {
      if (values == null || values.Count == 0) return 0f;

      var sorted = values.OrderBy(v => v).ToList();
      int mid = sorted.Count / 2;

      return sorted.Count % 2 == 0
          ? (sorted[mid - 1] + sorted[mid]) / 2f
          : sorted[mid];
    }

    private Vector2 ComputeMedian(List<Vector2> vectors)
    {
      if (vectors.Count == 0) return Vector2.Zero;

      var xs = vectors.Select(v => v.X).OrderBy(x => x).ToList();
      var ys = vectors.Select(v => v.Y).OrderBy(y => y).ToList();

      float medianX = xs[xs.Count / 2];
      float medianY = ys[ys.Count / 2];

      return new Vector2(medianX, medianY);
    }


  }

}
