using Microsoft.Xna.Framework.Graphics;
using RPLIDAR_Mapping.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using RPLIDAR_Mapping.Models;
using System.Diagnostics;
using RPLIDAR_Mapping.Features.Map.Statistics;



namespace RPLIDAR_Mapping.Features.Map.GridModel
{
  public class Grid
  {
    public int GridX { get; }
    public int GridY { get; }
    public Vector2 GridPosition { get; private set; }
    public readonly int tileSize;
    public readonly float GridScaleMmToPixels;
    public int _numberOfTiles;
    public int GridSizePixels;
    public readonly float _GridSizeM;
    private readonly Dictionary<(int, int), Tile> tiles;
    public readonly RenderTarget2D gridLinesCanvas;
    public readonly RenderTarget2D tilesCanvas;
    private readonly SpriteBatch _SpriteBatch;
    private readonly GraphicsDevice _GraphicsDevice;

    private readonly Texture2D GridLineTexture;
    public Dictionary<(int, int), Tile> _updatedTiles;
    public List<Tile> _trustedTiles;
    public GridManager GridManager;
    public GridStats Stats;

    public Grid(GridManager gm)
    {
      GridManager = gm;
      Stats = gm.GridStats;
      //int numberOfTiles = (int)((AppSettings.Default.GridSizeM * 100) / AppSettings.Default.GridTileSizeCM);
      GridSizePixels = AppSettings.Default.GridPixels;
      _GridSizeM = AppSettings.Default.GridSizeM;
      // tileseize in pixels calculated from settings
      tileSize = (int)(GridSizePixels * AppSettings.Default.GridTileSizeCM)/ (int)(_GridSizeM*100);
      _numberOfTiles = GridSizePixels / (tileSize);

      GridScaleMmToPixels = ((int)AppSettings.Default.GridSizeM*1000)/AppSettings.Default.GridPixels;
      //_numberOfTiles = (int)(GridSizePixels / (GridScaleCmToPixels * tileSize));
      _SpriteBatch = GraphicsDeviceProvider.SpriteBatch;
      _GraphicsDevice = GraphicsDeviceProvider.GraphicsDevice;
      //GridSizePixels = (numberOfTiles * AppSettings.Default.GridScaleCMtoPixels);
      GridX = _numberOfTiles;
      GridY = _numberOfTiles;
      _updatedTiles = new Dictionary<(int, int), Tile>();
      _trustedTiles = new List<Tile>();
      tiles = new Dictionary<(int, int), Tile>();
      // Use the global GraphicsDevice to create RenderTarget2D
      gridLinesCanvas = new RenderTarget2D(GraphicsDeviceProvider.GraphicsDevice, GridSizePixels, GridSizePixels);
      tilesCanvas = new RenderTarget2D(GraphicsDeviceProvider.GraphicsDevice, GridSizePixels, GridSizePixels);
      GridPosition = new Vector2(GridX * GridSizePixels, GridY * GridSizePixels);
      //GenerateGrid();
      //DrawGridLines();

    }
    private void GenerateGrid()
    {
      for (int x = 0; x < GridX; x++)
      {
        for (int y = 0; y < GridY; y++)
        {
          tiles[(x, y)] = new Tile(x, y, this);
        }
      }
    }
    private (int, int) GetTileCoordinates(float x, float y)
    {
      int tileX = (int)Math.Floor(x / tileSize);
      int tileY = (int)Math.Floor(y / tileSize);
      return (tileX, tileY);
    }
    public void Update()
    {
      //_updatedTiles.Clear();
    }
    public void DecayTiles()
    {
      // List to store keys for tiles that should be removed
      List<(int, int)> keysToRemove = new();

      // Decay each updated tile and adjust the global hit count
      foreach (var kvp in _updatedTiles)
      {
        Tile tile = kvp.Value;

        // Only decay if the tile has a positive hit count
        if (tile.pointsHitAtThisTile > 0)
        {
          tile.pointsHitAtThisTile--;
          Stats.totalHitCount--;
        }

        // Mark the tile for removal if its hit count is now zero or less
        if (tile.pointsHitAtThisTile <= 0)
        {
          keysToRemove.Add(kvp.Key);
        }
      }

      // Remove tiles that have decayed completely and update total hit tiles count
      foreach (var key in keysToRemove)
      {
        _updatedTiles.Remove(key);
        Stats.totalHitTiles--;
      }

      // Recalculate the average hit count (if any tiles remain)
      if (Stats.totalHitTiles > 0)
      {
        Stats.averageTileHitCount = (float)Stats.totalHitCount / Stats.totalHitTiles;
      }
      else
      {
        Stats.averageTileHitCount = 0;
      }

      // Recalculate high-intensity statistics:
      // A tile is considered high intensity if its hit count is above the average.
      int highTotalHitCount = 0;
      int highTotalHitTiles = 0;
      foreach (var tile in _updatedTiles.Values)
      {
        if (tile.pointsHitAtThisTile > Stats.averageTileHitCount)
        {
          highTotalHitCount += tile.pointsHitAtThisTile;
          highTotalHitTiles++;
        }
      }
      Stats.highTotalHitCount = highTotalHitCount;
      Stats.highTotalHitTiles = highTotalHitTiles;
      Stats.highAverageTileHitCount = highTotalHitTiles > 0 ? (float)highTotalHitCount / highTotalHitTiles : 0;
    }

    public void AddPoint(int X, int Y, MapPoint point)
    {


      //  Get the tile at (X, Y)
      Tile tile = GetTileAt(X, Y);
      //Debug.WriteLine($"🎯 Final Tile Position → Grid ({tile._selfGrid.GridX},{tile._selfGrid.GridY}), Tile ({X},{Y})");
      //  Check if this is the first time the tile is being hit
      bool isFirstHit = (tile.pointsHitAtThisTile == 0);

      //  Increase hit count
      if (tile.pointsHitAtThisTile <= Math.Ceiling(Stats.averageTileHitCount * AppSettings.Default.MaxHighintensityFactor))
      {
        tile.pointsHitAtThisTile++;
      }


      // Update highest tile hit count
      if (tile.pointsHitAtThisTile >= Stats.higestTileHitCount)
      {
        tile.IsTrusted = true;
        _trustedTiles.Add(tile);
        GridManager._globalTrustedMapPoints.Add(tile._representativeMapPoint);
        Stats.higestTileHitCount = tile.pointsHitAtThisTile;
        //Debug.WriteLine($"{X}, {Y}");
      }

      // Only update stats if it's the first time this tile is hit
      if (isFirstHit)
      {
        Stats.totalHitTiles++;
      }

      // Update total hit count
      Stats.totalHitCount++;

      // Recalculate the average hit count
      Stats.averageTileHitCount = (float)Stats.totalHitCount / Stats.totalHitTiles;

      // Track high-intensity hit tiles (above average)
      if (Stats.averageTileHitCount > 0 
        && tile.pointsHitAtThisTile >= Stats.averageTileHitCount 
        && tile.pointsHitAtThisTile <= Math.Ceiling( Stats.averageTileHitCount*AppSettings.Default.MaxHighintensityFactor))
      {

        Stats.highTotalHitCount++;
        // Update total high-hit tiles only if first time above average
        if (tile.pointsHitAtThisTile == (int)Math.Ceiling(Stats.averageTileHitCount) + 1)        {
          Stats.highTotalHitTiles++;
        }
        // Update the high average hit count
        Stats.highAverageTileHitCount = (float)Stats.highTotalHitCount / Stats.highTotalHitTiles;
      }

      // Track updated tile for drawing
      if (!_updatedTiles.ContainsKey((X, Y)))
      {
        _updatedTiles[(X, Y)] = tile;
      }
    }




    public Tile GetTileAt(int x, int y)
    {
      if (tiles.ContainsKey((x, y)))
      {
        return tiles[(x, y)];
      }
      tiles[(x, y)] = new Tile(x, y, this);
      return tiles[(x, y)];
    }
    public IEnumerable<Tile> GetDrawnTiles()
    {
      foreach (var tile in tiles.Values)
      {
        if (tile.IsDrawn)
          yield return tile;
      }
    }
    private void DrawGridLines()
    {
      var font = ContentManagerProvider.GetFont("DebugFont");

      _GraphicsDevice.SetRenderTarget(gridLinesCanvas);
      _GraphicsDevice.Clear(Color.Blue);

      _SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

      foreach (var tile in tiles.Values)
        tile.DrawGrid();
      _SpriteBatch.DrawString(font, "gridCanvas", Vector2.Zero, Color.White);

      _SpriteBatch.End();
      _GraphicsDevice.SetRenderTarget(null);

    }

    public void DrawTiles()
    {
      //var font = ContentManagerProvider.GetFont("DebugFont");

      //  Preserve previous render target content
      _GraphicsDevice.SetRenderTarget(tilesCanvas);
      _GraphicsDevice.Clear(Color.Transparent);

      _SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

      foreach (Tile tile in _updatedTiles.Values)
      {
        if (tile.pointsHitAtThisTile >= Stats.averageTileHitCount)
        {
          tile.Draw(Stats.higestTileHitCount);  // Draws new points while keeping previous ones
        }
      }

      _SpriteBatch.End();

      // remove only already drawn tiles
      //_updatedTiles.Clear();

      //Reset render target back to default
      _GraphicsDevice.SetRenderTarget(null);
    }


  }
}
