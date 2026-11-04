namespace ImageApp
{
	public struct Coordinate
	{
		public short X;
		public short Y;

		public Coordinate(int _x, int _y)
		{
			X = (short)_x;
			Y = (short)_y;
		}
	}

	public class ImageRegion
	{
		public Coordinate TopLeftCorner;
		public Coordinate BottomRightCorner;
		public byte[,] Region = new byte[0,0];
		public List<Coordinate> Contour = [];
		public List<Coordinate> Coordinates = [];
		public bool IsEnabled = true;

		public int Area { get { return Coordinates.Count; }}
		public int Perimeter { get { return Contour.Count; }}

		public ImageRegion() {}

		public void ComputeContour()
		{
		}

		// From the coordinate list, compute the region and its bounding box.
		public void ComputeRegion()
		{
			TopLeftCorner = new(9999,9999);
			BottomRightCorner = new(0, 0);

			foreach (Coordinate coord in Coordinates)
			{
				TopLeftCorner.X = Math.Min(coord.X, TopLeftCorner.X);
				TopLeftCorner.Y = Math.Min(coord.Y, TopLeftCorner.Y);
				BottomRightCorner.X = Math.Max(coord.X, BottomRightCorner.X);
				BottomRightCorner.Y = Math.Max(coord.Y, BottomRightCorner.Y);
			}

			Region = new byte[BottomRightCorner.X - TopLeftCorner.X,
				BottomRightCorner.Y - TopLeftCorner.Y];

			foreach (Coordinate coord in Coordinates)
			{
				Region[coord.X - TopLeftCorner.X, coord.Y - TopLeftCorner.Y] = 1;
			}
		}
	}
}
