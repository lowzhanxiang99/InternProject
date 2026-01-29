using Microsoft.AspNetCore.Mvc;
using QRCoder;
using System.Security.Cryptography;
using System.Text;

namespace InternProject1.Controllers
{
    public class QRCodeController : Controller
    {
        public IActionResult Generate(int employeeId)
        {

            var host = Request.Host.Value;
            var scheme = Request.Scheme;

            // 1. Calculate a 5-minute time block (Unix time divided by 300 seconds)
            long timeStep = (long)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds / 300;

            // 2. Create a dynamic secret using your base passphrase + the time step
            string baseSecret = "AlpineSolution2026"; // Matches your updated appsettings
            string dynamicSecret = $"{baseSecret}_{timeStep}";

            // 3. Generate the URL with the dynamic secret
            string loginUrl = $"{scheme}://{host}/Account/AutoLogin?empId={employeeId}&secret={dynamicSecret}";

            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(loginUrl, QRCodeGenerator.ECCLevel.Q))
            using (PngByteQRCode qrCode = new PngByteQRCode(qrCodeData))
            {
                byte[] qrCodeAsPngByteArr = qrCode.GetGraphic(20);
                return File(qrCodeAsPngByteArr, "image/png");
            }
        }
    }
}