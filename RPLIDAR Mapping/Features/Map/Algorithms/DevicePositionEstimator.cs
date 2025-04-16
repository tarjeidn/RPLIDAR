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
    private const float MaxDeltaThreshold = 100f; // max mm change allowed per beam
    private const int MinValidMatches = 10;
    private const float MinOffsetMagnitude = 2.0f;
    private Vector2 _lastOffset = Vector2.Zero;
    private const float LerpFactor = 0.2f;

    public Vector2 EstimateDeviceOffsetFromAngleHistory(List<DataPoint> currentBatch)
    {
      List<Vector2> deltas = new();

      foreach (var point in currentBatch)
      {
        float adjustedAngleDeg = point.Angle + point.Yaw;
        int angleKey = (int)(adjustedAngleDeg * 10f); // 0.1° resolution

        if (_lastKnownPoints.TryGetValue(angleKey, out var previousPoint))
        {
          Vector2 oldGlobal = previousPoint.GlobalPosition;
          Vector2 newGlobal = point.GlobalPosition;

          Vector2 delta = oldGlobal - newGlobal;

          if (delta.Length() < MaxDeltaThreshold)
            deltas.Add(delta);
        }

        _lastKnownPoints[angleKey] = point;  // Always update
      }

      if (deltas.Count < MinValidMatches)
        return Vector2.Zero;

      Vector2 averageOffset = ComputeAverage(deltas);

      // ✅ Suppress tiny movement updates (likely noise)
      if (averageOffset.Length() < MinOffsetMagnitude)
        return Vector2.Zero;

      Vector2 offset = ComputeAverage(deltas);
      if (offset.Length() < MinOffsetMagnitude)
        return Vector2.Zero;

      _lastOffset = Vector2.Lerp(_lastOffset, offset, LerpFactor);
      return _lastOffset;
    }

    private Vector2 ComputeAverage(List<Vector2> vectors)
    {
      if (vectors.Count == 0) return Vector2.Zero;

      float sumX = 0;
      float sumY = 0;
      foreach (var v in vectors)
      {
        sumX += v.X;
        sumY += v.Y;
      }

      return new Vector2(sumX / vectors.Count, sumY / vectors.Count);
    }
  }

}
