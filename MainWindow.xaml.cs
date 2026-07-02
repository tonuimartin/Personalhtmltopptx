using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Imaging;
using HtmlToSlidesPro.Models;
using HtmlToSlidesPro.Services;
using Microsoft.Win32;

namespace HtmlToSlidesPro;

public partial class MainWindow : Window
{
    private readonly ConversionPipeline _pipeline = new();
    private readonly ObservableCollection<DiagnosticRow> _diagnostics = [];
    private readonly ObservableCollection<QaRow> _qaRows = [];
    private CancellationTokenSource? _cts;
    private List<int> _appliedSplitYs = [];
    private int _appliedPreviewHeightPx;
    private bool _splitsApplied;
    private bool _splitsDirty;
    private bool _previewReady;
    private bool _diagnosticsVisible = true;
    private bool _livePreviewVisible = true;
    private GridLength _diagnosticsExpandedWidth = new(420);
    private GridLength _livePreviewExpandedWidth = new(1, GridUnitType.Star);

    public MainWindow()
    {
        InitializeComponent();
        _qaZoomComboReady = true;
        DiagnosticsGrid.ItemsSource = _diagnostics;
        QaItems.ItemsSource = _qaRows;
        HtmlSourceTabs.SelectionChanged += (_, _) => UpdatePreview();
        Loaded += async (_, _) => await InitWebViewAsync();
        UpdateSplitStatusText();
    }

    private async Task InitWebViewAsync()
    {
        try
        {
            await PreviewWebView.EnsureCoreWebView2Async();
            PreviewWebView.CoreWebView2.NavigationCompleted += async (_, _) => await InjectPreviewFontsAsync();
            _previewReady = true;
            UpdatePreview();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"WebView2 init failed: {ex.Message}";
        }
    }

    private async Task InjectPreviewFontsAsync()
    {
        if (PreviewWebView.CoreWebView2 is null) return;
        if (!Directory.Exists(LocalFontInjector.BundledFontsDirectory)) return;

        var css = new System.Text.StringBuilder();
        foreach (var ttf in Directory.GetFiles(LocalFontInjector.BundledFontsDirectory, "*.ttf"))
        {
            var family = Path.GetFileNameWithoutExtension(ttf);
            var uri = new Uri(Path.GetFullPath(ttf)).AbsoluteUri;
            css.Append("@font-face{font-family:'").Append(family).Append("';src:url('")
                .Append(uri).Append("') format('truetype');}");
        }

        if (css.Length == 0) return;
        var escaped = System.Text.Json.JsonSerializer.Serialize(css.ToString());
        await PreviewWebView.CoreWebView2.ExecuteScriptAsync(
            $"(() => {{ const s = document.createElement('style'); s.textContent = {escaped}; document.head.appendChild(s); }})()");
    }

    private void UpdatePreview()
    {
        if (PreviewWebView.CoreWebView2 is null) return;

        InvalidateQaPreview();

        if (HtmlSourceTabs.SelectedIndex == 0)
        {
            var path = HtmlFilePathBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                PreviewWebView.Source = new Uri(Path.GetFullPath(path));
                return;
            }
        }

        var html = HtmlPasteBox.Text;
        if (!string.IsNullOrWhiteSpace(html))
            PreviewWebView.NavigateToString(WrapHtml(html));
    }

    private static string WrapHtml(string html)
    {
        if (html.Contains("<html", StringComparison.OrdinalIgnoreCase)) return html;
        return $"<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>body{{margin:0}}</style></head><body>{html}</body></html>";
    }

    private void BrowseHtml_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "HTML files (*.html;*.htm)|*.html;*.htm|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            HtmlFilePathBox.Text = dlg.FileName;
            HtmlSourceTabs.SelectedIndex = 0;
            UpdatePreview();
        }
    }

    private void RefreshPreview_Click(object sender, RoutedEventArgs e) => UpdatePreview();

    private async void ApplySplits_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        ApplySplitsButton.IsEnabled = false;
        StatusText.Text = "Applying splits…";

        try
        {
            var (html, filePath) = GetHtmlInput();
            var options = BuildSplitOptionsFromQa();

            _appliedSplitYs = options.SlideSplitYs?.ToList() ?? [];
            _appliedPreviewHeightPx = options.PreviewTotalHeightPx;
            _splitsApplied = true;
            _splitsDirty = false;

            var statusProgress = new Progress<string>(s => Dispatcher.Invoke(() => StatusText.Text = s));
            var comparisons = await _pipeline.PreviewAppliedSplitsAsync(html, filePath, options, statusProgress, ct);

            _qaRows.Clear();
            foreach (var qa in comparisons)
                _qaRows.Add(QaRow.From(qa));

            UpdateSplitStatusText();
            StatusText.Text = $"Splits applied: {comparisons.Count} slide clip(s) ready for Convert.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Apply failed: {ex.Message}";
            MessageBox.Show(ex.Message, "Apply splits failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ApplySplitsButton.IsEnabled = true;
        }
    }

    private void UpdateSplitStatusText()
    {
        if (SplitStatusText is null) return;

        if (!_qaPreviewLoaded)
        {
            SplitStatusText.Text = "Refresh QA preview to load Puppeteer capture and drag split lines.";
            return;
        }

        if (!_splitsApplied)
        {
            var pending = _qaSplitYs.Count == 0
                ? "1 slide (no breaks)"
                : $"{_qaSplitYs.Count + 1} slides @ {string.Join(", ", _qaSplitYs)}px";
            SplitStatusText.Text = $"Current: {pending} — click Apply splits to confirm";
            return;
        }

        var splitSummary = _appliedSplitYs.Count == 0
            ? "1 slide (no breaks)"
            : $"{_appliedSplitYs.Count + 1} slides @ {string.Join(", ", _appliedSplitYs)}px";

        SplitStatusText.Text = _splitsDirty
            ? $"Applied: {splitSummary} — lines changed, re-apply before Convert"
            : $"Applied: {splitSummary}";
    }

    private (string Html, string? FilePath) GetHtmlInput()
    {
        if (HtmlSourceTabs.SelectedIndex == 0)
        {
            var path = HtmlFilePathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new InvalidOperationException("Select an HTML file or switch to Paste HTML.");
            return (File.ReadAllText(path), path);
        }

        var html = HtmlPasteBox.Text;
        if (string.IsNullOrWhiteSpace(html))
            throw new InvalidOperationException("Paste HTML or select a file.");
        return (html, null);
    }

    private Task ApplySplitOptionsForConversionAsync(ConversionOptions options)
    {
        if (_splitsApplied && !_splitsDirty)
        {
            options.UsePreviewSlideSplits = true;
            options.SlideSplitYs = _appliedSplitYs.ToList();
            options.PreviewTotalHeightPx = _appliedPreviewHeightPx;
            return Task.CompletedTask;
        }

        if (_qaPreviewLoaded)
        {
            var splitOptions = BuildSplitOptionsFromQa();
            options.UsePreviewSlideSplits = splitOptions.UsePreviewSlideSplits;
            options.SlideSplitYs = splitOptions.SlideSplitYs;
            options.PreviewTotalHeightPx = splitOptions.PreviewTotalHeightPx;
        }

        return Task.CompletedTask;
    }

    private void ToggleLivePreview_Click(object sender, RoutedEventArgs e) =>
        SetLivePreviewVisible(!_livePreviewVisible);

    private void SetLivePreviewVisible(bool visible)
    {
        _livePreviewVisible = visible;

        if (visible)
        {
            LivePreviewPanel.Visibility = Visibility.Visible;
            LivePreviewColumn.Width = _livePreviewExpandedWidth;
            if (_diagnosticsVisible)
                DiagnosticsColumn.Width = _diagnosticsExpandedWidth;
            ShowLivePreviewButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            _livePreviewExpandedWidth = LivePreviewColumn.Width;
            LivePreviewPanel.Visibility = Visibility.Collapsed;
            LivePreviewColumn.Width = new GridLength(0);
            if (_diagnosticsVisible)
                DiagnosticsColumn.Width = new GridLength(1, GridUnitType.Star);
            ShowLivePreviewButton.Visibility = Visibility.Visible;
        }

        if (_qaPreviewLoaded)
            ScheduleQaDisplayLayout();
    }

    private void ToggleDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        _diagnosticsVisible = !_diagnosticsVisible;

        if (_diagnosticsVisible)
        {
            DiagnosticsPanel.Visibility = Visibility.Visible;
            DiagnosticsColumn.Width = _livePreviewVisible
                ? _diagnosticsExpandedWidth
                : new GridLength(1, GridUnitType.Star);
            ToggleDiagnosticsButton.Content = "Hide diagnostics ▶";
        }
        else
        {
            if (DiagnosticsColumn.Width.IsAbsolute)
                _diagnosticsExpandedWidth = DiagnosticsColumn.Width;
            else
                _diagnosticsExpandedWidth = new GridLength(420);

            DiagnosticsPanel.Visibility = Visibility.Collapsed;
            DiagnosticsColumn.Width = new GridLength(0);
            ToggleDiagnosticsButton.Content = "◀ Show diagnostics";
        }
    }

    private void ModeLlmRadio_Changed(object sender, RoutedEventArgs e)
    {
        if (OllamaPanel is null) return;
        OllamaPanel.Visibility = ModeLlmRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private ConversionMode GetSelectedMode()
    {
        if (ModeLlmRadio.IsChecked == true) return ConversionMode.LlmSemantic;
        if (ModeEditableRadio.IsChecked == true) return ConversionMode.Editable;
        return ConversionMode.HighFidelity;
    }

    private async void Convert_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        ConvertButton.IsEnabled = false;
        _diagnostics.Clear();
        StatusText.Text = "Converting...";

        try
        {
            var mode = GetSelectedMode();
            var options = new ConversionOptions
            {
                Mode = mode,
                EmbedFonts = EmbedFontsCheck.IsChecked == true,
                LibreOfficePath = string.IsNullOrWhiteSpace(LibreOfficePathBox.Text) ? null : LibreOfficePathBox.Text.Trim(),
                OllamaEndpoint = string.IsNullOrWhiteSpace(OllamaEndpointBox.Text) ? "http://localhost:11434" : OllamaEndpointBox.Text.Trim(),
                OllamaModel = string.IsNullOrWhiteSpace(OllamaModelBox.Text) ? "llama3.1:8b" : OllamaModelBox.Text.Trim(),
                SkipLlmPlanning = SkipLlmCheck.IsChecked == true
            };

            string html = HtmlPasteBox.Text;
            string? filePath = HtmlSourceTabs.SelectedIndex == 0 ? HtmlFilePathBox.Text.Trim() : null;
            if (HtmlSourceTabs.SelectedIndex == 0 && string.IsNullOrWhiteSpace(filePath))
                throw new InvalidOperationException("Select an HTML file or switch to Paste HTML.");

            var defaultFileName = "output.pptx";
            string? initialDirectory = null;
            if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            {
                var fullPath = Path.GetFullPath(filePath);
                initialDirectory = Path.GetDirectoryName(fullPath);
                defaultFileName = Path.GetFileNameWithoutExtension(fullPath) + ".pptx";
            }

            var defaultFullPath = initialDirectory is not null
                ? Path.Combine(initialDirectory, defaultFileName)
                : defaultFileName;
            defaultFileName = Path.GetFileName(OutputPathHelper.GetUniquePath(defaultFullPath));

            var saveDlg = new SaveFileDialog
            {
                Filter = "PowerPoint (*.pptx)|*.pptx",
                FileName = defaultFileName,
                InitialDirectory = initialDirectory
            };
            if (saveDlg.ShowDialog() != true)
            {
                StatusText.Text = "Cancelled.";
                return;
            }

            options.OutputPath = OutputPathHelper.GetUniquePath(saveDlg.FileName);

            await ApplySplitOptionsForConversionAsync(options);

            var progress = new Progress<ConversionDiagnostic>(d =>
            {
                Dispatcher.Invoke(() => _diagnostics.Add(DiagnosticRow.From(d)));
            });

            var statusProgress = new Progress<string>(s =>
            {
                Dispatcher.Invoke(() => StatusText.Text = s);
            });

            var result = await _pipeline.ConvertAsync(html, filePath, options, progress, statusProgress, ct);

            foreach (var w in result.FontWarnings)
                _diagnostics.Add(new DiagnosticRow { ElementPath = "(system)", Tag = "font", Notes = w });

            _qaRows.Clear();
            foreach (var qa in result.QaComparisons)
                _qaRows.Add(QaRow.From(qa));

            if (DiagnosticsTabs.Items.Count > 1)
                DiagnosticsTabs.SelectedIndex = 1;

            StatusText.Text = mode switch
            {
                ConversionMode.HighFidelity => $"Saved: {result.OutputPath} ({result.Extraction.SlideCount} slide images)",
                ConversionMode.LlmSemantic => $"Saved: {result.OutputPath} (LLM semantic, {result.Diagnostics.Count} shapes)",
                _ => $"Saved: {result.OutputPath} ({result.Diagnostics.Count} elements)"
            };
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            MessageBox.Show(ex.Message, "Conversion failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ConvertButton.IsEnabled = true;
        }
    }

    protected override async void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        await _pipeline.DisposeAsync();
        base.OnClosed(e);
    }

    public sealed class DiagnosticRow
    {
        public string ElementPath { get; set; } = "";
        public string Tag { get; set; } = "";
        public int Slide { get; set; }
        public string ShapeType { get; set; } = "";
        public string Rasterized { get; set; } = "";
        public string? Reason { get; set; }
        public string? Notes { get; set; }
        public string? Error { get; set; }

        public static DiagnosticRow From(ConversionDiagnostic d) => new()
        {
            ElementPath = d.ElementPath,
            Tag = d.Tag,
            Slide = d.SlideIndex + 1,
            ShapeType = d.ShapeType.ToString(),
            Rasterized = d.Rasterized ? "Yes" : "No",
            Reason = d.RasterizeReason,
            Notes = d.Notes,
            Error = d.Error
        };
    }

    public sealed class QaRow
    {
        public int Slide { get; set; }
        public BitmapImage? PuppeteerImage { get; set; }
        public BitmapImage? LibreOfficeImage { get; set; }
        public string Caption { get; set; } = "";
        public string RightPanelLabel { get; set; } = "LibreOffice";

        public static QaRow From(QaComparisonItem item)
        {
            var hasLibreOffice = item.LibreOfficeImagePath is not null && File.Exists(item.LibreOfficeImagePath);
            return new QaRow
            {
                Slide = item.SlideIndex + 1,
                PuppeteerImage = LoadQaClipImage(item.PuppeteerImagePath),
                LibreOfficeImage = LoadQaClipImage(item.LibreOfficeImagePath),
                Caption = item.Caption ?? (hasLibreOffice
                    ? "Puppeteer vs LibreOffice"
                    : "Puppeteer slide clip"),
                RightPanelLabel = hasLibreOffice ? "LibreOffice" : "PowerPoint (after Convert)"
            };
        }

        private static BitmapImage? LoadQaClipImage(string? path)
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
}
