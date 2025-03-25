using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using RPLIDAR_Mapping.Features.Communications;
using RPLIDAR_Mapping.Models;
using RPLIDAR_Mapping.Providers;
using SharpDX.MediaFoundation;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
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

    public static void ProcessLidarBatchJson(string jsonData, ConcurrentQueue<DataPoint> queue)
    {
      try
      {
        List<string> points = JsonConvert.DeserializeObject<List<string>>(jsonData);

        

        List<DataPoint> lidarPoints = new List<DataPoint>();

        foreach (string point in points)
        {
          string[] values = point.Split(',');

          if (values.Length == 3)
          {
            float angle = float.Parse(values[0], CultureInfo.InvariantCulture);
            float distance = float.Parse(values[1], CultureInfo.InvariantCulture);
            byte quality = byte.Parse(values[2]);

            Device device = UtilityProvider.Device;
            float angleRad = MathHelper.ToRadians(angle) + device._deviceOrientation;
            int tilesize = 10;

            Vector2 offset = new Vector2(
                MathF.Cos(angleRad) * distance,
                MathF.Sin(angleRad) * distance
            );
            Vector2 globalPos = device._devicePosition + offset;

            // Correct tile position calculation
            Vector2 eqTilePosition = new Vector2(
                MathF.Floor(globalPos.X / tilesize) * tilesize,
                MathF.Floor(globalPos.Y / tilesize) * tilesize
            );
            Vector2 eqTileGlobalCenter = eqTilePosition + new Vector2(tilesize / 2f, tilesize / 2f);

            // Enqueue corrected datapoint
            queue.Enqueue(new DataPoint(angle, distance, quality, globalPos, eqTilePosition, eqTileGlobalCenter));
          }
        }


      }
      catch (Exception ex)
      {
        Debug.WriteLine($"Error parsing JSON LiDAR data: {ex.Message}");
      }
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
    public static void ProcessLidarBatchBinary(byte[] payload, ConcurrentQueue<DataPoint> queue)
    {
      int pointSize = 5;  // Each point is exactly 5 bytes (2 for angle, 2 for distance, 1 for quality)
      int numPoints = payload.Length / pointSize;

      if (payload.Length % pointSize != 0)
      {
        Debug.WriteLine("Error: Payload size is not a multiple of 5 bytes!");
        return;
      }

      //  Clear queue before adding new data
      while (queue.TryDequeue(out _)) { }  // 🚀 Empty the queue

      for (int i = 0; i < numPoints; i++)
      {
        int startIndex = i * pointSize;

        // Read 2 bytes for angle (int16)
        ushort angleRaw = BitConverter.ToUInt16(payload, startIndex);
        float angle = angleRaw / 100.0f; //  Convert back to float (2 decimal places)

        // Read 2 bytes for distance (uint16)
        ushort distance = BitConverter.ToUInt16(payload, startIndex + 2);

        // Read 1 byte for quality
        byte quality = payload[startIndex + 4];
        //Debug.WriteLine($"angled: {angle} distance: {distance} quality: {quality} ");
        // Enqueue parsed DataPoint into the queue
        // Calculate global position
        Device device = UtilityProvider.Device;
        float angleRad = MathHelper.ToRadians(angle) + device._deviceOrientation;
        int tilesize = 10;

        Vector2 offset = new Vector2(
            MathF.Cos(angleRad) * distance,
            MathF.Sin(angleRad) * distance
        );
        Vector2 globalPos = device._devicePosition + offset;

        // Correct tile position calculation
        Vector2 eqTilePosition = new Vector2(
            MathF.Floor(globalPos.X / tilesize) * tilesize,
            MathF.Floor(globalPos.Y / tilesize) * tilesize
        );
        Vector2 eqTileGlobalCenter = eqTilePosition + new Vector2(tilesize / 2f, tilesize / 2f);

        // Enqueue corrected datapoint
        queue.Enqueue(new DataPoint(angle, distance, quality, globalPos, eqTilePosition, eqTileGlobalCenter));
      }

      
    }
    public static int Modulo(int value, int mod) => ((value % mod) + mod) % mod;







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
