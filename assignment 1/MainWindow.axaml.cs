using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Data;

namespace ImageApp
{
	public partial class MainWindow : Window
	{
		readonly sbyte[,] horizontalSobelKernel3x3 = { 
			{ -1, -2, -1 },
			{ 0, 0, 0 },
			{ 1, 2, 1 },
		}; 
		readonly sbyte[,] verticalSobelKernel3x3 = {
			{ -1, 0, 1 },
			{ -2, 0, 2 },
			{ -1, 0, 1 }	
		}; 

		private WriteableBitmap? _loadedBitmap; // the raw loaded image, kept in color, for display in OriginalImage
		private byte[,,]? _loadedColorPixels; // [x, y, channel] with channel 0=R, 1=G, 2=B -- extracted once at load time
		private byte[,]? _processedGray; // Processed grayscale values (nullable)

		// Simple fixed defaults used by the functions below until you add your own
		// GUI controls (TextBoxes, ComboBoxes, etc.) to let the user set these values.
		private byte _threshold = 128;

		// If true, uses nearest neighbor filtering for images for debugging of morphological filters.
		private bool _pixelArtMode = false;

		// Enum for operations. As you implement each function, add a case for it
		// in OnApply below; the dropdown is populated automatically from this list.
		//
		// NOTE: Task1, Task2, and Task3 (from the assignment text) are NOT listed here.
		// You need to add those dropdown entries yourself as part of implementing them.
		private enum ProcessingFunctions
		{
			ConvertToGrayscale,
			InvertImage,
			AdjustContrast,
			ConvolveImage,
			MedianFilter,
			EdgeMagnitude,
			ThresholdImage,
			BinaryErodeImage,
			BinaryDilateImage,
			BinaryOpenImage,
			BinaryCloseImage,
			GrayscaleErodeImage,
			GrayscaleDilateImage,
			Task1,
			Task2,
			Task3,
		}

		public MainWindow()
		{
			InitializeComponent();

			OperationBox.ItemsSource = Enum.GetValues<ProcessingFunctions>();
			OperationBox.SelectedIndex = 0; // Select first item by default
		}

		/// <summary>
		/// Opens a file picker dialog, loads the selected image, and extracts its color pixel data.
		/// </summary>
		private async void OnLoadImage(object? sender, RoutedEventArgs e)
		{
			if (!StorageProvider.CanOpen)
			{
				StatusText.Text = "Opening files is not supported on this system.";
				return;
			}

			try
			{
				var files = await StorageProvider.OpenFilePickerAsync(
						new()
						{
						Title = "Open Image",
						AllowMultiple = false,
						FileTypeFilter = [FilePickerFileTypes.ImageAll]
						}
						);

				var file = files.FirstOrDefault();
				if (file == null)
					return;

				await using var stream = await file.OpenReadAsync();

				// Decode the file using Avalonia's own image loader
				using var decoded = new Bitmap(stream);
				var size = decoded.PixelSize;
				int width = size.Width;
				int height = size.Height;

				// Force a known, fixed pixel layout (RGBA, 8 bits per channel, unpremultiplied alpha)
				// so we can reliably read raw bytes regardless of the source file's own format.
				_loadedBitmap?.Dispose();
				_loadedBitmap = new(size, new(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);

				_loadedColorPixels = new byte[width, height, 3];
				using (var fb = _loadedBitmap.Lock())
				{
					decoded.CopyPixels(fb, AlphaFormat.Unpremul);
					// ^ transcodes the decoded image into our WriteableBitmap's RGBA8888 layout

					int totalBytes = fb.RowBytes * height;
					byte[] buffer = new byte[totalBytes];
					Marshal.Copy(fb.Address, buffer, 0, totalBytes);

					for (int y = 0; y < height; y++)
					{
						int rowStart = y * fb.RowBytes;
						for (int x = 0; x < width; x++)
						{
							int idx = rowStart + x * 4; // 4 bytes per pixel: R, G, B, A
							_loadedColorPixels[x, y, 0] = buffer[idx + 0];
							_loadedColorPixels[x, y, 1] = buffer[idx + 1];
							_loadedColorPixels[x, y, 2] = buffer[idx + 2];
						}
					}
				}

				_processedGray = null;
				(OriginalImage.Source as IDisposable)?.Dispose();
				OriginalImage.Source = _loadedBitmap;
				(ProcessedImage.Source as IDisposable)?.Dispose();
				ProcessedImage.Source = null;
				StatusText.Text = $"Loaded image ({width} \u00d7 {height} px).";
			}
			catch (Exception ex)
			{
				StatusText.Text = $"Failed to load image: {ex.Message}";
			}
		}

		/// <summary>
		/// Saves the current processed grayscale image to disk as a PNG file.
		/// </summary>
		private async void OnSaveImage(object? sender, RoutedEventArgs e)
		{
			if (_processedGray == null)
			{
				StatusText.Text = "No processed image to save. Apply an operation first.";
				return;
			}

			if (!StorageProvider.CanSave)
			{
				StatusText.Text = "Saving files is not supported on this system.";
				return;
			}

			try
			{
				var file = await StorageProvider.SaveFilePickerAsync(
						new()
						{
						Title = "Save Processed Image",
						SuggestedFileName = "processed.png",
						DefaultExtension = "png",
						FileTypeChoices = [FilePickerFileTypes.ImagePng]
						}
						);

				if (file == null)
					return;

				using var bmp = ByteArrayToBitmap(_processedGray);
				await using var stream = await file.OpenWriteAsync();
				bmp.Save(stream);
				StatusText.Text = "Processed image saved successfully.";
			}
			catch (Exception ex)
			{
				StatusText.Text = $"Failed to save image: {ex.Message}";
			}
		}

		private void OnToggledPixelArt(object? sender, RoutedEventArgs e)
		{
			_pixelArtMode = !_pixelArtMode;
			var interpolationMode = _pixelArtMode ? BitmapInterpolationMode.None: BitmapInterpolationMode.Unspecified;
			var edgeMode = _pixelArtMode ? EdgeMode.Aliased: EdgeMode.Unspecified;
	
			RenderOptions.SetBitmapInterpolationMode(OriginalImage, interpolationMode);
			RenderOptions.SetEdgeMode(OriginalImage, edgeMode);
			RenderOptions.SetBitmapInterpolationMode(ProcessedImage, interpolationMode);
			RenderOptions.SetEdgeMode(ProcessedImage, edgeMode);

			OriginalImage.InvalidateVisual();
			ProcessedImage.InvalidateVisual();

			StatusText.Text = "Toggled Filtering";
		}

		/// <summary>
		/// Dispatches the selected image processing operation on a background task
		/// to keep the UI responsive during heavy computations.
		/// </summary>
		private async void OnApply(object? sender, RoutedEventArgs e)
		{
			if (_loadedColorPixels == null)
			{
				StatusText.Text = "Please load an image first.";
				return;
			}

			if (OperationBox.SelectedItem is not ProcessingFunctions selected)
			{
				StatusText.Text = "Please select a valid operation.";
				return;
			}

			ApplyButton.IsEnabled = false;
			StatusText.Text = "Processing...";

			byte[,,] colorPixels = _loadedColorPixels;

			try
			{
				// Collect parameters from GUI.
				byte kernelSize = (byte)(KernelSize.Value ?? 4);
				byte threshold = (byte)(Threshold.Value ?? 128);
				float sigma = (float)(GaussianSigma.Value ?? 5.0m);
				bool bUseGaussian = SmoothingMethodSelector.SelectedIndex == 0;
				byte structureElementSize = (byte)(StructureSize.Value ?? 3);

				// Run computation and bitmap generation on a background task
				// to keep the UI dispatcher thread responsive during heavy operations.
				var (resultGray, resultBmp) = await Task.Run(() =>
						{
						// Grayscale conversion happens here on every Apply so every
						// operation always starts from the original loaded image, never chained
						// from a previous Apply's result.
						byte[,] gray = ConvertToGrayscale(colorPixels);

						switch (selected)
						{
						case ProcessingFunctions.Task3:
						{
							// Apply binary closing.
							gray = BinaryCloseImage(gray, CreateSquareBinaryStructureElement(structureElementSize));
							break;
						}
						case ProcessingFunctions.Task2:
						{
							// Apply grayscale erosion.
							gray = GrayscaleErodeImage(gray, CreateSquareStructureElement(structureElementSize));
							break;
						}
						case ProcessingFunctions.Task1:
						{
							// Step 1. Either Gaussian or Median Filter.
							if (bUseGaussian)
							{
								float[,] gaussianFilter = CreateGaussianFilter(kernelSize, sigma);
								gray = ConvolveImage(gray, gaussianFilter);
							}
							else
							{
								gray = MedianFilter(gray, kernelSize);
							}

							// Step 2. Apply Edge Detection.
							gray = EdgeMagnitude(gray, horizontalSobelKernel3x3, verticalSobelKernel3x3);
							
							// Step 3. Apply Thresholding.
							gray = ThresholdImage(gray, threshold);
							break;
						}

						case ProcessingFunctions.ConvertToGrayscale:
						// Already fully working; gray already holds the grayscale
						// conversion result at this point (computed above, before this switch),
						// so nothing further is needed here.
						break;
						case ProcessingFunctions.InvertImage:
						gray = InvertImage(gray);
						break;
						case ProcessingFunctions.AdjustContrast:
						gray = AdjustContrast(gray);
						break;
						case ProcessingFunctions.ConvolveImage:
						gray = ConvolveImage(gray, CreateGaussianFilter(kernelSize, sigma));
						break;
						case ProcessingFunctions.MedianFilter:
						gray = MedianFilter(gray, kernelSize);
						break;
						case ProcessingFunctions.EdgeMagnitude:
						{
							gray = EdgeMagnitude(gray, horizontalSobelKernel3x3, verticalSobelKernel3x3);
							break;
						}
						case ProcessingFunctions.ThresholdImage:
						gray = ThresholdImage(gray, _threshold);
						break;

						case ProcessingFunctions.BinaryErodeImage:
						{
							gray = BinaryErodeImage(gray, CreateSquareBinaryStructureElement(structureElementSize));
							break;
						}

						case ProcessingFunctions.BinaryDilateImage:
						{
							// Dilate expanding.
							gray = BinaryDilateImage(gray, CreateSquareBinaryStructureElement(structureElementSize));
							break;
						}

						case ProcessingFunctions.BinaryOpenImage:
						{
							gray = BinaryOpenImage(gray, CreateSquareBinaryStructureElement(structureElementSize));
							break;
						}

						case ProcessingFunctions.BinaryCloseImage:
						{
							gray = BinaryCloseImage(gray, CreateSquareBinaryStructureElement(structureElementSize));
							break;
						}

						case ProcessingFunctions.GrayscaleErodeImage:
						{
							int[,] grayStructElem = CreateRadialStructureElement(10);
							gray = GrayscaleErodeImage(gray, grayStructElem);
							break;
						}

						case ProcessingFunctions.GrayscaleDilateImage:
						{
							int[,] grayStructElem = CreateRadialStructureElement(10);
							gray = GrayscaleDilateImage(gray, grayStructElem);
							break;
						}

						default:
						throw new NotSupportedException($"Operation '{selected}' is not implemented in the OnApply switch.");
						}

						var bmp = ByteArrayToBitmap(gray);
						return (gray, bmp);
						});

				_processedGray = resultGray;
				(ProcessedImage.Source as IDisposable)?.Dispose();
				ProcessedImage.Source = resultBmp;

				// Compute number of different values and average.
				float averageIntensity = 0;
				int numDistinctIntensities = 0;
				int numForegroundPixels = 0;

				bool[] seenIntensities = new bool[256];
				foreach (byte b in resultGray)
				{
					if (b == 255)
					{
						numForegroundPixels++;
					}
					if (!seenIntensities[b])
					{
						seenIntensities[b] = true;
						numDistinctIntensities++;
					}
					averageIntensity += b;
				}
				averageIntensity /= resultGray.Length;

				StatusText.Text = $"Completed {selected}. Kernel={kernelSize}, Sigma={sigma}, Threshold={threshold}. Unique={numDistinctIntensities}, Average={averageIntensity} over {resultGray.Length} pixels, Foreground={numForegroundPixels}";
			}
			catch (Exception ex)
			{
				StatusText.Text = $"Error applying {selected}: {ex.Message}";
			}
			finally
			{
				ApplyButton.IsEnabled = true;
			}
		}

		// ====================================================================
		// ==================== GIVEN (already implemented) ==================
		// ====================================================================

		/// <summary>
		/// Converts loaded color pixel data (<c>[x, y, channel]</c> where channel 0=R, 1=G, 2=B)
		/// to single-channel grayscale by averaging RGB values.
		/// </summary>
		/// <param name="colorPixels">The 3D array of color pixels extracted at load time.</param>
		/// <returns>A 2D array of grayscale byte intensities with values in [0, 255].</returns>
		private static byte[,] ConvertToGrayscale(byte[,,] colorPixels)
		{
			int w = colorPixels.GetLength(0);
			int h = colorPixels.GetLength(1);
			byte[,] gray = new byte[w, h];
			for (int x = 0; x < w; x++)
				for (int y = 0; y < h; y++)
				{
					int r = colorPixels[x, y, 0];
					int g = colorPixels[x, y, 1];
					int b = colorPixels[x, y, 2];
					gray[x, y] = (byte)((r + g + b) / 3);
				}

			return gray;
		}

		// ====================================================================
		// ==================== FUNCTIONS TO IMPLEMENT =======================
		// ====================================================================

		/// <summary>
		/// Inverts the intensity values of the input grayscale image.
		/// </summary>
		/// <param name="inputImage">The 2D input grayscale image.</param>
		/// <returns>A new 2D grayscale image with inverted intensities.</returns>
		private byte[,] InvertImage(byte[,] inputImage)
		{
			// create temporary grayscale image
			byte[,] tempImage = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];

			// Get the largest value by enumerating.
			int aMax = inputImage.Cast<byte>().Max();

			for (int x = 0; x < inputImage.GetLength(0); x++)
				for (int y = 0; y < inputImage.GetLength(1); y++)
				{
					tempImage[x, y] = (byte)(aMax - inputImage[x, y]);
				}
			return tempImage;
		}

		/// <summary>
		/// Adjusts the contrast of the input grayscale image.
		/// </summary>
		/// <param name="inputImage">The 2D input grayscale image.</param>
		/// <returns>A new 2D grayscale image with adjusted contrast.</returns>
		private byte[,] AdjustContrast(byte[,] inputImage)
		{
			// create temporary grayscale image
			byte[,] tempImage = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];

			// Compute the histogram.
			int[] histogram = ComputeHistogram(inputImage);
			int aLow = 0;
			int aHigh = 255;

			// Find the first non-empty bin from the bottom.
			while (histogram[aLow] == 0) {
				aLow++;
			}

			// Find the first non-empty bin from the top.
			while (histogram[aHigh] == 0) {
				aHigh--;
			}

			// In case we want to change the range of contrast.
			int aLowNew = 0;
			int aHighNew = 255;

			// TODO: add your functionality and checks
			for (int x = 0; x < inputImage.GetLength(0); x++)
				for (int y = 0; y < inputImage.GetLength(1); y++)
				{
					float newA = aLowNew + (aHighNew - aLowNew) * (float)(inputImage[x, y] - aLow) / (aHigh - aLow);
					tempImage[x, y] = (byte)newA;
				}

			return tempImage;
		}

		/// <summary>
		/// Generates a normalized 2D Gaussian filter kernel of the specified size and standard deviation.
		/// </summary>
		/// <param name="size">Kernel dimension (odd integer, e.g. 3, 5, 7).</param>
		/// <param name="sigma">Gaussian standard deviation parameter.</param>
		/// <returns>A 2D float array representing the normalized filter kernel.</returns>
		private float[,] CreateGaussianFilter(byte size, float sigma)
		{
			// create the filter
			float[,] filter = new float[size, size];

			float sigma2 = 2 * sigma * sigma;
			float sum = 0;
			int center = (size-1)/2;

			// Specify filter and sum up.
			for (int i = 0; i < size; i++)
				for (int j = 0; j < size; j++)
				{
					int dx = i - center;
					int dy = j - center;
					int r2 = dx*dx + dy*dy;
					float f = MathF.Exp(-r2 / sigma2);
					sum += f;
					filter[i, j] = f;
				}

			// Normalize to 1.
			for (int i = 0; i < size; i++)
			{
				for (int j = 0; j < size; j++)
				{
					filter[i, j] /= sum;
				}
			}

			return filter;
		}

		/// A mirror repeat of the given coordinate x, for both positive and negative numbers.
		private int MirrorRepeat(int x, int w)
		{
			int period = 2 * (w - 1);
			int m = ((x % period) + period) % period;
			return Math.Min(m, period - m);
		}

		/// <summary>
		/// Convolves a grayscale image with a given 2D filter kernel.
		/// </summary>
		/// <param name="inputImage">The 2D input grayscale image.</param>
		/// <param name="filter">The 2D filter kernel to apply.</param>
		/// <returns>The convolved grayscale image.</returns>
		private byte[,] ConvolveImage(byte[,] inputImage, float[,] filter)
		{
			// create temporary grayscale image
			byte[,] tempImage = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];
			int w = inputImage.GetLength(0);
			int h = inputImage.GetLength(1);

			int filterSize = filter.GetLength(0);
			int filterCenter = (filterSize-1) / 2;

			// Iterate the image in (x,y), and apply a mirroring-filter to it.
			for (int x = 0; x < w; x++)
			for (int y = 0; y < h; y++)
			{
				// Sum all the values in the filter for this pixel.
				float sum = 0;

				for (int i = 0; i < filterSize; i++)
				for (int j = 0; j < filterSize; j++)
				{
					int sx = MirrorRepeat(x + i - filterCenter, w);
					int sy = MirrorRepeat(y + j - filterCenter, h);

					sum += inputImage[sx, sy] * filter[i, j];
				}

				tempImage[x, y] = (byte)MathF.Min(sum, 255);
			}

			return tempImage;
		}

		/// <summary>
		/// Applies a median filter of the given kernel size to reduce noise.
		/// </summary>
		/// <param name="inputImage">The 2D input grayscale image.</param>
		/// <param name="kernelSize">The size of the local neighborhood window (odd integer).</param>
		/// <returns>The filtered grayscale image.</returns>
		private byte[,] MedianFilter(byte[,] inputImage, byte kernelSize)
		{
			// create temporary grayscale image
			byte[,] tempImage = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];
			int w = inputImage.GetLength(0);
			int h = inputImage.GetLength(1);

			int center = (kernelSize-1) / 2;
			int meanIndex = (kernelSize * kernelSize - 1) / 2;
			byte[] meanBuffer = new byte[kernelSize * kernelSize];

			for (int x = 0; x < w; x++)
			for (int y = 0; y < h; y++)
			{
				for (int i = 0; i < kernelSize; i++)
				for (int j = 0; j < kernelSize; j++)
				{
					int sx = MirrorRepeat(x + i - center, w);
					int sy = MirrorRepeat(y + j - center, h);
					meanBuffer[i + j * kernelSize] = inputImage[sx, sy];
				}

				// Sort the pixels to find the mean.
				Array.Sort(meanBuffer);
				tempImage[x, y] = meanBuffer[meanIndex];
			}

			return tempImage;
		}

		/// <summary>
		/// Computes edge magnitude from horizontal and vertical derivative kernels.
		/// </summary>
		/// <param name="inputImage">The 2D input grayscale image.</param>
		/// <param name="horizontalKernel">Horizontal gradient kernel.</param>
		/// <param name="verticalKernel">Vertical gradient kernel.</param>
		/// <returns>The edge gradient magnitude image.</returns>
		private byte[,] EdgeMagnitude(
				byte[,] inputImage,
				sbyte[,] horizontalKernel,
				sbyte[,] verticalKernel
				)
		{
			// create temporary grayscale image
			byte[,] tempImage = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];
			float[,] edgeMagnitudeRaw = new float[inputImage.GetLength(0), inputImage.GetLength(1)];

			int w = inputImage.GetLength(0);
			int h = inputImage.GetLength(1);

			// Centers of our edge kernels.
			int hCenterX = (horizontalKernel.GetLength(0)-1) / 2;
			int hCenterY = (horizontalKernel.GetLength(1)-1) / 2;

			int vCenterX = (verticalKernel.GetLength(0)-1) / 2;
			int vCenterY = (verticalKernel.GetLength(1)-1) / 2;
			
			float highestMagnitude = 0;

			for (int x = 0; x < w; x++)
			for (int y = 0; y < h; y++)
			{
				// Compute the two derivatives.
				// Keep track of the sums of the kernels.
				float dx = 0;
				float dy = 0;

				for (int i = 0; i < horizontalKernel.GetLength(0); i++)
				for (int j = 0; j < horizontalKernel.GetLength(1); j++)
				{
					int sx = MirrorRepeat(x + i - hCenterX, w);
					int sy = MirrorRepeat(y + j - hCenterY, h);
					
					int val = horizontalKernel[i, j];
					dx += inputImage[sx, sy] * val;
				}

				for (int i = 0; i < verticalKernel.GetLength(0); i++)
				for (int j = 0; j < verticalKernel.GetLength(1); j++)
				{
					int sx = MirrorRepeat(x + i - vCenterX, w);
					int sy = MirrorRepeat(y + j - vCenterY, h);
					
					int val = verticalKernel[i, j];
					dy += inputImage[sx, sy] * val;
				}

				float intensity = MathF.Sqrt(dx * dx + dy * dy);
				edgeMagnitudeRaw[x, y] = intensity;
				highestMagnitude = Math.Max(highestMagnitude, intensity);
			}

			for (int x = 0; x < w; x++)
			for (int y = 0; y < h; y++)
			{
				tempImage[x, y] = (byte)(edgeMagnitudeRaw[x, y] / highestMagnitude * 255.0f);
				//tempImage[x, y] = (byte)Math.Min(edgeMagnitudeRaw[x, y], 255);
			}

			return tempImage;
		}

		/// <summary> ThresholdImage
		/// Thresholds a grayscale image into a binary representation based on a cutoff value.
		/// </summary>
		/// <param name="inputImage">The 2D input grayscale image.</param>
		/// <param name="threshold">Intensity threshold cutoff value in [0, 255].</param>
		/// <returns>A binary image represented as byte intensities (e.g. 0 and 255).</returns>
		private byte[,] ThresholdImage(byte[,] inputImage, byte threshold)
		{
			// create temporary grayscale image
			byte[,] tempImage = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];

			for (int x = 0; x < tempImage.GetLength(0); x++)
				for (int y = 0; y < tempImage.GetLength(1); y++)
				{
					tempImage[x, y] = (byte)(inputImage[x, y] < threshold ? 0: 255);
				}
			return tempImage;
		}

		/// <summary>
		/// Performs morphological binary erosion using the provided structuring element.
		/// </summary>
		/// <param name="inputImage">The binary input image.</param>
		/// <param name="structElem">2D boolean structuring element (true = foreground).</param>
		/// <returns>The eroded binary image.</returns>
		private byte[,] BinaryErodeImage(byte[,] inputImage, bool[,] structElem)
		{
			byte[,] output = InvertImage(inputImage);
			bool[,] mirroredElem = new bool[structElem.GetLength(0), structElem.GetLength(1) ];

			// Mirror the structure element.
			int w = structElem.GetLength(0);
			int h = structElem.GetLength(1);
			for (int i = 0; i < w; i++)
			for (int j = 0; j < h; j++)
			{
				mirroredElem[i, j] = structElem[w - 1 - i, h - 1 - j];
			}

			// Apply rule that Erosion with H = Inverted Dilation with H*.
			return InvertImage(BinaryDilateImage(output, mirroredElem));
		}

		/// <summary>
		/// Performs morphological binary dilation using the provided structuring element.
		/// </summary>
		/// <param name="inputImage">The binary input image.</param>
		/// <param name="structElem">2D boolean structuring element (true = foreground).</param>
		/// <returns>The dilated binary image.</returns>
		private byte[,] BinaryDilateImage(byte[,] inputImage, bool[,] structElem)
		{
			byte[,] output = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];

			// Center of the structuring element.
			int w = inputImage.GetLength(0);
			int h = inputImage.GetLength(1);
			int center = (structElem.GetLength(0)-1) / 2;

			for (int x = 0; x < inputImage.GetLength(0); x++)
			for (int y = 0; y < inputImage.GetLength(1); y++)
			{
				// Skip if this pixel is not part of the binary image.
				// BLACK = background.
				if (inputImage[x, y] == 0)
				{
					continue;
				}

				// Apply the binary dilation.
				for (int i = 0; i < structElem.GetLength(0); i++)
				for (int j = 0; j < structElem.GetLength(1); j++)
				{
					// Skip if this is not part of the structure element, or is out of bounds.
					int sx = x + i - center;
					int sy = y + j - center;
					if (!structElem[i,j] || sx < 0 || sx >= w || sy < 0 || sy >= h)
					{
						continue;
					}
					output[sx, sy] = 255;
				}
			}
			return output;
		}

		/// <summary>
		/// Performs morphological binary opening (erosion followed by dilation).
		/// </summary>
		/// <param name="inputImage">The binary input image.</param>
		/// <param name="structElem">2D boolean structuring element (true = foreground).</param>
		/// <returns>The opened binary image.</returns>
		private byte[,] BinaryOpenImage(byte[,] inputImage, bool[,] structElem)
		{
			return BinaryDilateImage(BinaryErodeImage(inputImage, structElem), structElem);
		}

		/// <summary>
		/// Performs morphological binary closing (dilation followed by erosion).
		/// </summary>
		/// <param name="inputImage">The binary input image.</param>
		/// <param name="structElem">2D boolean structuring element (true = foreground).</param>
		/// <returns>The closed binary image.</returns>
		private byte[,] BinaryCloseImage(byte[,] inputImage, bool[,] structElem)
		{
			return BinaryErodeImage(BinaryDilateImage(inputImage, structElem), structElem);
		}

		/// <summary>
		/// Performs morphological grayscale erosion using the provided structuring element.
		/// </summary>
		/// <param name="inputImage">The 2D grayscale input image.</param>
		/// <param name="structElem">2D integer structuring element defining neighborhood offsets.</param>
		/// <returns>The eroded grayscale image.</returns>
		private byte[,] GrayscaleErodeImage(byte[,] inputImage, int[,] structElem)
		{
			byte[,] output = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];

			int w = inputImage.GetLength(0);
			int h = inputImage.GetLength(1);
			int center = (structElem.GetLength(0)-1) / 2;

			for (int x = 0; x < inputImage.GetLength(0); x++)
			for (int y = 0; y < inputImage.GetLength(1); y++)
			{
				// Apply the grayscale erosion by taking the min of the differences.
				int lowest = 255;

				for (int i = 0; i < structElem.GetLength(0); i++)
				for (int j = 0; j < structElem.GetLength(1); j++)
				{
					int sx = x + i - center;
					int sy = y + j - center;

					// Skip values outside of the image.
					if (sx < 0 || sx >= w || sy < 0 || sy >= h || structElem[i, j] == int.MaxValue)
					{
						continue;
					}
					lowest = Math.Min(lowest, inputImage[sx, sy] - structElem[i, j]);
				}
				output[x, y] = (byte)Math.Max(lowest, 0);
			}
			return output;
		}

		/// <summary>
		/// Performs morphological grayscale dilation using the provided structuring element.
		/// </summary>
		/// <param name="inputImage">The 2D grayscale input image.</param>
		/// <param name="structElem">2D integer structuring element defining neighborhood offsets.</param>
		/// <returns>The dilated grayscale image.</returns>
		private byte[,] GrayscaleDilateImage(byte[,] inputImage, int[,] structElem)
		{
			byte[,] output = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];
			
			int w = inputImage.GetLength(0);
			int h = inputImage.GetLength(1);
			int center = (structElem.GetLength(0)-1) / 2;

			for (int x = 0; x < inputImage.GetLength(0); x++)
			for (int y = 0; y < inputImage.GetLength(1); y++)
			{
				// Apply the grayscale erosion by taking the min of the differences.
				int highest = 0;
 
				for (int i = 0; i < structElem.GetLength(0); i++)
				for (int j = 0; j < structElem.GetLength(1); j++)
				{
					int sx = x + i - center;
					int sy = y + j - center;

					// Skip values outside of the image.
					if (sx < 0 || sx >= w || sy < 0 || sy >= h || structElem[i,j]==int.MaxValue)
					{
						continue;
					}
					highest = Math.Max(highest, inputImage[sx, sy] + structElem[i, j]);
				}
				output[x, y] = (byte)Math.Min(highest, 255);
			}
			return output;
		}

		// ====================================================================
		// ==================== IMAGE <-> BITMAP HELPERS (given) =============
		// ====================================================================

		/// <summary>
		/// Builds a displayable and savable <see cref="WriteableBitmap"/> from a grayscale byte[,] array
		/// (replicated into R, G, B; alpha fully opaque), using Avalonia's native imaging APIs.
		/// </summary>
		/// <param name="gray">The 2D grayscale byte array.</param>
		/// <returns>A displayable and savable <see cref="WriteableBitmap"/>.</returns>
		private WriteableBitmap ByteArrayToBitmap(byte[,] gray)
		{
			int w = gray.GetLength(0);
			int h = gray.GetLength(1);
			var size = new PixelSize(w, h);
			var bmp = new WriteableBitmap(
					size,
					new(96, 96),
					PixelFormat.Rgba8888,
					AlphaFormat.Opaque
					);

			using var fb = bmp.Lock();

			int totalBytes = fb.RowBytes * h;
			byte[] buffer = new byte[totalBytes];
			for (int y = 0; y < h; y++)
			{
				int rowStart = y * fb.RowBytes;
				for (int x = 0; x < w; x++)
				{
					byte val = gray[x, y];
					int idx = rowStart + x * 4;
					buffer[idx + 0] = val; // R
					buffer[idx + 1] = val; // G
					buffer[idx + 2] = val; // B
					buffer[idx + 3] = 255; // A (fully opaque)
				}
			}

			Marshal.Copy(buffer, 0, fb.Address, totalBytes);

			return bmp;
		}

		private int[] ComputeHistogram(byte[,] grayImage)
		{
			int[] bins = new int[256];
			for (int x = 0; x < grayImage.GetLength(0); x++)
				for (int y = 0; y < grayImage.GetLength(1); y++)
				{
					bins[grayImage[x, y]] += 1;
				}
			return bins;
		}

		private int[,] CreateRadialStructureElement(int radius)
		{
			int[,] output = new int[2*radius+1, 2*radius+1];
			for (int i = -radius; i <= radius; i++)
			for (int j = -radius; j <= radius; j++)
			{
				bool inside = i*i + j*j < radius*radius;
				output[radius + i, radius + j] = inside ? 0: int.MaxValue; 
			}
			return output;
		}

		private int[,] CreateSquareStructureElement(int size)
		{
			int[,] output = new int[size, size];
			for (int i = 0; i < size; i++)
			for (int j = 0; j < size; j++)
			{
				output[i, j] = 0;
			}
			return output;
		}

		private bool[,] CreateSquareBinaryStructureElement(int size)
		{
			bool[,] output = new bool[size, size];
			for (int i = 0; i < size; i++)
			for (int j = 0; j < size; j++)
			{
				output[i, j] = true;
			}
			return output;
		}
	}
}
