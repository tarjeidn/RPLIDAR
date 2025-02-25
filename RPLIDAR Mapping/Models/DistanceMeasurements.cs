using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Models
{
  internal class DistanceMeasurements
  {
    
    private List<float> measurements;

    public DistanceMeasurements()
    {
      measurements = new List<float>();
    }

    public void AddMeasurement(float measurement)
    {
      measurements.Add(measurement);
    }

    public float GetAverage()
    {
      if (measurements.Count == 0) return 0;
      return measurements.Average();
    }

    public List<float> GetMeasurements()
    {
      return new List<float>(measurements);
    }
  }
}

