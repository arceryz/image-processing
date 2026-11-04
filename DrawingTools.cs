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
}
