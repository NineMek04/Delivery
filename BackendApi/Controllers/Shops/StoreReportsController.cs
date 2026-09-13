using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BackendApi.Core;
using BackendApi.Core.Constants;
using BackendApi.Core.Models.Response;
using BackendApi.Core.StateMachines;
using BackendApi.Data;
using BackendApi.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Controllers.Shops
{
    [ApiController]
    [Route("api/v1/shops/{shopId}/reports")]
    [Authorize]
    public class StoreReportsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public StoreReportsController(ApplicationDbContext db)
        {
            _db = db;
        }

        public record StoreReportSummaryDto(
            string ShopId,
            string ShopName,
            string Period,
            DateTime StartDate,
            DateTime EndDate,
            int TotalOrders,
            int CompletedOrders,
            int CancelledOrders,
            decimal TotalRevenue,
            decimal AverageOrderValue,
            List<TopMenuItemDto> TopItems,
            List<StoreReportOrderDto> Orders
        );

        public record TopMenuItemDto(
            string Name,
            int Quantity,
            decimal TotalAmount
        );

        public record StoreReportOrderDto(
            string OrderId,
            long RefNumber,
            DateTime CreatedAt,
            string CustomerName,
            string Status,
            int ItemCount,
            decimal OrderTotal,
            string ItemsSummary
        );

        [HttpGet("summary")]
        public async Task<ActionResult<ApiResponse<StoreReportSummaryDto>>> GetSummary(
            [FromRoute] string shopId,
            [FromQuery] string period = "day",
            [FromQuery] DateTime? date = null)
        {
            var shop = await _db.Shops.AsNoTracking().FirstOrDefaultAsync(s => s.Id == shopId);
            if (shop == null)
            {
                return NotFound(ApiResponse<StoreReportSummaryDto>.Fail("Shop not found"));
            }

            var targetDate = date ?? DateTime.UtcNow;
            var (startDate, endDate) = CalculateDateRange(period, targetDate);

            var orders = await _db.Orders
                .AsNoTracking()
                .Include(o => o.Items)
                .Include(o => o.Customer)
                .Where(o => o.ShopId == shopId && !o.IsDeleted)
                .Where(o => o.CreatedAt >= startDate && o.CreatedAt <= endDate)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            var totalOrders = orders.Count;
            var completedOrders = orders.Count(o => o.State == OrderState.COMPLETED);
            var cancelledOrders = orders.Count(o => o.State == OrderState.CANCELLED);

            // คำนวณยอดขายจากออเดอร์ที่สำเร็จ (หรือทุกออเดอร์ที่ไม่ถูกยกเลิก)
            var revenueOrders = orders.Where(o => o.State != OrderState.CANCELLED).ToList();
            var totalRevenue = revenueOrders.Sum(o => o.Items.Sum(i => i.UnitPrice * i.Quantity));
            var averageOrderValue = revenueOrders.Count > 0 ? totalRevenue / revenueOrders.Count : 0m;

            // เมนูยอดนิยม
            var topItems = revenueOrders
                .SelectMany(o => o.Items)
                .GroupBy(i => i.Name)
                .Select(g => new TopMenuItemDto(
                    Name: g.Key,
                    Quantity: g.Sum(x => x.Quantity),
                    TotalAmount: g.Sum(x => x.UnitPrice * x.Quantity)
                ))
                .OrderByDescending(x => x.Quantity)
                .Take(10)
                .ToList();

            // รายการออเดอร์แบบย่อ
            var orderDtos = orders.Select(o =>
            {
                var orderTotal = o.Items.Sum(i => i.UnitPrice * i.Quantity);
                var itemsSummary = string.Join(", ", o.Items.Select(i => $"{i.Quantity}x {i.Name}"));
                return new StoreReportOrderDto(
                    OrderId: o.Id,
                    RefNumber: o.RefNumber,
                    CreatedAt: o.CreatedAt,
                    CustomerName: o.Customer?.FullName ?? "ลูกค้าทั่วไป",
                    Status: o.Status,
                    ItemCount: o.Items.Sum(i => i.Quantity),
                    OrderTotal: orderTotal,
                    ItemsSummary: itemsSummary
                );
            }).ToList();

            var summary = new StoreReportSummaryDto(
                ShopId: shop.Id,
                ShopName: shop.Name,
                Period: period.ToLowerInvariant(),
                StartDate: startDate,
                EndDate: endDate,
                TotalOrders: totalOrders,
                CompletedOrders: completedOrders,
                CancelledOrders: cancelledOrders,
                TotalRevenue: Math.Round(totalRevenue, 2),
                AverageOrderValue: Math.Round(averageOrderValue, 2),
                TopItems: topItems,
                Orders: orderDtos
            );

            return Ok(ApiResponse<StoreReportSummaryDto>.Ok(summary));
        }

        [HttpGet("export")]
        public async Task<IActionResult> ExportReport(
            [FromRoute] string shopId,
            [FromQuery] string period = "day",
            [FromQuery] DateTime? date = null,
            [FromQuery] string format = "csv")
        {
            var shop = await _db.Shops.AsNoTracking().FirstOrDefaultAsync(s => s.Id == shopId);
            if (shop == null)
            {
                return NotFound("Shop not found");
            }

            var targetDate = date ?? DateTime.UtcNow;
            var (startDate, endDate) = CalculateDateRange(period, targetDate);

            var orders = await _db.Orders
                .AsNoTracking()
                .Include(o => o.Items)
                .Include(o => o.Customer)
                .Where(o => o.ShopId == shopId && !o.IsDeleted)
                .Where(o => o.CreatedAt >= startDate && o.CreatedAt <= endDate)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            var totalOrders = orders.Count;
            var completedOrders = orders.Count(o => o.State == OrderState.COMPLETED);
            var cancelledOrders = orders.Count(o => o.State == OrderState.CANCELLED);
            var revenueOrders = orders.Where(o => o.State != OrderState.CANCELLED).ToList();
            var totalRevenue = revenueOrders.Sum(o => o.Items.Sum(i => i.UnitPrice * i.Quantity));

            var thaiCulture = new CultureInfo("th-TH");
            var periodName = period.ToLowerInvariant() switch
            {
                "year" => $"ประจำปี {targetDate.ToString("yyyy", thaiCulture)}",
                "month" => $"ประจำเดือน {targetDate.ToString("MMMM yyyy", thaiCulture)}",
                _ => $"ประจำวันที่ {targetDate.ToString("dd MMMM yyyy", thaiCulture)}"
            };

            var sb = new StringBuilder();
            // Section 1: Executive Summary Header
            sb.AppendLine($"\"รายงานสรุปยอดขายร้านค้า (Sales Report)\",\"\"");
            sb.AppendLine($"\"ชื่อร้านค้า:\",\"{EscapeCsv(shop.Name)}\"");
            sb.AppendLine($"\"ช่วงเวลา:\",\"{periodName}\"");
            sb.AppendLine($"\"วันที่สร้างเอกสาร:\",\"{DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss", thaiCulture)}\"");
            sb.AppendLine($"\"จำนวนออเดอร์ทั้งหมด:\",\"{totalOrders}\"");
            sb.AppendLine($"\"ออเดอร์จัดส่งสำเร็จ:\",\"{completedOrders}\"");
            sb.AppendLine($"\"ออเดอร์ยกเลิก:\",\"{cancelledOrders}\"");
            sb.AppendLine($"\"ยอดขายรวมทั้งสิ้น:\",\"{totalRevenue:N2} บาท\"");
            sb.AppendLine();

            // Section 2: Detailed Order Breakdown Table
            sb.AppendLine("\"ลำดับ\",\"เลขอ้างอิง\",\"วันที่-เวลา\",\"ลูกค้า\",\"สถานะ\",\"จำนวนรายการ\",\"รายการอาหาร\",\"ยอดเงิน (บาท)\"");

            int index = 1;
            foreach (var order in orders)
            {
                var orderTotal = order.Items.Sum(i => i.UnitPrice * i.Quantity);
                var itemsDetail = string.Join("; ", order.Items.Select(i => $"{i.Quantity}x {i.Name} (฿{i.UnitPrice:N0})"));
                var customerName = order.Customer?.FullName ?? "ลูกค้าทั่วไป";
                var orderTime = order.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", thaiCulture);

                sb.AppendLine($"\"{index++}\",\"#{order.RefNumber}\",\"{orderTime}\",\"{EscapeCsv(customerName)}\",\"{order.Status}\",\"{order.Items.Sum(i => i.Quantity)}\",\"{EscapeCsv(itemsDetail)}\",\"{orderTotal:N2}\"");
            }

            // UTF-8 with BOM for Excel UTF-8 display
            var preamble = Encoding.UTF8.GetPreamble();
            var dataBytes = Encoding.UTF8.GetBytes(sb.ToString());
            var resultBytes = new byte[preamble.Length + dataBytes.Length];
            Buffer.BlockCopy(preamble, 0, resultBytes, 0, preamble.Length);
            Buffer.BlockCopy(dataBytes, 0, resultBytes, preamble.Length, dataBytes.Length);

            var fileName = $"store-report-{shopId.Substring(0, Math.Min(8, shopId.Length))}-{period.ToLowerInvariant()}-{targetDate:yyyyMMdd}.csv";
            return File(resultBytes, "text/csv; charset=utf-8", fileName);
        }

        private static (DateTime Start, DateTime End) CalculateDateRange(string period, DateTime targetDate)
        {
            var p = period.ToLowerInvariant();
            if (p == "year")
            {
                var start = new DateTime(targetDate.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var end = new DateTime(targetDate.Year, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc);
                return (start, end);
            }
            if (p == "month")
            {
                var start = new DateTime(targetDate.Year, targetDate.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                var daysInMonth = DateTime.DaysInMonth(targetDate.Year, targetDate.Month);
                var end = new DateTime(targetDate.Year, targetDate.Month, daysInMonth, 23, 59, 59, 999, DateTimeKind.Utc);
                return (start, end);
            }

            // Default: Day
            var dayStart = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, 0, 0, 0, DateTimeKind.Utc);
            var dayEnd = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, 23, 59, 59, 999, DateTimeKind.Utc);
            return (dayStart, dayEnd);
        }

        private static string EscapeCsv(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("\"", "\"\"");
        }
    }
}
