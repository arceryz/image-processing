namespace ImageApp
{
	public record struct Coordinate
	{
		public short X;
		public short Y;

		public Coordinate(int _x, int _y)
		{
			X = (short)_x;
			Y = (short)_y;
		}

		public static Coordinate operator +(Coordinate a, Coordinate b)
		{
			return new(a.X + b.X, a.Y + b.Y);
		}
	}

	public class BinaryRegion
	{
		public List<Coordinate> OuterContour = [];
		public List<List<Coordinate>> Contours = [];
		public bool IsEnabled = true;

		public int Area = 0;
		public int Perimeter { get { return OuterContour.Count; }}

		public BinaryRegion() {}
	}
}
