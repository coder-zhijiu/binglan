using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pogget.VisualStudy;

public partial class VisualStudyWindow : Window
{
    public VisualStudyWindow()
    {
        InitializeComponent();
        DataContext = new ObservableCollection<FileTile>
        {
            new("1_现代农学院网...", ".docx"),
            new("8期摘要.doc...", ".docx"),
            new("8期摘要_一致性...", ".docx"),
            new("8期摘要_英文校...", ".docx"),
            new("2026小兴安岭...", ".docx"),
            new("晋北土壤利用与碳...", ".docx"),
            new("退还金额格式.x...", ".xlsx"),
            new("委托业务专题可行...", ".docx"),
            new("专家咨询费模板...", ".xlsx"),
            new("查看信件.pdf", ".pdf"),
            new("2026-07-...", ".pdf")
        };
    }

    public sealed class FileTile
    {
        public FileTile(string name, string extension)
        {
            Name = name;
            Icon = ShellIcon.Get(extension);
        }

        public string Name { get; }
        public ImageSource? Icon { get; }
    }

    private static class ShellIcon
    {
        private const uint ShgfiIcon = 0x00000100;
        private const uint ShgfiLargeIcon = 0x00000000;
        private const uint ShgfiUseFileAttributes = 0x00000010;
        private const uint FileAttributeNormal = 0x00000080;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShellFileInfo
        {
            public nint IconHandle;
            public int IconIndex;
            public uint Attributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string DisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string TypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern nint SHGetFileInfo(
            string path,
            uint attributes,
            out ShellFileInfo info,
            uint infoSize,
            uint flags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(nint iconHandle);

        public static ImageSource? Get(string extension)
        {
            var result = SHGetFileInfo(
                $"visual-study{extension}",
                FileAttributeNormal,
                out var info,
                (uint)Marshal.SizeOf<ShellFileInfo>(),
                ShgfiIcon | ShgfiLargeIcon | ShgfiUseFileAttributes);
            if (result == 0 || info.IconHandle == 0)
            {
                return null;
            }

            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    info.IconHandle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(48, 48));
                source.Freeze();
                return source;
            }
            finally
            {
                DestroyIcon(info.IconHandle);
            }
        }
    }
}
