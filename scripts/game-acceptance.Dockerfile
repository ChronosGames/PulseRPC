FROM mcr.microsoft.com/dotnet/sdk:10.0.401
RUN apt-get update && apt-get install -y --no-install-recommends python3 openssl stunnel4 git \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /workspace
COPY . .
# SourceLink is disabled only for this non-published test image: .git and credentials stay outside.
RUN dotnet build samples/GameServer/GameServer.Host/GameServer.Host.csproj -c Release --warnaserror \
    -p:EnableSourceControlManagerQueries=false -p:EnableSourceLink=false
ENTRYPOINT ["python3", "scripts/game-acceptance-cluster.py"]
