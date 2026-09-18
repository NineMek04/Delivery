using System;
using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace BackendApi.Controllers.App
{
    [ApiController]
    [Route("api/v1/app")]
    [AllowAnonymous]
    public class AppVersionController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public AppVersionController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public record AppVersionInfoDto(
            string Platform,
            string LatestVersion,
            int BuildNumber,
            string MinSupportedVersion,
            string DownloadUrl,
            string ReleaseNotes,
            bool ForceUpdate,
            long FileSizeBytes,
            DateTime ReleasedAt
        );

        /// <summary>
        /// เช็คเวอร์ชันล่าสุดของ Rider App และดาวน์โหลดข้อมูลอัปเดต
        /// </summary>
        [HttpGet("version")]
        public IActionResult GetLatestVersion([FromQuery] string platform = "android")
        {
            var latestVersion = _configuration.GetValue<string>("AppVersion:LatestVersion") ?? "1.0.3";
            var buildNumber = _configuration.GetValue<int?>("AppVersion:BuildNumber") ?? 3;
            var minSupported = _configuration.GetValue<string>("AppVersion:MinSupportedVersion") ?? "1.0.0";
            var forceUpdate = _configuration.GetValue<bool?>("AppVersion:ForceUpdate") ?? true;

            var releaseNotes = _configuration.GetValue<string>("AppVersion:ReleaseNotes") ??
                "1. อัปเดตพิกัด GPS ลงเซิร์ฟเวอร์แบบ Real-time ตลอดเวลา\n" +
                "2. เข็มทิศ/ไจโร หมุนตามทิศตัวเครื่อง (Gyro / Heading)\n" +
                "3. แผนที่ Smooth Marker ไม่กระตุก เคลื่อนที่ลื่นไหล\n" +
                "4. แยกสีเส้นทางชัดเจน (สีส้มรับของ / สีฟ้าส่งของ) พร้อม Auto-Reroute\n" +
                "5. ระบบสรุปยอดร้านค้าและรายงานยอดขาย";

            var apkPath = ResolveApkFilePath();
            long fileSizeBytes = 0;
            if (System.IO.File.Exists(apkPath))
            {
                var fileInfo = new FileInfo(apkPath);
                fileSizeBytes = fileInfo.Length;
            }

            var downloadUrl = $"{Request.Scheme}://{Request.Host}/api/v1/app/download";

            var versionInfo = new AppVersionInfoDto(
                Platform: platform.ToLowerInvariant(),
                LatestVersion: latestVersion,
                BuildNumber: buildNumber,
                MinSupportedVersion: minSupported,
                DownloadUrl: downloadUrl,
                ReleaseNotes: releaseNotes,
                ForceUpdate: forceUpdate,
                FileSizeBytes: fileSizeBytes,
                ReleasedAt: DateTime.UtcNow
            );

            return Ok(versionInfo);
        }

        /// <summary>
        /// ดาวน์โหลดไฟล์ APK ตัวล่าสุดสำหรับติดตั้งทับลงมือถือ
        /// </summary>
        [HttpGet("download")]
        public IActionResult DownloadLatestApk()
        {
            var apkPath = ResolveApkFilePath();

            if (!System.IO.File.Exists(apkPath))
            {
                return NotFound(new
                {
                    message = "ยังไม่พบไฟล์ APK บนเซิร์ฟเวอร์ กรุณาบิลด์ APK แล้วนำไฟล์ไปวางที่โฟลเดอร์ /app/apk/rider-app.apk",
                    checkedPath = apkPath
                });
            }

            var fileName = $"rider-app-v{_configuration.GetValue<string>("AppVersion:LatestVersion") ?? "1.0.2"}.apk";
            return PhysicalFile(apkPath, "application/vnd.android.package-archive", fileName, enableRangeProcessing: true);
        }

        private string ResolveApkFilePath()
        {
            var configuredPath = _configuration.GetValue<string>("AppVersion:ApkPath");
            if (!string.IsNullOrWhiteSpace(configuredPath) && System.IO.File.Exists(configuredPath))
            {
                return configuredPath;
            }

            // Path 1: โฟลเดอร์ apk ใน Content Root (Docker / Production)
            var path1 = Path.Combine(AppContext.BaseDirectory, "apk", "rider-app.apk");
            if (System.IO.File.Exists(path1)) return path1;

            // Path 2: โฟลเดอร์ apk บน Root Directory
            var path2 = Path.Combine(Directory.GetCurrentDirectory(), "apk", "rider-app.apk");
            if (System.IO.File.Exists(path2)) return path2;

            // Path 3: Local Development build artifact ใน rider_app
            var path3 = Path.Combine(Directory.GetCurrentDirectory(), "..", "rider_app", "build", "app", "outputs", "flutter-apk", "app-release.apk");
            if (System.IO.File.Exists(path3)) return Path.GetFullPath(path3);

            return path1;
        }
    }
}
