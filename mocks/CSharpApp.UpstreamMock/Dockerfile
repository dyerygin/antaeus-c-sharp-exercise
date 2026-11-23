# The upstream mock image (ADR-0003, ADR-0017). Build from the repository root:
# docker build -f mocks/CSharpApp.UpstreamMock/Dockerfile -t csharpapp-upstream-mock .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore in its own layer from project and lock files only, so a source change keeps the package cache (ADR-0002).
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY mocks/CSharpApp.UpstreamMock/CSharpApp.UpstreamMock.csproj mocks/CSharpApp.UpstreamMock/packages.lock.json mocks/CSharpApp.UpstreamMock/
RUN dotnet restore mocks/CSharpApp.UpstreamMock/CSharpApp.UpstreamMock.csproj --locked-mode

COPY mocks/CSharpApp.UpstreamMock/ mocks/CSharpApp.UpstreamMock/
# Analyzers gate every commit in CI's build job; the image build skips them to stay fast.
RUN dotnet publish mocks/CSharpApp.UpstreamMock/CSharpApp.UpstreamMock.csproj -c Release --no-restore -o /app -p:UseAppHost=false -p:RunAnalyzers=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app .
# The non-root "app" user the base image provides ($APP_UID); numeric, so Kubernetes runAsNonRoot can verify it.
USER 1654
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --start-interval=2s --retries=3 \
    CMD ["dotnet", "CSharpApp.UpstreamMock.dll", "--health-probe"]
ENTRYPOINT ["dotnet", "CSharpApp.UpstreamMock.dll"]
