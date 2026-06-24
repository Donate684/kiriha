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
                Txt("13 концептов карточек. Тема переключается на лету.", 13, FontWeight.Normal, headerText2)
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
            ItemWidth = 184,
            ItemHeight = 360
        };

        var rnd = new Random();
        
        var imgPath = @"C:\Users\ASUS\.gemini\antigravity-ide\brain\924958af-7837-4fb1-88fa-5723fd951a62\anime_cover_mockup_1780311451533.png";
        try
        {
            var cacheDir = Kiriha.Core.PathHelper.GetImageCachePath();
            if (System.IO.Directory.Exists(cacheDir))
            {
                var files = System.IO.Directory.GetFiles(cacheDir);
                if (files.Length > 0)
                {
                    imgPath = files[rnd.Next(files.Length)];
                }
            }
        }
        catch { }

        // --- Новые концепты ---
        wrap.Children.Add(Variant("A",  "Poster First",     CardA_PosterFirst(_isLight, imgPath, rnd.Next(Titles.Length)),     _isLight));
        wrap.Children.Add(Variant("D",  "Full Cinematic",   CardD_FullCinematic(_isLight, imgPath, rnd.Next(Titles.Length)),   _isLight));

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















    // ═══════════════════════════════════════════════════════════════════
    //  НОВЫЕ КОНЦЕПТЫ A–F
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// A — Poster First: постер 62%, снизу чистый блок. Таблетки на постере,
    /// прогресс точками (зелёные/красные), управление сериями в одну строку.
    /// </summary>
    private static Control CardA_PosterFirst(bool isLight, string imgPath, int i)
    {
        var cardBg   = isLight ? "#FFFFFF" : "#16181E";
        var border   = isLight ? "#E4E7EC" : "#252830";
        var titleFg  = isLight ? "#0D1117" : "#EAEDF2";
        var subFg    = isLight ? "#6B7280" : "#6E7685";
        var epFg     = isLight ? "#374151" : "#C0C8D4";
        var epMuted  = isLight ? "#9CA3AF" : "#525C6B";
        var dotOn    = "#3DBA6F";
        var dotOff   = isLight ? "#F0493E" : "#8B2020";
        var shadow   = isLight ? "0 2 12 0 #10000000" : "0 4 20 0 #35000000";

        var card = new Border
        {
            Width = 164, Height = 318,
            CornerRadius = new CornerRadius(14),
            ClipToBounds = true,
            Background = Brush(cardBg),
            BorderBrush = Brush(border),
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse(shadow)
        };

        // постер с таблетками поверх
        var posterPanel = new Panel { Height = 195 };
        var posterImg = RealPoster(0, new Thickness(0), imgPath);
        posterImg.CornerRadius = new CornerRadius(14, 14, 0, 0);
        posterPanel.Children.Add(posterImg);
        // «Новый эп.» — яркая оранжевая таблетка
        posterPanel.Children.Add(new Border
        {
            Padding = new Thickness(7, 4),
            CornerRadius = new CornerRadius(20),
            Background = Brush("#FF4D00"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(9, 9, 0, 0),
            Child = Txt("Новый эп.", 10, FontWeight.Bold, "#FFFFFF")
        });
        // «★ —» рейтинг — тёмная таблетка
        posterPanel.Children.Add(new Border
        {
            Padding = new Thickness(6, 4),
            CornerRadius = new CornerRadius(20),
            Background = Brush("#CC000000"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 9, 9, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 3,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    MIcon(MaterialIconKind.Star, "#FFD700", 11),
                    Txt("—", 10, FontWeight.SemiBold, "#FFFFFF")
                }
            }
        });

        // нижняя секция
        var bottom = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,Auto"),
            Margin = new Thickness(12, 10, 12, 12),
            Children =
            {
                // Заголовок
                new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        Txt(Titles[i], 13, FontWeight.Bold, titleFg).WithMaxLines(2).WithMinHeight(36),
                        Txt(Subtitles[i], 10, FontWeight.Normal, subFg).WithMaxLines(2).WithMinHeight(28)
                    }
                }.WithRow(0),
                // Точечный прогресс 7/12
                NewDotProgress(7, 12, dotOn, dotOff).WithRow(1).WithAlign(HorizontalAlignment.Center, VerticalAlignment.Center, new Thickness(0, 0, 0, 8)),
                // Управление сериями
                NewEpRow("7", "12", epFg, epMuted, "#3DBA6F").WithRow(2)
            }
        };

        card.Child = new Grid
        {
            RowDefinitions = new RowDefinitions("195,*"),
            Children = { posterPanel.WithRow(0), bottom.WithRow(1) }
        };
        return card;
    }

    /// <summary>
    /// B — Glassy Overlay: постер во весь рост, снизу матовое стекло.
    /// Всё содержимое поверх постера.
    /// </summary>


    /// <summary>
    /// C — Banner Cut: постер с чётким горизонтальным срезом + тонкая акцент-линия,
    /// затем белая/тёмная секция с инфо.
    /// </summary>


    /// <summary>
    /// D — Full Cinematic: постер во весь рост, тёмный градиент снизу,
    /// весь текст и кнопки поверх.
    /// </summary>
    private static Control CardD_FullCinematic(bool isLight, string imgPath, int i)
    {
        var gradStart = isLight ? "#00000000" : "#00000000";
        var gradMid   = isLight ? "#B0000000" : "#C0000000";
        var gradEnd   = isLight ? "#EE000000" : "#F5000000";
        var accentLine= "#F178B6";
        var shadow    = isLight ? "0 4 20 0 #20000000" : "0 6 28 0 #50000000";

        var card = new Border
        {
            Width = 164, Height = 318,
            CornerRadius = new CornerRadius(14),
            ClipToBounds = true,
            BoxShadow = BoxShadows.Parse(shadow)
        };

        var poster = RealPoster(0, new Thickness(0), imgPath);

        // Тёмный градиент снизу
        var gradient = new Border
        {
            Height = 210,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint   = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse(gradStart), 0),
                    new GradientStop(Color.Parse(gradMid),   0.4),
                    new GradientStop(Color.Parse(gradEnd),   1)
                }
            }
        };

        // «Новый эп.» таблетка (верх-лево)
        var newPill = new Border
        {
            Padding = new Thickness(7, 4),
            CornerRadius = new CornerRadius(20),
            Background = Brush("#FF4D00"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(9, 9, 0, 0),
            Child = Txt("Новый эп.", 10, FontWeight.Bold, "#FFFFFF")
        };

        // Рейтинг (верх-право)
        var ratingPill = new Border
        {
            Padding = new Thickness(6, 4),
            CornerRadius = new CornerRadius(20),
            Background = Brush("#CC000000"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 9, 9, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 3,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { MIcon(MaterialIconKind.Star, "#FFD700", 11), Txt("—", 10, FontWeight.SemiBold, "#FFFFFF") }
            }
        };

        // Текст и контролы у низа
        var textBlock = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,Auto"),
            Margin = new Thickness(12, 205, 12, 12),
            Children =
            {
                new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        Txt(Titles[i], 13, FontWeight.Bold, "#FFFFFF").WithMaxLines(2).WithMinHeight(36),
                        Txt(Subtitles[i], 10, FontWeight.Normal, "#A0FFFFFF").WithMaxLines(2).WithMinHeight(28)
                    }
                }.WithRow(0),
                NewDotProgress(7, 12, "#F178B6", "#50FFFFFF").WithRow(1).WithAlign(HorizontalAlignment.Center, VerticalAlignment.Center, new Thickness(0, 0, 0, 8)),
                NewEpRow("7", "12", "#FFFFFF", "#80FFFFFF", accentLine).WithRow(2)
            }
        };

        card.Child = new Panel
        {
            Children = { poster, gradient, newPill, ratingPill, textBlock }
        };
        return card;
    }

    /// <summary>
    /// E — Neon Pulse: тёмная карточка с цветным свечением на фоне,
    /// постер с закруглёнными углами, неоновые акценты.
    /// </summary>


    /// <summary>
    /// F — Paper Card: тёплый минималистичный стиль, кремовый/коричневый,
    /// без жёстких теней — как на журнальной странице.
    /// </summary>


    // ─── Общие хелперы для новых концептов ───────────────────────────────

    /// <summary>Dot-прогресс: capsule-точки двух цветов.</summary>
    private static Control NewDotProgress(int current, int total, string onColor, string offColor)
    {
        var gap  = total <= 12 ? 3.5 : 2.5;
        var segW = Math.Max(5.0, (136.0 - (total - 1) * gap) / total);
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = gap
        };
        for (var j = 0; j < total; j++)
        {
            panel.Children.Add(new Border
            {
                Width = segW, Height = 5,
                CornerRadius = new CornerRadius(2.5),
                Background = j < current ? Brush(onColor) : Brush(offColor)
            });
        }
        return panel;
    }

    /// <summary>Строка управления сериями: [—] 7 из 12 эп. [+]</summary>
    private static Grid NewEpRow(string cur, string total, string fg, string muted, string accent)
    {
        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Children =
            {
                MIcon(MaterialIconKind.Minus, muted, 16).WithColumn(0),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 3,
                    Children =
                    {
                        Txt(cur,    12, FontWeight.SemiBold, fg),
                        Txt("из",   10, FontWeight.Normal,   muted),
                        Txt(total,  11, FontWeight.Normal,   muted),
                        Txt("эп.",  10, FontWeight.Normal,   muted)
                    }
                }.WithColumn(1),
                MIcon(MaterialIconKind.Plus, accent, 16).WithColumn(2)
            }
        };
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
                Txt(Subtitles[i], 10, FontWeight.Normal, secondary).WithMaxLines(2)
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
    public static TextBlock WithMinHeight(this TextBlock text, double h) { text.MinHeight = h; return text; }
}
