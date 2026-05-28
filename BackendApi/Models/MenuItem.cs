using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BackendApi.Core.Constants;
using BackendApi.Core.Helpers;
using BackendApi.Core.Models;
using NetTopologySuite.Geometries;

namespace BackendApi.Models
{
    /// <summary>
    /// โมเดลเมนูสินค้าสำหรับร้านค้าแต่ละร้าน
    /// </summary>
    public class MenuItem : BaseSoftDeleteEntity<string>, ITrackableEntity
    {
        public long RefNumber { get; init; }

        [NotMapped]
        public string TrackingCode => TrackingCodeFormatter.Format(TrackingPrefixes.MenuItem, RefNumber);

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }

        public string? ImageUrl { get; set; }

        [Required]
        public string ShopId { get; set; } = string.Empty;

        [ForeignKey(nameof(ShopId))]
        public Shop? Shop { get; set; }

        public string? MenuCategoryId { get; set; }

        [ForeignKey(nameof(MenuCategoryId))]
        public MenuCategory? MenuCategory { get; set; }

        public ICollection<MenuItemOption> Options { get; set; } = new List<MenuItemOption>();
    }

    /// <summary>
    /// โมเดลตัวเลือกของเมนู (เช่น ขนาด, เครื่องปรับเพิ่ม)
    /// </summary>
    public class MenuItemOption : BaseSoftDeleteEntity<string>
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public bool Required { get; set; }

        public int MaxSelections { get; set; }

        public string MenuItemId { get; set; } = string.Empty;

        [ForeignKey(nameof(MenuItemId))]
        public MenuItem? MenuItem { get; set; }

        public ICollection<MenuItemOptionItem> Items { get; set; } = new List<MenuItemOptionItem>();
    }

    /// <summary>
    /// รายการตัวเลือกย่อย (เช่น Medium, Large, Extra Cheese)
    /// </summary>
    public class MenuItemOptionItem : BaseSoftDeleteEntity<string>
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }

        public string MenuItemOptionId { get; set; } = string.Empty;

        [ForeignKey(nameof(MenuItemOptionId))]
        public MenuItemOption? MenuItemOption { get; set; }
    }
}
