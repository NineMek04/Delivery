using System.Data;
using System.Reflection;
using BackendApi.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace BackendApi.Core.DataHandlers;

public sealed class DBHandlerCore
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DBHandlerCore(
        ApplicationDbContext dbContext,
        ConditionContext conditionContext,
        IHttpContextAccessor httpContextAccessor)
    {
        DbContext = dbContext;
        ConditionContext = conditionContext;
        _httpContextAccessor = httpContextAccessor;
    }

    public ApplicationDbContext DbContext { get; }
    public ConditionContext ConditionContext { get; }
    public bool IncludeSystemRecord { get; set; } = true;

    public IQueryable<TEntity> GetQuery<TEntity>(bool asNoTracking = false)
        where TEntity : class
    {
        var query = DbContext.Set<TEntity>().AsQueryable();

        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        return ConditionContext.Apply(query);
    }

    public Task<List<TEntity>> GetObjectListAsync<TEntity>(
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        return GetQuery<TEntity>().ToListAsync(cancellationToken);
    }

    public Task<TEntity?> GetObjectByKeyAsync<TEntity>(
        object key,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        return DbContext.Set<TEntity>().FindAsync([key], cancellationToken).AsTask();
    }

    public TEntity CreateEntity<TEntity>()
        where TEntity : class, new()
    {
        return new TEntity();
    }

    public TEntity InsertObject<TEntity>(TEntity entity)
        where TEntity : class
    {
        ApplyAuditValues(entity, isCreate: true);
        DbContext.Set<TEntity>().Add(entity);
        return entity;
    }

    public TEntity UpdateObject<TEntity>(TEntity entity)
        where TEntity : class
    {
        ApplyAuditValues(entity, isCreate: false);
        DbContext.Set<TEntity>().Update(entity);
        return entity;
    }

    public async Task<TEntity?> DeleteObjectAsync<TEntity>(
        object key,
        bool softDelete = true,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        var entity = await GetObjectByKeyAsync<TEntity>(key, cancellationToken);

        if (entity is null)
        {
            return null;
        }

        if (softDelete && TrySetPropertyValue(entity, "DelFlag", "Y"))
        {
            ApplyAuditValues(entity, isCreate: false);
            DbContext.Set<TEntity>().Update(entity);
            return entity;
        }

        if (softDelete && TrySetPropertyValue(entity, "DEL_FLAG", "Y"))
        {
            ApplyAuditValues(entity, isCreate: false);
            DbContext.Set<TEntity>().Update(entity);
            return entity;
        }

        DbContext.Set<TEntity>().Remove(entity);
        return entity;
    }

    public Task<int> DirectDeleteAsync<TEntity>(
        object key,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        var entityType = DbContext.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"Entity type {typeof(TEntity).Name} is not registered in DbContext.");

        var primaryKey = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException($"Entity type {typeof(TEntity).Name} has no primary key.");

        if (primaryKey.Properties.Count != 1)
        {
            throw new NotSupportedException("DirectDeleteAsync supports single-column primary keys only.");
        }

        var tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException($"Entity type {typeof(TEntity).Name} has no table mapping.");

        var schema = entityType.GetSchema();
        var keyProperty = primaryKey.Properties[0];
        var columnName = keyProperty.GetColumnName(StoreObjectIdentifier.Table(tableName, schema));
        var qualifiedTableName = string.IsNullOrWhiteSpace(schema)
            ? QuoteIdentifier(tableName)
            : $"{QuoteIdentifier(schema)}.{QuoteIdentifier(tableName)}";

#pragma warning disable EF1002
        return DbContext.Database.ExecuteSqlRawAsync(
            $"DELETE FROM {qualifiedTableName} WHERE {QuoteIdentifier(columnName!)} = {{0}}",
            [key],
            cancellationToken);
#pragma warning restore EF1002
    }

    public Task<int> CommitChangesAsync(CancellationToken cancellationToken = default)
    {
        return DbContext.SaveChangesAsync(cancellationToken);
    }

    public void ClearAllChanges()
    {
        DbContext.ChangeTracker.Clear();
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        return DbContext.Database.BeginTransactionAsync(isolationLevel, cancellationToken);
    }

    public Task<int> ExecuteSqlAsync(
        string sql,
        IEnumerable<object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        return DbContext.Database.ExecuteSqlRawAsync(
            sql,
            parameters?.ToArray() ?? [],
            cancellationToken);
    }

    private void ApplyAuditValues<TEntity>(TEntity entity, bool isCreate)
        where TEntity : class
    {
        if (!IncludeSystemRecord)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var userId = GetCurrentUserId();

        if (isCreate)
        {
            TrySetPropertyValue(entity, "CreatedAt", now);
            TrySetPropertyValue(entity, "CreateDate", now);
            TrySetPropertyValue(entity, "CREATE_DATE", now);
            TrySetPropertyValue(entity, "CreatedBy", userId);
            TrySetPropertyValue(entity, "CreateUserId", userId);
            TrySetPropertyValue(entity, "CREATE_USER_ID", userId);
            TrySetPropertyValue(entity, "RecordStatus", "A");
            TrySetPropertyValue(entity, "RECORD_STATUS", "A");
            TrySetPropertyValue(entity, "DelFlag", "N");
            TrySetPropertyValue(entity, "DEL_FLAG", "N");
        }

        TrySetPropertyValue(entity, "UpdatedAt", now);
        TrySetPropertyValue(entity, "UpdateDate", now);
        TrySetPropertyValue(entity, "UPDATE_DATE", now);
        TrySetPropertyValue(entity, "UpdatedBy", userId);
        TrySetPropertyValue(entity, "UpdateUserId", userId);
        TrySetPropertyValue(entity, "UPDATE_USER_ID", userId);
    }

    private string? GetCurrentUserId()
    {
        var user = _httpContextAccessor.HttpContext?.User;

        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
    }

    private static bool TrySetPropertyValue<TEntity>(TEntity entity, string propertyName, object? value)
        where TEntity : class
    {
        var property = typeof(TEntity).GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is null || !property.CanWrite)
        {
            return false;
        }

        if (value is null)
        {
            property.SetValue(entity, null);
            return true;
        }

        var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (propertyType == typeof(char) && value is string stringValue)
        {
            property.SetValue(entity, stringValue.Length > 0 ? stringValue[0] : default(char));
            return true;
        }

        if (propertyType == typeof(string))
        {
            property.SetValue(entity, value.ToString());
            return true;
        }

        property.SetValue(entity, Convert.ChangeType(value, propertyType));
        return true;
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }
}
