using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using RPLIDAR_Mapping.Features.Communications;
using RPLIDAR_Mapping.Features.Map.GridModel;
using RPLIDAR_Mapping.Models;
using RPLIDAR_Mapping.Providers;
using SharpDX.MediaFoundation;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Utilities
{
  public static class Utility
  {
    public static bool IsUtf8String(byte[] payload, out string message)
    {
      try
      {
        // Ensure payload isn't empty or null
        if (payload == null || payload.Length == 0)
        {
          message = null;
          return false;
        }

        // Ensure the payload doesn't contain raw binary data
        if (payload.Any(b => b < 32 && b != 9 && b != 10 && b != 13)) // Exclude Tab/NewLine/Carriage Return
        {
          message = null;
          return false;
        }

        message = Encoding.UTF8.GetString(payload);
        //Log(message);

        ////  Ensure the message starts and ends like JSON
        //if (!(message.StartsWith("[") && message.EndsWith("]")) &&
        //    !(message.StartsWith("{") && message.EndsWith("}")))
        //{
        //  return false;
        //}

        return true;
      }
      catch
      {
        message = null;
        return false;
      }
    }
    public static Vector2 PolarToCartesian(float angleDegrees, float distance)
    {
      float angleRadians = MathHelper.ToRadians(angleDegrees);
      float x = distance * (float)Math.Cos(angleRadians);
      float y = distance * (float)Math.Sin(angleRadians);
      return new Vector2(x, y);
    }

    public static bool LineSegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, out Vector2 intersection)
    {
      intersection = Vector2.Zero;

      float s1_x = p2.X - p1.X;
      float s1_y = p2.Y - p1.Y;
      float s2_x = p4.X - p3.X;
      float s2_y = p4.Y - p3.Y;

      float denominator = (-s2_x * s1_y + s1_x * s2_y);

      if (denominator == 0)
        return false;  // Parallel lines

      float s = (-s1_y * (p1.X - p3.X) + s1_x * (p1.Y - p3.Y)) / denominator;
      float t = (s2_x * (p1.Y - p3.Y) - s2_y * (p1.X - p3.X)) / denominator;

      if (s >= 0 && s <= 1 && t >= 0 && t <= 1)
      {
        // Intersection detected
        intersection = new Vector2(p1.X + (t * s1_x), p1.Y + (t * s1_y));
        return true;
      }

      return false; // No intersection
    }
    //public static void ProcessIMUVelocityPayload(byte[] payload)
    //{
    //  if (UtilityProvider.Device == null) return;
    //  if (payload.Length < 12) return;

    //  uint timestamp = BitConverter.ToUInt32(payload, 0);
    //  float vx = BitConverter.ToSingle(payload, 4);
    //  float vy = BitConverter.ToSingle(payload, 8);

    //  Vector2 velocity = new Vector2(vx , vy);

    //  Debug.WriteLine($"📦 Velocity Packet - Time: {timestamp}, Velocity: {velocity}");
      
    //  UtilityProvider.Device.UpdateDevicePosition(velocity, timestamp);
    //}

    //public static void ProcessLidarBatchBinary(byte[] payload, ConcurrentQueue<DataPoint> queue)
    //{
    //  int pointSize = 11;  // 2 angle + 2 distance + 1 quality + 2 yaw + 2 ax + 2 ay + 2 az
    //  int numPoints = payload.Length / pointSize;

    //  if (payload.Length % pointSize != 0)
    //  {
    //    Debug.WriteLine("⚠ Payload size mismatch!");
    //    return;
    //  }

    //  queue.Clear();

    //  for (int i = 0; i < numPoints; i++)
    //  {
    //    int index = i * pointSize;

    //    float angle = BitConverter.ToUInt16(payload, index + 0) / 100f;
    //    ushort distance = BitConverter.ToUInt16(payload, index + 2);
    //    byte quality = payload[index + 4];
    //    float yaw = BitConverter.ToUInt16(payload, index + 5) / 100f;

    //    //float vxRaw = BitConverter.ToSingle(payload, index + 7);
    //    //float vyRaw = BitConverter.ToSingle(payload, index + 11);

    //    uint timestamp = BitConverter.ToUInt32(payload, index + 7);

    //    // Position calculation
    //    float angleRad = MathHelper.ToRadians(angle) + MathHelper.ToRadians(yaw);
    //    Vector2 offset = new(MathF.Cos(angleRad) * distance, MathF.Sin(angleRad) * distance);

    //    Device device = UtilityProvider.Device;
    //    Vector2 globalPos = device._devicePosition + offset;

    //    int tileSize = 10;
    //    //Tile tile = UtilityProvider.Map._gridManager.GetTileAtGlobalCoordinates(globalPos.X, globalPos.Y);

    //    Vector2 tilePos = new(MathF.Floor(globalPos.X / tileSize) * tileSize, MathF.Floor(globalPos.Y / tileSize) * tileSize);
    //    Vector2 tileCenter = tilePos + new Vector2(tileSize / 2f, tileSize / 2f);

    //    queue.Enqueue(new DataPoint(angle, distance, quality, globalPos, tilePos, tileCenter, yaw, 0, 0, timestamp,false));
    //  }
    //}
    public static void ProcessLidarBatchBinary(byte[] payload, ConcurrentQueue<DataPoint> queue)
    {
      int pointSize = 11;
      int numPoints = payload.Length / pointSize;
      if (payload.Length % pointSize != 0) return;

      queue.Clear();
      List<DataPoint> rawPoints = new(numPoints);
      Device device = UtilityProvider.Device;
      int tileSize = 10;

      // Parse points first
      for (int i = 0; i < numPoints; i++)
      {
        int index = i * pointSize;
        float angle = BitConverter.ToUInt16(payload, index + 0) / 100f;
        ushort distance = BitConverter.ToUInt16(payload, index + 2);
        byte quality = payload[index + 4];
        float yaw = BitConverter.ToUInt16(payload, index + 5) / 100f;
        uint timestamp = BitConverter.ToUInt32(payload, index + 7);

        float angleRad = MathHelper.ToRadians(angle) + MathHelper.ToRadians(yaw);
        Vector2 offset = new(MathF.Cos(angleRad) * distance, MathF.Sin(angleRad) * distance);
        Vector2 globalPos = device._devicePosition + offset;

        Vector2 tilePos = new(MathF.Floor(globalPos.X / tileSize) * tileSize, MathF.Floor(globalPos.Y / tileSize) * tileSize);
        Vector2 tileCenter = tilePos + new Vector2(tileSize / 2f, tileSize / 2f);
        DataPoint newPoint = new DataPoint(angle, distance, quality, globalPos, tilePos, tileCenter, yaw, 0, 0, timestamp, false);
        newPoint.DevicePositionAtHit = device._devicePosition;
        rawPoints.Add(newPoint);
      }

      // Sort by angle for efficient sliding window
      rawPoints.Sort((a, b) => a.Angle.CompareTo(b.Angle));

      float angleWindow = 5f;
      float distanceThresholdSq = 100 * 100;

      int start = 0;
      for (int i = 0; i < rawPoints.Count; i++)
      {
        var point = rawPoints[i];
        int neighborCount = 0;

        // Move start index to the beginning of the angle window
        while (start < rawPoints.Count && rawPoints[start].Angle < point.Angle - angleWindow)
          start++;

        // Check all points in [start, i) and (i, end] range
        for (int j = start; j < rawPoints.Count && rawPoints[j].Angle <= point.Angle + angleWindow; j++)
        {
          if (j == i) continue;
          float distSq = Vector2.DistanceSquared(point.GlobalPosition, rawPoints[j].GlobalPosition);
          if (distSq <= distanceThresholdSq)
          {
            neighborCount++;
            if (neighborCount >= 2) break; // Early accept
          }
        }

        if (neighborCount >= 1)
          queue.Enqueue(point);
      }
    }
    public static Tile CreateSimulatedTile(Vector2 globalCenter, MapPoint point)
    {
      var dummyGrid = DummyGridFactory.GetDummyGrid();

      var tile = new Tile(0, 0, dummyGrid)
      {
        GlobalCenter = globalCenter,
        WorldGlobalPosition = globalCenter - new Vector2(5f, 5f), // 10mm tile
        WorldRect = new Rectangle((int)(globalCenter.X - 5), (int)(globalCenter.Y - 5), 10, 10),
        _lastLIDARpoint = point
      };

      return tile;
    }


    public static float NormalizeAngle(float radians)
    {
      while (radians > MathF.PI) radians -= MathF.Tau;
      while (radians < -MathF.PI) radians += MathF.Tau;
      return radians;
    }


    public static int Modulo(int value, int mod) => ((value % mod) + mod) % mod;







  }
  public static class DummyGridFactory
  {
    private static Grid _dummyGrid;

    public static Grid GetDummyGrid()
    {
      if (_dummyGrid != null) return _dummyGrid;

      var gm = DummyGridManager.Instance;
      _dummyGrid = new Grid(gm, 0, 0);
      return _dummyGrid;
    }
  }
  public class DummyGridManager : GridManager
  {
    private static DummyGridManager _instance;
    public static DummyGridManager Instance => _instance ??= new DummyGridManager();

    private DummyGridManager() : base(null)
    {
      GridStats = null;
    }
  }

  public class FPSCounter
  {
    private int _frameCount;
    private double _elapsedTime;
    private int _fps;

    private int _pointsHandledPerSecond; // Track points per second
    private int _pointsAccumulated; // Store points in the current second

    private int _lidarUpdateCount; //  Track LiDAR updates per second

    public int FPS => _fps;
    public int PointsPerSecond => _pointsHandledPerSecond;
    public int LiDARUpdatesPerSecond => _lidarUpdateCount;

    public void Update(GameTime gameTime)
    {
      _elapsedTime += gameTime.ElapsedGameTime.TotalSeconds;
      _frameCount++;
      _pointsAccumulated += StatisticsProvider.MapStats.TotalPointsHandledThisFrame;

      if (_elapsedTime >= 1.0) //  Update stats every second
      {
        _fps = _frameCount;
        StatisticsProvider.MapStats.FPS = _fps;
        StatisticsProvider.MapStats.PointsPerSecond = _pointsAccumulated;
        StatisticsProvider.MapStats.LiDARUpdatesPerSecond = _lidarUpdateCount; //  Store LiDAR updates count

        //  Reset counters
        _frameCount = 0;
        _elapsedTime = 0;
        _pointsAccumulated = 0;
        _lidarUpdateCount = 0; //  Reset LiDAR update counter
      }
    }
    public void IncrementLiDARUpdate()
    {
      _lidarUpdateCount++;
    }
  }

 }
