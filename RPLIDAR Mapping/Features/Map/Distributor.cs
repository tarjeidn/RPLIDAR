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
    public Map _map { get; set; }
    public Distributor(Map map) 
    { 
      _GridManager = new GridManager(map);
    }
    public void Update()
    {
      _GridManager.Update();
    }
    public void Distribute(List<MapPoint> points)
    {
      DistributeToGrid(points);
    }
    private void DistributeToGrid(List<MapPoint> points) 
    {
      _GridManager.MapPointToGrid(points);
    }

  }
}
