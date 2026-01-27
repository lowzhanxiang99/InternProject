using Microsoft.AspNetCore.Mvc;
using QRCoder;

namespace InternProject1.Controllers
{
    public class QRCodeController : Controller
    {
        public IActionResult Generate(int employeeId)
        {
            // This grabs the tunnel URL automatically (e.g., https://xxx.devtunnels.ms)
            var host = Request.Host.Value;
            var scheme = Request.Scheme;

            // This creates the link your phone actually needs to follow
            string loginUrl = $"{scheme}://{host}/Account/AutoLogin?empId={employeeId}&secret=TimeVIA123";

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