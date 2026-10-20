using Lexis.Core.DTOs.UsageLogs;
using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.Interfaces
{
    public interface IUsageLogService
    {
        Task<UsageLogPageDTO> GetUsageLogsAsync(int authenticatedUserId, bool isAdmin, UsageLogQueryDTO query,
            CancellationToken cancellationToken = default);
    }
}
