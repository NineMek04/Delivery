using System;
using System.ComponentModel.DataAnnotations;

namespace BackendApi.Core.Models.Entities
{
    public abstract class BaseEntity<TKey> : IEntity<TKey>
    {
        [Key]
        public TKey Id { get; set; } = default!;
        // ���� Constructor �������
        protected BaseEntity()
        {
            // ��� TKey �� string ������ҧ New Guid �繤���������
            if (typeof(TKey) == typeof(string))
            {
                Id = (TKey)(object)Guid.NewGuid().ToString();
            }
        }
        [Timestamp]
        public byte[] RowVersion { get; set; } = null!;
    }

    public abstract class BaseAuditableEntity<TKey> : BaseEntity<TKey>, IAuditableEntity
    {
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedByUserId { get; set; }
        public string? CreatedByName { get; set; }
        public string? CreatedFromIp { get; set; }

        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedByUserId { get; set; }
        public string? UpdatedByName { get; set; }
        public string? UpdatedFromIp { get; set; }
    }

    public abstract class BaseSoftDeleteEntity<TKey> : BaseAuditableEntity<TKey>, ISoftDeletableEntity
    {
        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedAt { get; set; }
        public Guid? DeletedByUserId { get; set; }
        public string? DeletedByName { get; set; }
        public string? DeletedFromIp { get; set; }
    }
}

