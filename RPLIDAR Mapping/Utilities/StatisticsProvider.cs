using Microsoft.Xna.Framework.Graphics;
using RPLIDAR_Mapping.Features.Map.Statistics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Utilities
{
  public static class StatisticsProvider
  {
    public static GridStats GridStats { get; private set; }
    public static MapStats MapStats { get; private set; }

    public static void Initialize(GridStats gs, MapStats ms)
    {
      GridStats = gs;
      MapStats = ms;
    }
  }
}
