using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;

namespace Kiriha.Views.Controls;

public class SegmentedProgressBar : Control
{
    public static readonly StyledProperty<int> CurrentProperty =
        AvaloniaProperty.Register<SegmentedProgressBar, int>(nameof(Current), 0);
        
    public static readonly StyledProperty<int> TotalProperty =
        AvaloniaProperty.Register<SegmentedProgressBar, int>(nameof(Total), 12);
        
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<SegmentedProgressBar, IBrush?>(nameof(AccentBrush));
        
    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<SegmentedProgressBar, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<int> AiredProperty =
        AvaloniaProperty.Register<SegmentedProgressBar, int>(nameof(Aired), 0);
        
    public static readonly StyledProperty<IBrush?> AiredBrushProperty =
        AvaloniaProperty.Register<SegmentedProgressBar, IBrush?>(nameof(AiredBrush));

    public int Current
    {
        get => GetValue(CurrentProperty);
        set => SetValue(CurrentProperty, value);
    }

    public int Total
    {
        get => GetValue(TotalProperty);
        set => SetValue(TotalProperty, value);
    }
    
    public int Aired
    {
        get => GetValue(AiredProperty);
        set => SetValue(AiredProperty, value);
    }
    
    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }
    
    public IBrush? AiredBrush
    {
        get => GetValue(AiredBrushProperty);
        set => SetValue(AiredBrushProperty, value);
    }
    
    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    static SegmentedProgressBar()
    {
        AffectsRender<SegmentedProgressBar>(CurrentProperty, TotalProperty, AiredProperty, AccentBrushProperty, AiredBrushProperty, TrackBrushProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var total = Total;
        if (total <= 0 || total > 26)
            return new Size(0, 5);
            
        var rows = total > 13 ? 2 : 1;
        var h = (rows * 5) + ((rows - 1) * 2);
        return new Size(0, h);
    }

    public override void Render(DrawingContext context)
    {
        var total = Total;
        var current = Current;
        var aired = Aired;
        var width = Bounds.Width;
        var height = Bounds.Height;
        
        var trackBrush = TrackBrush ?? Brushes.Gray;
        var accentBrush = AccentBrush ?? Brushes.Green;
        var airedBrush = AiredBrush ?? Brushes.Red;

        if (total <= 0 || total > 26)
        {
            var cornerRadius = height / 2.0;
            context.DrawRectangle(trackBrush, null, new Rect(0, 0, width, height), cornerRadius, cornerRadius);
            
            if (total > 0)
            {
                var airedWidth = width * Math.Clamp((double)aired / total, 0, 1);
                var currentWidth = width * Math.Clamp((double)current / total, 0, 1);
                
                if (airedWidth > 0)
                    context.DrawRectangle(airedBrush, null, new Rect(0, 0, airedWidth, height), cornerRadius, cornerRadius);
                
                if (currentWidth > 0)
                    context.DrawRectangle(accentBrush, null, new Rect(0, 0, currentWidth, height), cornerRadius, cornerRadius);
            }
            return;
        }
        
        var pillHeight = 5.0;
        var rowGap = 2.0;
        var gap = 2.0;
        
        var columns = Math.Min(total, 13);
        var rows = total > 13 ? 2 : 1;
        var segmentWidth = Math.Max(2.0, (width - (gap * (columns - 1))) / columns);
        
        for (int r = 0; r < rows; r++)
        {
            var startIndex = r * columns;
            var itemsInThisRow = Math.Min(columns, total - startIndex);
            if (itemsInThisRow <= 0) break;
            
            var y = r * (pillHeight + rowGap);
            var cr = pillHeight / 2.0;
            
            for (int i = 0; i < itemsInThisRow; i++)
            {
                var globalIndex = startIndex + i;
                var brush = trackBrush;
                if (globalIndex < current) brush = accentBrush;
                else if (globalIndex < aired) brush = airedBrush;
                
                var x = i * (segmentWidth + gap);
                var actualWidth = Math.Min(segmentWidth, width - x);
                if (actualWidth <= 0) break;
                
                var rect = new Rect(x, y, actualWidth, pillHeight);
                context.DrawRectangle(brush, null, rect, cr, cr);
            }
        }
    }
}
