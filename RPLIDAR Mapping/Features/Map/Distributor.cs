using RPLIDAR_Mapping.Features.Map.GridModel;
using RPLIDAR_Mapping.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Features.Map
{
  public class Distributor
  {
    public GridManager _GridManager { get; set; }
    public Distributor() 
    { 
      _GridManager = new GridManager();
    }
    public void Update()
    {
      _GridManager.Update();
    }
    public void Distribute(MapPoint point)
    {
      DistributeToGrid(point);
    }
    private void DistributeToGrid(MapPoint point) 
    {
      _GridManager.MapPointToGrid(point);
    }

  }
}
