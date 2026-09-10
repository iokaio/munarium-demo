# SPDX-License-Identifier: Apache-2.0
# Build from the munarium-demo repository root after Sync-Assets.ps1.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ ./src/
COPY vendor/runbooks/*.yaml ./src/Demo.Web/wwwroot/downloads/runbooks/
RUN dotnet restore src/Demo.Web/Demo.Web.csproj --locked-mode \
 && dotnet publish src/Demo.Web/Demo.Web.csproj -c Release -o /app/publish --no-restore \
 && test ! -e /app/publish/appsettings.Secrets.json \
 && test "$(find /app/publish/wwwroot/downloads/runbooks -name '*.yaml' | wc -l)" -eq 12

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ARG SOURCE_REVISION=unknown
LABEL org.opencontainers.image.revision=$SOURCE_REVISION
LABEL org.opencontainers.image.licenses=Apache-2.0
COPY --from=build /app/publish .
COPY LICENSE NOTICE THIRD_PARTY_NOTICES.md ./
COPY licenses/ ./licenses/
EXPOSE 8080
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENTRYPOINT ["dotnet", "Demo.Web.dll"]
