using System.Security.Claims;
using ControlPlane.Application;
using Microsoft.AspNetCore.Http;

namespace ControlPlane.Infrastructure.OpenBao;

public sealed class HttpContextOpenBaoTokenAccessor(IHttpContextAccessor httpContextAccessor)
    : IOpenBaoTokenAccessor
{
    public string GetRequiredToken() =>
        httpContextAccessor.HttpContext?.User.FindFirstValue("openbao_token")
        ?? throw new UnauthorizedAccessException("A valid OpenBao session is required.");
}
