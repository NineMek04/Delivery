using BackendApi.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace BackendApi.Core.Filters;

public sealed class StandardApiResponsesOperationFilter : IOperationFilter
{
    private static readonly (string Status, string Description)[] CommonResponses =
    [
        ("400", "Bad Request"),
        ("404", "Not Found"),
        ("409", "Conflict"),
        ("429", "Too Many Requests"),
        ("500", "Internal Server Error"),
        ("503", "Service Unavailable")
    ];

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        foreach (var response in CommonResponses)
        {
            AddOrUpdateResponse(operation, response.Status, response.Description);
        }

        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        var allowsAnonymous = metadata.OfType<IAllowAnonymous>().Any();
        var requiresAuthorization = metadata.OfType<IAuthorizeData>().Any();

        if (requiresAuthorization && !allowsAnonymous)
        {
            AddOrUpdateResponse(operation, "401", "Unauthorized");
            AddOrUpdateResponse(operation, "403", "Forbidden");
        }
    }

    private static void AddOrUpdateResponse(
        OpenApiOperation operation,
        string status,
        string description)
    {
        if (!operation.Responses.TryGetValue(status, out var response))
        {
            response = new OpenApiResponse { Description = description };
            operation.Responses[status] = response;
        }
        else if (string.IsNullOrWhiteSpace(response.Description))
        {
            response.Description = description;
        }

        response.Content["application/json"] = new OpenApiMediaType
        {
            Schema = new OpenApiSchema
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.Schema,
                    Id = nameof(ApiResponse)
                }
            }
        };
    }
}
