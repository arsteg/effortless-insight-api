# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY src/EffortlessInsight.Api/EffortlessInsight.Api.csproj ./EffortlessInsight.Api/
RUN dotnet restore EffortlessInsight.Api/EffortlessInsight.Api.csproj

# Copy everything and build
COPY src/ ./
RUN dotnet build EffortlessInsight.Api/EffortlessInsight.Api.csproj -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish EffortlessInsight.Api/EffortlessInsight.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Create non-root user
RUN adduser --disabled-password --gecos '' appuser

# Copy published app
COPY --from=publish /app/publish .

# Set ownership and switch to non-root user
RUN chown -R appuser:appuser /app
USER appuser

# Expose port
EXPOSE 5000

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:5000/health || exit 1

# Set environment variables
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "EffortlessInsight.Api.dll"]
