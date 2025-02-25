using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Models
{
  public class DataPoint
  {
    public float Angle {  get; set; }
    public float Distance { get; set; }
    public byte Quality { get; set; }
    public DataPoint(float angle, float distance, byte quality)
    {
      Angle = angle;
      Distance = distance;
      Quality = quality;
    }
  }
}
