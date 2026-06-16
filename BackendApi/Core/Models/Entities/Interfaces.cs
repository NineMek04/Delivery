using System;

namespace BackendApi.Core.Models.Entities
{
    public interface IEntity<TKey>
    {
        TKey Id { get; set; }
    }

    public interface IAuditableEntity
    {
        DateTime CreatedAt { get; set; }
        Guid? CreatedByUserId { get; set; }
        string? CreatedByName { get; set; }
        string? CreatedFromIp { get; set; }

        DateTime? UpdatedAt { get; set; }
        Guid? UpdatedByUserId { get; set; }
        string? UpdatedByName { get; set; }
        string? UpdatedFromIp { get; set; }
    }

    public interface ISoftDeletableEntity
    {
        bool IsDeleted { get; set; }
        DateTime? DeletedAt { get; set; }
        Guid? DeletedByUserId { get; set; }
        string? DeletedByName { get; set; }
        string? DeletedFromIp { get; set; }
    }
}

