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
  public class DevicePositionEstimator
  {
    public struct MotionEstimate
    {
      public Vector2? Offset;
      public float? Rotation; // in radians

      public MotionEstimate(Vector2? offset, float? rotation)
      {
        Offset = offset;
        Rotation = rotation;
      }
    }

    public static void Update(List<DataPoint> _pointsBuffer, GridManager gm)
    {
      int matched, mismatched;
      Device _device = UtilityProvider.Device;
      Vector2 currentDevicePos = _device._devicePosition;
      float currentDeviceOrientation = _device._deviceOrientation;

      MotionEstimate estimated = DevicePositionEstimator.EstimateOffset(
          _pointsBuffer,
          Map.ObservationLookup,
          currentDevicePos,
          currentDeviceOrientation,
          //MapScaleManager.Instance.ScaledTileSizePixels,
          10,
          gm,
          out matched,
          out mismatched
      );


      // First, apply rotation
      // 1. Estimate rotation FIRST
      float simulatedOrientation = _device._deviceOrientation;

      if (estimated.Rotation.HasValue)
      {
        simulatedOrientation += estimated.Rotation.Value;
        simulatedOrientation = NormalizeAngleRadians(simulatedOrientation);
        Debug.WriteLine($"🌀 Simulated orientation after rotation: {MathHelper.ToDegrees(simulatedOrientation):0.00}°");
      }

      // 2. Recalculate the position offset using this new simulated orientation
      // You’ll need to update your EstimateOffset() to accept an override orientation for offset calculations
      var updatedEstimate = RecalculateOffsetOnly(
          _pointsBuffer,
          Map.ObservationLookup,
          simulatedOrientation,
          currentDevicePos,
          10,
          gm
      );

      // 3. Now apply both, in one go
      if (estimated.Rotation.HasValue)
      {
        float delta = estimated.Rotation.Value;

        float minThreshold = MathHelper.ToRadians(0.2f);   // Ignore tiny jitters
        float maxThreshold = MathHelper.ToRadians(10f);    // Ignore absurd jumps

        if (Math.Abs(delta) < minThreshold)
        {
          Debug.WriteLine($"⚠️ Ignored tiny rotation delta: {MathHelper.ToDegrees(delta):0.00}°");
        } else
        {
          // Clamp to max sane range
          //float clampedDelta = MathHelper.Clamp(delta, -maxThreshold, maxThreshold);

          // Optionally smooth
          //float targetRotation = _device._deviceOrientation + clampedDelta;
          float targetRotation = _device._deviceOrientation + delta;
          _device._deviceOrientation = NormalizeAngleRadians(targetRotation);
          _device._deviceOrientation = MathHelper.Lerp(
              _device._deviceOrientation,
              NormalizeAngleRadians(targetRotation),
              0.25f // smoothing factor
          );

          //Debug.WriteLine($"✅ Applied rotation: {MathHelper.ToDegrees(clampedDelta):0.00}°");
          Debug.WriteLine($"🎯 Final orientation: {MathHelper.ToDegrees(_device._deviceOrientation):0.00}°");
        }
      }



      if (updatedEstimate.Offset.HasValue)
      {
        var offset = -updatedEstimate.Offset.Value;
        if (offset.Length() > 0.05f)
        {
          var target = _device._devicePosition - offset;
          _device._devicePosition = Vector2.Lerp(_device._devicePosition, target, 0.25f);
          Debug.WriteLine($"📍 Adjusted device position to: {_device._devicePosition}");
        }
      }

      // Finally, clear cache
      Map.ObservationLookup.Clear();
    }
    public static float NormalizeAngleRadians(float angle)
    {
      while (angle > MathF.PI) angle -= MathF.Tau;
      while (angle < -MathF.PI) angle += MathF.Tau;
      return angle;
    }
    private static string GetSectorFromAngle(float angle)
    {
      angle = NormalizeAngleRadians(angle);
      float deg = MathHelper.ToDegrees(angle);
      if (deg < 0) deg += 360;

      if (deg >= 315 || deg < 45) return "Front";
      if (deg >= 45 && deg < 135) return "Right";
      if (deg >= 135 && deg < 225) return "Back";
      if (deg >= 225 && deg < 315) return "Left";

      return "Front"; // fallback
    }


    public static MotionEstimate RecalculateOffsetOnly(
    List<DataPoint> newPoints,
    Dictionary<ObservationKey, Tile> lookup,
    float simulatedOrientation,
    Vector2 currentDevicePosition,
    float tileSize,
    GridManager gridManager)
    {
      List<Vector2> offsets = new();
      int matched = 0;
      int mismatched = 0;

      foreach (var point in newPoints)
      {
        // Reconstruct global position of scan point
        Vector2 relative = Utility.PolarToCartesian(point.Angle + MathHelper.ToDegrees(simulatedOrientation), point.Distance);
        Vector2 newGlobal = currentDevicePosition + relative;

        // Try to find the tile from the old scan
        //if (TryFindMatchingTileFromWorld(currentDevicePosition, point.Angle, point.Distance, simulatedOrientation, gridManager, out Tile oldTile))
        if (TryFindSimilarObservation(currentDevicePosition, point.Angle, point.Distance, simulatedOrientation, out Tile oldTile, out ObservationKey _))
        {
          matched++;

          Vector2 oldCenter = oldTile.GlobalCenter;

          Tile newTile = gridManager.GetTileAtGlobalCoordinates(newGlobal.X, newGlobal.Y);
          Vector2 newCenter = newTile.GlobalCenter;

          if ((int)(oldCenter.X / tileSize) != (int)(newGlobal.X / tileSize) ||
              (int)(oldCenter.Y / tileSize) != (int)(newGlobal.Y / tileSize))
          {
            mismatched++;
            offsets.Add(oldCenter - newCenter);
          }
        }
      }

      if (offsets.Count >= 10)
      {
        Vector2 sum = Vector2.Zero;
        foreach (var offset in offsets)
          sum += offset;
        return new MotionEstimate(sum / offsets.Count, null);
      }

      return new MotionEstimate(null, null);
    }

    public static MotionEstimate EstimateOffset(
        List<DataPoint> newPoints,
        Dictionary<ObservationKey, Tile> lookup,
        Vector2 currentDevicePosition,
        float currentDeviceOrientation,
        float tileSize,
        GridManager gridManager,
        out int matched, out int mismatched)
    {
      //
      tileSize = 10;
      //
      List<Vector2> offsets = new();
      List<float> rotationOffsets = new();
      matched = 0;
      mismatched = 0;

      foreach (var point in newPoints)
      {
        float angleRad = NormalizeAngleRadians(currentDeviceOrientation + MathHelper.ToRadians(point.Angle));

        //var key = new ObservationKey(currentDevicePosition, point.Angle, point.Distance);
        //if (TryFindMatchingTileFromWorld(currentDevicePosition, point.Angle, point.Distance, currentDeviceOrientation, gridManager, out Tile oldTile))

        if (TryFindSimilarObservation(currentDevicePosition, point.Angle, point.Distance, currentDeviceOrientation, out Tile oldTile, out ObservationKey matchedKey))

        //if (Map.ObservationLookup.TryGetValue(key, out Tile oldTile))
        {
          matched++;

          Vector2 oldCenter = oldTile.GlobalCenter;
          Vector2 offset = Utility.PolarToCartesian(point.Angle, point.Distance);
          Vector2 newGlobal = currentDevicePosition + offset;



          int newX = (int)(newGlobal.X / tileSize);
          int newY = (int)(newGlobal.Y / tileSize);

          Tile newTile = gridManager.GetTileAtGlobalCoordinates(newGlobal.X, newGlobal.Y);
          Vector2 newCenter = newTile.GlobalCenter;

          // Always try to estimate rotation if we have a valid old observation
          // Always try to estimate rotation if we have a valid old observation
          float maxDistanceForRotation = 3000f;
          if (oldTile._lastLIDARpoint != null && oldTile._lastLIDARpoint.Distance < maxDistanceForRotation)
          {
            float oldBeamRadians = oldTile._lastLIDARpoint.DeviceOrientation + oldTile._lastLIDARpoint.Radians;
            float newBeamRadians = currentDeviceOrientation + MathHelper.ToRadians(point.Angle);

            Vector2 oldDir = Vector2.Normalize(new Vector2(MathF.Cos(oldBeamRadians), MathF.Sin(oldBeamRadians)));
            Vector2 newDir = Vector2.Normalize(new Vector2(MathF.Cos(newBeamRadians), MathF.Sin(newBeamRadians)));

            float angleOffset = MathF.Atan2(
                oldDir.X * newDir.Y - oldDir.Y * newDir.X, // 2D cross product
                Vector2.Dot(oldDir, newDir)               // Dot product
            );

            angleOffset = NormalizeAngleRadians(angleOffset);
            rotationOffsets.Add(angleOffset);

            //Debug.WriteLine($"🧭 oldBeam: {MathHelper.ToDegrees(oldBeamRadians):0.00}°, newBeam: {MathHelper.ToDegrees(newBeamRadians):0.00}°");
            //Debug.WriteLine($"🌀 Raw angle delta: {MathHelper.ToDegrees(angleOffset):0.00}°");
          }


          // Then handle position offset
          if ((int)(oldCenter.X / tileSize) != newX || (int)(oldCenter.Y / tileSize) != newY)
          {
            mismatched++;
            Vector2 movementOffset = oldCenter - newCenter;
            offsets.Add(movementOffset);
          }
        }

      }
      Vector2? averageOffset = null;
      float? averageRotation = null;
      float? fallbackRotation = null;

      // --- Position Offset ---
      if (offsets.Count >= 15)
      {
        Vector2 sum = Vector2.Zero;
        foreach (var o in offsets) sum += o;
        averageOffset = sum / offsets.Count;
      }

      // --- Rotation Offset via Tile Matching ---
      if (rotationOffsets.Count >= 15)
      {
        float sinSum = 0f;
        float cosSum = 0f;
        foreach (var r in rotationOffsets)
        {
          sinSum += MathF.Sin(r);
          cosSum += MathF.Cos(r);
        }

        averageRotation = MathF.Atan2(sinSum, cosSum);
        averageRotation = MathHelper.Clamp(averageRotation.Value, -MathF.PI / 72f, MathF.PI / 72f); // ±2.5°
        //Debug.WriteLine($"🧭 Estimated avg rotation (circular): {MathHelper.ToDegrees(averageRotation.Value):0.00}°");
      }

      // --- Fallback Rotation from Profile Matching ---
      if (rotationOffsets.Count < 15)
      {
        var comparedtoTiles = gridManager.GetAllTrustedTiles();
        fallbackRotation = EstimateRotationFromDistanceProfiles(
          newPoints,
          comparedtoTiles,
          currentDeviceOrientation
        );
        //float maxFallback = MathHelper.ToRadians(5f); // try 2.5–5°
        //fallbackRotation = MathHelper.Clamp(fallbackRotation.Value, -maxFallback, maxFallback);
        if (fallbackRotation.HasValue)
        {
          //Debug.WriteLine($"🧭 [Fallback] Rotation estimated from distance profiles: {MathHelper.ToDegrees(fallbackRotation.Value):0.00}°");
        }
      }

      // --- Debug Info ---
      //Debug.WriteLine($"📊 Rotation offset count: {rotationOffsets.Count}");
      if (rotationOffsets.Count > 0)
      {
        float debugAvg = rotationOffsets.Sum() / rotationOffsets.Count;
        //Debug.WriteLine($"🧭 Estimated avg rotation: {MathHelper.ToDegrees(debugAvg):0.00}°");
      }

      // --- Final Return (use fallback if needed) ---
      return new MotionEstimate(averageOffset, averageRotation ?? fallbackRotation);
    }
    public static bool TryFindSimilarObservation(
        Vector2 currentPos,
        float angle,
        float distance,
        float orientation, // 🔥 New parameter
        out Tile matchedTile,
        out ObservationKey matchedKey,
        float posTolerance = 10f,
        float angleTolerance = 0.5f,
        float distanceTolerance = 10f,
        float orientationTolerance = 0.05f // 🔥 New tolerance
    )
    {
      foreach (var kvp in Map.ObservationLookup)
      {
        var key = kvp.Key;
        if (Vector2.Distance(currentPos, key.DevicePosition) < posTolerance &&
            Math.Abs(angle - key.Angle) < angleTolerance &&
            Math.Abs(distance - key.Distance) < distanceTolerance &&
            Math.Abs(orientation - key.Orientation) < orientationTolerance) // 🔥 New condition
        {
          matchedTile = kvp.Value;
          matchedKey = key;
          return true;
        }
      }

      matchedTile = null;
      matchedKey = default;
      return false;
    }
    public static bool TryFindMatchingTileFromWorld(
    Vector2 currentDevicePosition,
    float angle,
    float distance,
    float orientation, // Optional, for debug or fallback if needed
    GridManager gridManager,
    out Tile matchedTile,
    float maxDistanceToTileCenter = 10f
)
    {
      matchedTile = null;

      // Convert polar measurement to global coordinates
      Vector2 hitPoint = currentDevicePosition + Utility.PolarToCartesian(angle, distance);

      foreach (var tile in gridManager.GetAllTrustedTiles())
      {
        float distToTile = Vector2.Distance(tile.GlobalCenter, hitPoint);
        if (distToTile < maxDistanceToTileCenter)
        {
          matchedTile = tile;
          return true;
        }
      }

      return false;
    }




    private static float? EstimateRotationFromDistanceProfiles(
      List<DataPoint> newPoints,
      Dictionary<ObservationKey, Tile> lookup,
      float currentDeviceOrientation,
      float maxDistance = 3000f,
      float angleResolution = 1f, // degrees per bin
      float maxAngleShift = 30f // degrees
  )
    {
      int binCount = (int)(360f / angleResolution);
      float[] oldDistances = new float[binCount];
      float[] newDistances = new float[binCount];
      int[] oldCounts = new int[binCount];
      int[] newCounts = new int[binCount];

      // Fill oldDistances from matched observations
      foreach (var kvp in lookup)
      {
        var point = kvp.Key;
        if (point.Distance > maxDistance) continue;

        int bin = (int)(point.Angle / angleResolution) % binCount;
        oldDistances[bin] += point.Distance;
        oldCounts[bin]++;
      }

      // Fill newDistances from new scan
      foreach (var point in newPoints)
      {
        if (point.Distance > maxDistance) continue;

        int bin = (int)(point.Angle / angleResolution) % binCount;
        newDistances[bin] += point.Distance;
        newCounts[bin]++;
      }

      // Average the bins
      for (int i = 0; i < binCount; i++)
      {
        if (oldCounts[i] > 0) oldDistances[i] /= oldCounts[i];
        if (newCounts[i] > 0) newDistances[i] /= newCounts[i];
      }

      // Try shifts
      float minError = float.MaxValue;
      int bestShift = 0;
      int maxShift = (int)(maxAngleShift / angleResolution);

      for (int shift = -maxShift; shift <= maxShift; shift++)
      {
        float error = 0f;

        for (int i = 0; i < binCount; i++)
        {
          int j = (i + shift + binCount) % binCount;
          float d1 = oldDistances[i];
          float d2 = newDistances[j];

          if (d1 > 0 && d2 > 0) // valid bins
          {
            float diff = d1 - d2;
            error += diff * diff;
          }
        }

        if (error < minError)
        {
          minError = error;
          bestShift = shift;
        }
      }

      float estimatedRotationDegrees = -bestShift * angleResolution;
      float estimatedRotationRadians = MathHelper.ToRadians(estimatedRotationDegrees);

      //Debug.WriteLine($"🧭 [ProfileMatch] Best shift: {bestShift} bins → {estimatedRotationDegrees:0.00}° radians: {estimatedRotationRadians}");
      return estimatedRotationRadians;
    }
    private static float? EstimateRotationFromDistanceProfiles(
    List<DataPoint> newPoints,
    HashSet<Tile> trustedTiles,
    float currentDeviceOrientation,
    float maxDistance = 1000f,
    float angleResolution = 3f, // degrees per bin
    float maxAngleShift = 10f  // degrees
)
    {
      int binCount = (int)(360f / angleResolution);
      float[] oldDistances = new float[binCount];
      float[] newDistances = new float[binCount];
      int[] oldCounts = new int[binCount];
      int[] newCounts = new int[binCount];
      float estimatedRotationDegrees = 0;
      float estimatedRotationRadians = 0;
      // Fill oldDistances using trusted tiles
      if (trustedTiles.Count > 100)
      {
        foreach (var tile in trustedTiles)
        {

          if (tile._lastLIDARpoint.Distance > maxDistance) continue;

          int bin = (int)(tile._lastLIDARpoint.Angle / angleResolution) % binCount;
          oldDistances[bin] += tile._lastLIDARpoint.Distance;
          oldCounts[bin]++;

        }

        // Fill newDistances from incoming scan
        foreach (var point in newPoints)
        {
          if (point.Distance > maxDistance) continue;

          int bin = (int)(point.Angle / angleResolution) % binCount;
          newDistances[bin] += point.Distance;
          newCounts[bin]++;
        }

        // Average the bins
        for (int i = 0; i < binCount; i++)
        {
          if (oldCounts[i] > 0) oldDistances[i] /= oldCounts[i];
          if (newCounts[i] > 0) newDistances[i] /= newCounts[i];
        }

        // Try shifts
        float minError = float.MaxValue;
        int bestShift = 0;
        int maxShift = (int)(maxAngleShift / angleResolution);

        for (int shift = -maxShift; shift <= maxShift; shift++)
        {
          float error = 0f;

          for (int i = 0; i < binCount; i++)
          {
            int j = (i + shift + binCount) % binCount;
            float d1 = oldDistances[i];
            float d2 = newDistances[j];

            if (d1 > 0 && d2 > 0)
            {
              float diff = (d1 - d2) / ((d1 + d2) / 2f + 1f); // avoid divide-by-zero
              error += diff * diff;
              //float diff = d1 - d2;
              //error += diff * diff;
            }
          }

          if (error < minError)
          {
            minError = error;
            bestShift = shift;
          }
        }
        estimatedRotationDegrees = -bestShift * angleResolution;
        estimatedRotationRadians = MathHelper.ToRadians(estimatedRotationDegrees);
        Debug.WriteLine($"🧭 [ProfileMatch] Best shift: {bestShift} bins → {estimatedRotationDegrees:0.00}°");
      }



      return estimatedRotationRadians;
    }


  }
}
