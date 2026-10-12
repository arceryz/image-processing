using System.Runtime.InteropServices;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Data;
using System.Diagnostics;

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
			Extra1CannyEdgeDetection,
			SequentialRegionLabeling,
			HoughTransform,
			HoughPeakFinding,
			HoughLineSegments,
			BinaryLineDetection,
			GrayscaleLineDetection,
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
			if (ProcessedImage.Source == null)
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

				using var bmp = ProcessedImage.Source as Bitmap;
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
				float hysterLow = (float)(HysteresisLow.Value ?? 0.05m);
				float hysterHigh = (float)(HysteresisHigh.Value ?? 0.2m);

				// Run computation and bitmap generation on a background task
				// to keep the UI dispatcher thread responsive during heavy operations.
				object result = await Task.Run(() =>
				{
					// Grayscale conversion happens here on every Apply so every
					// operation always starts from the original loaded image, never chained
					// from a previous Apply's result.
					byte[,] gray = ConvertToGrayscale(colorPixels);
					int w = gray.GetLength(0);
					int h = gray.GetLength(1);
					Color[,] colorImg = new Color[gray.GetLength(0), gray.GetLength(1)];
					
					switch (selected)
					{
					case ProcessingFunctions.HoughTransform:
					{
						byte[,] hough = NormalizeIntImage(HoughTransform(gray, 512));
						return hough;
					}
					case ProcessingFunctions.SequentialRegionLabeling:
					{
						(int[,] regionImg, int numRegions) = SequentialRegionLabeling(gray);
						Debug.Print($"{numRegions} regions");
						Color[] colors = GetRandomColors(numRegions, 98743);

						for (int x = 0; x < w; x++)
						for (int y = 0; y < h; y++)
						{
							int lb = regionImg[x, y];
							colorImg[x, y] = lb > 0 ? colors[lb - 1]: Colors.Black;
						}
						return colorImg;
					}
					case ProcessingFunctions.Extra1CannyEdgeDetection:
					{
						(int[,] classes, int numClasses) = CannyEdgeDetection(gray, sigma, hysterLow, hysterHigh);
						Color[] colors = GetRandomColors(numClasses);

						// Create the image data.
						for (int x = 0; x < w; x++)
						for (int y = 0; y < h; y++)
						{
							int colorClass = classes[x, y];
							colorImg[x, y] = colorClass > 0 ? Colors.White: Colors.Black;
						}

						return colorImg;
					}
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
						(gray, _, _, _) = EdgeMagnitude(gray, horizontalSobelKernel3x3, verticalSobelKernel3x3);

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
						(gray, _, _, _) = EdgeMagnitude(gray, horizontalSobelKernel3x3, verticalSobelKernel3x3);
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

					return (object)gray;
				});

				WriteableBitmap bmp = result switch 
				{
					Color[,] colorImg => ColorArrayToBitmap(colorImg),
					byte[,] grayImg => ByteArrayToBitmap(grayImg),
					_ => throw new NotSupportedException()
				};

				(ProcessedImage.Source as IDisposable)?.Dispose();
				ProcessedImage.Source = bmp;

				StatusText.Text = "Completed";
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

			int filterSizeX = filter.GetLength(0);
			int filterSizeY = filter.GetLength(1);
			int filterCenterX = (filterSizeX-1) / 2;
			int filterCenterY = (filterSizeY-1) / 2;

			// Iterate the image in (x,y), and apply a mirroring-filter to it.
			for (int x = 0; x < w; x++)
			for (int y = 0; y < h; y++)
			{
				// Sum all the values in the filter for this pixel.
				float sum = 0;

				for (int i = 0; i < filterSizeX; i++)
				for (int j = 0; j < filterSizeY; j++)
				{
					int sx = MirrorRepeat(x + i - filterCenterX, w);
					int sy = MirrorRepeat(y + j - filterCenterY, h);

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
		private (byte[,], float[,], float[,], float[,]) EdgeMagnitude(
				byte[,] inputImage,
				sbyte[,] horizontalKernel,
				sbyte[,] verticalKernel
				)
		{
			// create temporary grayscale image
			byte[,] tempImage = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];
			float[,] edgeMagnitudeRaw = new float[inputImage.GetLength(0), inputImage.GetLength(1)];
			float[,] dxMap = new float[inputImage.GetLength(0), inputImage.GetLength(1)];
			float[,] dyMap = new float[inputImage.GetLength(0), inputImage.GetLength(1)];

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
				dxMap[x, y] = dx;
				dyMap[x, y] = dy;
				highestMagnitude = Math.Max(highestMagnitude, intensity);
			}

			for (int x = 0; x < w; x++)
			for (int y = 0; y < h; y++)
			{
				tempImage[x, y] = (byte)(edgeMagnitudeRaw[x, y] / highestMagnitude * 255.0f);
				edgeMagnitudeRaw[x, y] /= highestMagnitude;
				//tempImage[x, y] = (byte)Math.Min(edgeMagnitudeRaw[x, y], 255);
			}

			return (tempImage, edgeMagnitudeRaw, dxMap, dyMap);
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

		// EXTRA 1: CANNY EDGE DETECTION.
		private (int[,], int) CannyEdgeDetection(byte[,] image, float sigma, float lo, float hi)
		{
			int w = image.GetLength(0);
			int h = image.GetLength(1);

			byte[,] smoothImage = ConvolveImage(image, CreateGaussianFilter((byte)(5*sigma + 1), sigma));
			(_, float[,] edgeMagnitudes, float[,] dxMap, float[,] dyMap) = EdgeMagnitude(smoothImage, horizontalSobelKernel3x3, verticalSobelKernel3x3);

			float[,] localMaxima = new float[w, h];
			bool[,] edgePixels = new bool[w, h];

			// Step 2. Compute local maxima.
			for (int x = 1; x < w-1; x++)
			for (int y = 1; y < h-1; y++)
			{
				if (IsLocalMaxima(x, y, edgeMagnitudes, dxMap, dyMap, lo))
				{
					localMaxima[x, y] = edgeMagnitudes[x, y];
				}
			}

			// Step 3. Trace and threshold.
			// All color classes 1 and above are edges.
			int[,] colorClasses = new int[w, h];
			int currentColorClass = 1;

			for (int x = 0; x < w; x++)
			for (int y = 0; y < h; y++)
			{
				// If this is a local maxima above the hysteresis threshold,
				// perform a flood-fill to try and extract the edge.
				if (localMaxima[x, y] > hi && !edgePixels[x, y])
				{
					TraceAndThreshold(x, y, localMaxima, edgePixels, lo, colorClasses, currentColorClass);
					currentColorClass++;
				}
			}

			return (colorClasses, currentColorClass-1);
		}

		// Returns an integer 0,1,2,4 for the sector (clockwise from right) that this dx,dy lies in.
		// Uses technique of the Advanced Algorithms book by rotating by PI/8
		int GetCompassSector(float dx, float dy)
		{
			// Rotate by PI/8.
			float cos8 = MathF.Cos(MathF.PI / 8);
			float sin8 = MathF.Sin(MathF.PI / 8);
			float _dx = cos8 * dx - sin8 * dy;
			float _dy = sin8 * dx + cos8 * dy;

			// Mirror to positive octants.
			if (_dy < 0)
			{
				_dx *= -1;
				_dy *= -1;
			}
			return _dx > 0 ? (_dx > _dy ? 0: 1): 
				(-_dx < _dy ? 2: 3);
		}

		bool IsLocalMaxima(int u, int v, float[,] magnitudes, float[,] dx, float[,] dy, float lo)
		{
			float mag = magnitudes[u, v];

			// Can never be a local maximum of below the threshold.
			if (mag < lo)
			{
				return false;
			}

			int sector = GetCompassSector(dx[u, v], dy[u, v]);
			float magL = 0;
			float magR = 0;

			switch(sector)
			{
				case 0: // Horizontal case.
				{
					magL = magnitudes[u-1,v];
					magR = magnitudes[u+1,v];
					break;
				}
				case 1: // Diagonal-right case.
				{
					magL = magnitudes[u-1,v-1];
					magR = magnitudes[u+1,v+1];
					break;
				}
				case 2: // Vertical case.
				{
					magL = magnitudes[u,v-1];
					magR = magnitudes[u,v+1];
					break;
				}
				case 3: // Diagonal-left case.
				{
					magL = magnitudes[u-1,v+1];
					magR = magnitudes[u+1,v-1];
					break;
				}
			}

			return magL < mag && magR < mag;
		}

		void TraceAndThreshold(int u, int v, float[,] localMaxima, bool[,] edgePixels, float lo, int[,] colorClasses, int currentColorClass)
		{
			// This pixel becomes part of an edge.
			// Also assign color class.
			edgePixels[u, v] = true;
			colorClasses[u, v] = currentColorClass;
	
			int w = localMaxima.GetLength(0);
			int h = localMaxima.GetLength(1);

			int x1 = Math.Max(u-1, 0);
			int x2 = Math.Min(u+1, w-1);

			int y1 = Math.Max(v-1, 0);
			int y2 = Math.Min(v+1, h-1);

			// Now iterate the neighborhood.
			for (int x = x1; x <= x2; x++)
			for (int y = y1; y <= y2; y++)
			{
				// If this pixel is a local maxima with sufficient value,
				// and it has not been seen yet -> Recurse into it.
				if (localMaxima[x, y] >= lo && !edgePixels[x, y])
				{
					TraceAndThreshold(x, y, localMaxima, edgePixels, lo, colorClasses, currentColorClass);
				}
			}
		}

		float[,] GetNormalizedFilter(sbyte[,] filter)
		{
			float[,] arr = new float[filter.GetLength(0), filter.GetLength(1)];
			float sum = 0.0f;
			for (int x = 0; x < filter.GetLength(0); x++)
			for (int y = 0; y < filter.GetLength(1); y++)
			{
				float f = filter[x, y];
				sum += MathF.Abs(f);
				arr[x, y] = f;
			}
			for (int x = 0; x < filter.GetLength(0); x++)
			for (int y = 0; y < filter.GetLength(1); y++)
			{
				arr[x, y] /= sum;
			}
			return arr;
		}

		byte[,] EdgeClassesToEdgeImage(int[,] classes)
		{
			int w = classes.GetLength(0);
			int h = classes.GetLength(1);
			byte[,] edgeMap = new byte[w, h];

			for (int x = 0; x < w; x++)
			for (int y = 0; y < h; y++)
			{
				edgeMap[x, y] = (byte)(classes[x, y] > 0 ? 255: 0);
			}
			
			return edgeMap;
		}

		// PART 2

		(int[,], int) SequentialRegionLabeling(byte[,] input)
		{
			int[,] outputLabels = new int[input.GetLength(0), input.GetLength(1)];

			// Define the 8 pixels in the neighborhood.
			int[,] n8 = {
				{ -1, 0 }, { -1, -1 }, { 0, -1 }, { 1, -1 }
			};

			int w = input.GetLength(0);
			int h = input.GetLength(1);
			int numLabels = 0;

			List<(int, int)> collisions = [];

			// Step 1: Assign labels.
			for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				// Set default value.
				if (input[x, y] == 0)
				{
					outputLabels[x, y] = 0;
					continue;
				}

				// Determine smallest neighboring label, if any.
				List<int> neighboringLabels = [];
				for (int i = 0; i < 4; i++)
				{
					int nx = x + n8[i, 0];
					int ny = y + n8[i, 1];
					if (nx >= 0 && nx < w && ny >= 0)
					{
						// Add the label if it is not a background pixel.
						int lb = outputLabels[nx, ny];
						if (lb > 0)
						{
							neighboringLabels.Add(lb);
						}
					}
				}

				int label = numLabels + 1;
				if (neighboringLabels.Count == 0)
				{
					numLabels++;
				}
				else
				{
					label = neighboringLabels[0];
					if (neighboringLabels.Count > 1)
					{
						for (int i = 1; i < neighboringLabels.Count; i++)
						{
							collisions.Add((label, neighboringLabels[i]));
						}
					}
				}
				outputLabels[x, y] = label;
			}

			// Step 2: Resolve collisions.
			
			// Create initial partition of label sets.
			List<List<int>> labelSets = [];
			for (int i = 0; i < numLabels; i++)
			{
				labelSets.Add([ i+1 ]);
			}

			foreach (var (a, b) in collisions)
			{
				// Find sets containing these labels.
				List<int> aSet = labelSets.Find((list) => list.Contains(a));
				List<int> bSet = labelSets.Find((list) => list.Contains(b));

				// Merge if not the same.
				if (aSet != bSet)
				{
					aSet.AddRange(bSet.Except(aSet));
					labelSets.Remove(bSet);
				}
			}

			// Step 3: Relabel image.
			for (int x = 0; x < w; x++)
			for (int y = 0; y < h; y++)
			{
				int label = outputLabels[x, y];
				if (label == 0)
					continue;
				int regionIndex = labelSets.FindIndex((list) => list.Contains(label));
				outputLabels[x, y] = regionIndex + 1;
			}

			return (outputLabels, labelSets.Count);
		}

		int[,] HoughTransform(byte[,] edgeImage, int nOutput)
		{
			int w = edgeImage.GetLength(0);
			int h = edgeImage.GetLength(1);
			int[,] accum = new int[nOutput, nOutput];

			double xCenter = (double)w / 2;
			double yCenter = (double)h / 2;

			double rStep = 2 * Math.Sqrt(xCenter * xCenter + yCenter * yCenter) / nOutput;
			float thetaStep = MathF.PI / nOutput;

			for (int x = 0; x < w; x++)
			for (int y = 0; y < h; y++)
			{
				if (edgeImage[x, y] == 0)
				{
					continue;
				}

				double xrel = x - xCenter;
				double yrel = y - yCenter;

				// Iterate all possible theta.
				for (int i = 0; i < nOutput; i++)
				{
					double theta = i * thetaStep;
					double r = xrel * Math.Cos(theta) + yrel * Math.Sin(theta);
					int rIndex = Math.Clamp(nOutput / 2 + (int)Math.Round(r / rStep), 0, nOutput-1);

					// Insert into accumulator array.
					accum[i, rIndex] += 1;
				}
			}

			return accum;
		}

		byte[,] NormalizedHoughTransform(byte[,] edgeImage, int nOutput)
		{
			int w = edgeImage.GetLength(0);
			int h = edgeImage.GetLength(1);
			byte[,] white = new byte[w, h];
			
			for (int x = 0; x < w; x++)
			for (int y = 0; y < h; y++)
			{
				white[x, y] = 255;
			}

			int[,] maxCounts = HoughTransform(white, nOutput);
			int[,] hough = HoughTransform(edgeImage, nOutput);
			byte[,] normalizedHough = new byte[nOutput, nOutput];

			for (int x = 0; x < nOutput; x++)
			for (int y = 0; y < nOutput; y++)
			{
				normalizedHough[x, y] = (byte)(255 * (float)hough[x, y] / maxCounts[x, y]);
			}

			return normalizedHough;
		}

		WriteableBitmap ColorArrayToBitmap(Color[,] colorImage)
		{
			int w = colorImage.GetLength(0);
			int h = colorImage.GetLength(1);
			var bmp = new WriteableBitmap(new PixelSize(w, h), new(96, 96), PixelFormat.Rgba8888,
					AlphaFormat.Opaque);
			using var fb = bmp.Lock();
			byte[] buffer = new byte[fb.RowBytes * h];
			for (int y = 0; y < h; y++)
			{
				int rowStart = y * fb.RowBytes;
				for (int x = 0; x < w; x++)
				{
					int idx = rowStart + x * 4;
					buffer[idx + 0] = colorImage[x, y].R; // R
					buffer[idx + 1] = colorImage[x, y].G; // G
					buffer[idx + 2] = colorImage[x, y].B; // B
					buffer[idx + 3] = 255;
				}
			}
			Marshal.Copy(buffer, 0, fb.Address, buffer.Length);
			return bmp;
		}

		// Get random colors that are guaranteed to be evenly spaced by hue.
		Color[] GetRandomColors(int count, int seed=0)
		{
			// Assign random hues to each of the color classes.
			int[] shuffled = [ .. Enumerable.Range(0, count) ];
			Random rnd = new(seed);
			rnd.Shuffle(shuffled);

			Color[] colors = new Color[count];
			for (int i = 0; i < count; i++)
			{
				colors[i] = new HsvColor(1.0, (float)shuffled[i] / count * 360.0f, 1.0, 1.0).ToRgb();
			}

			return colors;
		}

		byte[,] NormalizeIntImage(int[,] input)
		{
			int w = input.GetLength(0);
			int h = input.GetLength(1);
			byte[,] output = new byte[w, h];

			int max = Math.Max(1, input.Cast<int>().Max());

			for (int x = 0; x < w; x++)
			for (int y = 0; y < h; y++)
			{
				output[x, y] = (byte)(255 * (float)input[x, y] / max);
			}

			return output;
		}
	}
}
