using System;

namespace BackendApi.Services.Auth
{
    public interface IAuditLogger
    {
        void LogAdminAction(
            string action,
            string targetType,
            string targetId,
            string? beforeState,
            string? afterState);
    }
}
