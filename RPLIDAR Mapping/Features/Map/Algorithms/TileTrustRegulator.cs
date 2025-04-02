using RPLIDAR_Mapping.Features.Map.GridModel;
using RPLIDAR_Mapping.Providers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Features.Map.Algorithms
{
  public class TileTrustRegulator
  {
    private float _decayAdjustmentCooldown = 0;

    // PID Constants (Tune These)
    private float Kp = 0.5f;  // Proportional gain (direct correction)
    private float Ki = 0.1f;  // Integral gain (accumulates correction)
    private float Kd = 0.2f;  // Derivative gain (smooths oscillations)

    private float _integral = 0;
    private float _previousError = 0;

    private const float MinStableDecayRate = 3f;
    private float _smoothedDecayRate = 5f;

    private float _integralDecay = 0;
    private float _previousErrorDecay = 0;

    private float _integralTrust = 0;
    private float _previousErrorTrust = 0;

    public bool RegulatorEnabled = false;
    public float TrustIncrement = 50;
    public float TrustDecrement = 18;

    public int TileDecayRate = 10;
    public int MaxTileDecayRate = 300;

    public int DecayFrequency = 4;
    public int TrustThreshold = 30;
    public float TileTrustTreshHold = 50;

    public int MinTileTrustThreshold = 10;
    public int MaxTileTrustThreshold = 100;
    public int BaseTileTrustThreshold = 50;

    private float TimeSinceLastUpdate = 0;

    public void Update(float deltaTime)
    {
      var gridStats = StatisticsProvider.GridStats;
      TimeSinceLastUpdate += deltaTime;

      if (gridStats.HighTrustTilesLostLastCycle > 0)
      {
 
        int highTrustTileLoss = gridStats.HighTrustTilesLostLastCycle; // 🔹 Track lost trusted tiles per decay cycle
        int totalHighTrustTiles = gridStats.TotalHighTrustTiles;
        float highTrustTilePercentage = gridStats.HighTrustTilePercentage;
        // Target: Maintain a stable number of high-trust tiles lost per cycle
        float targetTrustLoss = totalHighTrustTiles * 0.02f;  // 🔹 Aim for 2% loss per cycle
        float errorDecay = targetTrustLoss - highTrustTileLoss;

        // 🔹 Compute Decay Rate Adjustment Using PID
        _integralDecay += errorDecay * TimeSinceLastUpdate;
        float derivativeDecay = (errorDecay - _previousErrorDecay) / TimeSinceLastUpdate;
        _previousErrorDecay = errorDecay;

        float decayAdjustment = (Kp * errorDecay) + (Ki * _integralDecay) + (Kd * derivativeDecay);
        _smoothedDecayRate = Math.Clamp(_smoothedDecayRate + decayAdjustment, MinStableDecayRate, MaxTileDecayRate);

        TileDecayRate = (int)_smoothedDecayRate;

        // 🔹 Compute Trust Adjustment Using PID
        float targetHighTrustTiles = totalHighTrustTiles * 0.8f;  // 🔹 Target: Maintain 80% of previously trusted tiles
        float errorTrust = targetHighTrustTiles - totalHighTrustTiles;

        _integralTrust += errorTrust * TimeSinceLastUpdate;
        float derivativeTrust = (errorTrust - _previousErrorTrust) / TimeSinceLastUpdate;
        _previousErrorTrust = errorTrust;

        float trustAdjustment = (Kp * errorTrust) + (Ki * _integralTrust) + (Kd * derivativeTrust);

        // 🔹 Adjust Trust Increment and Decrement
        TrustIncrement = Math.Clamp(TrustIncrement + trustAdjustment, 10, 100);  // Prevent extreme trust jumps
        TrustDecrement = Math.Clamp(TrustDecrement - trustAdjustment * 0.5f, 1, 20);  // Smoother decrement control

        // 🎨 Adjust trust threshold dynamically
        float targetThreshold = BaseTileTrustThreshold * Math.Clamp(1f + highTrustTilePercentage, 1f, 2f);

        // 🔹 Smooth trust threshold changes
        TileTrustTreshHold = Math.Clamp(
            (int)((TileTrustTreshHold * 0.9f) + (targetThreshold * 0.1f)),  // 🔹 Soft smoothing factor
            MinTileTrustThreshold,
            MaxTileTrustThreshold
        );
      }
      TimeSinceLastUpdate = 0;
    }
  }

 


}


