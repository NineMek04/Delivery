using System;

namespace BackendApi.Services.Tracking
{
    public class TrackingSearchService : ITrackingSearchService
    {
        public long? ParseSearchQuery(string? query, string expectedPrefix)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;

            var clean = query.Trim().ToUpperInvariant();
            var prefix = expectedPrefix.ToUpperInvariant();

            // Case 1: ค้นหาแบบเต็มรูปแบบ เช่น "ORD-000015" หรือ "ORD-15"
            if (clean.StartsWith(prefix + "-"))
            {
                var rest = clean.Substring(prefix.Length + 1);
                if (long.TryParse(rest, out var num))
                    return num;
            }

            // Case 2: ค้นหาแบบย่อไม่ใส่ขีด เช่น "ORD000015" หรือ "ORD15"
            if (clean.StartsWith(prefix))
            {
                var rest = clean.Substring(prefix.Length);
                if (long.TryParse(rest, out var num))
                    return num;
            }

            // Case 3: ค้นหาด้วยตัวเลขเดี่ยว ๆ เช่น "15" 
            // จำกัดความยาวเลขอ้างอิงไม่เกิน 6 หลัก เพื่อไม่ให้เบอร์โทรศัพท์ (เช่น 0812345678) หรือรหัสไปรษณีย์ไปทับซ้อน
            if (clean.Length <= 6 && long.TryParse(clean, out var parsedInt))
            {
                return parsedInt;
            }

            return null;
        }
    }
}
