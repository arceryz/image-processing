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

	public int Area { get { return Coordinates.Count; } }
	public int Perimeter { get { return OuterContour.Count; }}

	public BinaryRegion() {}

	public void ComputeCentroid()
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
	public void ComputeOrientation()
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
		int step = 10;
		int maximumRadius = 10;
		if (OuterContour.Count < step)
		{
			return;
		}

		var scores = new float[OuterContour.Count];

		for (int i = 0; i < OuterContour.Count; i++)
		{
			Vector2 center = OuterContour[i];
			Vector2 sumBack = OuterContour[(i + OuterContour.Count - step) % OuterContour.Count] - center;
			Vector2 sumForward = OuterContour[(i+step) % OuterContour.Count] - center;

			float lenBack = sumBack.Length() + 0.01f;
			float lenForward = sumForward.Length() + 0.01f;

			sumBack /= lenBack;
			sumForward /= lenForward;

			float dotp = Vector2.Dot(sumBack, sumForward);
			float score = 1.0f - MathF.Abs(dotp);
			scores[i] = score;
		}

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
				i += maximumRadius+1;
			}
			else
			{
				i += 1;
			}
		}
	}

	// Computes the best fitting line through the points in the upper Y-quantiles (bottom) and
	// inner X-quantiles (middle). If the region is rectangular, this gives a good fit through the bottom edge.
	private void ComputeBestFitBottomLine()
	{
		List<Coordinate> bottomPoints = [];
		Vector2 mean = Vector2.Zero;

		float lowerX = XProjections[(int)(XProjections.Count * 0.2f)];
		float upperX = XProjections[(int)(XProjections.Count * 0.8f)];
		float upperY = XProjections[(int)(XProjections.Count * 0.9f)];

		// Extract lower contour points.
		for (int i = 0; i < OuterContour.Count; i += 10)
		{
			Coordinate point = OuterContour[i];
			Vector2 diff = point - Centroid;
			float xp = Vector2.Dot(diff, MajorAxis);
			float yp = Vector2.Dot(diff, MinorAxis);

			if (lowerX <= xp && xp <= upperX && yp >= upperY)
			{
				bottomPoints.Add(point);
				mean += point;
			}
		}

		// Compute best-fit line using least squares.
		mean /= bottomPoints.Count;
		float xySum = 0;
		float xxSum = 0;

		foreach (var coord in bottomPoints)
		{
			float dx = coord.X - mean.X;
			xySum += dx * (coord.Y - mean.Y);
			xxSum += dx * dx;
		}

		float slope = xySum / xxSum;
		float intercept = mean.Y - slope * mean.X;
	}

	private void ComputeBestFitTopCornerLine()
	{

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
		DrawingTools.DrawLine(image, Colors.White, tl, tr);
		DrawingTools.DrawLine(image, Colors.White, tr, br);
		DrawingTools.DrawLine(image, Colors.White, br, bl);
		DrawingTools.DrawLine(image, Colors.White, bl, tl);

		DrawingTools.DrawDot(image, color, Centroid, 16);

		foreach (var corner in Corners)
		{
			DrawingTools.DrawDot(image, color, corner, 8);
		}
	}
}
