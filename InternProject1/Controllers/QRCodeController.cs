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
            // IMPORTANT: Use your actual local IP (e.g., 192.168.1.15) if Request.Host 
            // is showing "localhost", otherwise your phone won't connect.
            var host = Request.Host.Value;
            var scheme = Request.Scheme;

            // 1. Use the static secret only (No more 5-minute time blocks)
            string baseSecret = "AlpineSolution2026";

            // 2. Generate the URL with just the employeeId and the static secret
            string loginUrl = $"{scheme}://{host}/Account/AutoLogin?empId={employeeId}&secret={baseSecret}";

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