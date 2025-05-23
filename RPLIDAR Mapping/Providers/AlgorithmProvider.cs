﻿using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using RPLIDAR_Mapping.Features.Map.Algorithms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Providers
{
  public static class AlgorithmProvider
  {

    public static TileTrustRegulator TileTrustRegulator { get; private set; }
    public static TileMerge TileMerge { get; private set; }
    public static ICP ICP { get; private set; }
    public static VirtualTileScanMatcher ScanMatcher = new VirtualTileScanMatcher(); 
    public static void Initialize()
    {
      TileTrustRegulator = new TileTrustRegulator();
      TileMerge = new TileMerge();

      ICP = new ICP();
    }
  }
}
