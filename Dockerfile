# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY CloudPass.sln ./
COPY src/CloudPass.Domain/CloudPass.Domain.csproj src/CloudPass.Domain/
COPY src/CloudPass.Application/CloudPass.Application.csproj src/CloudPass.Application/
COPY src/CloudPass.Infrastructure/CloudPass.Infrastructure.csproj src/CloudPass.Infrastructure/
COPY src/CloudPass.Web/CloudPass.Web.csproj src/CloudPass.Web/

RUN dotnet restore src/CloudPass.Web/CloudPass.Web.csproj

COPY src/ src/
RUN dotnet publish src/CloudPass.Web/CloudPass.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN adduser --disabled-password --gecos "" cloudpassuser && \
    mkdir -p /app/App_Data/CloudStorage && \
    chown -R cloudpassuser:cloudpassuser /app

COPY --from=build --chown=cloudpassuser:cloudpassuser /app/publish .
USER cloudpassuser

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD curl -f http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "CloudPass.Web.dll"]
