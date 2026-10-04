using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using BingLan.App.Windows;
using BingLan.Core.Models;
using BingLan.Core.Services;
using static BingLan.InformationTests.TestSupport;

namespace BingLan.InformationTests;

internal static class SettingsWindowTests
{
    private const string SearchJson = """
        {
          "results": [
            {
              "id": 1816670,
              "name": "北京",
              "latitude": 39.9075,
              "longitude": 116.3972,
              "country": "中国",
              "admin1": "北京市"
            }
          ]
        }
        """;

    public static void SearchSelectAndSave()
    {
        var stub = new StubHttpMessageHandler
        {
            Behavior = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SearchJson)
            })
        };
        var state = new InformationWidgetState
        {
            GreetingName = "旧称呼",
            Use24HourClock = true
        };
        string? savedGreeting = null;
        bool? savedClock = null;
        CitySearchResult? savedCity = null;
        var window = new SettingsWindow(
            new CitySearchService(stub),
            state,
            (greeting, use24Hour) =>
            {
                savedGreeting = greeting;
                savedClock = use24Hour;
            },
            city => savedCity = city);
        ShowAndPump(window);
        try
        {
            var currentCity = Require<TextBlock>(window, "CurrentCityText");
            Assert(currentCity.Text.Contains("未设置"), "默认城市状态可见");

            Require<TextBox>(window, "CityQueryEditor").Text = "北京";
            Click(Require<Button>(window, "SearchCityButton"));
            var results = Require<ListBox>(window, "CityResultsList");
            PumpUntil(
                () => results.Items.Count == 1,
                TimeSpan.FromSeconds(5),
                "城市候选写入设置窗口");

            results.SelectedIndex = 0;
            Pump();
            var apply = Require<Button>(window, "ApplyCityButton");
            Assert(apply.IsEnabled, "选择候选后允许应用");
            Click(apply);
            Assert(savedCity is not null, "选择结果传给宿主");
            AssertEqual("北京 · 北京市 · 中国", savedCity!.DisplayName, "选择城市显示名称");
            Assert(currentCity.Text.Contains("北京"), "当前城市立即更新");

            Require<TextBox>(window, "GreetingNameEditor").Text = "  小明  ";
            Require<CheckBox>(window, "Use24HourClockCheckBox").IsChecked = false;
            AssertEqual(false, savedClock, "切换 24 小时制应立即保存");
            window.CommitPendingChanges();
            AssertEqual("小明", savedGreeting, "称呼去除首尾空白后保存");
            AssertEqual(false, savedClock, "12 小时制设置保存");
            AssertEqual("已保存", Require<TextBlock>(window, "DisplaySettingsStatusText").Text,
                "显示设置保存反馈");
        }
        finally
        {
            window.Close();
            Pump();
        }
    }

    public static void ApplyPresetAndComponents()
    {
        var experience = DesktopExperienceRules.CreateDefault();
        DesktopExperienceState? componentApplied = null;
        DesktopExperienceState? presetApplied = null;
        bool? todoLocked = null;
        var todoWindow = new TodoWidgetWindow(new TodoWidgetState { Title = "今日待办" });
        ShowAndPump(todoWindow);
        var window = new SettingsWindow(
            new CitySearchService(new StubHttpMessageHandler()),
            new InformationWidgetState(),
            experience,
            (_, _) => { },
            _ => { },
            state => componentApplied = state,
            state => presetApplied = state,
            () => [todoWindow],
            (target, locked) =>
            {
                target.ApplyWidgetLock(locked);
                todoLocked = locked;
            });
        ShowAndPump(window);
        try
        {
            var previewBefore = Require<Image>(window, "PresetPreviewImage").Source;
            Require<RadioButton>(window, "GlacierWorkbenchPresetRadio").IsChecked = true;
            Click(Require<Button>(window, "ApplyLayoutButton"));

            Assert(presetApplied is not null, "应用布局应通知预设宿主");
            AssertEqual(
                DesktopLayoutPreset.GlacierWorkbench,
                presetApplied!.ActivePreset,
                "冰川工作台预设传给宿主");
            var previewAfter = Require<Image>(window, "PresetPreviewImage").Source;
            Assert(previewBefore is not null && previewAfter is not null && !ReferenceEquals(previewBefore, previewAfter),
                "预设选择应重新绘制预览");

            Require<Slider>(window, "SelectedComponentFontScaleSlider").Value = 1.5;
            Require<TextBox>(window, "SelectedComponentTextColorEditor").Text = "#000000";
            Require<TextBox>(window, "SelectedComponentBackgroundColorEditor").Text =
                "#A0B4E1";
            Require<Slider>(window, "SelectedComponentOpacitySlider").Value = 0.5;
            Require<Slider>(window, "SelectedComponentCornerRadiusSlider").Value = 22;
            Require<TextBox>(window, "SelectedComponentWidthEditor").Text = "420";
            Require<TextBox>(window, "SelectedComponentHeightEditor").Text = "240";
            Assert(componentApplied is null, "输入停顿前不应逐字应用");
            window.CommitPendingChanges();

            Assert(componentApplied is not null, "组件设置应自动通知组件宿主");
            var timeDate = componentApplied!.GetComponent(DesktopComponentKind.TimeDate);
            AssertEqual(1.5, timeDate.FontScale, "组件字号缩放传给宿主");
            AssertEqual("#000000", timeDate.Appearance.TextColor, "组件文字颜色传给宿主");
            AssertEqual(0.5, timeDate.Appearance.BackgroundOpacity,
                "组件玻璃不透明度传给宿主");
            AssertEqual("#A0B4E1", timeDate.Appearance.BackgroundColor,
                "组件玻璃颜色传给宿主");
            AssertEqual(22d, timeDate.CornerRadius, "组件圆角传给宿主");
            AssertEqual(420d, timeDate.Placement.Width, "组件宽度传给宿主");
            AssertEqual(240d, timeDate.Placement.Height, "组件高度传给宿主");

            window.ShowWidgetSettings(todoWindow);
            Pump();
            var componentList = Require<ListBox>(window, "DesktopComponentList");
            Assert(
                componentList.SelectedItem is SettingsWindow.DesktopComponentSettingsEntry
                {
                    Kind: DesktopComponentKind.Todo
                },
                "从待办卡片进入设置应在组件列表选中对应卡片");
            AssertEqual(
                "今日待办",
                Require<TextBlock>(window, "SelectedComponentHeadingText").Text,
                "统一设置没有显示当前卡片名称");
            Require<CheckBox>(window, "SelectedComponentLockedCheckBox").IsChecked = true;
            AssertEqual(true, todoLocked, "勾选锁定应立即通知宿主");
        }
        finally
        {
            window.Close();
            todoWindow.CanClose = true;
            todoWindow.Close();
            Pump();
        }
    }

    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
    }
}
