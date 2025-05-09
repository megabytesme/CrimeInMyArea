using Microsoft.Extensions.Logging;
using Shared_Code.Services;
using Syncfusion.Maui.Toolkit.Hosting;

namespace CrimeInMyArea
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureSyncfusionToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
            builder.Logging.AddDebug();
#endif

            builder.Services.AddSingleton<HttpClient>();
            builder.Services.AddSingleton<CrimeDataService>();
            builder.Services.AddSingleton<IGeolocation>(Geolocation.Default);
            builder.Services.AddTransient<MainPage>();
            builder.Services.AddTransient<LogPage>();

            return builder.Build();
        }
    }
}