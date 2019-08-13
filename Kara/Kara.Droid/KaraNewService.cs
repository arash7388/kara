using System;
using System.Collections.Generic;

using Android.App;
using Android.Content;
using Android.OS;
using System.Linq;
using Android.Locations;
using Android.Runtime;
using Android.Widget;
using SQLite;
using Plugin.Settings;
using System.Threading.Tasks;
using Kara.Assets;
using Kara.Droid.Helpers;
using Android.Util;
using Kara.Models;
using Kara.Helpers;

namespace Kara.Droid
{
    [BroadcastReceiver]
    [IntentFilter(new[] { Android.Content.Intent.ActionBootCompleted },
        Categories = new[] { Android.Content.Intent.CategoryDefault }
    )]
    public class KaraNewServiceLauncher : BroadcastReceiver
    {
        public override void OnReceive(Context context, Intent intent)
        {
            if ((intent.Action != null) && (intent.Action == Intent.ActionBootCompleted))
            {
                StartAndScheduleAlarmManagerForkaraNewService(context);
            }
        }

        public static void StartAndScheduleAlarmManagerForkaraNewService(Context context)
        {
            KaraNewService.MainContext = context;
            var karaNewServiceIntent = new Intent("com.kara.KaraNewService");
            karaNewServiceIntent.SetPackage(context.PackageName);
            context.StartService(karaNewServiceIntent);
            ScheduleAlarmManagerForkaraNewService(context, karaNewServiceIntent);
        }

        public static void ScheduleAlarmManagerForkaraNewService(Context context, Intent karaNewServiceIntent)
        {
            if (PendingIntent.GetBroadcast(context, 0, karaNewServiceIntent, PendingIntentFlags.NoCreate) == null)
            {
                var alarm = (AlarmManager)context.GetSystemService(Context.AlarmService);

                var pendingServiceIntent = PendingIntent.GetService(context, 0, karaNewServiceIntent, PendingIntentFlags.CancelCurrent);
                alarm.SetRepeating(AlarmType.ElapsedRealtimeWakeup, SystemClock.ElapsedRealtime(), 30 * 1000, pendingServiceIntent);
            }
        }
    }



    [Service]
    [IntentFilter(new String[] { "com.kara.KaraNewService" })]
    public class KaraNewService : Service, ILocationListener
    {
        public static Context MainContext { get; set; }
        public static KaraNewService KaraNewServiceInstance;
        //TODO
        //GetLocationsPerid
        //MaxAcceptableAccuracy
        //GPSTracking_GPSShouldBeTurnedOnToWorkWithApp
        //GPSTracking_NetworkShouldBeTurnedOnToWorkWithApp
        public override void OnCreate()
        {
            MainActivity.InitializeSharedResources(this, ContentResolver);

            KaraNewServiceInstance = this;

            if (locMgr == null)
            {
                locMgr = (LocationManager)GetSystemService(LocationService);
                RequestLocationUpdates();
            }

            CheckForPointsForeverAsync();

            //CheckForAutoTimeEnabledForeverAsync();

            //TODO
            //var autoTimeChangedReceiver = new AutoTimeChangedReceiver() { ContentResolver = ContentResolver };
            //
            //var autoTimeChangedFilter = new IntentFilter();
            //autoTimeChangedFilter.AddAction(Intent.ActionTimeChanged);
            //RegisterReceiver(autoTimeChangedReceiver, autoTimeChangedFilter);

            base.OnCreate();
        }
        //TODO
        //class AutoTimeChangedReceiver : BroadcastReceiver
        //{
        //    public ContentResolver ContentResolver { get; set; }
        //    public override void OnReceive(Context context, Android.Content.Intent intent)
        //    {
        //        if (Android.Provider.Settings.Global.GetInt(ContentResolver, Android.Provider.Settings.Global.AutoTime, 0) == 1)
        //            App.MajorDeviceSettingsChanged(App.ChangedMajorDeviceSetting.AutomaticTimeEnabled);
        //        if (Android.Provider.Settings.Global.GetInt(ContentResolver, Android.Provider.Settings.Global.AutoTime, 0) != 1)
        //            App.MajorDeviceSettingsChanged(App.ChangedMajorDeviceSetting.AutomaticTimeDisabled);
        //    }
        //}

        public override void OnStart(Intent intent, int startId)
        {
            base.OnStart(intent, startId);
        }

        public override StartCommandResult OnStartCommand(Android.Content.Intent intent, StartCommandFlags flags, int startId)
        {
            return StartCommandResult.Sticky;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();

            var karaNewServiceIntent = new Intent("com.kara.KaraNewService");
            karaNewServiceIntent.SetPackage(this.PackageName);
            this.StartService(karaNewServiceIntent);
        }

        public override IBinder OnBind(Intent intent)
        {
            return null;
        }






        LocationManager locMgr;
        static string GpsProvider = LocationManager.GpsProvider;
        static string NetworkProvider = LocationManager.NetworkProvider;
        List<LocationModel> locations = new List<LocationModel>();
        private void addLocationToList(Location location)
        {
            if (locations.Count >= 300)
                locations = locations.Select((a, index) => new { a, index }).OrderByDescending(a => a.index).Take(200).OrderBy(a => a.index).Select(a => a.a).ToList();

            var NowTimeStamp = DateTime.Now.ToTimeStamp();
            var RecentLocations = locations.Where(a => a.Timestamp < NowTimeStamp && a.Timestamp > NowTimeStamp - App.GetLocationsPerid.Value * 1000 / 2).ToArray();
            if (!RecentLocations.Any() || location.Accuracy < RecentLocations.Min(a => a.Accuracy))
            {
                foreach (var RecentLocation in RecentLocations)
                    locations.Remove(RecentLocation);
                var NewLocation = new LocationModel()
                {
                    Timestamp = NowTimeStamp,
                    Latitude = location.Latitude,
                    Longitude = location.Longitude,
                    Accuracy = location.Accuracy,
                    DeviceState = (int)(location.Accuracy > App.MaxAcceptableAccuracy.Value ? DeviceState.LocationWithTooMuchError : DeviceState.GoodLocation),
                    SentToApplication = false
                };

                locations.Add(NewLocation);

                App.LastLocation = NewLocation;
            }
        }
        public void OnLocationChanged(Location location)
        {
            try
            {
                addLocationToList(location);
            }
            catch (Exception err)
            {
                Log.Error("Kara Tracking Service", "exception: " + err.Message + ", StackTrace: " + (err.StackTrace == null ? "---" : err.StackTrace));
            }
        }

        public void OnProviderDisabled(string provider)
        {
            if (provider == GpsProvider)
                GPSIsOn = false;
            if (provider == NetworkProvider)
                GPSIsOn = false;
        }
        public void OnProviderEnabled(string provider)
        {
            if (provider == GpsProvider)
                GPSIsOn = true;
            if (provider == NetworkProvider)
                GPSIsOn = true;
            RequestLocationUpdates();
        }
        public void OnStatusChanged(string provider, [GeneratedEnum] Availability status, Bundle extras)
        {
        }

        static bool? UserGaveGPSPermission = null;
        private void RequestLocationUpdates()
        {
            try
            {
                if (locMgr.IsProviderEnabled(GpsProvider))
                    GPSIsOn = true;
                else
                    GPSIsOn = false;

                if (locMgr.IsProviderEnabled(NetworkProvider))
                    GPSIsOn = true;
                else
                    GPSIsOn = false;

                if (locMgr.IsProviderEnabled(GpsProvider))
                    locMgr.RequestLocationUpdates(GpsProvider, 0, 0, this);
                else if (locMgr.IsProviderEnabled(NetworkProvider))
                    locMgr.RequestLocationUpdates(NetworkProvider, 0, 0, this);
                else
                    locMgr.RequestLocationUpdates(GpsProvider, 0, 0, this);
            }
            catch (Exception err)
            {
                Log.Error("Kara Tracking Service", err.ProperMessage() + err.StackTrace);
            }
        }

        static bool? TimeIsAutomatic = null;
        public static bool? GPSIsOn = null;
        static bool? GPSPermissionIsGranted = null;
        static bool? InternetIsConnected = null;
        //private async Task CheckForAutoTimeEnabledForeverAsync()
        //{
        //    try
        //    {
        //        while (true)
        //        {
        //            try
        //            {
        //                await Task.Delay(10000);

        //                var IsTimeAutomatic = Android.Provider.Settings.Global.GetInt(ContentResolver, Android.Provider.Settings.Global.AutoTime, 0) == 1;
        //                if (!IsTimeAutomatic)
        //                    App.MajorDeviceSetting.CheckDateTimeSetting();
                        
        //                if (TimeIsAutomatic.HasValue && TimeIsAutomatic.Value != IsTimeAutomatic)
        //                    App.MajorDeviceSetting.MajorDeviceSettingsChanged(IsTimeAutomatic ? ChangedMajorDeviceSetting.AutomaticTimeEnabled : ChangedMajorDeviceSetting.AutomaticTimeDisabled);

        //                TimeIsAutomatic = IsTimeAutomatic;



        //                var IsGPSOn = locMgr.IsProviderEnabled(LocationManager.GpsProvider);
        //                if (!IsGPSOn &&
        //                    App.GPSShouldBeTurnedOnDuringWorkTime.Value &&
        //                    (
        //                        !App.VisitorBeginWorkTime.Value.HasValue ||
        //                        !App.VisitorEndWorkTime.Value.HasValue ||
        //                        (App.VisitorBeginWorkTime.Value.Value <= DateTime.Now.TimeOfDay && DateTime.Now.TimeOfDay <= App.VisitorEndWorkTime.Value.Value)
        //                    ))
        //                    App.MajorDeviceSetting.CheckGPSSetting();

        //                if (GPSIsOn.HasValue && GPSIsOn.Value != IsGPSOn)
        //                    App.MajorDeviceSetting.MajorDeviceSettingsChanged(IsGPSOn ? ChangedMajorDeviceSetting.GPSEnabled : ChangedMajorDeviceSetting.GPSDisabled);

        //                GPSIsOn = IsGPSOn;

                        
        //                //var IsGPSPermissionGranted = Android.Support.V4.Content.ContextCompat.CheckSelfPermission(MainActivity.Instance, Android.Manifest.Permission.AccessFineLocation) == Android.Content.PM.Permission.Granted;
        //                var IsGPSPermissionGranted = PackageManager.CheckPermission(Android.Manifest.Permission.AccessFineLocation, PackageName) == Android.Content.PM.Permission.Granted;
        //                if (!IsGPSPermissionGranted &&
        //                    App.GPSShouldBeTurnedOnDuringWorkTime.Value &&
        //                    (
        //                        !App.VisitorBeginWorkTime.Value.HasValue ||
        //                        !App.VisitorEndWorkTime.Value.HasValue ||
        //                        (App.VisitorBeginWorkTime.Value.Value <= DateTime.Now.TimeOfDay && DateTime.Now.TimeOfDay <= App.VisitorEndWorkTime.Value.Value)
        //                    ))
        //                {
        //                    if (MainActivity.MainActivityInstance != null)
        //                        App.MajorDeviceSetting.CheckGPSPermission();
        //                    else
        //                    {
        //                        Intent dialogIntent = new Intent(this, typeof(MainActivity));
        //                        dialogIntent.AddFlags(ActivityFlags.NewTask);
        //                        StartActivity(dialogIntent);
        //                    }
        //                }

        //                if (GPSPermissionIsGranted.HasValue && GPSPermissionIsGranted.Value != IsGPSPermissionGranted)
        //                {
        //                    App.MajorDeviceSetting.MajorDeviceSettingsChanged(IsGPSPermissionGranted ? ChangedMajorDeviceSetting.GPSPermissionGranted : ChangedMajorDeviceSetting.GPSPermissionDenied);
        //                    if (IsGPSPermissionGranted)
        //                        RequestLocationUpdates();
        //                }

        //                GPSPermissionIsGranted = IsGPSPermissionGranted;



        //                var IsInternetConnected = await App.MajorDeviceSetting.CheckInternetConnection(true);
        //                if (!IsInternetConnected &&
        //                    App.InternetShouldBeConnectedDuringWorkTime.Value &&
        //                    (
        //                        !App.VisitorBeginWorkTime.Value.HasValue ||
        //                        !App.VisitorEndWorkTime.Value.HasValue ||
        //                        (App.VisitorBeginWorkTime.Value.Value <= DateTime.Now.TimeOfDay && DateTime.Now.TimeOfDay <= App.VisitorEndWorkTime.Value.Value)
        //                    ))
        //                    App.MajorDeviceSetting.CheckInternetConnection(false);

        //                if (InternetIsConnected.HasValue && InternetIsConnected.Value != IsInternetConnected)
        //                    App.MajorDeviceSetting.MajorDeviceSettingsChanged(IsInternetConnected ? ChangedMajorDeviceSetting.InternetConnected : ChangedMajorDeviceSetting.InternetDisconnected);

        //                InternetIsConnected = IsInternetConnected;
        //            }
        //            catch (Exception err)
        //            {
        //                Log.Error("Kara Tracking Service", "exception: " + err.Message + ", StackTrace: " + (err.StackTrace == null ? "---" : err.StackTrace));
        //            }
        //        }
        //    }
        //    catch (Exception err)
        //    {
        //        Log.Error("Kara Tracking Service", "exception: " + err.Message + ", StackTrace: " + (err.StackTrace == null ? "---" : err.StackTrace));
        //    }
        //}

        private async Task CheckForPointsForeverAsync()
        {
            try
            {
                while (true)
                {
                    try
                    {
                        await Task.Delay(App.GetLocationsPerid.Value * 1000 / 10);

                        var CurrentTimeStamp = DateTime.Now.ToTimeStamp();
                        var NewLocation = locations.FirstOrDefault(a => CurrentTimeStamp - a.Timestamp <= App.GetLocationsPerid.Value * 1000);
                        if (NewLocation == null)
                        {
                            var LastKnownLocation = locMgr.GetLastKnownLocation(GpsProvider);
                            if (LastKnownLocation != null)
                            {
                                addLocationToList(LastKnownLocation);
                                NewLocation = locations.FirstOrDefault(a => CurrentTimeStamp - a.Timestamp <= App.GetLocationsPerid.Value * 1000);
                            }
                        }
                        
                        if (NewLocation == null)
                        {
                            NewLocation = new LocationModel()
                            {
                                Timestamp = CurrentTimeStamp,
                                Latitude = null,
                                Longitude = null,
                                Accuracy = null,
                                DeviceState = (int)(GPSIsOn.GetValueOrDefault(false) ? DeviceState.LocationNotAvailable : DeviceState.GPSIsOff),
                                SentToApplication = false
                            };
                        }

                        await App.DB.InsertOrUpdateRecordAsync(NewLocation);
                        
                        SendPointsToServer();

                        await Task.Delay(App.GetLocationsPerid.Value * 1000 * 9 / 10);
                    }
                    catch (Exception err)
                    {
                        Log.Error("Kara Tracking Service", "exception: " + err.Message + ", StackTrace: " + (err.StackTrace == null ? "---" : err.StackTrace));
                    }
                }
            }
            catch (Exception err)
            {
                Log.Error("Kara Tracking Service", "exception: " + err.Message + ", StackTrace: " + (err.StackTrace == null ? "---" : err.StackTrace));
            }
        }

        async void SendPointsToServer()
        {
            try
            {
                var UnsentLocations = App.DB.conn.Table<LocationModel>().Where(a => !a.SentToApplication)
                    .OrderBy(a => a.Timestamp).ToList().Take(100).ToArray();
                if (UnsentLocations.Count() >= 5)
                {
                    var result = await Connectivity.SubmitLocationsAsync(UnsentLocations);
                    if (result.Success)
                    {
                        foreach (var item in UnsentLocations)
                            item.SentToApplication = true;
                        await App.DB.InsertOrUpdateAllRecordsAsync(UnsentLocations);
                    }
                    else
                        throw new Exception(result.Message);
                }
            }
            catch (Exception err)
            {
                Log.Error("Kara Tracking Service", "exception: " + err.Message + ", StackTrace: " + (err.StackTrace == null ? "---" : err.StackTrace));
            }
        }
    }
}
