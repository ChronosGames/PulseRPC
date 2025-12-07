# Stage 1: Base runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

# Install curl for Docker health checks
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Stage 2: Build image
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy global configuration files
COPY ["Directory.Packages.props", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["global.json", "./"]

# Copy project files
COPY ["samples/DistributedGameApp/src/DistributedGameApp.GameServer/DistributedGameApp.GameServer.csproj", "samples/DistributedGameApp/src/DistributedGameApp.GameServer/"]
COPY ["samples/DistributedGameApp/src/DistributedGameApp.Shared/DistributedGameApp.Shared.csproj", "samples/DistributedGameApp/src/DistributedGameApp.Shared/"]
COPY ["samples/DistributedGameApp/src/DistributedGameApp.Infrastructure/DistributedGameApp.Infrastructure.csproj", "samples/DistributedGameApp/src/DistributedGameApp.Infrastructure/"]
COPY ["src/PulseRPC.Client/PulseRPC.Client.csproj", "src/PulseRPC.Client/"]
COPY ["src/PulseRPC.Server/PulseRPC.Server.csproj", "src/PulseRPC.Server/"]
COPY ["src/PulseRPC.Server.SourceGenerator/PulseRPC.Server.SourceGenerator.csproj", "src/PulseRPC.Server.SourceGenerator/"]

# Restore dependencies
RUN dotnet restore "samples/DistributedGameApp/src/DistributedGameApp.GameServer/DistributedGameApp.GameServer.csproj"

# Copy source code
COPY . .
WORKDIR "/src/samples/DistributedGameApp/src/DistributedGameApp.GameServer"

# Build
RUN dotnet build "DistributedGameApp.GameServer.csproj" -c Release -o /app/build

# Stage 3: Publish image
FROM build AS publish
RUN dotnet publish "DistributedGameApp.GameServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 4: Final image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Expose ports (TCP and KCP)
EXPOSE 8080
EXPOSE 8081/udp

ENTRYPOINT ["dotnet", "DistributedGameApp.GameServer.dll"]
