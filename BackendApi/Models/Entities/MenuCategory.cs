using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BackendApi.Core.Constants;
using BackendApi.Core.Helpers;
using BackendApi.Core.Models;
using BackendApi.Core.Models.Response;
using BackendApi.Core.Models.Entities;

namespace BackendApi.Models.Entities
{
    /// <summary>
    /// โมเดลหมวดหมู่เมนูสินค้าสำหรับร้านค้า
    /// </summary>
    public class MenuCategory : BaseSoftDeleteEntity<string>, ITrackableEntity
    {
        public long RefNumber { get; init; }

        [NotMapped]
        public string TrackingCode => TrackingCodeFormatter.Format(TrackingPrefixes.MenuCategory, RefNumber);

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        public int DisplayOrder { get; set; } // ลำดับในการแสดงผล

        [Required]
        public string ShopId { get; set; } = string.Empty;

        [ForeignKey(nameof(ShopId))]
        public Shop? Shop { get; set; }

        public ICollection<MenuItem> MenuItems { get; set; } = new List<MenuItem>();
    }
}


