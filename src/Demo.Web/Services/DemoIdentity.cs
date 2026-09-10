// SPDX-License-Identifier: Apache-2.0
namespace Demo.Web.Services;

public sealed class DemoIdentity(IConfiguration configuration)
{
    public string Name { get; } = configuration["Demo:Name"] ?? "Munarium Demo";
    public string ContactEmail { get; } = configuration["Demo:ContactEmail"] ?? "";
    public string LogoUrl { get; } = configuration["Demo:LogoUrl"] ?? "/images/demo-mark.svg";
}
