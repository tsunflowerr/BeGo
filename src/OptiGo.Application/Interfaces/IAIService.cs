using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OptiGo.Application.Interfaces
{
    public interface IAIService
    {
        Task<string> ResolveCategoryAsync(string query, CancellationToken cancellationToken = default);
        Task<string> SummarizeReviewsAsync(IEnumerable<string> reviews, CancellationToken ct = default);
    }
}
