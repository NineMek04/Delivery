using System.Linq.Expressions;
using System.Reflection;

namespace BackendApi.Core.DataHandlers;

public sealed class ConditionContext
{
    private readonly Dictionary<string, string> _mappedFieldNames = new(StringComparer.OrdinalIgnoreCase);

    public bool RecordStatus { get; set; } = true;
    public bool DeleteFlag { get; set; } = true;

    public void MapFieldName<TProperty>(
        Expression<Func<ConditionContext, TProperty>> property,
        string fieldName)
    {
        if (property.Body is not MemberExpression memberExpression)
        {
            throw new ArgumentException("Expression must reference a ConditionContext property.", nameof(property));
        }

        _mappedFieldNames[memberExpression.Member.Name] = fieldName;
    }

    public IQueryable<TEntity> Apply<TEntity>(IQueryable<TEntity> query)
        where TEntity : class
    {
        if (RecordStatus)
        {
            query = ApplyEqualsIfPropertyExists(query, ResolveFieldName(nameof(RecordStatus), "RecordStatus", "RECORD_STATUS"), "A");
        }

        if (DeleteFlag)
        {
            query = ApplyEqualsIfPropertyExists(query, ResolveFieldName(nameof(DeleteFlag), "DelFlag", "DEL_FLAG"), "N");
        }

        return query;
    }

    private string ResolveFieldName(string contextName, params string[] defaults)
    {
        return _mappedFieldNames.TryGetValue(contextName, out var mappedName)
            ? mappedName
            : defaults[0];
    }

    private static IQueryable<TEntity> ApplyEqualsIfPropertyExists<TEntity>(
        IQueryable<TEntity> query,
        string preferredPropertyName,
        object expectedValue)
        where TEntity : class
    {
        var property = FindProperty(typeof(TEntity), preferredPropertyName);

        if (property is null && preferredPropertyName.Equals("RecordStatus", StringComparison.OrdinalIgnoreCase))
        {
            property = FindProperty(typeof(TEntity), "RECORD_STATUS");
        }

        if (property is null && preferredPropertyName.Equals("DelFlag", StringComparison.OrdinalIgnoreCase))
        {
            property = FindProperty(typeof(TEntity), "DEL_FLAG");
        }

        if (property is null || !property.CanRead)
        {
            return query;
        }

        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var propertyAccess = Expression.Property(parameter, property);
        var convertedValue = ConvertValue(expectedValue, property.PropertyType);
        var expectedConstant = Expression.Constant(convertedValue, property.PropertyType);
        var equals = Expression.Equal(propertyAccess, expectedConstant);
        var lambda = Expression.Lambda<Func<TEntity, bool>>(equals, parameter);

        return query.Where(lambda);
    }

    private static PropertyInfo? FindProperty(Type type, string propertyName)
    {
        return type.GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
    }

    private static object? ConvertValue(object value, Type targetType)
    {
        var actualType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (actualType == typeof(char) && value is string stringValue)
        {
            return stringValue.Length > 0 ? stringValue[0] : default(char);
        }

        return Convert.ChangeType(value, actualType);
    }
}
