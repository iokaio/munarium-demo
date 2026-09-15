// SPDX-License-Identifier: Apache-2.0
namespace Demo.Web.Services;

public sealed record ModelTierPolicy(bool FastOnly)
{
    public bool Allows(string tier) => !FastOnly || tier == "fast";
}
