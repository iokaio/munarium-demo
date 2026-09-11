# SPDX-License-Identifier: Apache-2.0
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:4ea6fe75dd36706bb6d8c3c293d4c4315840f5d76ea28ac97def77e3ec487fa5
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
RUN apt-get update && apt-get install -y --no-install-recommends python3-pil fonts-dejavu-core && rm -rf /var/lib/apt/lists/*
RUN git init /opt/munarium && cd /opt/munarium && git remote add origin https://github.com/iokaio/munarium.git && git fetch --depth 1 origin bb6e92a72a3944cff4d4bf0c1b470afcf3f4dfb3 && git checkout --detach FETCH_HEAD
WORKDIR /app
COPY . .
RUN dotnet restore tests/Meetings.Tests.csproj --locked-mode && dotnet restore /opt/munarium/clients/dotnet/tests/Ioka.Munarium.Client.Tests && dotnet restore /opt/munarium/clients/dotnet/tests/Ioka.Munarium.Client.Conformance
RUN dotnet build tests/Meetings.Tests.csproj --no-restore -c Release && dotnet build /opt/munarium/clients/dotnet/tests/Ioka.Munarium.Client.Tests --no-restore -c Release && dotnet build /opt/munarium/clients/dotnet/tests/Ioka.Munarium.Client.Conformance --no-restore -c Release
ENTRYPOINT ["dotnet", "/app/Meetings/bin/Release/net10.0/Meetings.dll"]
