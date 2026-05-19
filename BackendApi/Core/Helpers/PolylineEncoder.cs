using System;
using System.Collections.Generic;
using System.Text;

namespace BackendApi.Core.Helpers
{
    /// <summary>
    /// ยูทิลิตี้สำหรับเข้ารหัสพิกัดละติจูด/ลองจิจูดแบบทศนิยมตามมาตรฐาน Google Polyline Algorithm เพื่อการบีบอัดระดับสูง
    /// </summary>
    public static class PolylineEncoder
    {
        public static string Encode(IEnumerable<double[]> points)
        {
            var str = new StringBuilder();
            int lastLat = 0;
            int lastLng = 0;

            foreach (var point in points)
            {
                // แปลงพิกัดเป็นจำนวนเต็มความละเอียด 5 หลักทศนิยม (1e5)
                int lat = (int)Math.Round(point[0] * 1e5);
                int lng = (int)Math.Round(point[1] * 1e5);

                int dLat = lat - lastLat;
                int dLng = lng - lastLng;

                EncodeValue(dLat, str);
                EncodeValue(dLng, str);

                lastLat = lat;
                lastLng = lng;
            }

            return str.ToString();
        }

        private static void EncodeValue(int value, StringBuilder str)
        {
            // แปลงสัญญาณ (Sign Bit) ของจำนวน
            int sValue = value << 1;
            if (value < 0)
            {
                sValue = ~sValue;
            }

            while (sValue >= 0x20)
            {
                str.Append((char)((0x20 | (sValue & 0x1f)) + 63));
                sValue >>= 5;
            }
            str.Append((char)(sValue + 63));
        }
    }
}
