using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Kiriha.Views.Controls;

public partial class TitleBarButtons : UserControl
{
    private Window? _parentWindow;

    public TitleBarButtons()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _parentWindow = TopLevel.GetTopLevel(this) as Window;

        if (_parentWindow != null)
        {
            _parentWindow.PropertyChanged += OnWindowPropertyChanged;
            UpdateWindowState(_parentWindow.WindowState);

            // Hide maximize button if window cannot be resized
            if (!_parentWindow.CanResize)
            {
                MaximizeBtn.IsVisible = false;
            }
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_parentWindow != null)
        {
            _parentWindow.PropertyChanged -= OnWindowPropertyChanged;
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty)
        {
            UpdateWindowState((WindowState)e.NewValue!);
        }
    }

    private void UpdateWindowState(WindowState state)
    {
        if (state == WindowState.Maximized)
        {
            MaximizeIcon.Text = "\uE923"; // Restore icon
            // Remove corner radius when maximized
            CloseBtn.CornerRadius = new CornerRadius(0);
        }
        else
        {
            MaximizeIcon.Text = "\uE922"; // Maximize icon
            // Restore Windows 11 corner radius
            CloseBtn.CornerRadius = new CornerRadius(0, 8, 0, 0);
        }
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        if (_parentWindow != null)
            _parentWindow.WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        if (_parentWindow != null)
        {
            _parentWindow.WindowState = _parentWindow.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        _parentWindow?.Close();
    }
}
