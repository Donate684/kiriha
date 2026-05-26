using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace Kiriha.Views.Controls;

/// <summary>
/// A specialized WrapPanel that perfectly centers items on each row individually,
/// creating a balanced and aesthetic flow similar to justified/centered text.
/// </summary>
public class CenteredWrapPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        double curWidth = 0, curHeight = 0, maxWidth = 0, totalHeight = 0;

        foreach (var child in Children)
        {
            child.Measure(availableSize);
            var desired = child.DesiredSize;

            // If item exceeds available width and it isn't the first item on the line, wrap
            if (curWidth + desired.Width > availableSize.Width && curWidth > 0)
            {
                maxWidth = Math.Max(maxWidth, curWidth);
                totalHeight += curHeight;
                curWidth = desired.Width;
                curHeight = desired.Height;
            }
            else
            {
                curWidth += desired.Width;
                curHeight = Math.Max(curHeight, desired.Height);
            }
        }

        maxWidth = Math.Max(maxWidth, curWidth);
        totalHeight += curHeight;

        return new Size(
            double.IsInfinity(availableSize.Width) ? maxWidth : Math.Max(maxWidth, availableSize.Width), 
            totalHeight
        );
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double curWidth = 0, curHeight = 0, totalHeight = 0;
        var rowItems = new List<Control>();

        void ArrangeRow()
        {
            // If there are items, we potentially subtract last item's excess right margin for perfect visual centering
            // but standard DesiredSize is accurate enough for typical margins.
            double visualRowWidth = curWidth;
            
            // Calculate start X point to keep row contents centered
            double x = (finalSize.Width - visualRowWidth) / 2;

            foreach (var item in rowItems)
            {
                // Arrange with its own desired dimension
                item.Arrange(new Rect(x, totalHeight, item.DesiredSize.Width, item.DesiredSize.Height));
                x += item.DesiredSize.Width;
            }

            totalHeight += curHeight;
            rowItems.Clear();
            curWidth = 0;
            curHeight = 0;
        }

        foreach (var child in Children)
        {
            var desired = child.DesiredSize;

            if (curWidth + desired.Width > finalSize.Width && curWidth > 0)
            {
                ArrangeRow();
            }

            rowItems.Add(child);
            curWidth += desired.Width;
            curHeight = Math.Max(curHeight, desired.Height);
        }

        if (rowItems.Count > 0)
        {
            ArrangeRow();
        }

        return finalSize;
    }
}
