using System;
using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Kiriha.Services.Data;

namespace Kiriha.Views;

public class KirihaWindowBase : Window
{
    protected SettingsService? SettingsService { get; set; }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        
        if (SettingsService != null)
        {
            ApplyUiScale(SettingsService.Current.UI.UiScale);
        }
    }

    public void ApplyUiScale(double factor)
    {
        if (this.FindControl<LayoutTransformControl>("ScaleRoot")?.LayoutTransform is ScaleTransform st)
        {
            st.ScaleX = factor;
            st.ScaleY = factor;
        }
    }
}
