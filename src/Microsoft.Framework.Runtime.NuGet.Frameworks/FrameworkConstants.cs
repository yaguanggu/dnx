using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NuGet.Frameworks
{
    // TODO: Replace this with NuGet.Frameworks

    public static class FrameworkConstants
    {
        public static class FrameworkIdentifiers
        {
            public const string Net = ".NETFramework";
            public const string NetFrameworkCore = "NETFrameworkCore"; // the actual .NET Core
            public const string NetCore = ".NETCore"; // deprecated
            public const string WinRT = "WinRT"; // deprecated
            public const string NetMicro = ".NETMicroFramework";
            public const string Portable = ".NETPortable";
            public const string WindowsPhone = "WindowsPhone";
            public const string Windows = "Windows";
            public const string WindowsPhoneApp = "WindowsPhoneApp";
            public const string CoreCLR = "CoreCLR";
            public const string Dnx = "DNX";
            public const string DnxCore = "DNXCore";
            public const string AspNet = "ASP.NET";
            public const string AspNetCore = "ASP.NETCore";
            public const string Silverlight = "Silverlight";
            public const string Native = "native";
            public const string MonoAndroid = "MonoAndroid";
            public const string MonoTouch = "MonoTouch";
            public const string MonoMac = "MonoMac";
            public const string XamarinIOs = "Xamarin.iOS";
            public const string XamarinMac = "Xamarin.Mac";
            public const string XamarinPlayStation3 = "Xamarin.PlayStation3";
            public const string XamarinPlayStation4 = "Xamarin.PlayStation4";
            public const string XamarinPlayStationVita = "Xamarin.PlayStationVita";
            public const string XamarinXbox360 = "Xamarin.Xbox360";
            public const string XamarinXboxOne = "Xamarin.XboxOne";
        }
    }
}
