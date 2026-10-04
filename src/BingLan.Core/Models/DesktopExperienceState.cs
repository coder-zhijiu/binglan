namespace BingLan.Core.Models;

public enum DesktopLayoutPreset
{
    QuietInformation,
    GlacierWorkbench,
    TransparentApple,
    CenterClock
}

public enum DesktopComponentKind
{
    TimeDate,
    Greeting,
    Weather,
    Performance,
    Todo,
    AudioVisualizer,
    QuickLaunch
}

public enum DesktopInformationDensity
{
    Comfortable,
    Standard,
    Compact
}

public sealed class DesktopComponentState
{
    private double _fontScale = 1d;
    private double _cornerRadius = WidgetAppearanceRules.DefaultCornerRadius;

    public DesktopComponentKind Kind { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool IsLocked { get; set; }

    /// <summary>For the time card: a thin line under the date that fades out at both ends.</summary>
    public bool ShowDivider { get; set; }

    private double _dividerThickness = 2d;

    /// <summary>How thick that line is, from 1 to 6 DIPs.</summary>
    public double DividerThickness
    {
        get => _dividerThickness;
        set => _dividerThickness = double.IsFinite(value) ? Math.Clamp(value, 1d, 6d) : 2d;
    }

    public DesktopInformationDensity Density { get; set; } = DesktopInformationDensity.Standard;
    public WindowPlacement Placement { get; set; } = new();
    public WidgetAppearanceState Appearance { get; set; } = new();

    /// <summary>The font of the card's text; null keeps the app's interface font.</summary>
    public string? FontFamily { get; set; }

    public double FontScale
    {
        get => _fontScale;
        set => _fontScale = DesktopExperienceRules.CoerceFontScale(value);
    }

    public double CornerRadius
    {
        get => _cornerRadius;
        set => _cornerRadius = WidgetAppearanceRules.CoerceCornerRadius(value);
    }
}

public sealed class DesktopExperienceState
{
    public DesktopLayoutPreset ActivePreset { get; set; } =
        DesktopLayoutPreset.QuietInformation;
    public List<DesktopComponentState> Components { get; set; } =
        DesktopExperienceRules.CreateDefaultComponents();

    public DesktopComponentState GetComponent(DesktopComponentKind kind) =>
        Components.First(component => component.Kind == kind);
}

public static class DesktopExperienceRules
{
    public const double MinimumFontScale = 0.7d;
    public const double MaximumFontScale = 2d;

    public static IReadOnlyList<DesktopComponentKind> ComponentOrder { get; } =
    [
        DesktopComponentKind.TimeDate,
        DesktopComponentKind.Greeting,
        DesktopComponentKind.Weather,
        DesktopComponentKind.Performance,
        DesktopComponentKind.Todo,
        DesktopComponentKind.AudioVisualizer,
        DesktopComponentKind.QuickLaunch
    ];

    public static DesktopExperienceState CreateDefault() => new()
    {
        Components = CreateDefaultComponents()
    };

    public static List<DesktopComponentState> CreateDefaultComponents() =>
        ComponentOrder.Select(CreateDefaultComponent).ToList();

    public static DesktopComponentState CreateDefaultComponent(DesktopComponentKind kind)
    {
        var state = new DesktopComponentState
        {
            Kind = kind,
            IsVisible = kind is not DesktopComponentKind.AudioVisualizer and
                not DesktopComponentKind.QuickLaunch,
            Placement = kind switch
            {
                DesktopComponentKind.TimeDate => Placement(92, 92, 320, 166),
                DesktopComponentKind.Greeting => Placement(536, 856, 900, 115),
                DesktopComponentKind.Weather => Placement(92, 270, 360, 150),
                DesktopComponentKind.Performance => Placement(92, 430, 380, 126),
                DesktopComponentKind.Todo => Placement(92, 570, 340, 300),
                DesktopComponentKind.AudioVisualizer => Placement(536, 620, 850, 220),
                DesktopComponentKind.QuickLaunch => Placement(92, 830, 380, 72),
                _ => new WindowPlacement()
            }
        };
        if (kind is DesktopComponentKind.TimeDate or DesktopComponentKind.Greeting)
        {
            state.Appearance.BackgroundOpacity = WidgetAppearanceRules.MinimumBackgroundOpacity;
            state.Appearance.HeaderOpacity = 0d;
            state.CornerRadius = 0d;
            state.Appearance.TextColor = "#FFFFFF";
        }
        return state;
    }

    public static void Normalize(DesktopExperienceState state)
    {
        if (!Enum.IsDefined(state.ActivePreset))
        {
            state.ActivePreset = DesktopLayoutPreset.QuietInformation;
        }

        state.Components ??= [];
        foreach (var kind in ComponentOrder)
        {
            var component = state.Components.FirstOrDefault(candidate => candidate.Kind == kind);
            if (component is null)
            {
                state.Components.Add(CreateDefaultComponent(kind));
                continue;
            }

            component.Appearance ??= new WidgetAppearanceState();
            component.FontFamily = string.IsNullOrWhiteSpace(component.FontFamily)
                ? null
                : WidgetAppearanceRules.CoerceTitleFontFamily(component.FontFamily);
            component.Placement ??= CreateDefaultComponent(kind).Placement;
            component.FontScale = CoerceFontScale(component.FontScale);
            component.CornerRadius = WidgetAppearanceRules.CoerceCornerRadius(
                component.CornerRadius);
            component.Appearance.BackgroundColor =
                WidgetAppearanceRules.CoerceBackgroundColor(component.Appearance.BackgroundColor);
            component.Appearance.BackgroundOpacity =
                WidgetAppearanceRules.CoerceBackgroundOpacity(component.Appearance.BackgroundOpacity);
            component.Appearance.HeaderColor =
                WidgetAppearanceRules.CoerceHeaderColor(component.Appearance.HeaderColor);
            component.Appearance.HeaderOpacity =
                WidgetAppearanceRules.CoerceHeaderOpacity(component.Appearance.HeaderOpacity);
            component.Appearance.TextColor =
                WidgetAppearanceRules.CoerceTextColor(component.Appearance.TextColor);
            component.Appearance.TitleFontFamily =
                WidgetAppearanceRules.CoerceTitleFontFamily(component.Appearance.TitleFontFamily);
            if (!Enum.IsDefined(component.Density))
            {
                component.Density = DesktopInformationDensity.Standard;
            }
        }

        state.Components = ComponentOrder
            .Select(kind => state.Components.First(component => component.Kind == kind))
            .ToList();
    }

    public static double CoerceFontScale(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 1d;
        }
        return Math.Clamp(value, MinimumFontScale, MaximumFontScale);
    }

    public static string GetPresetName(DesktopLayoutPreset preset) => preset switch
    {
        DesktopLayoutPreset.QuietInformation => "静谧信息层",
        DesktopLayoutPreset.GlacierWorkbench => "冰川工作台",
        DesktopLayoutPreset.TransparentApple => "通透苹果桌面",
        DesktopLayoutPreset.CenterClock => "居中时钟",
        _ => "静谧信息层"
    };

    public static string GetComponentName(DesktopComponentKind kind) => kind switch
    {
        DesktopComponentKind.TimeDate => "时间日期",
        DesktopComponentKind.Greeting => "问候语",
        DesktopComponentKind.Weather => "天气",
        DesktopComponentKind.Performance => "性能",
        DesktopComponentKind.Todo => "待办",
        DesktopComponentKind.AudioVisualizer => "音频可视化",
        DesktopComponentKind.QuickLaunch => "快捷入口",
        _ => kind.ToString()
    };

    /// <summary>
    /// Where a layout preset puts each card inside a work area. Used both to apply a
    /// preset and to draw its preview, so the two always match.
    /// </summary>
    public static IReadOnlyDictionary<DesktopComponentKind, WindowPlacement> PresetPlacements(
        DesktopLayoutPreset preset,
        double left,
        double top,
        double right,
        double bottom)
    {
        var area = (Left: left, Top: top, Right: right, Bottom: bottom);
        if (preset == DesktopLayoutPreset.CenterClock)
        {
            // A large clock centred at the top, the greeting centred near the bottom, and
            // performance, weather and to-dos in one column at the top right.
            var center = (area.Left + area.Right) / 2d;
            var column = area.Right - 400;
            return new Dictionary<DesktopComponentKind, WindowPlacement>
            {
                [DesktopComponentKind.TimeDate] = Placement(center - 280, area.Top + 56, 560, 200),
                [DesktopComponentKind.Greeting] = Placement(center - 320, area.Bottom - 150, 640, 84),
                [DesktopComponentKind.Performance] = Placement(column, area.Top + 30, 370, 126),
                [DesktopComponentKind.Weather] = Placement(column, area.Top + 172, 370, 150),
                [DesktopComponentKind.Todo] = Placement(column, area.Top + 338, 370, 300)
            };
        }

        var (timeDate, greeting, weather, performance) = preset switch
        {
            DesktopLayoutPreset.GlacierWorkbench =>
            (
                Placement(area.Left + 62, area.Top + 70, 320, 170),
                Placement(area.Left + 430, area.Bottom - 138, 760, 100),
                Placement(area.Left + 62, area.Top + 250, 320, 150),
                Placement(area.Left + 62, area.Top + 410, 320, 136)
            ),
            DesktopLayoutPreset.TransparentApple =>
            (
                Placement(area.Left + 70, area.Top + 60, 410, 170),
                Placement(area.Left + 510, area.Bottom - 130, 820, 96),
                Placement(area.Left + 70, area.Top + 248, 360, 150),
                Placement(area.Left + 70, area.Top + 414, 380, 126)
            ),
            _ =>
            (
                Placement(area.Left + 70, area.Top + 70, 410, 176),
                Placement(area.Left + 500, area.Bottom - 132, 820, 96),
                Placement(area.Left + 70, area.Top + 260, 360, 150),
                Placement(area.Left + 70, area.Top + 424, 380, 126)
            )
        };
        var todo = preset switch
        {
            DesktopLayoutPreset.GlacierWorkbench => Placement(area.Right - 390, area.Top + 110, 350, 420),
            DesktopLayoutPreset.TransparentApple => Placement(area.Right - 390, area.Top + 500, 350, 360),
            _ => Placement(area.Right - 370, area.Top + 86, 330, 410)
        };
        return new Dictionary<DesktopComponentKind, WindowPlacement>
        {
            [DesktopComponentKind.TimeDate] = timeDate,
            [DesktopComponentKind.Greeting] = greeting,
            [DesktopComponentKind.Weather] = weather,
            [DesktopComponentKind.Performance] = performance,
            [DesktopComponentKind.Todo] = todo
        };
    }

    private static WindowPlacement Placement(
        double left,
        double top,
        double width,
        double height) => new()
        {
            Left = left,
            Top = top,
            Width = width,
            Height = height
        };
}
