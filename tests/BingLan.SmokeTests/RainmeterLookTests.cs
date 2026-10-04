using BingLan.Core.Themes;

/// <summary>Reading the look of a Rainmeter skin from its text.</summary>
internal static class RainmeterLookTests
{
    internal static void TestPerformanceSkinLook()
    {
        // Shaped like the Fluent_Design performance skin.
        var skin = """
            [Rainmeter]
            Update=1000
            [StyleLabel]
            FontFace=Segoe UI
            FontColor=255,255,255,185
            [StyleValue]
            FontFace=Segoe UI
            FontColor=255,255,255,250
            [MeterBackground]
            Meter=Shape
            Shape=Rectangle 0,0,354,76,18 | Fill Color 255,255,255,16 | StrokeWidth 1 | Stroke Color 255,255,255,62
            [MeterCPUBarBack]
            Meter=Shape
            Shape=Rectangle 16,61,80,3,1 | Fill Color 255,255,255,55 | StrokeWidth 0
            [MeterCPUBar]
            Meter=Bar
            BarColor=30,169,255,245
            [MeterNetIn]
            FontColor=30,169,255,245
            FontFace=Segoe UI
            """;
        var look = RainmeterSkinReader.Read([skin]);
        Check(look.FontFace == "Segoe UI", "应读出最常用的字体");
        Check(look.TextColor == "#FFFFFF", "应读出最常用的文字颜色");
        Check(look.BackgroundColor == "#FFFFFF" && look.BackgroundOpacity == 0.06,
            "应读出最大底板的颜色和不透明度");
        Check(look.CornerRadius == 18, "应读出最大底板的圆角");
        Check(look.AccentColor == "#1EA9FF", "应读出进度条的强调色");
    }

    internal static void TestVariablesAndEmptySkins()
    {
        var skin = """
            [Variables]
            @include=#@#Variables.inc
            [Style]
            FontColor=#FontColor#
            FontFace=#FontFace#
            [MeasureLua]
            Measure=Script
            ScriptFile=evil.lua
            """;
        var variables = """
            [Variables]
            FontColor=E0F0FF
            FontFace=Jost
            """;
        var look = RainmeterSkinReader.Read([skin, variables]);
        Check(look.FontFace == "Jost" && look.TextColor == "#E0F0FF", "应解析 @Resources 里定义的变量和十六进制颜色");
        Check(look.BackgroundColor is null && look.AccentColor is null, "没有设置的部分应为空");
        Check(RainmeterSkinReader.Read(["[Rainmeter]\nUpdate=1000"]).IsEmpty, "没有外观设置的皮肤应为空");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
