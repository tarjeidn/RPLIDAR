using RPLIDAR_Mapping.Features.Map.Algorithms;
using RPLIDAR_Mapping.Features.Map.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Providers
{
  internal class GUIProvider
  {
    public static UserSelection UserSelection { get; private set; }
    public static void Initialize()
    {
      UserSelection = new UserSelection();
    }
  }
}
