using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using RPLIDAR_Mapping.Features.Map.GridModel;
using RPLIDAR_Mapping.Models;

/// <summary>
/// Holds a 2D rotation (2x2 matrix) and translation (2x1 vector).
/// </summary>
public class Transformation
{
  public Matrix<double> R;  // 2x2 rotation matrix
  public Vector<double> t;  // 2x1 translation vector

  public Transformation()
  {
    R = DenseMatrix.CreateIdentity(2);
    t = DenseVector.Create(2, 0);
  }
}

/// <summary>
/// Implements a simple Iterative Closest Point (ICP) algorithm for scan matching.
/// </summary>
public static class ICP
{
  public static Transformation RunICP(List<PointF> sourcePoints, List<PointF> targetPoints, int maxIterations = 20, double tolerance = 1e-4)
  {
    // Start with the identity transformation.
    Transformation transform = new Transformation();

    for (int iter = 0; iter < maxIterations; iter++)
    {
      // Transform source points using the current transformation estimate.
      List<PointF> transformedPoints = sourcePoints.Select(p => ApplyTransformation(p, transform)).ToList();

      // For each transformed source point, find the closest point in the target scan.
      List<PointF> correspondences = new List<PointF>();
      foreach (var p in transformedPoints)
      {
        PointF closest = FindClosestPoint(p, targetPoints);
        correspondences.Add(closest);
      }

      // Compute centroids of both sets.
      PointF centroidSrc = ComputeCentroid(transformedPoints);
      PointF centroidTgt = ComputeCentroid(correspondences);

      // Build the covariance matrix.
      Matrix<double> H = DenseMatrix.Create(2, 2, 0.0);
      for (int i = 0; i < transformedPoints.Count; i++)
      {
        double srcX = transformedPoints[i].X - centroidSrc.X;
        double srcY = transformedPoints[i].Y - centroidSrc.Y;
        double tgtX = correspondences[i].X - centroidTgt.X;
        double tgtY = correspondences[i].Y - centroidTgt.Y;

        H[0, 0] += srcX * tgtX;
        H[0, 1] += srcX * tgtY;
        H[1, 0] += srcY * tgtX;
        H[1, 1] += srcY * tgtY;
      }

      // Compute the SVD of H.
      var svd = H.Svd(true);
      Matrix<double> U = svd.U;
      Matrix<double> V = svd.VT.Transpose();

      // Calculate the optimal rotation.
      Matrix<double> R_delta = V * U.Transpose();
      if (R_delta.Determinant() < 0)
      {
        V.SetColumn(1, V.Column(1).Multiply(-1));
        R_delta = V * U.Transpose();
      }

      // Calculate the translation.
      Vector<double> t_delta = DenseVector.OfArray(new double[] { centroidTgt.X, centroidTgt.Y }) -
                                R_delta * DenseVector.OfArray(new double[] { centroidSrc.X, centroidSrc.Y });

      // Update the transformation.
      Transformation newTransform = new Transformation
      {
        R = R_delta * transform.R,
        t = R_delta * transform.t + t_delta
      };

      // Evaluate mean error.
      List<PointF> newTransformedPoints = sourcePoints.Select(p => ApplyTransformation(p, newTransform)).ToList();
      double error = 0;
      for (int i = 0; i < newTransformedPoints.Count; i++)
      {
        error += Distance(newTransformedPoints[i], correspondences[i]);
      }
      error /= newTransformedPoints.Count;

      transform = newTransform;
      if (error < tolerance)
      {
        break;
      }
    }

    return transform;
  }

  public static PointF ApplyTransformation(PointF p, Transformation transform)
  {
    Vector<double> pt = DenseVector.OfArray(new double[] { p.X, p.Y });
    Vector<double> transformed = transform.R * pt + transform.t;
    return new PointF((float)transformed[0], (float)transformed[1]);
  }

  public static PointF FindClosestPoint(PointF p, List<PointF> points)
  {
    PointF best = new PointF();
    double bestDistance = double.MaxValue;
    foreach (var pt in points)
    {
      double dist = Distance(p, pt);
      if (dist < bestDistance)
      {
        bestDistance = dist;
        best = pt;
      }
    }
    return best;
  }

  public static double Distance(PointF p1, PointF p2)
  {
    double dx = p1.X - p2.X;
    double dy = p1.Y - p2.Y;
    return Math.Sqrt(dx * dx + dy * dy);
  }

  public static PointF ComputeCentroid(List<PointF> points)
  {
    float sumX = 0;
    float sumY = 0;
    foreach (var p in points)
    {
      sumX += p.X;
      sumY += p.Y;
    }
    return new PointF(sumX / points.Count, sumY / points.Count);
  }
}

/// <summary>
/// The ScanMatcher class manages scan matching between consecutive scans.
/// </summary>
public class ScanMatcher
{
  private List<MapPoint> _trustedPoints;

  // Store the previous scan to compare with the current one.
  public ScanMatcher(List<MapPoint> trustedPoints)
  {
    _trustedPoints = trustedPoints;
  }
 

  /// <summary>
  /// Process the current scan, compute the transformation relative to the previous scan,
  /// and update the stored scan.
  /// </summary>
  public Transformation ProcessScan(List<MapPoint> currentScan, int maxIterations = 20, double tolerance = 1e-4)
  {
    // If no previous scan exists, initialize it.
    if (_trustedPoints == null)
    {
      _trustedPoints = currentScan;
      return new Transformation(); // Identity transformation.
    }

    // Convert MapPoint to PointF for the ICP algorithm.
    List<PointF> sourcePoints = currentScan.Select(mp => new PointF(mp.X, mp.Y)).ToList();
    List<PointF> targetPoints = _trustedPoints.Select(mp => new PointF(mp.X, mp.Y)).ToList();

    // Run ICP to compute the relative transformation.
    Transformation transform = ICP.RunICP(sourcePoints, targetPoints, maxIterations, tolerance);



    return transform;
  }
}
