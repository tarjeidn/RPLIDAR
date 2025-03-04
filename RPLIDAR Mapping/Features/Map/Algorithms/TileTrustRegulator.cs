using RPLIDAR_Mapping.Features.Map.GridModel;
using RPLIDAR_Mapping.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Features.Map.Algorithms
{
  public class TileTrustRegulator
  {
    private float _decayAdjustmentCooldown = 0;
    private const float HighTrustThreshold = 90;
    private const float MaxTrust = 100;
    private const float MinTrust = 0;

    private const float TrustThresholdBuffer = 5;  // 🟢 Prevents flickering
    private const float TrustSmoothingFactor = 0.1f;  // 🟢 Dampens sharp trust changes
    private const float DecaySmoothingFactor = 0.05f;  // 🟢 NEW: Gradually adjusts decay rate
    private const float MinStableDecayRate = 3f;  // 🟢 NEW: Ensures a stable minimum decay rate
    private float _smoothedDecayRate = 5f;  // 🟢 NEW: Stores a smoothed decay rate

    public void Update(float deltaTime)
    {
      var gridStats = StatisticsProvider.GridStats;
      int currentTileCount = gridStats.TotalHitTiles;
      float pointsPerSecond = StatisticsProvider.MapStats.PointsPerSecond;

      int highTrustTileHits = gridStats.HighTrustTileHits;
      int totalHighTrustTiles = gridStats.TotalHighTrustTiles;
      float highTrustTilePercentage = gridStats.HighTrustTilePercentage;

      int totalPointsAdded = gridStats.TotalPointsAddedLastBatch;
      int uniqueTilesHit = gridStats.UniqueTilesHitLastBatch;
      int batchSize = 45;

      //  Adjust every second
      _decayAdjustmentCooldown += deltaTime;
      if (_decayAdjustmentCooldown < 1f) return;
      _decayAdjustmentCooldown = 0;

      // 🟢 Adjust decay dynamically based on tile activity
      float decayTarget = Math.Max(
          MinStableDecayRate,  //  Ensures decay never becomes too aggressive
          TileRegulatorSettings.Default.BaseTileDecayRate *
          (1f + (totalHighTrustTiles / 2000f) + (currentTileCount / 3000f))
      );

      // 🟢 Smoothly adjust decay rate to avoid large fluctuations
      _smoothedDecayRate = (_smoothedDecayRate * (1f - DecaySmoothingFactor)) + (decayTarget * DecaySmoothingFactor);

      AppSettings.Default.TileDecayRate = Math.Clamp(
          (int)_smoothedDecayRate,
          1,
          TileRegulatorSettings.Default.MaxTileDecayRate
      );

      // 🎨 Adjust tile trust threshold dynamically (prevent flickering)
      float targetThreshold = TileRegulatorSettings.Default.BaseTileTrustThreshold * Math.Clamp(1f + highTrustTilePercentage, 1f, 2f);

      // 🟢 **Smooth the threshold change** to avoid rapid oscillations
      AppSettings.Default.TileTrustThreshold = Math.Clamp(
          (int)((AppSettings.Default.TileTrustThreshold * (1f - TrustSmoothingFactor)) + (targetThreshold * TrustSmoothingFactor)),
          TileRegulatorSettings.Default.MinTileTrustThreshold,
          TileRegulatorSettings.Default.MaxTileTrustThreshold
      );
    }
  }








  //public void Update(float deltaTime)
  //{
  //  int currentTileCount = StatisticsProvider.GridStats.TotalHitTiles;
  //  float pointsPerSecond = StatisticsProvider.MapStats.PointsPerSecond;

  //  //  Adjust decay frequency and trust values dynamically every second
  //  _decayAdjustmentCooldown += deltaTime;
  //  if (_decayAdjustmentCooldown < 1f) return;
  //  _decayAdjustmentCooldown = 0;

  //  // 📉 Adjust trust increment per hit (less trust added if too many new points)
  //  AppSettings.Default.TileTrustIncrement = Math.Clamp(
  //      (int)(TileRegulatorSettings.Default.BaseTileTrustIncrement * (1f - pointsPerSecond / 500f)),
  //      1,
  //      TileRegulatorSettings.Default.MaxTileTrustIncrement
  //  );

  //  // 📉 Adjust decay rate dynamically based on tile count (more tiles → faster decay)
  //  AppSettings.Default.TileDecayRate = Math.Clamp(
  //      (int)(TileRegulatorSettings.Default.BaseTileDecayRate * (1f + currentTileCount / 1000f)),
  //      1,
  //      TileRegulatorSettings.Default.MaxTileDecayRate
  //  );

  //  // 📉 Adjust trust decay per step (higher if too many tiles)
  //  AppSettings.Default.TileTrustDecrement = Math.Clamp(
  //      (int)(TileRegulatorSettings.Default.BaseTileTrustDecrement * (1f + currentTileCount / 500f)),
  //      1,
  //      TileRegulatorSettings.Default.MaxTileTrustDecrement
  //  );

  //  // 🎨 Adjust tile drawing threshold dynamically (avoid clutter)
  //  AppSettings.Default.TileTrustThreshold = Math.Clamp(
  //      (int)(TileRegulatorSettings.Default.BaseTileTrustThreshold * (1f + currentTileCount / 2000f)),
  //      TileRegulatorSettings.Default.MinTileTrustThreshold,
  //      TileRegulatorSettings.Default.MaxTileTrustThreshold
  //  );
  //}
}


