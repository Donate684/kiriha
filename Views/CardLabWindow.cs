using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Material.Icons;
using Material.Icons.Avalonia;

namespace Kiriha.Views;

public sealed class CardLabWindow : Window
{
    private static readonly string[] Titles =
    {
        "Sousou no Frieren",
        "Kusuriya no Hitorigoto",
        "Girls Band Cry",
        "Dungeon Meshi",
        "Makeine: Too Many Losing Heroines!",
        "Yoru no Kurage wa Oyogenai"
    };

    private static readonly string[] Subtitles =
    {
        "Провожающая в последний путь Фрирен",
        "Монолог фармацевта",
        "Девочки из рок-группы",
        "Подземелье вкусностей",
        "Слишком много проигравших героинь",
        "Медуза не умеет плавать ночью"
    };

    private bool _isLight;

    public CardLabWindow()
    {
        _isLight = Application.Current?.ActualThemeVariant == ThemeVariant.Light;
        
        Title = "Kiriha — Card Lab";
        Width = 1280;
        Height = 800;
        MinWidth = 960;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        RequestedThemeVariant = _isLight ? ThemeVariant.Light : ThemeVariant.Dark;
        
        Content = BuildContent();
    }

    private Control BuildContent()
    {
        var winBg = _isLight ? "#F3F4F6" : "#0D1117";
        Background = Brush(winBg);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(32, 24, 32, 28)
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            Margin = new Thickness(0, 0, 0, 20)
        };

        var headerText1 = _isLight ? "#111827" : "#F0F3F7";
        var headerText2 = _isLight ? "#6B7280" : "#8B95A5";

        header.Children.Add(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                Txt("Card Lab", 22, FontWeight.SemiBold, headerText1),
                Txt("6 концепций карточек. Тема переключается на лету.", 13, FontWeight.Normal, headerText2)
            }
        });

        var btnBg = _isLight ? "#FFFFFF" : "#161B24";
        var btnBorder = _isLight ? "#E5E7EB" : "#2A3544";
        var btnFg = _isLight ? "#374151" : "#DDE4EC";

        var themeBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    MIcon(_isLight ? MaterialIconKind.WeatherNight : MaterialIconKind.WeatherSunny, _isLight ? "#4B5563" : "#F6D365"),
                    Txt(_isLight ? "Тёмная тема" : "Светлая тема", 12, FontWeight.SemiBold, btnFg)
                }
            },
            Height = 36,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 0),
            Background = Brush(btnBg),
            BorderBrush = Brush(btnBorder),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 8, 0)
        };
        themeBtn.Click += (_, _) =>
        {
            _isLight = !_isLight;
            RequestedThemeVariant = _isLight ? ThemeVariant.Light : ThemeVariant.Dark;
            Content = BuildContent();
        };
        Grid.SetColumn(themeBtn, 1);
        header.Children.Add(themeBtn);

        var close = new Button
        {
            Content = MIcon(MaterialIconKind.Close, btnFg, 18),
            Width = 36, Height = 36,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(0),
            Background = Brush(btnBg),
            BorderBrush = Brush(btnBorder),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 2);
        header.Children.Add(close);
        
        root.Children.Add(header);

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(scroll, 1);

        var wrap = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            ItemWidth = 176,
            ItemHeight = 342
        };

        wrap.Children.Add(Variant("01", "Neo Glass", CardNeoGlass(0, _isLight), _isLight));
        wrap.Children.Add(Variant("02", "Accent Stripe", CardAccentStripe(1, _isLight), _isLight));
        wrap.Children.Add(Variant("03", "Floating Poster", CardFloatingPoster(2, _isLight), _isLight));
        wrap.Children.Add(Variant("04", "Full Bleed", CardFullBleedCinematic(3, _isLight), _isLight));
        wrap.Children.Add(Variant("05", "Magazine", CardMagazineEditorial(4, _isLight), _isLight));
        wrap.Children.Add(Variant("06", "Soft Depth", CardSoftDepth(5, _isLight), _isLight));
        
        var imgPath = @"C:\Users\ASUS\.gemini\antigravity-ide\brain\924958af-7837-4fb1-88fa-5723fd951a62\anime_cover_mockup_1780311451533.png";
        wrap.Children.Add(Variant("07", "Floating Mag", CardFloatingMagazine(2, _isLight, imgPath), _isLight));

        scroll.Content = wrap;
        root.Children.Add(scroll);
        return root;
    }

    private static Control Variant(string number, string name, Control card, bool isLight)
    {
        var numBg = isLight ? "#E0E7FF" : "#1A2A3E";
        var numFg = isLight ? "#4338CA" : "#6AAFEF";
        var nameFg = isLight ? "#4B5563" : "#C0CAD8";

        return new StackPanel
        {
            Width = 176,
            Height = 342,
            Margin = new Thickness(0, 0, 18, 18),
            Spacing = 8,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                    Children =
                    {
                        NumberPill(number, numBg, numFg),
                        Txt(name, 12, FontWeight.SemiBold, nameFg, new Thickness(8, 0, 0, 0))
                            .WithColumn(1)
                    }
                },
                card
            }
        };
    }

    private static Control CardNeoGlass(int i, bool isLight)
    {
        var bg = isLight ? "#B3FFFFFF" : "#D91A1F2A";
        var border = isLight ? "#40FFFFFF" : "#18FFFFFF";
        var shadow = isLight ? "0 4 20 0 #15000000" : "0 0 18 0 #154FC3F7";
        var track = isLight ? "#E0F2FE" : "#1A2A3A";

        var card = new Border
        {
            Width = 158, Height = 305,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Background = Brush(bg),
            BorderBrush = Brush(border),
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse(shadow)
        };

        card.Child = new Panel
        {
            Children =
            {
                new Grid
                {
                    RowDefinitions = new RowDefinitions("195,*"),
                    Children =
                    {
                        Poster(0, new Thickness(0), "#1B3A5C", "#4FC3F7").WithRow(0),
                        new Grid
                        {
                            RowDefinitions = new RowDefinitions("*,Auto,Auto"),
                            Margin = new Thickness(11, 9, 11, 10),
                            Children =
                            {
                                TitleBlock(i, light: isLight).WithRow(0),
                                GradientBar(0.29, "#4FC3F7", "#81D4FA", track).WithRow(1),
                                EpisodeControls("8", "28", light: isLight, accent: "#4FC3F7").WithRow(2)
                            }
                        }.WithRow(1)
                    }
                },
                GlassScore("9.1", isLight)
                    .WithAlign(HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, 8, 8, 0))
            }
        };

        return card;
    }

    private static Control CardAccentStripe(int i, bool isLight)
    {
        var bg = isLight ? "#FFFFFF" : "#161B24";
        var border = isLight ? "#E5E7EB" : "#1E2A38";
        var track = isLight ? "#EDE9FE" : "#2A2540";
        var epText = isLight ? "#4C1D95" : "#E8E0FA";
        var statText = isLight ? "#7C3AED" : "#7B6FA0";
        var accentSub = isLight ? "#6D28D9" : "#9C7CF4";

        var card = new Border
        {
            Width = 158, Height = 305,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Background = Brush(bg),
            BorderBrush = Brush(border),
            BorderThickness = new Thickness(1)
        };

        var stripe = new Border
        {
            Width = 3,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(10, 0, 0, 10),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#9C7CF4"), 0),
                    new GradientStop(Color.Parse("#5BB8F5"), 1)
                }
            }
        };

        var body = new Grid
        {
            RowDefinitions = new RowDefinitions("168,*"),
            Margin = new Thickness(3, 0, 0, 0),
            Children =
            {
                Poster(8, new Thickness(7, 7, 7, 0), "#2A1F42", "#9C7CF4").WithRow(0),
                new StackPanel
                {
                    Margin = new Thickness(11, 9, 11, 10),
                    Spacing = 6,
                    Children =
                    {
                        TitleBlock(i, light: isLight, accentSub: accentSub),
                        new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                            VerticalAlignment = VerticalAlignment.Bottom,
                            Children =
                            {
                                RingProgress(0.67, "#9C7CF4", track, 28).WithColumn(0),
                                new StackPanel
                                {
                                    Margin = new Thickness(8, 0, 0, 0),
                                    VerticalAlignment = VerticalAlignment.Center,
                                    Spacing = 1,
                                    Children =
                                    {
                                        Txt("8 / 12", 12, FontWeight.SemiBold, epText),
                                        Txt("Смотрю", 9, FontWeight.Normal, statText)
                                    }
                                }.WithColumn(1)
                            }
                        }
                    }
                }.WithRow(1)
            }
        };

        card.Child = new Panel { Children = { stripe, body } };
        return card;
    }

    private static Control CardFloatingPoster(int i, bool isLight)
    {
        var bg = isLight ? "#FFFFFF" : "#14181F";
        var shadow = isLight ? "0 4 16 0 #15000000" : "0 4 16 0 #30000000";
        var track = isLight ? "#E5E7EB" : "#1E2A30";
        var text = isLight ? "#4B5563" : "#5A7A70";

        var card = new Border
        {
            Width = 158, Height = 305,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Background = Brush(bg)
        };

        if (isLight) 
        {
            card.BorderBrush = Brush("#F3F4F6");
            card.BorderThickness = new Thickness(1);
        }

        var poster = Poster(10, new Thickness(10, 10, 10, 0), "#1A3D38", "#67D7A1");
        poster.BoxShadow = BoxShadows.Parse(shadow);

        card.Child = new Grid
        {
            RowDefinitions = new RowDefinitions("178,*"),
            Children =
            {
                poster.WithRow(0),
                new StackPanel
                {
                    Margin = new Thickness(11, 10, 11, 12),
                    Spacing = 8,
                    Children =
                    {
                        TitleBlock(i, light: isLight),
                        SegmentedProgress(8, 13, "#67D7A1", track),
                        new TextBlock
                        {
                            Text = "8 из 13 серий",
                            FontSize = 10,
                            FontWeight = FontWeight.Medium,
                            Foreground = Brush(text),
                            HorizontalAlignment = HorizontalAlignment.Center
                        }
                    }
                }.WithRow(1)
            }
        };

        return card;
    }

    private static Control CardFullBleedCinematic(int i, bool isLight)
    {
        var bg = isLight ? "#FFFFFF" : "#0C0E14";
        
        var card = new Border
        {
            Width = 158, Height = 305,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Background = Brush(bg)
        };

        card.Child = new Panel
        {
            Children =
            {
                Poster(0, new Thickness(0), "#3D1F35", "#F178B6"),
                new Border
                {
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Height = 190,
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint   = new RelativePoint(0, 1, RelativeUnit.Relative),
                        GradientStops =
                        {
                            new GradientStop(Color.Parse("#00000000"), 0),
                            new GradientStop(Color.Parse("#90000000"), 0.35),
                            new GradientStop(Color.Parse("#E8000000"), 0.7),
                            new GradientStop(Color.Parse("#F5000000"), 1)
                        }
                    }
                },
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(11, 0, 11, 16),
                    Spacing = 4,
                    Children =
                    {
                        TitleBlock(i, white: true),
                        SolidBar(0.74, "#F178B6", "#3FFFFFFF"),
                        EpisodeControls("9", "12", white: true, accent: "#F178B6")
                    }
                },
                new Border
                {
                    Height = 3,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint   = new RelativePoint(1, 0, RelativeUnit.Relative),
                        GradientStops =
                        {
                            new GradientStop(Color.Parse("#F178B6"), 0),
                            new GradientStop(Color.Parse("#FF6B9D"), 1)
                        }
                    }
                },
                InfoPill("Онгоинг", "#CC000000", "#FFFFFF")
                    .WithAlign(HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(8, 8, 0, 0)),
                DarkScore("8.4")
                    .WithAlign(HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, 8, 8, 0))
            }
        };

        if (isLight)
        {
            card.BoxShadow = BoxShadows.Parse("0 4 12 0 #15000000");
        }

        return card;
    }

    private static Control CardMagazineEditorial(int i, bool isLight)
    {
        var bg = isLight ? "#FFFFFF" : "#1C222D";
        var border = isLight ? "#E5E7EB" : "#252D3A";
        var pillBg = isLight ? "#F3F4F6" : "#2A2540";
        var pillFgPurple = isLight ? "#6D28D9" : "#B388FF";
        var pillFgGold = isLight ? "#B45309" : "#F6D365";
        var pillFgGray = isLight ? "#4B5563" : "#8B95A5";
        var track = isLight ? "#EDE9FE" : "#2A2540";

        var card = new Border
        {
            Width = 158, Height = 305,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Background = Brush(bg),
            BorderBrush = Brush(border),
            BorderThickness = new Thickness(1)
        };

        card.Child = new Grid
        {
            RowDefinitions = new RowDefinitions("165,Auto,*"),
            Children =
            {
                Poster(0, new Thickness(0), "#1F2648", "#7C4DFF").WithRow(0),
                new Border
                {
                    Height = 2,
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint   = new RelativePoint(1, 0, RelativeUnit.Relative),
                        GradientStops =
                        {
                            new GradientStop(Color.Parse("#7C4DFF"), 0),
                            new GradientStop(Color.Parse("#B388FF"), 0.5),
                            new GradientStop(Color.Parse("#7C4DFF"), 1)
                        }
                    }
                }.WithRow(1),
                new Grid
                {
                    RowDefinitions = new RowDefinitions("*,Auto,Auto"),
                    Margin = new Thickness(10, 8, 10, 8),
                    Children =
                    {
                        TitleBlock(i, light: isLight).WithRow(0),
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 4,
                            Margin = new Thickness(0, 4, 0, 6),
                            Children =
                            {
                                MiniPill("TV", pillBg, pillFgPurple),
                                MiniPill("★ 8.4", pillBg, pillFgGold),
                                MiniPill("24 эп", pillBg, pillFgGray)
                            }
                        }.WithRow(1),
                        GradientBar(0.46, "#7C4DFF", "#B388FF", track, 5).WithRow(2)
                    }
                }.WithRow(2)
            }
        };

        return card;
    }

    private static Control CardSoftDepth(int i, bool isLight)
    {
        var bg = isLight ? "#F3F4F6" : "#181D26";
        var border = isLight ? "#FFFFFF" : "#22FFFFFF";
        var shadow = isLight ? "4 4 14 0 #15000000, -4 -4 14 0 #FFFFFF" : "4 4 14 0 #20000000";
        var track = isLight ? "#E5E7EB" : "#2A2520";
        var percentFg = isLight ? "#D97706" : "#F6D365";
        var epFg = isLight ? "#6B7280" : "#8B8578";

        var card = new Border
        {
            Width = 158, Height = 305,
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            Background = Brush(bg),
            BorderBrush = Brush(border),
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse(shadow)
        };

        var poster = Poster(8, new Thickness(10, 10, 10, 0), "#3A2E1E", "#F6D365");

        card.Child = new Grid
        {
            RowDefinitions = new RowDefinitions("155,*"),
            Children =
            {
                poster.WithRow(0),
                new StackPanel
                {
                    Margin = new Thickness(12, 9, 12, 10),
                    Spacing = 6,
                    Children =
                    {
                        TitleBlock(i, light: isLight),
                        StarRating(3.5, isLight),
                        new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                            Children =
                            {
                                RingProgress(0.67, percentFg, track, 32).WithColumn(0),
                                new StackPanel
                                {
                                    Margin = new Thickness(8, 0, 0, 0),
                                    VerticalAlignment = VerticalAlignment.Center,
                                    Spacing = 0,
                                    Children =
                                    {
                                        Txt("67%", 16, FontWeight.Bold, percentFg),
                                        Txt("8 / 12", 9, FontWeight.Normal, epFg)
                                    }
                                }.WithColumn(1)
                            }
                        },
                        NeumorphicButtons(isLight)
                    }
                }.WithRow(1)
            }
        };

        return card;
    }

    private static Control CardFloatingMagazine(int i, bool isLight, string imagePath)
    {
        var bg = isLight ? "#FFFFFF" : "#1C222D";
        var border = isLight ? "#E5E7EB" : "#252D3A";
        var track = isLight ? "#E5E7EB" : "#1E2A30";
        var text = isLight ? "#4B5563" : "#5A7A70";

        var card = new Border
        {
            Width = 158, Height = 305,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Background = Brush(bg),
            BorderBrush = Brush(border),
            BorderThickness = new Thickness(1)
        };

        var poster = RealPoster(0, new Thickness(0), imagePath);

        card.Child = new Grid
        {
            RowDefinitions = new RowDefinitions("165,Auto,*"),
            Children =
            {
                poster.WithRow(0),
                new Border
                {
                    Height = 2,
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint   = new RelativePoint(1, 0, RelativeUnit.Relative),
                        GradientStops =
                        {
                            new GradientStop(Color.Parse("#7C4DFF"), 0),
                            new GradientStop(Color.Parse("#B388FF"), 0.5),
                            new GradientStop(Color.Parse("#7C4DFF"), 1)
                        }
                    }
                }.WithRow(1),
                new StackPanel
                {
                    Margin = new Thickness(11, 10, 11, 12),
                    Spacing = 8,
                    Children =
                    {
                        TitleBlock(i, light: isLight),
                        SegmentedProgress(8, 13, "#67D7A1", track),
                        new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                            Children =
                            {
                                MIcon(MaterialIconKind.Minus, isLight ? "#9CA3AF" : "#6B7888", 16).WithColumn(0),
                                new TextBlock
                                {
                                    Text = "8 из 13 серий",
                                    FontSize = 10,
                                    FontWeight = FontWeight.Medium,
                                    Foreground = Brush(text),
                                    HorizontalAlignment = HorizontalAlignment.Center,
                                    VerticalAlignment = VerticalAlignment.Center
                                }.WithColumn(1),
                                MIcon(MaterialIconKind.Plus, "#67D7A1", 16).WithColumn(2)
                            }
                        }
                    }
                }.WithRow(2)
            }
        };

        return card;
    }

    private static Border RealPoster(double radius, Thickness margin, string imagePath)
    {
        Avalonia.Media.Imaging.Bitmap? bmp = null;
        try
        {
            if (System.IO.File.Exists(imagePath))
                bmp = new Avalonia.Media.Imaging.Bitmap(imagePath);
        }
        catch { }

        var border = new Border
        {
            Margin = margin,
            CornerRadius = new CornerRadius(radius),
            ClipToBounds = true,
            Background = Brush("#2A2A2A")
        };

        if (bmp != null)
        {
            border.Child = new Avalonia.Controls.Image
            {
                Source = bmp,
                Stretch = Stretch.UniformToFill
            };
        }
        else
        {
            border.Child = new Grid
            {
                Children =
                {
                    MIcon(MaterialIconKind.ImageOffOutline, "#FFFFFF", 38)
                        .WithAlign(HorizontalAlignment.Center, VerticalAlignment.Center, new Thickness(0))
                        .WithOpacity(0.22)
                }
            };
        }
        return border;
    }

    private static Border Poster(double radius, Thickness margin, string fromColor, string toColor)
    {
        return new Border
        {
            Margin = margin,
            CornerRadius = new CornerRadius(radius),
            ClipToBounds = true,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse(fromColor), 0),
                    new GradientStop(Color.Parse(toColor), 1)
                }
            },
            Child = new Grid
            {
                Children =
                {
                    MIcon(MaterialIconKind.MovieOpenOutline, "#FFFFFF", 38)
                        .WithAlign(HorizontalAlignment.Center, VerticalAlignment.Center, new Thickness(0))
                        .WithOpacity(0.22),
                    Txt("POSTER", 10, FontWeight.Bold, "#80FFFFFF")
                        .WithAlign(HorizontalAlignment.Center, VerticalAlignment.Bottom, new Thickness(0, 0, 0, 14))
                }
            }
        };
    }

    private static StackPanel TitleBlock(int i, bool white = false, bool light = false, string? accentSub = null)
    {
        var primary   = white ? "#FFFFFF" : light ? "#111827" : "#F0F3F7";
        var secondary = accentSub ?? (white ? "#B0FFFFFF" : light ? "#6B7280" : "#8B95A5");

        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Txt(Titles[i], 12, FontWeight.SemiBold, primary).WithMaxLines(2),
                Txt(Subtitles[i], 10, FontWeight.Normal, secondary).WithMaxLines(1)
            }
        };
    }

    private static Border SolidBar(double value, string accent, string track, double h = 4)
    {
        return new Border
        {
            Height = h,
            CornerRadius = new CornerRadius(h / 2),
            ClipToBounds = true,
            Background = Brush(track),
            Child = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 134 * value,
                Background = Brush(accent),
                CornerRadius = new CornerRadius(h / 2)
            }
        };
    }

    private static Border GradientBar(double value, string from, string to, string track, double h = 3)
    {
        return new Border
        {
            Height = h,
            CornerRadius = new CornerRadius(h / 2),
            ClipToBounds = true,
            Background = Brush(track),
            Child = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 134 * value,
                CornerRadius = new CornerRadius(h / 2),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.Parse(from), 0),
                        new GradientStop(Color.Parse(to), 1)
                    }
                }
            }
        };
    }

    private static Control SegmentedProgress(int current, int total, string accent, string track)
    {
        var gap = 2.0;
        var segW = Math.Max(4, (134.0 - (total - 1) * gap) / total);

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = gap,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        for (var j = 0; j < total; j++)
        {
            panel.Children.Add(new Border
            {
                Width = segW, Height = 5,
                CornerRadius = new CornerRadius(2.5),
                Background = j < current ? Brush(accent) : Brush(track)
            });
        }
        return panel;
    }

    private static Control RingProgress(double fraction, string accentColor, string trackColor, double diameter = 26)
    {
        const double thickness = 2.5;
        var radius = (diameter - thickness) / 2.0;
        var cx = diameter / 2.0;
        var cy = diameter / 2.0;

        var track = new Ellipse
        {
            Width = diameter, Height = diameter,
            Stroke = Brush(trackColor),
            StrokeThickness = thickness,
            Fill = Brushes.Transparent
        };

        if (fraction <= 0) return new Panel { Width = diameter, Height = diameter, Children = { track } };
        if (fraction >= 1.0)
        {
            var full = new Ellipse
            {
                Width = diameter, Height = diameter,
                Stroke = Brush(accentColor),
                StrokeThickness = thickness,
                Fill = Brushes.Transparent
            };
            return new Panel { Width = diameter, Height = diameter, Children = { track, full } };
        }

        var angle = fraction * 360.0;
        var startRad = -Math.PI / 2.0;
        var endRad   = startRad + angle * Math.PI / 180.0;
        var sx = cx + radius * Math.Cos(startRad);
        var sy = cy + radius * Math.Sin(startRad);
        var ex = cx + radius * Math.Cos(endRad);
        var ey = cy + radius * Math.Sin(endRad);

        var segments = new PathSegments();
        segments.Add(new ArcSegment
        {
            Point = new Point(ex, ey),
            Size = new Size(radius, radius),
            IsLargeArc = angle > 180,
            SweepDirection = SweepDirection.Clockwise
        });

        var arc = new Avalonia.Controls.Shapes.Path
        {
            Data = new PathGeometry 
            { 
                Figures = new PathFigures { new PathFigure { StartPoint = new Point(sx, sy), IsClosed = false, Segments = segments } } 
            },
            Stroke = Brush(accentColor),
            StrokeThickness = thickness,
            StrokeLineCap = PenLineCap.Round,
            Stretch = Stretch.None
        };

        return new Panel { Width = diameter, Height = diameter, Children = { track, arc } };
    }

    private static StackPanel StarRating(double rating, bool isLight)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var emptyColor = isLight ? "#D1D5DB" : "#3A3528";
        var fillColor = isLight ? "#F59E0B" : "#F6D365";

        for (var s = 1; s <= 5; s++)
        {
            var kind = s <= (int)rating ? MaterialIconKind.Star :
                       s == (int)rating + 1 && rating % 1 >= 0.5 ? MaterialIconKind.StarHalfFull :
                       MaterialIconKind.StarOutline;
            var filled = s <= Math.Ceiling(rating);
            panel.Children.Add(MIcon(kind, filled ? fillColor : emptyColor, 14));
        }
        return panel;
    }

    private static StackPanel EpisodeControls(string current, string total, bool white = false, bool light = false, string accent = "#74B6FF")
    {
        var fg    = white ? "#FFFFFF" : light ? "#374151" : "#DDE3EB";
        var muted = white ? "#8FFFFFFF" : light ? "#9CA3AF" : "#6B7888";

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 7,
            Margin = new Thickness(0, 4, 0, 0),
            Children =
            {
                MIcon(MaterialIconKind.Minus, muted),
                Txt(current, 12, FontWeight.SemiBold, fg),
                Txt("/", 12, FontWeight.Normal, muted),
                Txt(total, 12, FontWeight.Normal, muted),
                MIcon(MaterialIconKind.Plus, accent)
            }
        };
    }

    private static StackPanel NeumorphicButtons(bool isLight)
    {
        var bg = isLight ? "#F3F4F6" : "#1E2330";
        var shadow = isLight ? "2 2 6 0 #15000000, -2 -2 6 0 #FFFFFF" : "2 2 6 0 #18000000, -1 -1 4 0 #08FFFFFF";
        var grayFg = isLight ? "#6B7280" : "#6B6560";
        var goldFg = isLight ? "#D97706" : "#F6D365";

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 10,
            Children =
            {
                NeuBtn(MaterialIconKind.Minus, grayFg, bg, shadow),
                NeuBtn(MaterialIconKind.PlayArrow, goldFg, bg, shadow),
                NeuBtn(MaterialIconKind.Plus, grayFg, bg, shadow)
            }
        };
    }

    private static Border NeuBtn(MaterialIconKind icon, string fg, string bg, string shadow)
    {
        return new Border
        {
            Width = 30, Height = 30,
            CornerRadius = new CornerRadius(8),
            Background = Brush(bg),
            BoxShadow = BoxShadows.Parse(shadow),
            Child = MIcon(icon, fg, 15).WithAlign(HorizontalAlignment.Center, VerticalAlignment.Center, new Thickness(0))
        };
    }

    private static Border GlassScore(string value, bool isLight)
    {
        var bg = isLight ? "#B3FFFFFF" : "#B3000000";
        var border = isLight ? "#20000000" : "#20FFFFFF";
        var fg = isLight ? "#111827" : "#FFFFFF";
        return new Border
        {
            Padding = new Thickness(7, 4), CornerRadius = new CornerRadius(8),
            Background = Brush(bg), BorderBrush = Brush(border), BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 3,
                Children = { MIcon(MaterialIconKind.Star, "#4FC3F7", 12), Txt(value, 11, FontWeight.SemiBold, fg) }
            }
        };
    }

    private static Border DarkScore(string value)
    {
        return new Border
        {
            Padding = new Thickness(7, 4), CornerRadius = new CornerRadius(8),
            Background = Brush("#CC000000"),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 3,
                Children = { MIcon(MaterialIconKind.Star, "#F6D365", 12), Txt(value, 11, FontWeight.SemiBold, "#FFFFFF") }
            }
        };
    }

    private static Border InfoPill(string text, string bg, string fg)
    {
        return new Border { Padding = new Thickness(7, 3), CornerRadius = new CornerRadius(7), Background = Brush(bg), Child = Txt(text, 10, FontWeight.SemiBold, fg) };
    }

    private static Border NumberPill(string text, string bg, string fg)
    {
        return new Border { Padding = new Thickness(7, 3), CornerRadius = new CornerRadius(7), Background = Brush(bg), Child = Txt(text, 10, FontWeight.SemiBold, fg) };
    }

    private static Border MiniPill(string text, string bg, string fg)
    {
        return new Border { Padding = new Thickness(5, 2), CornerRadius = new CornerRadius(5), Background = Brush(bg), Child = Txt(text, 9, FontWeight.Medium, fg) };
    }

    private static TextBlock Txt(string text, double size, FontWeight weight, string color, Thickness? margin = null)
    {
        return new TextBlock { Text = text, FontSize = size, FontWeight = weight, Foreground = Brush(color), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0) };
    }

    private static MaterialIcon MIcon(MaterialIconKind kind, string color, double size = 14)
    {
        return new MaterialIcon { Kind = kind, Width = size, Height = size, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center };
    }

    private static IBrush Brush(string color) => SolidColorBrush.Parse(color);
}

internal static class CardLabControlExtensions
{
    public static T WithRow<T>(this T control, int row) where T : Control { Grid.SetRow(control, row); return control; }
    public static T WithColumn<T>(this T control, int column) where T : Control { Grid.SetColumn(control, column); return control; }
    public static T WithAlign<T>(this T control, HorizontalAlignment h, VerticalAlignment v, Thickness m) where T : Control { control.HorizontalAlignment = h; control.VerticalAlignment = v; control.Margin = m; return control; }
    public static T WithOpacity<T>(this T control, double opacity) where T : Control { control.Opacity = opacity; return control; }
    public static TextBlock WithMaxLines(this TextBlock text, int lines) { text.MaxLines = lines; return text; }
}
