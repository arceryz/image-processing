using Avalonia.Media;
using System.Numerics;

namespace ImageApp;

public static class DrawingTools
{
	// Draw dot with white border.
	public static void DrawDot(Color[,] image, Color color, Vector2 point, byte size)
	{
		float radius = size / 2.0f;
		float center = (size - 1) / 2.0f;

		int originX = (int)MathF.Round(point.X) - size / 2;
	    int originY = (int)MathF.Round(point.Y) - size / 2;

		for (int dy = 0; dy < size; dy++)
		{
			for (int dx = 0; dx < size; dx++)
			{
				int x = originX + dx;
				int y = originY + dy;

				if (x < 0 || x >= image.GetLength(0) || y < 0 || y >= image.GetLength(1))
					continue;

				double ox = dx - center;
				double oy = dy - center;
				double dist = Math.Sqrt(ox * ox + oy * oy);

				if (dist > radius)
					continue;

				image[x, y] = dist > radius - 2 ? Colors.White : color;
			}
		}
	}

	// Bresenham Line Algorithm.
	public static void DrawLine(Color[,] image, Color color, Vector2 a, Vector2 b)
	{
		int x1 = (int)MathF.Round(a.X);
		int y1 = (int)MathF.Round(a.Y);
		int x2 = (int)MathF.Round(b.X);
		int y2 = (int)MathF.Round(b.Y);

		int dx = Math.Abs(x2 - x1);
		int dy = -Math.Abs(y2 - y1);
		int sx = x1 < x2 ? 1 : -1;
		int sy = y1 < y2 ? 1 : -1;
		int err = dx + dy;

		while (true)
		{
			if (x1 >= 0 && x1 < image.GetLength(0) && y1 >= 0 && y1 < image.GetLength(1))
				image[x1, y1] = color;

			if (x1 == x2 && y1 == y2)
				break;

			int e2 = 2 * err;
			if (e2 >= dy) { err += dy; x1 += sx; }
			if (e2 <= dx) { err += dx; y1 += sy; }
		}
	}

	// Return (slope, intercept) that is the least-square fitted line through a list of points.
	public static (float, float) ComputeLsqLineFit(List<Vector2> points)
	{
		Vector2 mean = Vector2.Zero;

		foreach (var point in points)
		{
			mean += point;
		}

		mean /= points.Count;
		float xxSum = 0;
		float xySum = 0;

		foreach (var point in points)
		{
			float dx = point.X - mean.X;
			xxSum += dx*dx;
			xySum += dx*(point.Y - mean.Y);
		}

		float a = xySum / xxSum;
		float b = mean.Y - a*mean.X;
		return (a, b);
	}

	// Compute mean and standard deviation of a set of points along both axes.
	public static (float, float) ComputeMeanSD(List<float> values)
	{
		float mean = 0;
		foreach (var val in values)
		{
			mean += val;
		}
		mean /= values.Count;

		float variance = 0;
		foreach (var val in values)
		{
			variance += (val - mean) * (val - mean);
		}

		return (mean, MathF.Sqrt(variance));
	}

	// Projects a point onto a line given by slope and intercept a,b.
	public static Vector2 ProjectPointOnLine(float a, float b, Vector2 point)
	{
		// Derived from point on line and projection onto its direction vector.
		Vector2 q = new(0, b);
		Vector2 dir = new(1, a);
		Vector2 proj = q + (point.X + a * (point.Y - b)) / (1+a*a) * dir;
		return proj;
	}
}
