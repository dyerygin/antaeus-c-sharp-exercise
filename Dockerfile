# The API image (ADR-0017). Build from the repository root: docker build -t csharpapp-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore in its own layer from project and lock files only, so a source change keeps the package cache (ADR-0002).
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY CSharpApp.Api/CSharpApp.Api.csproj CSharpApp.Api/packages.lock.json CSharpApp.Api/
COPY CSharpApp.Application/CSharpApp.Application.csproj CSharpApp.Application/packages.lock.json CSharpApp.Application/
COPY CSharpApp.Core/CSharpApp.Core.csproj CSharpApp.Core/packages.lock.json CSharpApp.Core/
COPY CSharpApp.Infrastructure/CSharpApp.Infrastructure.csproj CSharpApp.Infrastructure/packages.lock.json CSharpApp.Infrastructure/
RUN dotnet restore CSharpApp.Api/CSharpApp.Api.csproj --locked-mode

COPY CSharpApp.Api/ CSharpApp.Api/
COPY CSharpApp.Application/ CSharpApp.Application/
COPY CSharpApp.Core/ CSharpApp.Core/
COPY CSharpApp.Infrastructure/ CSharpApp.Infrastructure/
# Analyzers gate every commit in CI's build job; the image build skips them to stay fast.
RUN dotnet publish CSharpApp.Api/CSharpApp.Api.csproj -c Release --no-restore -o /app -p:UseAppHost=false -p:RunAnalyzers=false

# The regular runtime image keeps a shell for debugging during the review; the chiseled one needs only this FROM line.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# The health probe calls 127.0.0.1, so the image's own host list allows it; compose and real hosts override it.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    AllowedHosts="localhost;127.0.0.1"
COPY --from=build /app .
# The non-root "app" user the base image provides ($APP_UID); numeric, so Kubernetes runAsNonRoot can verify it.
USER 1654
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --start-interval=2s --retries=3 \
    CMD ["dotnet", "CSharpApp.Api.dll", "--health-probe"]
ENTRYPOINT ["dotnet", "CSharpApp.Api.dll"]
