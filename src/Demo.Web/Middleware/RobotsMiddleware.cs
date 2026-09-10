// SPDX-License-Identifier: Apache-2.0
namespace Demo.Web.Middleware;

/// <summary>
/// The demo URL is obfuscated but public: every response carries
/// X-Robots-Tag: noindex, nofollow and Referrer-Policy: no-referrer, and
/// /robots.txt disallows everything. The gate is the real access control —
/// this keeps the URL out of indexes and referrer headers.
/// </summary>
public sealed class RobotsMiddleware
{
    private const string RobotsBody = "User-agent: *\nDisallow: /\n";

    private readonly RequestDelegate _next;

    public RobotsMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            return Task.CompletedTask;
        });

        if (context.Request.Path.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(RobotsBody);
            return;
        }

        await _next(context);
    }
}
