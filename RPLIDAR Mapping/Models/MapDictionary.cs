using SharpDX.Direct3D11;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Models
{
  internal class MapDictionary
  {
    Dictionary<int, Dictionary<int, DistanceMeasurements>> _mainDict = new Dictionary<int, Dictionary<int, DistanceMeasurements>>();

    public MapDictionary()
    {
      //Create();
    }

    // Populate the dictionary for all degrees (0-360) and decimals (0-9)
    private void Create()
    {
      for (int degree = 0; degree <= 360; degree++)
      {
        Dictionary<int, DistanceMeasurements> innerDict = new Dictionary<int, DistanceMeasurements>();
        for (int decimalPart = 0; decimalPart <= 9; decimalPart++)
        {
          innerDict[decimalPart] = new DistanceMeasurements();
        }
        _mainDict[degree] = innerDict;
      }    
    }
    public void AddMeasurement(int degree, int decimalPart, float measurement)
    {
      if (_mainDict.ContainsKey(degree) && _mainDict[degree].ContainsKey(decimalPart))
      {
        _mainDict[degree][decimalPart].AddMeasurement(measurement);
      }
    }
    public float GetAverageForBin(int degree, int decimalPart)
    {
      if (_mainDict.ContainsKey(degree) && _mainDict[degree].ContainsKey(decimalPart))
      {
        return _mainDict[degree][decimalPart].GetAverage();
      }
      return 0; // Default if no data exists
    }
  }
}
