FROM mcr.microsoft.com/dotnet/sdk:10.0.401
RUN apt-get update && apt-get install -y --no-install-recommends python3 openssl stunnel4 git \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /workspace
COPY . .
RUN dotnet build samples/GameServer/GameServer.Host/GameServer.Host.csproj -c Release --warnaserror
ENTRYPOINT ["python3", "scripts/game-acceptance-cluster.py"]
