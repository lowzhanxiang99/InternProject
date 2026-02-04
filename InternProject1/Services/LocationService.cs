namespace InternProject1.Services
{
    public class LocationService
    {
        // Office Coordinates (Change these to your actual office location)
        private const double OfficeLat = 3.1390;
        private const double OfficeLon = 101.6869;

        public bool IsUserInOffice(double userLat, double userLon)
        {
            double radius = 6371000;
            var dLat = (userLat - OfficeLat) * Math.PI / 180;
            var dLon = (userLon - OfficeLon) * Math.PI / 180;

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(OfficeLat * Math.PI / 180) * Math.Cos(userLat * Math.PI / 180) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            var d = 2 * radius * Math.Asin(Math.Sqrt(a));
            return d <= 50;
        }
    }
}