using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using AvaloniaPdfViewer.Internals;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using SkiaSharp;
using PdfConvert = PDFtoImage.Conversion;

namespace AvaloniaPdfViewer;

public partial class PdfViewer : UserControl, IDisposable
{
    public static readonly DirectProperty<PdfViewer, string?> SourceProperty =
        AvaloniaProperty.RegisterDirect<PdfViewer, string?>(nameof(Source), o => o.Source, (o, v) => o.Source = v);

    static PdfViewer()
    {
        SourceProperty.Changed.AddClassHandler<PdfViewer>((x, e) => x.Load(e));
    }
    public PdfViewer()
    {
        _thumbnailImagesCache.Connect()
            .Sort(SortExpressionComparer<DrawableThumbnailImage>.Ascending(i => i.Index))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out _thumbnailImages)
            .DisposeMany()
            .Subscribe();
        
        InitializeComponent();
    }

    private string? _source;
    public string? Source
    {
        get => _source;
        set => SetAndRaise(SourceProperty, ref _source, value);
    }
    
    // ReSharper disable once MemberCanBePrivate.Global
    public int ThumbnailCacheSize { get; set; } = 10;
    
    private readonly SourceCache<DrawableThumbnailImage, int> _thumbnailImagesCache = new(i => i.PageNumber);
    private readonly ReadOnlyObservableCollection<DrawableThumbnailImage> _thumbnailImages;
    // ReSharper disable once UnusedMember.Local
    private ReadOnlyObservableCollection<DrawableThumbnailImage> ThumbnailImages => _thumbnailImages;
    
    private Stream? _fileStream;
    private DisposingLimitCache<int, SKBitmap>? _bitmapCache;
    private bool _loading;

    private void Load(AvaloniaPropertyChangedEventArgs args)
    {
        try
        {
            _loading = true;
            CleanUp();

            var source = args.NewValue as string;
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return;

            _fileStream = File.OpenRead(source);

            var doc = PdfDocumentReflection.Load(_fileStream, null, false);
            var sizes = PdfDocumentReflection.PageSizes(doc);

            PageSelector.Value = 1;
            PageSelector.Maximum = sizes.Count;
            PageCount.Text = sizes.Count.ToString();

            _bitmapCache ??= new DisposingLimitCache<int, SKBitmap>(ThumbnailCacheSize);

            _thumbnailImagesCache.Edit(edit =>
            {
                edit.Load(sizes.Select((size, index) =>
                    new DrawableThumbnailImage(size, _fileStream, index, _bitmapCache)));
            });

#pragma warning disable CA1416
            var skbitMap = PdfConvert.ToImage(_fileStream, new Index(0), leaveOpen: true);
#pragma warning restore CA1416

            MainImage.Source = skbitMap.ToAvaloniaImage();
        }
        finally
        {
            _loading = false;
        }
    }

    private void ThumbnailListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if(_loading) return;
        PageSelector.Value = ThumbnailListBox.SelectedIndex + 1;
        SetPage(ThumbnailListBox.SelectedIndex);
    }

    private void PageSelector_OnValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_loading || e.NewValue == null) return;
        var pageNumber = (int)e.NewValue;
        ThumbnailListBox.SelectedIndex = pageNumber - 1;
        SetPage(pageNumber - 1);
    }

    private void SetPage(int index)
    {
        CleanUpMainImage();
        if (_fileStream == null || index < 0) return;
#pragma warning disable CA1416
        MainImage.Source = PdfConvert.ToImage(_fileStream, new Index(index), leaveOpen: true).ToAvaloniaImage();
#pragma warning restore CA1416
    }

    private void CleanUp()
    {
        CleanUpMainImage();
        foreach (var drawableThumbnailImage in _thumbnailImagesCache.Items)
        {
            drawableThumbnailImage.Dispose();
        }
        _thumbnailImagesCache.Clear();
        _bitmapCache?.Dispose();
        _bitmapCache = null;
        _fileStream?.Dispose();
        PageSelector.Value = null;
        PageSelector.Maximum = 0;
        PageCount.Text = "0";
    }
    
    private void CleanUpMainImage()
    {
        if (MainImage.Source is IDisposable disposable)
        {
            MainImage.Source = null;
            disposable.Dispose();
        }
    }


    public void Dispose()
    {
        CleanUp();
        _thumbnailImagesCache.Dispose();
    }
}