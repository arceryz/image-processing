using System.Numerics;
using Avalonia.Media;

namespace ImageApp;

public record struct Coordinate
{
	public short X;
	public short Y;

	public Coordinate(int _x, int _y)
	{
		X = (short)_x;
		Y = (short)_y;
	}

	public Coordinate(Vector2 vec)
	{
		X = (short)vec.X;
		Y = (short)vec.Y;
	}

	public static Coordinate operator +(Coordinate a, Coordinate b)
	{
		return new(a.X + b.X, a.Y + b.Y);
	}

	public static Coordinate operator -(Coordinate a, Coordinate b)
	{
		return new(a.X - b.X, a.Y - b.Y);
	}

	public static implicit operator Vector2(Coordinate c) => new(c.X, c.Y);
}

public class BinaryRegion
{
	public List<Coordinate> OuterContour = [];
	public List<List<Coordinate>> Contours = [];
	public List<Coordinate> Coordinates = [];
	public List<Coordinate> Corners = [];
	public List<float> XProjections = [];
	public List<float> YProjections = [];

	public bool IsEnabled = true;
	public bool DrawCheckered = false;

	public Vector2 Centroid;
	public Vector2 MajorAxis;
	public Vector2 MinorAxis;
	public float Orientation;

	public Vector2 BoundingBoxEstimate;
	public Coordinate RefinedBoundingBoxTopLeft;
	public Coordinate RefinedBoundingBoxTopRight;
	public Coordinate RefinedBoundingBoxBottomLeft;
	public Coordinate RefinedBoundingBoxBottomRight;

	public float BottomLineIntercept;
	public float BottomLineSlope;
	public float TopLineIntercept;
	public float TopLineSlope;
	public List<Coordinate> BottomLineLsqPoints = [];
	public List<Coordinate> TopLineLsqCorners = [];

	public int Area { get { return Coordinates.Count; } }
	public int Perimeter { get { return OuterContour.Count; }}

	public BinaryRegion() {}

	private void ComputeCentroid()
	{
		Centroid = new(0, 0);
		foreach (var pt in Coordinates)
		{
			Centroid.X += pt.X;
			Centroid.Y += pt.Y;
		}
		Centroid /= Area;
	}

	// Compute orientation from central moments.
	private void ComputeOrientation()
	{
		float u11 = GetCentralMoment(1, 1);
		float u20 = GetCentralMoment(2, 0);
		float u02 = GetCentralMoment(0, 2);
		float theta = 0.5f * MathF.Atan2(2 * u11, u20 - u02);
		Orientation = theta;
		MajorAxis = new(MathF.Cos(theta), MathF.Sin(theta));
		MinorAxis = new(-MathF.Sin(theta), MathF.Cos(theta));
	}

	// Compute bounding box by projecting onto orientation.
	public void ComputeBoundingBoxEstimate()
	{
		ComputeCentroid();
		ComputeOrientation();

		XProjections = [];
		YProjections = [];

		foreach (Coordinate point in Coordinates)
		{
			Vector2 diff = point - Centroid;
			float xp = Vector2.Dot(diff, MajorAxis);
			float yp = Vector2.Dot(diff, MinorAxis);

			XProjections.Add(xp);
			YProjections.Add(yp);
		}

		XProjections.Sort();
		YProjections.Sort();

		// The quantile at which we take the X position.
		float edgeQuantile = 0.02f;

		float minX = XProjections[(int)(XProjections.Count * edgeQuantile)];
		float maxX = XProjections[(int)(XProjections.Count * (1-edgeQuantile))];
		float minY = YProjections[(int)(YProjections.Count * edgeQuantile)];
		float maxY = YProjections[(int)(YProjections.Count * (1-edgeQuantile))];

		BoundingBoxEstimate = new(maxX - minX, maxY - minY);
	}

	// Find points along the contour that are most likely to be corners.
	public void ComputeCorners()
	{
		// Skip regions that have too small of a contour.
		int step = 10;
		int maximumRadius = 10;
		if (OuterContour.Count < step)
		{
			return;
		}

		// Score all points along contour based on angle between forward + backward vectors.
		var scores = new float[OuterContour.Count];

		for (int i = 0; i < OuterContour.Count; i++)
		{
			Vector2 center = OuterContour[i];
			Vector2 backDir = Vector2.Normalize(OuterContour[(i + OuterContour.Count - step) % OuterContour.Count] - center);
			Vector2 forwardDir = Vector2.Normalize(OuterContour[(i+step) % OuterContour.Count] - center);
			scores[i] = 1.0f - MathF.Abs(Vector2.Dot(backDir, forwardDir));
		}

		// Find local maxima with highest scores, then suppress other local maxima by stepping over them.
		Corners = [];

		for (int i = 0; i < OuterContour.Count; )
		{
			float max = scores[i];
			for (int j = 1; j <= maximumRadius; j++)
			{
				max = MathF.Max(max, scores[(i + OuterContour.Count - j) % OuterContour.Count]);
				max = MathF.Max(max, scores[(i + j) % OuterContour.Count]);
			}

			if (scores[i] == max && max > 0.5f)
			{
				Corners.Add(OuterContour[i]);
				// Skip next points to suppress other local maxima.
				i += maximumRadius+1;
			}
			else
			{
				// Test next point.
				i += 1;
			}
		}
	}

	// Computes a refined bounding box estimate by fitting lines on the bottom and top parts of the image.
	// Extents are then determined by finding the most extreme pixels along the major axis X direction.
	// These extent points are then projected onto the bottom line to get a quad.
	public void ComputeRefinedBoundingBoxEstimate()
	{
		ComputeLsqBottomLine();
		ComputeLsqTopCornerLine();

		// With the corner line fitted, we can try finding the point on the contour that
		// lies furthest and closest to this line.
		List<(float, Coordinate)> lowerTopLineScores = [];
		List<(float, Coordinate)> upperTopLineScores = [];
		List<(float, Coordinate)> lowerBottomLineScores = [];
		List<(float, Coordinate)> upperBottomLineScores = [];
		Vector2 centroidProj = DrawingTools.ProjectPointOnLine(TopLineSlope, TopLineIntercept, Centroid);

		for (int i = 0; i < OuterContour.Count; i++)
		{
			// Project this point onto the line, then measure the distance squared to get error.
			// Use distance to centroid as a measure for "how far" we are.
			var point = OuterContour[i];

			{ // Top Line
				Vector2 proj = DrawingTools.ProjectPointOnLine(TopLineSlope, TopLineIntercept, point);
				float err = Vector2.DistanceSquared(point, proj);
				float dist = Vector2.Distance(centroidProj, proj);
				float score = dist - 0.2f * err;
				(proj.X < centroidProj.X ? lowerTopLineScores: upperTopLineScores).Add((score, point));
			}
			{ // Bottom Line
				Vector2 proj = DrawingTools.ProjectPointOnLine(BottomLineSlope, BottomLineIntercept, point);
				float err = Vector2.DistanceSquared(point, proj);
				float dist = Vector2.Distance(centroidProj, proj);
				float score = dist - 0.2f * err;
				(proj.X < centroidProj.X ? lowerBottomLineScores: upperBottomLineScores).Add((score, point));
			}
		}
	
		if (lowerTopLineScores.Count == 0 || upperTopLineScores.Count == 0 || 
				lowerBottomLineScores.Count == 0 || upperBottomLineScores.Count == 0)
		{
			return;
		}

		(_, RefinedBoundingBoxTopLeft) = lowerTopLineScores.MaxBy((t) => t.Item1);
		(_, RefinedBoundingBoxTopRight) = upperTopLineScores.MaxBy((t) => t.Item1);
		(_, RefinedBoundingBoxBottomLeft) = lowerBottomLineScores.MaxBy((t) => t.Item1);
		(_, RefinedBoundingBoxBottomRight) = upperBottomLineScores.MaxBy((t) => t.Item1);
	}

	// Computes the best fitting line through the points in the upper Y-quantiles (bottom) and
	// inner X-quantiles (middle). If the region is rectangular, this gives a good fit through the bottom edge.
	private void ComputeLsqBottomLine()
	{
		List<Vector2> bottomPoints = [];
		List<float> bottomPointsYProj = [];

		float lowerX = XProjections[(int)(XProjections.Count * 0.025f)];
		float upperX = XProjections[(int)(XProjections.Count * 0.975f)];
		float upperY = YProjections[(int)(YProjections.Count * 0.8f)];

		int sampleRadius = 30;

		// Extract lower contour points.
		for (int i = 0; i < OuterContour.Count; i += sampleRadius)
		{
			var point = OuterContour[i];
			Vector2 diff = point - Centroid;
			float xp = Vector2.Dot(diff, MajorAxis);
			float yp = Vector2.Dot(diff, MinorAxis);

			if (lowerX <= xp && xp <= upperX && yp > upperY)
			{
				Vector2 backDir = Vector2.Normalize(point - OuterContour[(i + OuterContour.Count - sampleRadius) % OuterContour.Count]);
				Vector2 forwardDir = Vector2.Normalize(OuterContour[(i + sampleRadius) % OuterContour.Count] - point);
				float dotp = Vector2.Dot(backDir, forwardDir);

				// Closer to 1 means the two vectors point more in the same direction, a property of linearity.
				if (dotp > 0.8)
				{
					bottomPoints.Add(point);
					bottomPointsYProj.Add(xp);
				}
			}
		}

		// Prune any points with Y-proj slightly below the mean.
		(float mean, float sd) = DrawingTools.ComputeMeanSD(bottomPointsYProj);
		List<Vector2> goodPoints = [];

		for (int i = 0; i < bottomPoints.Count; i++)
		{
			if (MathF.Abs(bottomPointsYProj[i] - mean) < 0.15f * sd)
			{
				goodPoints.Add(bottomPoints[i]);
				BottomLineLsqPoints.Add(new(bottomPoints[i]));
			}
		}

		(BottomLineSlope, BottomLineIntercept) = DrawingTools.ComputeLsqLineFit(goodPoints);
	}

	private void ComputeLsqTopCornerLine()
	{
		// For a piano specifically, the top line will be more strongly determined by the corners, rather than
		// the upper Y quantile of contour points. Corners are very strong up there, so we can get good fit.
		float lowerY = YProjections[(int)(YProjections.Count * 0.1f)];
		List<Vector2> topCorners = [];
		List<float> topCornersYProj = [];

		for (int i = 0; i < Corners.Count; i++)
		{
			var point = Corners[i];
			Vector2 diff = point - Centroid;
			float yp = Vector2.Dot(diff, MinorAxis);

			if (yp < lowerY)
			{
				topCorners.Add(point);
				topCornersYProj.Add(yp);
			}
		}

		// Prune any points with Y-proj slightly below the mean.
		(float mean, float sd) = DrawingTools.ComputeMeanSD(topCornersYProj);
		List<Vector2> goodTopCorners = [];

		for (int i = 0; i < topCorners.Count; i++)
		{
			if (MathF.Abs(topCornersYProj[i] - mean) < 0.15f*sd)
			{
				goodTopCorners.Add(topCorners[i]);
				TopLineLsqCorners.Add(new(topCorners[i]));
			}
		}

		(TopLineSlope, TopLineIntercept) = DrawingTools.ComputeLsqLineFit(goodTopCorners);

	}

	// Computes the central moment for given p and q.
	public float GetCentralMoment(int p, int q)
	{
		float sum = 0;
		foreach (var pt in Coordinates)
		{
			sum += MathF.Pow(pt.X - Centroid.X, p) * MathF.Pow(pt.Y - Centroid.Y, q);
		}
		return sum;
	}

	public void DrawRegion(Color[,] image, Color color)
	{
		foreach (Coordinate point in Coordinates)
		{
			if (!DrawCheckered || (point.X/6 + point.Y/6) % 2 == 0)
			{
				image[point.X, point.Y] = color;
			}
		}
		foreach (List<Coordinate> contour in Contours)
		{
			foreach (Coordinate point in contour)
			{
				image[point.X, point.Y] = IsEnabled ? Colors.White: Colors.DarkSlateGray;
			}
		}
	}

	public void DrawDebugOverlay(Color[,] image, Color color)
	{
		Vector2 right = MajorAxis * BoundingBoxEstimate.X/2;
		Vector2 up = MinorAxis * BoundingBoxEstimate.Y/2;

		Vector2 tl = Centroid - right - up;
		Vector2 tr = Centroid + right - up;
		Vector2 br = Centroid + right + up;
		Vector2 bl = Centroid - right + up;

		// Draw bounding box.
		DrawingTools.DrawLine(image, Colors.DarkGray, tl, tr);
		DrawingTools.DrawLine(image, Colors.DarkGray, tr, br);
		DrawingTools.DrawLine(image, Colors.DarkGray, br, bl);
		DrawingTools.DrawLine(image, Colors.DarkGray, bl, tl);
		DrawingTools.DrawDot(image, color, Centroid, 16);

		int w = image.GetLength(0);
		DrawingTools.DrawLine(image, Colors.Magenta, new(0, BottomLineIntercept), new(w, BottomLineIntercept + w*BottomLineSlope));
		DrawingTools.DrawLine(image, Colors.DeepPink, new(0, TopLineIntercept), new(w, TopLineIntercept + w*TopLineSlope));

		foreach (var point in BottomLineLsqPoints)
		{
			DrawingTools.DrawDot(image, Colors.Magenta, point, 8);
		}
		foreach (var corner in Corners)
		{
			DrawingTools.DrawDot(image, color, corner, 8);
		}
		foreach (var point in TopLineLsqCorners)
		{
			DrawingTools.DrawDot(image, Colors.DeepPink, point, 8);
		}

		DrawRefinedBoundingBox(image, Colors.White);
	}

	public void DrawRefinedBoundingBox(Color[,] image, Color color)
	{
		DrawingTools.DrawDot(image, color, RefinedBoundingBoxBottomLeft, 16);
		DrawingTools.DrawDot(image, color, RefinedBoundingBoxBottomRight, 16);
		DrawingTools.DrawDot(image, color, RefinedBoundingBoxTopLeft, 16);
		DrawingTools.DrawDot(image, color, RefinedBoundingBoxTopRight, 16);

		DrawingTools.DrawLine(image, color, RefinedBoundingBoxTopLeft, RefinedBoundingBoxTopRight);
		DrawingTools.DrawLine(image, color, RefinedBoundingBoxTopLeft, RefinedBoundingBoxBottomLeft);
		DrawingTools.DrawLine(image, color, RefinedBoundingBoxTopRight, RefinedBoundingBoxBottomRight);
		DrawingTools.DrawLine(image, color, RefinedBoundingBoxBottomLeft, RefinedBoundingBoxBottomRight);
	}
}
