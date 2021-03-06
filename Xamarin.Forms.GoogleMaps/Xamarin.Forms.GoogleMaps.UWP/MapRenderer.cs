using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;
using Windows.UI;
using Windows.UI.Xaml.Controls.Maps;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
using Xamarin.Forms.GoogleMaps.Extensions.UWP;
using Xamarin.Forms.GoogleMaps.Internals;
using Xamarin.Forms.GoogleMaps.Logics;
using Xamarin.Forms.GoogleMaps.Logics.UWP;
using Xamarin.Forms.GoogleMaps.UWP.Logics;
#if WINDOWS_UWP
using Xamarin.Forms.Platform.UWP;

#else
using Xamarin.Forms.Platform.WinRT;

#endif

#if WINDOWS_UWP

namespace Xamarin.Forms.GoogleMaps.UWP
#else

namespace Xamarin.Forms.Maps.WinRT
#endif
{
    public class MapRenderer : ViewRenderer<Map, MapControl>
    {
        private readonly CameraLogic _cameraLogic = new CameraLogic();

        private Map Map
        {
            get { return Element as Map; }
        }

        private MapControl NativeMap
        {
            get { return Control as MapControl; }
        }

        readonly BaseLogic<MapControl>[] _logics;

        public MapRenderer() : base()
        {
            _logics = new BaseLogic<MapControl>[]
            {
                new PinLogic(),
                new TileLayerLogic(),
            };
        }

        protected override async void OnElementChanged(ElementChangedEventArgs<Map> e)
        {
            base.OnElementChanged(e);

            var oldMapView = (MapControl)Control;

            if (e.OldElement != null)
            {
                var mapModel = e.OldElement;
                _cameraLogic.Unregister();

                if (oldMapView != null)
                {
                    oldMapView.ActualCameraChanged  -= OnActualCameraChanged;
                    oldMapView.ZoomLevelChanged -= OnZoomLevelChanged;
                }
            }

            if (e.NewElement != null)
            {
                var mapModel = e.NewElement;
                if (Control == null)
                {
                    SetNativeControl(new MapControl());
                    Control.MapServiceToken = FormsGoogleMaps.AuthenticationToken;
                    Control.TrafficFlowVisible = Map.IsTrafficEnabled;
                    Control.ZoomLevelChanged += OnZoomLevelChanged;
                    Control.CenterChanged += async (s, a) => await UpdateVisibleRegion();
                    Control.ActualCameraChanged += OnActualCameraChanged;
                }

                _cameraLogic.Register(Map, NativeMap);

                UpdateMapType();
                UpdateHasScrollEnabled();
                UpdateHasZoomEnabled();

                await UpdateIsShowingUser();

                foreach (var logic in _logics)
                {
                    logic.Register(oldMapView, e.OldElement, NativeMap, Map);
                    logic.RestoreItems();
                    logic.OnMapPropertyChanged(new PropertyChangedEventArgs(Map.SelectedPinProperty.PropertyName));
                }
            }
        }

        private async void OnZoomLevelChanged(MapControl sender, object args)
        {
            var camera = sender.ActualCamera;
            var pos = new CameraPosition(
                camera.Location.Position.ToPosition(),
                camera.Roll,
                camera.Pitch,
                sender.ZoomLevel);
            Map.SendCameraChanged(pos);
            await UpdateVisibleRegion();
        }

        private void OnActualCameraChanged(MapControl sender, MapActualCameraChangedEventArgs args)
        {
            var camera = args.Camera;
            var pos = new CameraPosition(
                camera.Location.Position.ToPosition(),
                camera.Heading,
                camera.Pitch,
                sender.ZoomLevel);
            Map.SendCameraChanged(pos);
        }

        protected override async void OnElementPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            base.OnElementPropertyChanged(sender, e);

            if (e.PropertyName == Map.MapTypeProperty.PropertyName)
                UpdateMapType();
            else if (e.PropertyName == Map.IsShowingUserProperty.PropertyName)
                await UpdateIsShowingUser();
            else if (e.PropertyName == Map.HasScrollEnabledProperty.PropertyName)
                UpdateHasScrollEnabled();
            else if (e.PropertyName == Map.HasZoomEnabledProperty.PropertyName)
                UpdateHasZoomEnabled();
            else if (e.PropertyName == Map.IsTrafficEnabledProperty.PropertyName)
                Control.TrafficFlowVisible = Map.IsTrafficEnabled;

            foreach (var logic in _logics)
            {
                logic.OnMapPropertyChanged(e);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;

                _cameraLogic.Unregister();
            }
            base.Dispose(disposing);
        }

        bool _disposed;
        bool _firstZoomLevelChangeFired;
        Ellipse _userPositionCircle;

        async Task UpdateIsShowingUser()
        {
            if (Element.IsShowingUser)
            {
                var myGeolocator = new Geolocator();
                if (myGeolocator.LocationStatus != PositionStatus.NotAvailable &&
                    myGeolocator.LocationStatus != PositionStatus.Disabled)
                {
                    var userPosition = await myGeolocator.GetGeopositionAsync();
                    if (userPosition?.Coordinate != null)
                        LoadUserPosition(userPosition.Coordinate, true);
                }
            }
            else if (_userPositionCircle != null && Control.Children.Contains(_userPositionCircle))
                Control.Children.Remove(_userPositionCircle);
        }

        async Task UpdateVisibleRegion()
        {
            if (Control == null || Element == null)
                return;

            if (!_firstZoomLevelChangeFired)
            {
                await _cameraLogic.MoveToRegion(Element.LastMoveToRegion, MapAnimationKind.None);
                _firstZoomLevelChangeFired = true;
                return;
            }
            Geopoint nw, se = null;
            try
            {
                Control.GetLocationFromOffset(new Windows.Foundation.Point(0, 0), out nw);
                Control.GetLocationFromOffset(new Windows.Foundation.Point(Control.ActualWidth, Control.ActualHeight), out se);
            }
            catch (Exception)
            {
                return;
            }

            if (nw != null && se != null)
            {
                var boundingBox = new GeoboundingBox(nw.Position, se.Position);
                var center = new Position(boundingBox.Center.Latitude, boundingBox.Center.Longitude);
                var latitudeDelta = Math.Abs(center.Latitude - boundingBox.NorthwestCorner.Latitude);
                var longitudeDelta = Math.Abs(center.Longitude - boundingBox.NorthwestCorner.Longitude);
                Element.VisibleRegion = new MapSpan(center, latitudeDelta, longitudeDelta);
            }
        }

        void LoadUserPosition(Geocoordinate userCoordinate, bool center)
        {
            var userPosition = new BasicGeoposition
            {
                Latitude = userCoordinate.Point.Position.Latitude,
                Longitude = userCoordinate.Point.Position.Longitude
            };

            var point = new Geopoint(userPosition);

            if (_userPositionCircle == null)
            {
                _userPositionCircle = new Ellipse
                {
                    Stroke = new SolidColorBrush(Colors.White),
                    Fill = new SolidColorBrush(Colors.Blue),
                    StrokeThickness = 2,
                    Height = 20,
                    Width = 20,
                    Opacity = 50
                };
            }

            if (Control.Children.Contains(_userPositionCircle))
                Control.Children.Remove(_userPositionCircle);

            MapControl.SetLocation(_userPositionCircle, point);
            MapControl.SetNormalizedAnchorPoint(_userPositionCircle, new Windows.Foundation.Point(0.5, 0.5));

            Control.Children.Add(_userPositionCircle);

            if (center)
            {
                Control.Center = point;
                Control.ZoomLevel = 13;
            }
        }

        void UpdateMapType()
        {
            switch (Element.MapType)
            {
                case MapType.Street:
                    Control.Style = MapStyle.Road;
                    break;
                case MapType.Satellite:
                    Control.Style = MapStyle.Aerial;
                    break;
                case MapType.Hybrid:
                    Control.Style = MapStyle.AerialWithRoads;
                    break;
                case MapType.None:
                    Control.Style = MapStyle.None;
                    break;
            }
        }

#if WINDOWS_UWP
        void UpdateHasZoomEnabled()
        {
            Control.ZoomInteractionMode = Element.HasZoomEnabled
                ? MapInteractionMode.GestureAndControl
                : MapInteractionMode.ControlOnly;
        }

        void UpdateHasScrollEnabled()
        {
            Control.PanInteractionMode = Element.HasScrollEnabled ? MapPanInteractionMode.Auto : MapPanInteractionMode.Disabled;
        }
#else
        void UpdateHasZoomEnabled()
        {
        }

        void UpdateHasScrollEnabled()
        {
        }
#endif
    }
}
