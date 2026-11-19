using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Morph.UI;

/// <summary>
/// Developer aid behind `Morph.exe --screenshot out.png [files…]`: opens the window off-screen,
/// lets it lay out, and saves what it shows. A file argument of "--state:running" or
/// "--state:finished" fakes those screens with the files given.
/// </summary>
static class Screenshot
{
    public static async Task TakeAsync(string output, List<string> args)
    {
        var state = args.Find(a => a.StartsWith("--state:"))?["--state:".Length..];
        args.RemoveAll(a => a.StartsWith("--state:"));

        var w = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false };
        w.Show();
        if (args.Count > 0) w.Model.Add(args);
        if (state != null) w.Model.Preview(state);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(400);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

        var root = (FrameworkElement)w.Content;
        var dpi = VisualTreeHelper.GetDpi(w);
        var bmp = new RenderTargetBitmap(
            (int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle((Brush)Application.Current.Resources["Bg"], null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        }
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = File.Create(output)) enc.Save(fs);
        w.Close();
    }
}
