# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy solution and project definitions
COPY Directory.Build.props WriteStudio.slnx ./
COPY src/ ./src/

# Publish WriteStudio.Web
RUN dotnet publish src/WriteStudio.Web/WriteStudio.Web.csproj -c Release -o /app/publish

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS final
WORKDIR /app

# Install FFmpeg and font/graphics libraries for SkiaSharp and video rendering
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    ffmpeg \
    libfontconfig1 \
    libfreetype6 \
    libpng16-16 \
    ca-certificates \
    curl && \
    rm -rf /var/lib/apt/lists/*

# Copy published application
COPY --from=build /app/publish .

# Environment configuration for Render and Linux containers
ENV PORT=8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV DOTNET_USE_POLLING_FILE_WATCHER=true
ENV DOTNET_SYSTEM_IO_DISABLEFILEWATCHING=true

EXPOSE 8080

ENTRYPOINT ["dotnet", "WriteStudio.Web.dll"]
