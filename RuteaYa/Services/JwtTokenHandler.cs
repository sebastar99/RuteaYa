
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace RuteaYa.Web.Services
{
    public class JwtTokenHandler : DelegatingHandler
    {
        private const string AccessTokenCookieName = "RuteaYa.AccessToken";
        private readonly IHttpContextAccessor _httpContextAccessor;

        public JwtTokenHandler(
            IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var httpContext = _httpContextAccessor.HttpContext;

            if (httpContext?.User.Identity?.IsAuthenticated == true)
            {
                var token = httpContext.Request.Cookies[
                    AccessTokenCookieName];

                token ??= await httpContext.GetTokenAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    "access_token");

                if (!string.IsNullOrWhiteSpace(token))
                {
                    request.Headers.Authorization =
                        new AuthenticationHeaderValue("Bearer", token);
                }
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
