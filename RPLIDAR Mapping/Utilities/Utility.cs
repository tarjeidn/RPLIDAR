using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using RPLIDAR_Mapping.Models;
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

            queue.Enqueue(new DataPoint(angle, distance, quality));
          }
        }


      }
      catch (Exception ex)
      {
        Debug.WriteLine($"Error parsing JSON LiDAR data: {ex.Message}");
      }
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
        queue.Enqueue(new DataPoint(angle, distance, quality));
      }

      
    }







  }
  public class FPSCounter
  {
    private int _frameCount;
    private double _elapsedTime;
    private int _fps;

    private int _pointsHandledPerSecond; //  Track points per second
    private int _pointsAccumulated; //  Store points in the current second

    public int FPS => _fps; //  Get the latest FPS value
    public int PointsPerSecond => _pointsHandledPerSecond;

    public void Update(GameTime gameTime)
    {
      _elapsedTime += gameTime.ElapsedGameTime.TotalSeconds;
      _frameCount++;
      _pointsAccumulated += StatisticsProvider.MapStats.TotalPointsHandledThisFrame;

      if (_elapsedTime >= 1.0) //  Update FPS every second
      {
        _fps = _frameCount;
        StatisticsProvider.MapStats.FPS = _fps;
        StatisticsProvider.MapStats.PointsPerSecond = _pointsAccumulated;
        _frameCount = 0;
        _elapsedTime = 0;
        _pointsAccumulated = 0;
      }
    }
  }

}
