using RPLIDAR_Mapping.Interfaces;
using System;
using System.Text.Json;

public class LidarSettings
{
  public int BatchSize { get; set; } = 30;
  public int ScanSpeed { get; set; } = 10;
  public int QualityThreshold { get; set; } = 5;

  public string ToJson()
  {
    return JsonSerializer.Serialize(this);
  }
}
