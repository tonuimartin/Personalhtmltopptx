using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HtmlToSlidesPro.Models;
using HtmlToSlidesPro.Services;

namespace HtmlToSlidesPro;

public partial class MainWindow
{
    private const int QaLineHitHeight = 20;
    private const int MinSplitGap = 1;
    private const int MinRegionHeightPx = 72;

    private List<int> _qaSplitYs = [];
    private int _qaPageHeight;
    private int _qaPageWidth = ConversionOptions.DefaultViewportWidth;
    private int _qaImageWidth;
    private int _qaImageHeight;
    private bool _qaZoomComboReady;
    private double _qaDisplayScale = 1;
    private bool _qaPreviewLoaded;
    private bool _qaPreviewStale = true;
    private Border? _draggingQaLine;
    private int _dragQaLineIndex = -1;
    private double _dragQaStartMouseY;
    private int _dragQaStartSplitY;
    private int _selectedQaLineIndex = -1;

    private async void DiagnosticsTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DiagnosticsTabs.SelectedIndex != 1 || !_qaPreviewStale)
            return;

        await RefreshQaPreviewAsync();
    }

    private async void RefreshQaPreview_Click(object sender, RoutedEventArgs e) => await RefreshQaPreviewAsync();

    private async Task RefreshQaPreviewAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        RefreshQaPreviewButton.IsEnabled = false;
        StatusText.Text = "Loading QA split preview (Puppeteer)…";

        try
        {
            var (html, filePath) = GetHtmlInput();
            var options = new ConversionOptions();
            var statusProgress = new Progress<string>(s => Dispatcher.Invoke(() => StatusText.Text = s));

            var result = await _pipeline.CaptureFullPagePreviewAsync(html, filePath, options, statusProgress, ct);

            _qaPageWidth = result.ViewportWidth;
            _qaPageHeight = result.TotalHeight;
            _qaSplitYs = result.DefaultSplitYs.ToList();
            _qaPreviewStale = false;
            _splitsApplied = false;
            _splitsDirty = false;
            _appliedSplitYs = [];
            _appliedPreviewHeightPx = 0;

            var image = LoadImage(result.ImagePath);
            if (image is null)
                throw new InvalidOperationException("Puppeteer capture image could not be loaded.");

            QaPageImage.Source = image;
            _qaImageWidth = image.PixelWidth;
            _qaImageHeight = image.PixelHeight;
            if (_qaImageWidth > 0)
                _qaPageWidth = _qaImageWidth;
            if (_qaImageHeight > 0)
                _qaPageHeight = _qaImageHeight;

            _qaPreviewLoaded = true;
            QaPreviewPlaceholder.Visibility = Visibility.Collapsed;
            QaSplitFrame.Visibility = Visibility.Visible;

            ScheduleQaDisplayLayout();
            UpdateSplitStatusText();
            StatusText.Text = "QA split preview ready — drag red lines on the Puppeteer capture.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"QA preview failed: {ex.Message}";
            MessageBox.Show(ex.Message, "QA preview failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RefreshQaPreviewButton.IsEnabled = true;
        }
    }

    private void QaPageScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_qaPreviewLoaded)
            ScheduleQaDisplayLayout();
    }

    private void QaZoomCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_qaZoomComboReady || !_qaPreviewLoaded)
            return;

        ScheduleQaDisplayLayout();
    }

    private void ScheduleQaDisplayLayout()
    {
        Dispatcher.BeginInvoke(UpdateQaDisplayLayout, DispatcherPriority.Loaded);
    }

    private void UpdateQaDisplayLayout()
    {
        if (!_qaPreviewLoaded || _qaPageHeight <= 0 || QaPageImage.Source is null)
            return;

        var available = GetQaAvailableWidth();
        if (available <= 1)
        {
            ScheduleQaDisplayLayout();
            return;
        }

        var sourceWidth = Math.Max(1, _qaImageWidth > 0 ? _qaImageWidth : _qaPageWidth);
        var sourceHeight = Math.Max(1, _qaImageHeight > 0 ? _qaImageHeight : _qaPageHeight);
        var fitScale = available / sourceWidth;
        _qaDisplayScale = GetSelectedQaZoomScale(fitScale);

        var displayW = sourceWidth * _qaDisplayScale;
        var displayH = sourceHeight * _qaDisplayScale;

        QaPageImage.Width = displayW;
        QaPageImage.Height = displayH;
        QaSplitCanvas.Width = displayW;
        QaSplitCanvas.Height = displayH;
        QaSplitHost.Width = displayW;
        QaSplitHost.Height = displayH;
        QaSplitFrame.Width = displayW;
        QaSplitFrame.Height = displayH;

        RenderQaSplitLines();
    }

    private double GetSelectedQaZoomScale(double fitScale)
    {
        var index = QaZoomCombo?.SelectedIndex ?? 0;
        return index switch
        {
            1 => Math.Min(1, fitScale) * 0.75,
            2 => Math.Min(1, fitScale) * 0.50,
            3 => 1.0,
            _ => fitScale
        };
    }

    private double GetQaAvailableWidth()
    {
        if (QaPageScrollViewer.ViewportWidth > 1)
            return QaPageScrollViewer.ViewportWidth;

        if (QaPageScrollViewer.ActualWidth > 1)
        {
            var verticalBar = QaPageScrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible ? 18 : 0;
            return Math.Max(1, QaPageScrollViewer.ActualWidth - verticalBar - 2);
        }

        if (DiagnosticsPanel.ActualWidth > 1)
            return Math.Max(1, DiagnosticsPanel.ActualWidth - 32);

        return 360;
    }

    private void RenderQaSplitLines()
    {
        if (_draggingQaLine is not null)
            return;

        QaSplitCanvas.Children.Clear();

        foreach (var (y, index) in _qaSplitYs.Select((y, i) => (y, i)))
        {
            var handle = CreateQaSplitHandle(y, index);
            QaSplitCanvas.Children.Add(handle);
        }
    }

    private Border CreateQaSplitHandle(int y, int index)
    {
        var handle = new Border
        {
            Height = QaLineHitHeight,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeNS,
            ToolTip = $"Slide {index + 1} | {index + 2} @ {y}px"
        };

        var bar = new Border
        {
            Height = index == _selectedQaLineIndex ? 5 : 3,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(index == _selectedQaLineIndex
                ? Color.FromRgb(190, 24, 93)
                : Color.FromRgb(225, 29, 72)),
            IsHitTestVisible = false
        };
        handle.Child = bar;

        var lineWidth = Math.Max(1, QaSplitCanvas.Width);
        handle.Width = lineWidth;

        Canvas.SetTop(handle, y * _qaDisplayScale - QaLineHitHeight / 2);
        Canvas.SetLeft(handle, 0);

        var lineIndex = index;
        handle.MouseLeftButtonDown += (_, e) =>
        {
            _selectedQaLineIndex = lineIndex;
            UpdateQaLineSelectionHighlights();
            StartQaLineDrag(lineIndex, handle, e);
        };
        handle.MouseRightButtonDown += (_, e) => RemoveQaSplitAt(lineIndex, e);
        return handle;
    }

    private void UpdateQaLineSelectionHighlights()
    {
        for (var i = 0; i < QaSplitCanvas.Children.Count; i++)
        {
            if (QaSplitCanvas.Children[i] is not Border handle || handle.Child is not Border bar)
                continue;

            var selected = i == _selectedQaLineIndex;
            bar.Height = selected ? 5 : 3;
            bar.Background = new SolidColorBrush(selected
                ? Color.FromRgb(190, 24, 93)
                : Color.FromRgb(225, 29, 72));
        }
    }

    private void QaSplitCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_qaPreviewLoaded || e.ClickCount < 2 || e.OriginalSource is not Canvas)
            return;

        var y = (int)Math.Round(e.GetPosition(QaSplitCanvas).Y / _qaDisplayScale);
        if (TryInsertSplitAt(y))
            e.Handled = true;
    }

    private void AddQaSplit_Click(object sender, RoutedEventArgs e)
    {
        if (!_qaPreviewLoaded)
        {
            StatusText.Text = "Load QA preview first.";
            return;
        }

        var boundaries = GetSplitBoundaries();
        var bestMidpoint = -1;
        var bestGap = 0;
        for (var i = 0; i < boundaries.Count - 1; i++)
        {
            var gap = boundaries[i + 1] - boundaries[i];
            if (gap > bestGap)
            {
                bestGap = gap;
                bestMidpoint = boundaries[i] + gap / 2;
            }
        }

        if (bestMidpoint < 0 || bestGap < MinRegionHeightPx * 2)
        {
            StatusText.Text = $"Cannot add split — each slide needs at least {MinRegionHeightPx}px.";
            return;
        }

        TryInsertSplitAt(bestMidpoint);
    }

    private void RemoveQaSplit_Click(object sender, RoutedEventArgs e)
    {
        if (!_qaPreviewLoaded)
        {
            StatusText.Text = "Load QA preview first.";
            return;
        }

        if (_qaSplitYs.Count == 0)
        {
            StatusText.Text = "No splits to remove.";
            return;
        }

        var index = _selectedQaLineIndex >= 0 && _selectedQaLineIndex < _qaSplitYs.Count
            ? _selectedQaLineIndex
            : _qaSplitYs.Count - 1;
        RemoveQaSplitAt(index);
    }

    private void RemoveQaSplitAt(int index, MouseButtonEventArgs? e = null)
    {
        if (!_qaPreviewLoaded || index < 0 || index >= _qaSplitYs.Count)
            return;

        _qaSplitYs.RemoveAt(index);
        _selectedQaLineIndex = -1;
        if (_splitsApplied)
            _splitsDirty = true;

        RenderQaSplitLines();
        UpdateSplitStatusText();
        StatusText.Text = $"Removed split — {_qaSplitYs.Count + 1} slide(s).";
        if (e is not null)
            e.Handled = true;
    }

    private bool TryInsertSplitAt(int y)
    {
        var boundaries = GetSplitBoundaries();
        for (var i = 0; i < boundaries.Count - 1; i++)
        {
            var top = boundaries[i];
            var bottom = boundaries[i + 1];
            if (y <= top || y >= bottom)
                continue;

            if (y - top < MinRegionHeightPx || bottom - y < MinRegionHeightPx)
            {
                StatusText.Text = $"Cannot add split — keep at least {MinRegionHeightPx}px per slide.";
                return false;
            }

            var ordered = _qaSplitYs.ToList();
            ordered.Add(y);
            _qaSplitYs = ClampQaSplits(ordered);
            _selectedQaLineIndex = _qaSplitYs.IndexOf(y);
            if (_selectedQaLineIndex < 0)
                _selectedQaLineIndex = _qaSplitYs.Count - 1;

            if (_splitsApplied)
                _splitsDirty = true;

            RenderQaSplitLines();
            UpdateSplitStatusText();
            StatusText.Text = $"Added split @ {y}px — {_qaSplitYs.Count + 1} slide(s).";
            return true;
        }

        StatusText.Text = $"Cannot add split — keep at least {MinRegionHeightPx}px per slide.";
        return false;
    }

    private List<int> GetSplitBoundaries()
    {
        var boundaries = new List<int> { 0 };
        boundaries.AddRange(_qaSplitYs.OrderBy(y => y));
        boundaries.Add(_qaPageHeight);
        return boundaries;
    }

    private void StartQaLineDrag(int index, Border handle, MouseButtonEventArgs e)
    {
        _draggingQaLine = handle;
        _dragQaLineIndex = index;
        _dragQaStartMouseY = e.GetPosition(QaSplitCanvas).Y;
        _dragQaStartSplitY = _qaSplitYs[index];
        QaSplitCanvas.CaptureMouse();
        QaSplitCanvas.MouseMove += QaLine_MouseMove;
        QaSplitCanvas.MouseLeftButtonUp += QaLine_MouseUp;
        e.Handled = true;

        if (_splitsApplied)
            _splitsDirty = true;
        UpdateSplitStatusText();
    }

    private void QaLine_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingQaLine is null || _dragQaLineIndex < 0)
            return;

        var delta = (e.GetPosition(QaSplitCanvas).Y - _dragQaStartMouseY) / _qaDisplayScale;
        var tentative = _qaSplitYs.ToList();
        tentative[_dragQaLineIndex] = ClampSingleQaSplit(_dragQaLineIndex, tentative, (int)Math.Round(_dragQaStartSplitY + delta));
        _qaSplitYs = tentative;

        Canvas.SetTop(_draggingQaLine, _qaSplitYs[_dragQaLineIndex] * _qaDisplayScale - QaLineHitHeight / 2);
        _draggingQaLine.ToolTip = $"Slide {_dragQaLineIndex + 1} | {_dragQaLineIndex + 2} @ {_qaSplitYs[_dragQaLineIndex]}px";
    }

    private void QaLine_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggingQaLine is null)
            return;

        QaSplitCanvas.ReleaseMouseCapture();
        QaSplitCanvas.MouseMove -= QaLine_MouseMove;
        QaSplitCanvas.MouseLeftButtonUp -= QaLine_MouseUp;
        _draggingQaLine = null;
        _dragQaLineIndex = -1;

        _qaSplitYs = ClampQaSplits(_qaSplitYs);
        RenderQaSplitLines();
        UpdateSplitStatusText();
        e.Handled = true;
    }

    private List<int> ClampQaSplits(IReadOnlyList<int> splits)
    {
        if (_qaPageHeight <= MinSplitGap)
            return [];

        var ordered = splits.OrderBy(y => y).ToList();
        var result = new List<int>();
        var prev = 0;
        foreach (var raw in ordered)
        {
            var minY = prev + MinSplitGap;
            var maxY = _qaPageHeight - MinSplitGap;
            if (minY > maxY)
                break;
            var y = Math.Clamp((int)Math.Round((double)raw), minY, maxY);
            if (y > prev)
            {
                result.Add(y);
                prev = y;
            }
        }

        return result;
    }

    private int ClampSingleQaSplit(int index, IReadOnlyList<int> splits, int value)
    {
        var minY = index == 0 ? MinSplitGap : splits[index - 1] + MinSplitGap;
        var maxY = index == splits.Count - 1
            ? _qaPageHeight - MinSplitGap
            : splits[index + 1] - MinSplitGap;
        return Math.Clamp(value, minY, maxY);
    }

    private void ResetQaSplits_Click(object sender, RoutedEventArgs e)
    {
        if (!_qaPreviewLoaded)
        {
            StatusText.Text = "Load QA preview first.";
            return;
        }

        _qaSplitYs = SlideSplitHelper.ComputeDefaultSplits(_qaPageHeight, ConversionOptions.DefaultViewportWidth * 9 / 16);
        _selectedQaLineIndex = -1;
        _splitsApplied = false;
        _splitsDirty = false;
        _appliedSplitYs = [];
        RenderQaSplitLines();
        UpdateSplitStatusText();
    }

    private ConversionOptions BuildSplitOptionsFromQa()
    {
        if (!_qaPreviewLoaded)
            throw new InvalidOperationException("Load the QA split preview first.");

        return new ConversionOptions
        {
            UsePreviewSlideSplits = true,
            SlideSplitYs = _qaSplitYs.ToList(),
            PreviewTotalHeightPx = _qaPageHeight
        };
    }

    private void InvalidateQaPreview()
    {
        _qaPreviewStale = true;
        _qaPreviewLoaded = false;
        _splitsApplied = false;
        _splitsDirty = false;
        _appliedSplitYs = [];
        _qaSplitYs = [];
        _selectedQaLineIndex = -1;
        _qaImageWidth = 0;
        _qaImageHeight = 0;
        QaPreviewPlaceholder.Visibility = Visibility.Visible;
        QaSplitFrame.Visibility = Visibility.Collapsed;
        QaPageImage.Source = null;
        QaSplitCanvas.Children.Clear();
        UpdateSplitStatusText();
    }

    private static BitmapImage? LoadImage(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.UriSource = new Uri(path);
        img.EndInit();
        img.Freeze();
        return img;
    }
}
