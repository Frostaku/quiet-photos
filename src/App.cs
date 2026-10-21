using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("QuietPhotos.Tests")]

namespace QuietPhotos
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                var app = new Application();
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("QuietPhotos.Theme.xaml"))
                    app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
                app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                {
                    MessageBox.Show(e.Exception.Message, "Quiet Photos", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.Handled = true;
                };
                app.Run(new ViewerWindow(args));
                return 0;
            }
            catch (Exception e) { MessageBox.Show(e.Message, "Quiet Photos could not start"); return 1; }
        }
    }
}
