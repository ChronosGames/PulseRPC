# GameApp 部署计划

## 部署架构概述

GameApp 采用微服务容器化部署架构，支持多环境部署和水平扩展，确保系统的高可用性和可维护性。

```mermaid
graph TB
    LB[负载均衡器<br/>Nginx/HAProxy]

    subgraph "应用层"
        Auth1[AuthServer-1]
        Auth2[AuthServer-2]
        Game1[GameServer-1]
        Game2[GameServer-2]
        Battle1[BattleServer-1]
        Battle2[BattleServer-2]
    end

    subgraph "数据层"
        MongoDB[(MongoDB集群)]
        Redis[(Redis集群)]
        Consul[Consul集群]
    end

    subgraph "基础设施"
        Monitor[监控系统<br/>Prometheus+Grafana]
        Log[日志系统<br/>ELK Stack]
        Backup[备份系统]
    end

    Internet --> LB
    LB --> Auth1
    LB --> Auth2
    LB --> Game1
    LB --> Game2
    LB --> Battle1
    LB --> Battle2

    Auth1 --> MongoDB
    Auth1 --> Redis
    Auth1 --> Consul
    Game1 --> MongoDB
    Game1 --> Redis
    Game1 --> Consul
    Battle1 --> MongoDB
    Battle1 --> Redis
    Battle1 --> Consul

    Monitor --> Auth1
    Monitor --> Game1
    Monitor --> Battle1
    Log --> Auth1
    Log --> Game1
    Log --> Battle1

    style LB fill:#e1f5fe
    style MongoDB fill:#f3e5f5
    style Redis fill:#ffebee
    style Consul fill:#e0f2f1
    style Monitor fill:#fff3e0
    style Log fill:#fce4ec
```

## 环境规划

### 1. 开发环境 (Development)

**目标**: 为开发团队提供稳定的开发测试环境

**配置规格**:
```yaml
环境: Docker Compose 本地部署
服务器: 开发机本地 + 共享开发服务器

资源配置:
  AuthServer: 1 实例, 512MB RAM, 1 CPU
  GameServer: 1 实例, 1GB RAM, 1 CPU
  BattleServer: 1 实例, 1GB RAM, 1 CPU
  MongoDB: 单节点, 2GB RAM, 2 CPU
  Redis: 单节点, 512MB RAM, 1 CPU
  Consul: 单节点, 256MB RAM, 1 CPU
```

**部署文件**: `docker/dev/docker-compose.dev.yml`

```yaml
version: '3.8'
services:
  # MongoDB 开发数据库
  mongodb-dev:
    image: mongo:7.0
    container_name: gameapp-mongodb-dev
    ports:
      - "27017:27017"
    environment:
      MONGO_INITDB_ROOT_USERNAME: admin
      MONGO_INITDB_ROOT_PASSWORD: dev_password
      MONGO_INITDB_DATABASE: gameapp_dev
    volumes:
      - mongodb-dev-data:/data/db
      - ./mongodb/init-dev.js:/docker-entrypoint-initdb.d/init.js
    networks:
      - gameapp-dev

  # Redis 开发缓存
  redis-dev:
    image: redis:7.0-alpine
    container_name: gameapp-redis-dev
    ports:
      - "6379:6379"
    command: redis-server --requirepass dev_password
    volumes:
      - redis-dev-data:/data
    networks:
      - gameapp-dev

  # Consul 开发服务发现
  consul-dev:
    image: consul:1.15
    container_name: gameapp-consul-dev
    ports:
      - "8500:8500"
      - "8600:8600"
    environment:
      CONSUL_BIND_INTERFACE: eth0
    volumes:
      - consul-dev-data:/consul/data
    networks:
      - gameapp-dev

  # AuthServer 开发服务
  authserver-dev:
    build:
      context: ../../src/GameApp.AuthServer
      dockerfile: Dockerfile.dev
    container_name: gameapp-authserver-dev
    ports:
      - "8080:8080"
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__MongoDB: "mongodb://admin:dev_password@mongodb-dev:27017/gameapp_dev"
      ConnectionStrings__Redis: "redis-dev:6379"
      Consul__Host: "consul-dev"
    depends_on:
      - mongodb-dev
      - redis-dev
      - consul-dev
    networks:
      - gameapp-dev

networks:
  gameapp-dev:
    driver: bridge

volumes:
  mongodb-dev-data:
  redis-dev-data:
  consul-dev-data:
```

### 2. 测试环境 (Testing)

**目标**: 提供与生产环境相似的测试验证环境

**配置规格**:
```yaml
环境: Kubernetes 集群部署
服务器: 3 节点 K8s 集群

资源配置:
  AuthServer: 2 实例, 1GB RAM, 1 CPU
  GameServer: 2 实例, 2GB RAM, 2 CPU
  BattleServer: 2 实例, 2GB RAM, 2 CPU
  MongoDB: 3 节点副本集, 4GB RAM, 2 CPU
  Redis: 3 节点集群, 2GB RAM, 1 CPU
  Consul: 3 节点集群, 512MB RAM, 1 CPU
```

**Kubernetes 部署文件**: `k8s/test/`

```yaml
# authserver-deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: authserver-test
  namespace: gameapp-test
spec:
  replicas: 2
  selector:
    matchLabels:
      app: authserver
      environment: test
  template:
    metadata:
      labels:
        app: authserver
        environment: test
    spec:
      containers:
      - name: authserver
        image: gameapp/authserver:test-latest
        ports:
        - containerPort: 8080
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Testing"
        - name: ConnectionStrings__MongoDB
          valueFrom:
            secretKeyRef:
              name: gameapp-secrets
              key: mongodb-connection
        - name: ConnectionStrings__Redis
          valueFrom:
            secretKeyRef:
              name: gameapp-secrets
              key: redis-connection
        resources:
          requests:
            memory: "512Mi"
            cpu: "500m"
          limits:
            memory: "1Gi"
            cpu: "1000m"
        livenessProbe:
          httpGet:
            path: /health
            port: 8080
          initialDelaySeconds: 30
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /ready
            port: 8080
          initialDelaySeconds: 5
          periodSeconds: 5

---
apiVersion: v1
kind: Service
metadata:
  name: authserver-service
  namespace: gameapp-test
spec:
  selector:
    app: authserver
    environment: test
  ports:
  - protocol: TCP
    port: 80
    targetPort: 8080
  type: ClusterIP
```

### 3. 预生产环境 (Staging)

**目标**: 生产环境的完整镜像，用于最终发布前验证

**配置规格**:
```yaml
环境: Kubernetes 集群部署
服务器: 5 节点 K8s 集群

资源配置:
  AuthServer: 3 实例, 2GB RAM, 2 CPU
  GameServer: 4 实例, 4GB RAM, 4 CPU
  BattleServer: 4 实例, 4GB RAM, 4 CPU
  MongoDB: 3 节点副本集, 8GB RAM, 4 CPU
  Redis: 6 节点集群, 4GB RAM, 2 CPU
  Consul: 3 节点集群, 1GB RAM, 1 CPU
```

### 4. 生产环境 (Production)

**目标**: 为最终用户提供稳定可靠的服务

**配置规格**:
```yaml
环境: Kubernetes 集群部署 + 云服务
服务器: 10+ 节点 K8s 集群

资源配置:
  AuthServer: 5 实例, 4GB RAM, 4 CPU
  GameServer: 10 实例, 8GB RAM, 8 CPU
  BattleServer: 8 实例, 8GB RAM, 8 CPU
  MongoDB: 5 节点分片集群, 16GB RAM, 8 CPU
  Redis: 6 节点集群, 8GB RAM, 4 CPU
  Consul: 5 节点集群, 2GB RAM, 2 CPU
```

## Docker 容器化

### 1. 应用服务容器化

**AuthServer Dockerfile**:
```dockerfile
# src/GameApp.AuthServer/Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 复制项目文件
COPY ["GameApp.AuthServer/GameApp.AuthServer.csproj", "GameApp.AuthServer/"]
COPY ["GameApp.Shared/GameApp.Shared.csproj", "GameApp.Shared/"]
COPY ["GameApp.Infrastructure/GameApp.Infrastructure.csproj", "GameApp.Infrastructure/"]

# 还原依赖
RUN dotnet restore "GameApp.AuthServer/GameApp.AuthServer.csproj"

# 复制源代码
COPY . .
WORKDIR "/src/GameApp.AuthServer"

# 构建应用
RUN dotnet build "GameApp.AuthServer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "GameApp.AuthServer.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# 创建非 root 用户
RUN adduser --disabled-password --gecos '' appuser && chown -R appuser /app
USER appuser

HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "GameApp.AuthServer.dll"]
```

**GameServer Dockerfile**:
```dockerfile
# src/GameApp.GameServer/Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 7000

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["GameApp.GameServer/GameApp.GameServer.csproj", "GameApp.GameServer/"]
COPY ["GameApp.Shared/GameApp.Shared.csproj", "GameApp.Shared/"]
COPY ["GameApp.Infrastructure/GameApp.Infrastructure.csproj", "GameApp.Infrastructure/"]

RUN dotnet restore "GameApp.GameServer/GameApp.GameServer.csproj"

COPY . .
WORKDIR "/src/GameApp.GameServer"
RUN dotnet build "GameApp.GameServer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "GameApp.GameServer.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

RUN adduser --disabled-password --gecos '' appuser && chown -R appuser /app
USER appuser

HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
  CMD curl -f http://localhost:7000/health || exit 1

ENTRYPOINT ["dotnet", "GameApp.GameServer.dll"]
```

### 2. 多阶段构建优化

**优化策略**:
```dockerfile
# 构建优化版本
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# 使用构建缓存
COPY ["Directory.Packages.props", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["GameApp.sln", "./"]
COPY ["*/*.csproj", "./"]

# 分层复制，最大化缓存命中
RUN for file in $(ls *.csproj); do mkdir -p ${file%.*}/ && mv $file ${file%.*}/; done

RUN dotnet restore "GameApp.sln"

# 复制源代码
COPY . .

# 构建和发布
RUN dotnet publish "src/GameApp.AuthServer/GameApp.AuthServer.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    --self-contained false

# 运行时镜像
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app

# 安装必要的运行时依赖
RUN apk add --no-cache curl

COPY --from=build /app/publish .

# 安全配置
RUN addgroup -g 1001 -S appgroup && \
    adduser -S appuser -G appgroup -u 1001 && \
    chown -R appuser:appgroup /app

USER appuser

ENTRYPOINT ["dotnet", "GameApp.AuthServer.dll"]
```

## Kubernetes 部署

### 1. 命名空间和资源配额

```yaml
# k8s/namespaces.yaml
apiVersion: v1
kind: Namespace
metadata:
  name: gameapp-prod
  labels:
    name: gameapp-prod
    environment: production

---
apiVersion: v1
kind: ResourceQuota
metadata:
  name: gameapp-quota
  namespace: gameapp-prod
spec:
  hard:
    requests.cpu: "20"
    requests.memory: 40Gi
    limits.cpu: "40"
    limits.memory: 80Gi
    pods: "50"
    services: "20"
    persistentvolumeclaims: "10"

---
apiVersion: v1
kind: LimitRange
metadata:
  name: gameapp-limits
  namespace: gameapp-prod
spec:
  limits:
  - default:
      cpu: "2"
      memory: "4Gi"
    defaultRequest:
      cpu: "500m"
      memory: "1Gi"
    type: Container
```

### 2. 配置管理

```yaml
# k8s/configmap.yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: gameapp-config
  namespace: gameapp-prod
data:
  appsettings.json: |
    {
      "Logging": {
        "LogLevel": {
          "Default": "Information",
          "Microsoft.AspNetCore": "Warning"
        }
      },
      "AllowedHosts": "*",
      "Consul": {
        "Host": "consul-service",
        "Port": 8500,
        "ServiceName": "gameapp-authserver",
        "ServiceId": "gameapp-authserver-{env:HOSTNAME}",
        "HealthCheckPath": "/health"
      },
      "Jwt": {
        "Issuer": "GameApp",
        "Audience": "GameApp.Client",
        "ExpirationMinutes": 60
      }
    }

---
apiVersion: v1
kind: Secret
metadata:
  name: gameapp-secrets
  namespace: gameapp-prod
type: Opaque
data:
  mongodb-connection: bW9uZ29kYjovL3VzZXI6cGFzc3dvcmRAaG9zdDoyNzAxNy9kYXRhYmFzZQ==
  redis-connection: cmVkaXM6Ly86cGFzc3dvcmRAaG9zdDo2Mzc5LzA=
  jwt-key: eW91ci1zdXBlci1zZWNyZXQta2V5LWZvci1qd3QtdG9rZW5z
```

### 3. 服务部署

```yaml
# k8s/prod/gameserver-deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: gameserver
  namespace: gameapp-prod
  labels:
    app: gameserver
    version: v1.0.0
spec:
  replicas: 10
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxUnavailable: 2
      maxSurge: 2
  selector:
    matchLabels:
      app: gameserver
  template:
    metadata:
      labels:
        app: gameserver
    spec:
      containers:
      - name: gameserver
        image: gameapp/gameserver:1.0.0
        ports:
        - containerPort: 7000
          name: grpc
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: ASPNETCORE_URLS
          value: "http://+:7000"
        - name: POD_NAME
          valueFrom:
            fieldRef:
              fieldPath: metadata.name
        - name: NODE_NAME
          valueFrom:
            fieldRef:
              fieldPath: spec.nodeName
        envFrom:
        - configMapRef:
            name: gameapp-config
        - secretRef:
            name: gameapp-secrets
        resources:
          requests:
            memory: "4Gi"
            cpu: "2"
          limits:
            memory: "8Gi"
            cpu: "4"
        livenessProbe:
          grpc:
            port: 7000
          initialDelaySeconds: 60
          periodSeconds: 30
          timeoutSeconds: 10
          failureThreshold: 3
        readinessProbe:
          grpc:
            port: 7000
          initialDelaySeconds: 10
          periodSeconds: 10
          timeoutSeconds: 5
          failureThreshold: 3
        volumeMounts:
        - name: logs
          mountPath: /app/logs
        - name: config
          mountPath: /app/config
          readOnly: true
      volumes:
      - name: logs
        emptyDir: {}
      - name: config
        configMap:
          name: gameapp-config
      affinity:
        podAntiAffinity:
          preferredDuringSchedulingIgnoredDuringExecution:
          - weight: 100
            podAffinityTerm:
              labelSelector:
                matchExpressions:
                - key: app
                  operator: In
                  values:
                  - gameserver
              topologyKey: kubernetes.io/hostname

---
apiVersion: v1
kind: Service
metadata:
  name: gameserver-service
  namespace: gameapp-prod
spec:
  selector:
    app: gameserver
  ports:
  - port: 7000
    targetPort: 7000
    protocol: TCP
    name: grpc
  type: ClusterIP

---
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: gameserver-hpa
  namespace: gameapp-prod
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: gameserver
  minReplicas: 3
  maxReplicas: 20
  metrics:
  - type: Resource
    resource:
      name: cpu
      target:
        type: Utilization
        averageUtilization: 70
  - type: Resource
    resource:
      name: memory
      target:
        type: Utilization
        averageUtilization: 80
```

### 4. 数据库部署

```yaml
# k8s/prod/mongodb-statefulset.yaml
apiVersion: apps/v1
kind: StatefulSet
metadata:
  name: mongodb
  namespace: gameapp-prod
spec:
  serviceName: mongodb-headless
  replicas: 3
  selector:
    matchLabels:
      app: mongodb
  template:
    metadata:
      labels:
        app: mongodb
    spec:
      containers:
      - name: mongodb
        image: mongo:7.0
        ports:
        - containerPort: 27017
        env:
        - name: MONGO_INITDB_ROOT_USERNAME
          valueFrom:
            secretKeyRef:
              name: mongodb-secret
              key: username
        - name: MONGO_INITDB_ROOT_PASSWORD
          valueFrom:
            secretKeyRef:
              name: mongodb-secret
              key: password
        - name: MONGO_REPLICA_SET_NAME
          value: "rs0"
        resources:
          requests:
            memory: "8Gi"
            cpu: "4"
          limits:
            memory: "16Gi"
            cpu: "8"
        volumeMounts:
        - name: mongodb-data
          mountPath: /data/db
        - name: mongodb-config
          mountPath: /data/configdb
        livenessProbe:
          exec:
            command:
            - mongosh
            - --eval
            - "db.adminCommand('ping')"
          initialDelaySeconds: 30
          periodSeconds: 10
        readinessProbe:
          exec:
            command:
            - mongosh
            - --eval
            - "db.adminCommand('ping')"
          initialDelaySeconds: 5
          periodSeconds: 5
      volumes:
      - name: mongodb-config
        emptyDir: {}
  volumeClaimTemplates:
  - metadata:
      name: mongodb-data
    spec:
      accessModes: ["ReadWriteOnce"]
      storageClassName: "fast-ssd"
      resources:
        requests:
          storage: 100Gi

---
apiVersion: v1
kind: Service
metadata:
  name: mongodb-headless
  namespace: gameapp-prod
spec:
  clusterIP: None
  selector:
    app: mongodb
  ports:
  - port: 27017
    targetPort: 27017

---
apiVersion: v1
kind: Service
metadata:
  name: mongodb-service
  namespace: gameapp-prod
spec:
  selector:
    app: mongodb
  ports:
  - port: 27017
    targetPort: 27017
  type: ClusterIP
```

## 负载均衡和网关

### 1. Nginx 配置

```nginx
# deploy/nginx/nginx.conf
upstream authserver_backend {
    least_conn;
    server authserver-service:80 max_fails=3 fail_timeout=30s;
    keepalive 32;
}

upstream gameserver_backend {
    least_conn;
    server gameserver-service:7000 max_fails=3 fail_timeout=30s;
    keepalive 32;
}

# HTTP 服务器配置
server {
    listen 80;
    server_name auth.gameapp.com;

    # 重定向到 HTTPS
    return 301 https://$server_name$request_uri;
}

# HTTPS 服务器配置
server {
    listen 443 ssl http2;
    server_name auth.gameapp.com;

    # SSL 配置
    ssl_certificate /etc/ssl/certs/gameapp.crt;
    ssl_certificate_key /etc/ssl/private/gameapp.key;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers ECDHE-RSA-AES256-GCM-SHA512:DHE-RSA-AES256-GCM-SHA512;
    ssl_prefer_server_ciphers off;
    ssl_session_cache shared:SSL:10m;
    ssl_session_timeout 10m;

    # 安全头
    add_header X-Frame-Options DENY;
    add_header X-Content-Type-Options nosniff;
    add_header X-XSS-Protection "1; mode=block";
    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains";

    # API 代理
    location /api/ {
        proxy_pass http://authserver_backend/api/;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection 'upgrade';
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_cache_bypass $http_upgrade;

        # 超时配置
        proxy_connect_timeout 5s;
        proxy_send_timeout 60s;
        proxy_read_timeout 60s;

        # 缓存配置
        proxy_cache api_cache;
        proxy_cache_valid 200 5m;
        proxy_cache_key "$scheme$request_method$host$request_uri";
    }

    # 健康检查
    location /health {
        access_log off;
        return 200 "healthy\n";
        add_header Content-Type text/plain;
    }

    # 限流配置
    location /api/auth/login {
        limit_req zone=login_limit burst=5 nodelay;
        proxy_pass http://authserver_backend/api/auth/login;
        # ... 其他代理配置
    }
}

# gRPC 服务器配置
server {
    listen 7000 http2;
    server_name game.gameapp.com;

    # gRPC 代理
    location / {
        grpc_pass grpc://gameserver_backend;
        grpc_set_header Host $host;
        grpc_set_header X-Real-IP $remote_addr;
        grpc_set_header X-Forwarded-For $proxy_add_x_forwarded_for;

        # 超时配置
        grpc_connect_timeout 5s;
        grpc_send_timeout 60s;
        grpc_read_timeout 60s;
    }
}

# 限流配置
http {
    limit_req_zone $binary_remote_addr zone=login_limit:10m rate=10r/m;
    limit_req_zone $binary_remote_addr zone=api_limit:10m rate=100r/m;

    # 缓存配置
    proxy_cache_path /var/cache/nginx levels=1:2 keys_zone=api_cache:10m;
}
```

### 2. Kubernetes Ingress

```yaml
# k8s/prod/ingress.yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: gameapp-ingress
  namespace: gameapp-prod
  annotations:
    kubernetes.io/ingress.class: "nginx"
    cert-manager.io/cluster-issuer: "letsencrypt-prod"
    nginx.ingress.kubernetes.io/ssl-redirect: "true"
    nginx.ingress.kubernetes.io/force-ssl-redirect: "true"
    nginx.ingress.kubernetes.io/rate-limit: "100"
    nginx.ingress.kubernetes.io/rate-limit-window: "1m"
spec:
  tls:
  - hosts:
    - auth.gameapp.com
    - game.gameapp.com
    secretName: gameapp-tls
  rules:
  - host: auth.gameapp.com
    http:
      paths:
      - path: /
        pathType: Prefix
        backend:
          service:
            name: authserver-service
            port:
              number: 80
  - host: game.gameapp.com
    http:
      paths:
      - path: /
        pathType: Prefix
        backend:
          service:
            name: gameserver-service
            port:
              number: 7000
```

## 监控和日志

### 1. Prometheus 监控

```yaml
# k8s/monitoring/prometheus-config.yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: prometheus-config
  namespace: monitoring
data:
  prometheus.yml: |
    global:
      scrape_interval: 15s
      evaluation_interval: 15s

    rule_files:
      - "gameapp-rules.yml"

    scrape_configs:
    - job_name: 'kubernetes-pods'
      kubernetes_sd_configs:
      - role: pod
      relabel_configs:
      - source_labels: [__meta_kubernetes_pod_annotation_prometheus_io_scrape]
        action: keep
        regex: true
      - source_labels: [__meta_kubernetes_pod_annotation_prometheus_io_path]
        action: replace
        target_label: __metrics_path__
        regex: (.+)
      - source_labels: [__address__, __meta_kubernetes_pod_annotation_prometheus_io_port]
        action: replace
        regex: ([^:]+)(?::\d+)?;(\d+)
        replacement: $1:$2
        target_label: __address__
      - action: labelmap
        regex: __meta_kubernetes_pod_label_(.+)
      - source_labels: [__meta_kubernetes_namespace]
        action: replace
        target_label: kubernetes_namespace
      - source_labels: [__meta_kubernetes_pod_name]
        action: replace
        target_label: kubernetes_pod_name

    - job_name: 'gameapp-services'
      static_configs:
      - targets:
        - 'authserver-service:8080'
        - 'gameserver-service:7000'
        - 'battleserver-service:7001'
      metrics_path: /metrics
      scrape_interval: 10s

---
apiVersion: v1
kind: ConfigMap
metadata:
  name: gameapp-alert-rules
  namespace: monitoring
data:
  gameapp-rules.yml: |
    groups:
    - name: gameapp-alerts
      rules:
      - alert: ServiceDown
        expr: up{job="gameapp-services"} == 0
        for: 1m
        labels:
          severity: critical
        annotations:
          summary: "GameApp service is down"
          description: "Service {{ $labels.instance }} has been down for more than 1 minute."

      - alert: HighCPUUsage
        expr: rate(container_cpu_usage_seconds_total{pod=~"gameapp-.*"}[5m]) > 0.8
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "High CPU usage detected"
          description: "Pod {{ $labels.pod }} CPU usage is above 80% for 5 minutes."

      - alert: HighMemoryUsage
        expr: container_memory_usage_bytes{pod=~"gameapp-.*"} / container_spec_memory_limit_bytes > 0.9
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "High memory usage detected"
          description: "Pod {{ $labels.pod }} memory usage is above 90% for 5 minutes."

      - alert: DatabaseConnectionFailure
        expr: mongodb_connections_available < 10
        for: 2m
        labels:
          severity: critical
        annotations:
          summary: "MongoDB connection pool exhausted"
          description: "MongoDB available connections is less than 10."
```

### 2. Grafana 仪表板

```json
{
  "dashboard": {
    "title": "GameApp System Overview",
    "panels": [
      {
        "title": "Service Status",
        "type": "stat",
        "targets": [
          {
            "expr": "up{job=\"gameapp-services\"}",
            "legendFormat": "{{ instance }}"
          }
        ]
      },
      {
        "title": "Request Rate",
        "type": "graph",
        "targets": [
          {
            "expr": "rate(http_requests_total{job=\"gameapp-services\"}[5m])",
            "legendFormat": "{{ method }} {{ status }}"
          }
        ]
      },
      {
        "title": "Response Time",
        "type": "graph",
        "targets": [
          {
            "expr": "histogram_quantile(0.95, rate(http_request_duration_seconds_bucket{job=\"gameapp-services\"}[5m]))",
            "legendFormat": "95th percentile"
          }
        ]
      },
      {
        "title": "Database Connections",
        "type": "graph",
        "targets": [
          {
            "expr": "mongodb_connections_current",
            "legendFormat": "Current connections"
          }
        ]
      }
    ]
  }
}
```

### 3. 日志收集 (ELK Stack)

```yaml
# k8s/logging/elasticsearch.yaml
apiVersion: apps/v1
kind: StatefulSet
metadata:
  name: elasticsearch
  namespace: logging
spec:
  serviceName: elasticsearch
  replicas: 3
  selector:
    matchLabels:
      app: elasticsearch
  template:
    metadata:
      labels:
        app: elasticsearch
    spec:
      containers:
      - name: elasticsearch
        image: docker.elastic.co/elasticsearch/elasticsearch:8.5.0
        ports:
        - containerPort: 9200
        - containerPort: 9300
        env:
        - name: cluster.name
          value: "gameapp-logs"
        - name: node.name
          valueFrom:
            fieldRef:
              fieldPath: metadata.name
        - name: discovery.seed_hosts
          value: "elasticsearch-0.elasticsearch,elasticsearch-1.elasticsearch,elasticsearch-2.elasticsearch"
        - name: cluster.initial_master_nodes
          value: "elasticsearch-0,elasticsearch-1,elasticsearch-2"
        - name: ES_JAVA_OPTS
          value: "-Xms4g -Xmx4g"
        resources:
          requests:
            memory: "8Gi"
            cpu: "2"
          limits:
            memory: "8Gi"
            cpu: "4"
        volumeMounts:
        - name: elasticsearch-data
          mountPath: /usr/share/elasticsearch/data
  volumeClaimTemplates:
  - metadata:
      name: elasticsearch-data
    spec:
      accessModes: ["ReadWriteOnce"]
      storageClassName: "fast-ssd"
      resources:
        requests:
          storage: 50Gi
```

## CI/CD 流水线

### 1. GitHub Actions 工作流

```yaml
# .github/workflows/deploy.yml
name: Build and Deploy GameApp

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

env:
  REGISTRY: ghcr.io
  IMAGE_NAME: gameapp

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v3

    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '8.0.x'

    - name: Restore dependencies
      run: dotnet restore

    - name: Build
      run: dotnet build --no-restore

    - name: Test
      run: dotnet test --no-build --verbosity normal

    - name: Code Coverage
      run: |
        dotnet test --collect:"XPlat Code Coverage"
        dotnet tool install -g dotnet-reportgenerator-globaltool
        reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:"coverage" -reporttypes:Html

  build:
    needs: test
    runs-on: ubuntu-latest
    if: github.ref == 'refs/heads/main'

    strategy:
      matrix:
        service: [authserver, gameserver, battleserver]

    steps:
    - uses: actions/checkout@v3

    - name: Log in to Container Registry
      uses: docker/login-action@v2
      with:
        registry: ${{ env.REGISTRY }}
        username: ${{ github.actor }}
        password: ${{ secrets.GITHUB_TOKEN }}

    - name: Extract metadata
      id: meta
      uses: docker/metadata-action@v4
      with:
        images: ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}-${{ matrix.service }}
        tags: |
          type=ref,event=branch
          type=ref,event=pr
          type=sha,prefix={{branch}}-
          type=raw,value=latest,enable={{is_default_branch}}

    - name: Build and push Docker image
      uses: docker/build-push-action@v4
      with:
        context: ./src/GameApp.${{ matrix.service }}
        file: ./src/GameApp.${{ matrix.service }}/Dockerfile
        push: true
        tags: ${{ steps.meta.outputs.tags }}
        labels: ${{ steps.meta.outputs.labels }}

  deploy-staging:
    needs: build
    runs-on: ubuntu-latest
    if: github.ref == 'refs/heads/main'
    environment: staging

    steps:
    - uses: actions/checkout@v3

    - name: Configure kubectl
      uses: azure/k8s-set-context@v1
      with:
        method: kubeconfig
        kubeconfig: ${{ secrets.KUBE_CONFIG_STAGING }}

    - name: Deploy to staging
      run: |
        kubectl apply -f k8s/staging/
        kubectl rollout status deployment/authserver -n gameapp-staging
        kubectl rollout status deployment/gameserver -n gameapp-staging
        kubectl rollout status deployment/battleserver -n gameapp-staging

  deploy-production:
    needs: deploy-staging
    runs-on: ubuntu-latest
    if: github.ref == 'refs/heads/main'
    environment: production

    steps:
    - uses: actions/checkout@v3

    - name: Configure kubectl
      uses: azure/k8s-set-context@v1
      with:
        method: kubeconfig
        kubeconfig: ${{ secrets.KUBE_CONFIG_PROD }}

    - name: Deploy to production
      run: |
        kubectl apply -f k8s/prod/
        kubectl rollout status deployment/authserver -n gameapp-prod
        kubectl rollout status deployment/gameserver -n gameapp-prod
        kubectl rollout status deployment/battleserver -n gameapp-prod

    - name: Verify deployment
      run: |
        kubectl get pods -n gameapp-prod
        kubectl get services -n gameapp-prod
```

### 2. 部署脚本

```bash
#!/bin/bash
# scripts/deploy/deploy-production.sh

set -e

NAMESPACE="gameapp-prod"
IMAGE_TAG=${1:-latest}

echo "Deploying GameApp to production environment..."
echo "Image tag: $IMAGE_TAG"
echo "Namespace: $NAMESPACE"

# 更新镜像标签
sed -i "s|image: gameapp/authserver:.*|image: gameapp/authserver:$IMAGE_TAG|g" k8s/prod/authserver-deployment.yaml
sed -i "s|image: gameapp/gameserver:.*|image: gameapp/gameserver:$IMAGE_TAG|g" k8s/prod/gameserver-deployment.yaml
sed -i "s|image: gameapp/battleserver:.*|image: gameapp/battleserver:$IMAGE_TAG|g" k8s/prod/battleserver-deployment.yaml

# 应用配置
echo "Applying configurations..."
kubectl apply -f k8s/prod/namespaces.yaml
kubectl apply -f k8s/prod/configmap.yaml
kubectl apply -f k8s/prod/secrets.yaml

# 部署数据库
echo "Deploying databases..."
kubectl apply -f k8s/prod/mongodb-statefulset.yaml
kubectl apply -f k8s/prod/redis-statefulset.yaml
kubectl apply -f k8s/prod/consul-statefulset.yaml

# 等待数据库就绪
echo "Waiting for databases to be ready..."
kubectl wait --for=condition=ready pod -l app=mongodb -n $NAMESPACE --timeout=300s
kubectl wait --for=condition=ready pod -l app=redis -n $NAMESPACE --timeout=300s
kubectl wait --for=condition=ready pod -l app=consul -n $NAMESPACE --timeout=300s

# 部署应用服务
echo "Deploying application services..."
kubectl apply -f k8s/prod/authserver-deployment.yaml
kubectl apply -f k8s/prod/gameserver-deployment.yaml
kubectl apply -f k8s/prod/battleserver-deployment.yaml

# 等待部署完成
echo "Waiting for deployments to complete..."
kubectl rollout status deployment/authserver -n $NAMESPACE --timeout=600s
kubectl rollout status deployment/gameserver -n $NAMESPACE --timeout=600s
kubectl rollout status deployment/battleserver -n $NAMESPACE --timeout=600s

# 部署网关和负载均衡
echo "Deploying ingress and load balancers..."
kubectl apply -f k8s/prod/ingress.yaml
kubectl apply -f k8s/prod/services.yaml

# 验证部署
echo "Verifying deployment..."
kubectl get pods -n $NAMESPACE
kubectl get services -n $NAMESPACE
kubectl get ingress -n $NAMESPACE

# 健康检查
echo "Performing health checks..."
kubectl get pods -n $NAMESPACE -o jsonpath='{range .items[*]}{.metadata.name}{"\t"}{.status.phase}{"\n"}{end}'

echo "Deployment completed successfully!"
```

## 安全配置

### 1. RBAC 权限控制

```yaml
# k8s/security/rbac.yaml
apiVersion: v1
kind: ServiceAccount
metadata:
  name: gameapp-service-account
  namespace: gameapp-prod

---
apiVersion: rbac.authorization.k8s.io/v1
kind: Role
metadata:
  namespace: gameapp-prod
  name: gameapp-role
rules:
- apiGroups: [""]
  resources: ["pods", "services", "endpoints"]
  verbs: ["get", "list", "watch"]
- apiGroups: ["apps"]
  resources: ["deployments", "replicasets"]
  verbs: ["get", "list", "watch"]

---
apiVersion: rbac.authorization.k8s.io/v1
kind: RoleBinding
metadata:
  name: gameapp-role-binding
  namespace: gameapp-prod
subjects:
- kind: ServiceAccount
  name: gameapp-service-account
  namespace: gameapp-prod
roleRef:
  kind: Role
  name: gameapp-role
  apiGroup: rbac.authorization.k8s.io
```

### 2. 网络策略

```yaml
# k8s/security/network-policy.yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: gameapp-network-policy
  namespace: gameapp-prod
spec:
  podSelector:
    matchLabels:
      app: gameapp
  policyTypes:
  - Ingress
  - Egress
  ingress:
  - from:
    - namespaceSelector:
        matchLabels:
          name: ingress-nginx
    ports:
    - protocol: TCP
      port: 8080
    - protocol: TCP
      port: 7000
  - from:
    - podSelector:
        matchLabels:
          app: gameapp
    ports:
    - protocol: TCP
      port: 8080
    - protocol: TCP
      port: 7000
  egress:
  - to:
    - podSelector:
        matchLabels:
          app: mongodb
    ports:
    - protocol: TCP
      port: 27017
  - to:
    - podSelector:
        matchLabels:
          app: redis
    ports:
    - protocol: TCP
      port: 6379
  - to: []
    ports:
    - protocol: TCP
      port: 53
    - protocol: UDP
      port: 53
```

## 备份和恢复

### 1. 数据库备份策略

```bash
#!/bin/bash
# scripts/backup/mongodb-backup.sh

NAMESPACE="gameapp-prod"
BACKUP_DIR="/backups/mongodb"
DATE=$(date +%Y%m%d_%H%M%S)
BACKUP_NAME="gameapp_backup_$DATE"

# 创建备份目录
mkdir -p $BACKUP_DIR

# 执行 MongoDB 备份
kubectl exec -n $NAMESPACE mongodb-0 -- mongodump \
  --host mongodb-service:27017 \
  --username $MONGODB_USER \
  --password $MONGODB_PASSWORD \
  --authenticationDatabase admin \
  --out /tmp/$BACKUP_NAME

# 复制备份文件到本地
kubectl cp $NAMESPACE/mongodb-0:/tmp/$BACKUP_NAME $BACKUP_DIR/$BACKUP_NAME

# 压缩备份文件
cd $BACKUP_DIR
tar -czf $BACKUP_NAME.tar.gz $BACKUP_NAME
rm -rf $BACKUP_NAME

# 上传到对象存储（可选）
aws s3 cp $BACKUP_NAME.tar.gz s3://gameapp-backups/mongodb/

# 清理本地文件
rm -f $BACKUP_NAME.tar.gz

echo "Backup completed: $BACKUP_NAME.tar.gz"
```

### 2. 自动备份 CronJob

```yaml
# k8s/backup/backup-cronjob.yaml
apiVersion: batch/v1
kind: CronJob
metadata:
  name: mongodb-backup
  namespace: gameapp-prod
spec:
  schedule: "0 2 * * *"  # 每天凌晨2点执行
  jobTemplate:
    spec:
      template:
        spec:
          containers:
          - name: mongodb-backup
            image: mongo:7.0
            command:
            - /bin/bash
            - -c
            - |
              DATE=$(date +%Y%m%d_%H%M%S)
              BACKUP_NAME="gameapp_backup_$DATE"

              mongodump \
                --host mongodb-service:27017 \
                --username $MONGODB_USER \
                --password $MONGODB_PASSWORD \
                --authenticationDatabase admin \
                --out /backups/$BACKUP_NAME

              tar -czf /backups/$BACKUP_NAME.tar.gz -C /backups $BACKUP_NAME
              rm -rf /backups/$BACKUP_NAME

              # 上传到云存储
              aws s3 cp /backups/$BACKUP_NAME.tar.gz s3://gameapp-backups/mongodb/

              # 清理本地备份（保留7天）
              find /backups -name "*.tar.gz" -mtime +7 -delete
            env:
            - name: MONGODB_USER
              valueFrom:
                secretKeyRef:
                  name: mongodb-secret
                  key: username
            - name: MONGODB_PASSWORD
              valueFrom:
                secretKeyRef:
                  name: mongodb-secret
                  key: password
            - name: AWS_ACCESS_KEY_ID
              valueFrom:
                secretKeyRef:
                  name: aws-secret
                  key: access-key-id
            - name: AWS_SECRET_ACCESS_KEY
              valueFrom:
                secretKeyRef:
                  name: aws-secret
                  key: secret-access-key
            volumeMounts:
            - name: backup-storage
              mountPath: /backups
          volumes:
          - name: backup-storage
            persistentVolumeClaim:
              claimName: backup-pvc
          restartPolicy: OnFailure
  successfulJobsHistoryLimit: 3
  failedJobsHistoryLimit: 1
```

---

本部署计划确保了 GameApp 系统的稳定部署、高可用性和可维护性，为游戏服务的长期稳定运行提供了坚实保障。
