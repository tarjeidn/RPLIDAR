using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using Microsoft.Xna.Framework;
using RPLIDAR_Mapping.Features.Communications;
using RPLIDAR_Mapping.Features.Map.GridModel;
using RPLIDAR_Mapping.Models;
using RPLIDAR_Mapping.Providers;
using RPLIDAR_Mapping.Utilities;
using static RPLIDAR_Mapping.Features.Map.Algorithms.DevicePositionEstimator;


public class ICP
{
  private float _smoothedRotation = 0f;
  private Vector2 _smoothedOffset = Vector2.Zero;

  private const float rotationSmoothFactor = 0.2f;
  private const float offsetSmoothFactor = 0.2f;

  private Queue<(float rotation, Vector2 offset)> icpHistory = new();

  private const int maxHistory = 5;

  private float _maxMatchDistance = 20f;
  private float _convergenceThreshold = 0.5f;
  private int _maxIterations = 5;


  public ICP(float maxMatchDistance = 20f, float convergenceThreshold = 0.5f, int maxIterations = 5)
  {
    _maxMatchDistance = maxMatchDistance;
    _convergenceThreshold = convergenceThreshold;
    _maxIterations = maxIterations;
  }
  public void Update(List<DataPoint> _pointsBuffer, GridManager gm)
  {
    //1.Get the trusted map(reference) points
    Device _device = UtilityProvider.Device;
    List<Vector2> sourcePoints = _pointsBuffer
      .Where(p => p.EqTileGlobalCenter != Vector2.Zero)
      .Select(p => p.EqTileGlobalCenter)
      .ToList();

    List<Vector2> targetPoints = gm
        .GetAllTrustedTiles()
        .Select(t => t.GlobalCenter)
        .ToList();

    var icpResult = RunAndSmooth(sourcePoints, targetPoints);

    if (icpResult.HasValue)
    {
      float rotation = icpResult.Value.rotation;
      Vector2 offset = icpResult.Value.offset;

      _device._deviceOrientation += rotation;
      _device._deviceOrientation = NormalizeAngleRadians(_device._deviceOrientation);
      _device._devicePosition += offset;

      //Debug.WriteLine($"📌 ICP (smoothed) rotation: {MathHelper.ToDegrees(rotation):0.00}°, offset: {offset}");
    }
  }
  public (float rotation, Vector2 offset)? RunAndSmooth(List<Vector2> sourcePoints, List<Vector2> targetPoints)
  {
    var rawResult = Run(sourcePoints, targetPoints);
    if (rawResult == null)
      return null;

    icpHistory.Enqueue(rawResult.Value);
    if (icpHistory.Count > maxHistory)
      icpHistory.Dequeue();

    var avgRotation = icpHistory.Average(r => r.rotation);
    var avgOffset = new Vector2(icpHistory.Average(r => r.offset.X), icpHistory.Average(r => r.offset.Y));

    // Apply exponential smoothing
    _smoothedRotation = MathHelper.Lerp(_smoothedRotation, avgRotation, rotationSmoothFactor);
    _smoothedOffset = Vector2.Lerp(_smoothedOffset, avgOffset, offsetSmoothFactor);

    return (_smoothedRotation, _smoothedOffset);
  }


  /// <summary>
  /// Runs the ICP algorithm and returns the estimated offset and rotation between the scans.
  /// </summary>
  /// <param name="sourcePoints">New scan (DataPoint.GlobalPosition)</param>
  /// <param name="targetPoints">Trusted map points (e.g. tiles)</param>
  /// <param name="maxIterations">Maximum ICP iterations</param>
  /// <param name="convergenceThreshold">Stop early if change is small</param>
  /// <returns>A tuple: (rotation in radians, position offset vector)</returns>
  public (float rotation, Vector2 offset)? Run(List<Vector2> sourcePoints, List<Vector2> targetPoints)
  {
    if (sourcePoints.Count == 0 || targetPoints.Count == 0)
      return null;

    List<Vector2> workingSource = new(sourcePoints);
    float totalRotation = 0f;
    Vector2 totalOffset = Vector2.Zero;

    for (int iter = 0; iter < _maxIterations; iter++)
    {
      var matches = new List<(Vector2 source, Vector2 target)>();

      foreach (var p in workingSource)
      {
        Vector2 closest = FindClosestPoint(p, targetPoints);
        if (Vector2.Distance(p, closest) <= _maxMatchDistance)
          matches.Add((p, closest));
      }

      if (matches.Count < 3)
        break;

      var sourceCentroid = Average(matches.Select(m => m.source));
      var targetCentroid = Average(matches.Select(m => m.target));

      var sourceZeroed = matches.Select(m => m.source - sourceCentroid).ToList();
      var targetZeroed = matches.Select(m => m.target - targetCentroid).ToList();

      float sinSum = 0f, cosSum = 0f;
      for (int i = 0; i < matches.Count; i++)
      {
        var s = sourceZeroed[i];
        var t = targetZeroed[i];
        sinSum += s.X * t.Y - s.Y * t.X;
        cosSum += Vector2.Dot(s, t);
      }

      float rotation = MathF.Atan2(sinSum, cosSum);

      float rotationDegrees = MathHelper.ToDegrees(rotation);
      rotationDegrees = Math.Clamp(rotationDegrees, -3f, 3f); // stricter clamp
      rotation = MathHelper.ToRadians(rotationDegrees);

      float cosR = MathF.Cos(rotation);
      float sinR = MathF.Sin(rotation);

      for (int i = 0; i < workingSource.Count; i++)
      {
        var p = workingSource[i] - sourceCentroid;
        workingSource[i] = new Vector2(
            p.X * cosR - p.Y * sinR,
            p.X * sinR + p.Y * cosR
        ) + sourceCentroid;
      }

      var offset = targetCentroid - sourceCentroid;

      if (offset.Length() > _maxMatchDistance / 2f)
        offset = Vector2.Normalize(offset) * (_maxMatchDistance / 2f);

      for (int i = 0; i < workingSource.Count; i++)
        workingSource[i] += offset;

      totalOffset += offset;
      totalRotation += rotation;

      if (offset.Length() < _convergenceThreshold && Math.Abs(rotation) < MathHelper.ToRadians(0.1f))
        break;
    }

    return (totalRotation, totalOffset);
  }




  private static Vector2 FindClosestPoint(Vector2 point, List<Vector2> candidates)
  {
    float minDist = float.MaxValue;
    Vector2 closest = point;
    foreach (var c in candidates)
    {
      float d = Vector2.DistanceSquared(point, c);
      if (d < minDist)
      {
        minDist = d;
        closest = c;
      }
    }
    return closest;
  }

  private static Vector2 Average(IEnumerable<Vector2> points)
  {
    Vector2 sum = Vector2.Zero;
    int count = 0;
    foreach (var p in points)
    {
      sum += p;
      count++;
    }
    return (count > 0) ? sum / count : Vector2.Zero;
  }

  private static Vector2 RotateVector(Vector2 v, float radians)
  {
    float cos = MathF.Cos(radians);
    float sin = MathF.Sin(radians);
    return new Vector2(
        v.X * cos - v.Y * sin,
        v.X * sin + v.Y * cos
    );
  }
}

  








