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
		// By tracking min/max along the orientation axis, we can get
		// a good sense of the extents of the bounding box.
		// Is sensitive to outliers, which is why protrusions were minimized in preprocessing.
		float minX = float.MaxValue;
		float maxX = float.MinValue;
		float minY = float.MaxValue;
		float maxY = float.MinValue;

		foreach (Coordinate point in Coordinates)
		{
			Vector2 diff = point - Centroid;
			float xp = Vector2.Dot(diff, MajorAxis);
			float yp = Vector2.Dot(diff, MinorAxis);

			minX = MathF.Min(minX, xp);
			maxX = MathF.Max(maxX, xp);
			minY = MathF.Min(minY, yp);
			maxY = MathF.Max(maxY, yp);
		}

		BoundingBoxEstimate = new(maxX - minX, maxY - minY);
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
	}
}
