using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PetroLogStudio.Batch;
using PetroLogStudio.Parsing;
using PetroLogStudio.Conditioning;
using PetroLogStudio.Models;

namespace PetroLogStudio.UI
{
	public partial class MainWindow : Window
	{
		private readonly string _outputBaseDir;
		private readonly string _outputPlotDir;
		private string? _currentLasDirectory;

		public MainWindow()
		{
			InitializeComponent();
			_outputBaseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");
			_outputPlotDir = Path.Combine(_outputBaseDir, "plots");
			Directory.CreateDirectory(_outputPlotDir);
			_currentLasDirectory = FindTestDataDirectory();
		}

		private static string FindTestDataDirectory()
		{
			var currentDir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
			while (currentDir != null)
			{
				string candidate = Path.Combine(currentDir.FullName, "TestData");
				if (Directory.Exists(candidate))
				{
					return candidate;
				}
				currentDir = currentDir.Parent;
			}
			return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData");
		}

		private async void BtnSelectFolder_Click(object sender, RoutedEventArgs e)
		{
			var dialog = new OpenFolderDialog
			{
				Title = "Select LAS Directory"
			};

			if (dialog.ShowDialog() == true)
			{
				string selectedFolder = dialog.FolderName;
				var lasFiles = Directory.GetFiles(selectedFolder, "*.las");

				if (lasFiles.Length == 0)
				{
					MessageBox.Show("No .las files found in selected directory.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
					return;
				}

				_currentLasDirectory = selectedFolder;
				ListWells.ItemsSource = lasFiles.Select(Path.GetFileName).ToList();
				BtnSelectFolder.IsEnabled = false;
				BatchProgress.IsIndeterminate = false;
				BatchProgress.Minimum = 0;
				BatchProgress.Maximum = lasFiles.Length;
				BatchProgress.Value = 0;

				var progressReporter = new Progress<(int Current, int Total, string FileName)>(report =>
				{
					BatchProgress.Value = report.Current;
					TxtStatus.Text = $"[{report.Current}/{report.Total}] Processing {report.FileName}...";
				});

				await Task.Run(() =>
				{
					var processor = new BatchProcessor();
					processor.ProcessDirectory(selectedFolder, _outputBaseDir, progressReporter);
				});

				BtnSelectFolder.IsEnabled = true;
				TxtStatus.Text = $"Completed! Processed {lasFiles.Length} files.";

				if (ListWells.Items.Count > 0)
				{
					ListWells.SelectedIndex = 0;
				}
			}
		}

		private enum ViewMode
		{
			FitWidth,
			FitWindow,
			CustomZoom
		}

		private ViewMode _currentViewMode = ViewMode.FitWidth;
		private double _customZoom = 1.0;

		private void ListWells_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (ListWells.SelectedItem == null) return;

			string selectedFile = ListWells.SelectedItem.ToString()!;
			string wellBaseName = Path.GetFileNameWithoutExtension(selectedFile);

			// 1. Locate and parse LAS file to extract diagnostic info
			string? lasFilePath = null;
			if (!string.IsNullOrEmpty(_currentLasDirectory))
			{
				string candidate = Path.Combine(_currentLasDirectory, selectedFile);
				if (File.Exists(candidate)) lasFilePath = candidate;
			}

			if (lasFilePath == null)
			{
				string testDataDir = FindTestDataDirectory();
				string candidate = Path.Combine(testDataDir, selectedFile);
				if (File.Exists(candidate)) lasFilePath = candidate;
			}

			if (lasFilePath != null && File.Exists(lasFilePath))
			{
				try
				{
					var parser = new LasParser();
					var well = parser.ParseFile(lasFilePath);
					var conditioner = new DataConditioner();
					conditioner.ConditionWell(well);

					// Update Quick Overview on Left Panel
					TxtWellName.Text = $"Well: {well.WellName}";
					TxtWellDepth.Text = $"Depth: {well.StartDepth:F1} - {well.StopDepth:F1} ft (Step: {well.Step:F1})";
					int rowCount = well.Curves.ContainsKey("DEPT") ? well.Curves["DEPT"].Values.Count : (well.Curves.Count > 0 ? well.Curves.First().Value.Values.Count : 0);
					TxtWellRows.Text = $"Samples: {rowCount:N0} rows";

					var curveNames = well.Curves.Values.Take(6).Select(c => c.Definition.Mnemonic);
					TxtWellCurves.Text = $"Curves: {string.Join(", ", curveNames)}{(well.Curves.Count > 6 ? $" (+{well.Curves.Count - 6})" : "")}";

					if (well.ParseErrors.Count > 0)
					{
						TxtWellStatus.Text = $"Status: {well.ParseErrors.Count} parse error(s)";
						TxtWellStatus.Foreground = System.Windows.Media.Brushes.Crimson;
					}
					else if (well.ParseNotices.Count > 0)
					{
						TxtWellStatus.Text = "Status: Clean with notices";
						TxtWellStatus.Foreground = System.Windows.Media.Brushes.DarkOrange;
					}
					else
					{
						TxtWellStatus.Text = "Status: Healthy / Clean";
						TxtWellStatus.Foreground = System.Windows.Media.Brushes.SeaGreen;
					}

					// Update Full Diagnostic Log in Tab 2
					TxtDiagnosticLog.Text = GenerateDiagnosticText(well);
				}
				catch (Exception ex)
				{
					TxtDiagnosticLog.Text = $"Error reading diagnostics: {ex.Message}";
				}
			}

			// 2. Locate and display composite plot
			var matchFile = Directory.GetFiles(_outputPlotDir, $"*{wellBaseName}*_3Track.png", SearchOption.AllDirectories).FirstOrDefault();

			if (matchFile != null && File.Exists(matchFile))
			{
				var bitmap = new BitmapImage();
				bitmap.BeginInit();
				bitmap.CacheOption = BitmapCacheOption.OnLoad;
				bitmap.UriSource = new Uri(matchFile);
				bitmap.EndInit();

				ImgPlotPreview.Source = bitmap;
				TxtNoPlot.Visibility = Visibility.Collapsed;

				// Retain current view mode (FitWidth, FitWindow, or custom zoom) when switching files
				Dispatcher.BeginInvoke(new Action(ApplyCurrentViewMode), System.Windows.Threading.DispatcherPriority.Loaded);
			}
			else
			{
				ImgPlotPreview.Source = null;
				TxtNoPlot.Visibility = Visibility.Visible;
				TxtNoPlot.Text = "Plot image not found for this well log";
			}
		}

		private string GenerateDiagnosticText(WellLog well)
		{
			var sb = new StringBuilder();
			sb.AppendLine($"Well Diagnostics: {Path.GetFileName(well.FilePath)}");
			sb.AppendLine(new string('=', 65));
			sb.AppendLine();
			sb.AppendLine("[1] Well Header Information:");
			sb.AppendLine($"  Well Name   : {well.WellName}");
			sb.AppendLine($"  Null Value  : {well.NullValue}");
			sb.AppendLine($"  Start Depth : {well.StartDepth}");
			sb.AppendLine($"  Stop Depth  : {well.StopDepth}");
			sb.AppendLine($"  Step        : {well.Step}");
			sb.AppendLine();
			sb.AppendLine($"[2] Curves Discovered ({well.Curves.Count} total):");
			foreach (var kvp in well.Curves)
			{
				var def = kvp.Value.Definition;
				sb.AppendLine($"  - Col {def.ColumnIndex,2}: {def.Mnemonic,-12} | Unit: {def.Unit,-8} | Desc: {def.Description}");
			}
			sb.AppendLine();
			sb.AppendLine("[3] Data Section Verification:");
			if (well.Curves.ContainsKey("DEPT"))
			{
				var deptCurve = well.Curves["DEPT"];
				sb.AppendLine($"  Total Depth Rows : {deptCurve.Values.Count:N0}");
				if (deptCurve.Values.Count > 0)
				{
					sb.AppendLine($"  First Depth Value: {deptCurve.Values[0]}");
					sb.AppendLine($"  Last Depth Value : {deptCurve.Values[^1]}");
				}
			}
			else
			{
				sb.AppendLine("  Warning: 'DEPT' mnemonic not found directly. Check available curve keys.");
			}
			sb.AppendLine();
			sb.AppendLine("[4] Parser Diagnostics:");
			if (well.ParseErrors.Count == 0 && well.ParseNotices.Count == 0)
			{
				sb.AppendLine("  No parsing errors or notices encountered! Clean parse.");
			}
			else
			{
				if (well.ParseErrors.Count > 0)
				{
					sb.AppendLine($"  Errors ({well.ParseErrors.Count}):");
					foreach (var err in well.ParseErrors)
					{
						sb.AppendLine($"    ! [Error] {err}");
					}
				}
				if (well.ParseNotices.Count > 0)
				{
					sb.AppendLine($"  Notices ({well.ParseNotices.Count}):");
					foreach (var note in well.ParseNotices)
					{
						sb.AppendLine($"    * [Notice] {note}");
					}
				}
			}
			sb.AppendLine();
			sb.AppendLine("[5] Data Conditioning Report:");
			if (well.ConditioningLogs.Count == 0)
			{
				sb.AppendLine("  No conditioning logs recorded.");
			}
			else
			{
				foreach (var log in well.ConditioningLogs)
				{
					sb.AppendLine($"  * {log}");
				}
			}

			return sb.ToString();
		}

		private void ApplyCurrentViewMode()
		{
			if (ImgPlotPreview.Source == null) return;

			switch (_currentViewMode)
			{
				case ViewMode.FitWidth:
					FitWidth();
					break;
				case ViewMode.FitWindow:
					FitWindow();
					break;
				case ViewMode.CustomZoom:
					SetZoom(_customZoom, updateCustomZoom: false);
					break;
			}
		}

		private void SetZoom(double zoom, bool updateCustomZoom = true)
		{
			zoom = Math.Clamp(zoom, 0.1, 3.0);
			if (updateCustomZoom)
			{
				_customZoom = zoom;
			}

			if (PlotScaleTransform != null)
			{
				PlotScaleTransform.ScaleX = zoom;
				PlotScaleTransform.ScaleY = zoom;
			}
			if (SliderZoom != null && Math.Abs(SliderZoom.Value - zoom) > 0.001)
			{
				SliderZoom.Value = zoom;
			}
			if (TxtZoomLevel != null)
			{
				TxtZoomLevel.Text = $"{(int)Math.Round(zoom * 100)}%";
			}
		}

		private void SliderZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
		{
			if (IsLoaded)
			{
				_currentViewMode = ViewMode.CustomZoom;
				SetZoom(e.NewValue);
			}
		}

		private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
		{
			_currentViewMode = ViewMode.CustomZoom;
			SetZoom((PlotScaleTransform?.ScaleX ?? 1.0) + 0.15);
		}

		private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
		{
			_currentViewMode = ViewMode.CustomZoom;
			SetZoom((PlotScaleTransform?.ScaleX ?? 1.0) - 0.15);
		}

		private void BtnActualSize_Click(object sender, RoutedEventArgs e)
		{
			_currentViewMode = ViewMode.CustomZoom;
			SetZoom(1.0);
		}

		private void BtnFitWidth_Click(object sender, RoutedEventArgs e)
		{
			_currentViewMode = ViewMode.FitWidth;
			FitWidth();
		}

		private void BtnFitWindow_Click(object sender, RoutedEventArgs e)
		{
			_currentViewMode = ViewMode.FitWindow;
			FitWindow();
		}

		private void FitWidth()
		{
			if (ImgPlotPreview.Source is BitmapSource bmp && bmp.PixelWidth > 0 && PlotScrollViewer.ViewportWidth > 20)
			{
				double targetScale = (PlotScrollViewer.ViewportWidth - 25) / bmp.PixelWidth;
				SetZoom(targetScale, updateCustomZoom: false);
			}
		}

		private void FitWindow()
		{
			if (ImgPlotPreview.Source is BitmapSource bmp && bmp.PixelWidth > 0 && bmp.PixelHeight > 0)
			{
				double availW = Math.Max(100, PlotScrollViewer.ViewportWidth - 25);
				double availH = Math.Max(100, PlotScrollViewer.ViewportHeight - 25);
				double scaleX = availW / bmp.PixelWidth;
				double scaleY = availH / bmp.PixelHeight;
				SetZoom(Math.Min(scaleX, scaleY), updateCustomZoom: false);
			}
		}

		private void PlotScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
		{
			if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
			{
				_currentViewMode = ViewMode.CustomZoom;
				double delta = e.Delta > 0 ? 0.12 : -0.12;
				SetZoom((PlotScaleTransform?.ScaleX ?? 1.0) + delta);
				e.Handled = true;
			}
		}

		private void PlotScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
		{
			if (ImgPlotPreview.Source != null)
			{
				if (_currentViewMode == ViewMode.FitWidth) FitWidth();
				else if (_currentViewMode == ViewMode.FitWindow) FitWindow();
			}
		}
	}
}