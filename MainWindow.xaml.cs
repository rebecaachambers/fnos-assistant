using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using FnosAssistant.Models;

namespace FnosAssistant;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SetIcon();
    }

    private void SetIcon()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("FnosAssistant.icon.ico");
            if (stream != null)
            {
                var decoder = new System.Windows.Media.Imaging.IconBitmapDecoder(
                    stream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnDemand);
                Icon = decoder.Frames[0];
            }
        }
        catch { }

    }

    private void ListViewItem_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListView listView && listView.SelectedItem is DeviceInfo device)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = device.WebUrl,
                UseShellExecute = true
            });
        }
    }
}

public class BoolInvertConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}
