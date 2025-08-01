# GameApp Production Deployment Guide

## 🚀 Production Environment Setup

This directory contains all the necessary configuration files and scripts for deploying GameApp to a production environment.

## 📁 Directory Structure

```
deploy/production/
├── README.md                    # This file
├── docker-compose.prod.yml      # Production Docker Compose configuration
├── env.template                 # Environment variables template
├── nginx/
│   ├── nginx.conf              # Nginx load balancer configuration
│   └── ssl/                    # SSL certificates directory
├── monitoring/
│   ├── prometheus.yml          # Prometheus configuration
│   ├── rules/                  # Alerting rules
│   └── grafana/               # Grafana dashboards
├── scripts/
│   ├── rolling-update.sh       # Zero-downtime rolling update script
│   ├── rollback.sh            # Emergency rollback script
│   └── setup.sh               # Initial setup script
└── configs/
    ├── mongodb/               # MongoDB configuration
    ├── redis/                 # Redis configuration
    └── consul/                # Consul configuration
```

## 🔧 Prerequisites

### System Requirements

- **Operating System**: Ubuntu 20.04 LTS or newer / CentOS 8+
- **CPU**: Minimum 8 cores (16+ recommended)
- **Memory**: Minimum 16GB RAM (32GB+ recommended)
- **Storage**: Minimum 100GB SSD (500GB+ recommended)
- **Network**: Static IP address, domain name configured

### Software Requirements

- **Docker**: 24.0 or newer
- **Docker Compose**: 2.0 or newer
- **Git**: For deployment scripts
- **curl**: For health checks
- **openssl**: For certificate management

### Network Requirements

- **Ports**: 80, 443, 7000-7003, 8000-8003 open
- **Domain**: Valid domain with DNS configured
- **SSL**: SSL certificates (Let's Encrypt recommended)

## 🚀 Quick Start

### 1. Initial Server Setup

```bash
# Update system
sudo apt update && sudo apt upgrade -y

# Install Docker
curl -fsSL https://get.docker.com -o get-docker.sh
sudo sh get-docker.sh
sudo usermod -aG docker $USER

# Install Docker Compose
sudo curl -L "https://github.com/docker/compose/releases/download/v2.21.0/docker-compose-$(uname -s)-$(uname -m)" -o /usr/local/bin/docker-compose
sudo chmod +x /usr/local/bin/docker-compose

# Create deployment directory
sudo mkdir -p /opt/gameapp/production
sudo chown -R $USER:$USER /opt/gameapp
```

### 2. Deploy Files

```bash
# Copy deployment files to server
scp -r deploy/production/* user@server:/opt/gameapp/production/

# Or clone repository on server
cd /opt/gameapp/production
git clone <repository-url> .
```

### 3. Configure Environment

```bash
# Copy environment template
cp env.template .env

# Edit environment variables
nano .env

# Generate secure passwords and keys
openssl rand -base64 32  # For JWT_SECRET_KEY
openssl rand -base64 32  # For database passwords
```

### 4. SSL Certificate Setup

```bash
# Install Certbot
sudo apt install certbot python3-certbot-nginx

# Generate certificates
sudo certbot certonly --standalone -d api.gameapp.com -d monitoring.gameapp.com

# Copy certificates
sudo cp /etc/letsencrypt/live/api.gameapp.com/fullchain.pem nginx/ssl/
sudo cp /etc/letsencrypt/live/api.gameapp.com/privkey.pem nginx/ssl/
sudo chown -R $USER:$USER nginx/ssl/
```

### 5. Initialize Services

```bash
# Make scripts executable
chmod +x scripts/*.sh

# Set environment variables
export IMAGE_TAG=v1.0.0
export MONGODB_ROOT_USERNAME=admin
export MONGODB_ROOT_PASSWORD=your-secure-password
# ... other variables from .env

# Start services
docker-compose -f docker-compose.prod.yml up -d
```

## 🔄 Deployment Operations

### Rolling Update

For zero-downtime deployments:

```bash
# Set the new image tag
export IMAGE_TAG=v1.0.1

# Perform rolling update
./scripts/rolling-update.sh
```

### Rollback

In case of deployment issues:

```bash
# Quick rollback to previous version
./scripts/rollback.sh

# Rollback to specific version
./scripts/rollback.sh --tag v1.0.0

# Restore from specific backup
./scripts/rollback.sh --backup /path/to/backup
```

### Health Checks

```bash
# Check service status
docker-compose -f docker-compose.prod.yml ps

# API health check
curl https://api.gameapp.com/api/auth/health

# Monitoring health check
curl https://api.gameapp.com/api/monitoring/health

# Individual service checks
curl http://localhost:5000/api/auth/health  # AuthServer
curl http://localhost:7000/health           # GameServer
curl http://localhost:8000/health           # BattleServer
```

## 📊 Monitoring and Alerting

### Access Monitoring

- **Grafana Dashboard**: https://monitoring.gameapp.com
- **Prometheus**: https://monitoring.gameapp.com/prometheus
- **Consul UI**: http://server-ip:8500

### Default Credentials

- **Grafana**: admin / (set in GRAFANA_ADMIN_PASSWORD)

### Alert Destinations

Configure the following in your environment:

- **Email**: SMTP settings in .env
- **Slack**: Webhook URL in .env
- **PagerDuty**: Integration key in .env

## 🔒 Security Checklist

### Pre-Deployment Security

- [ ] Strong passwords for all services
- [ ] JWT secret key is cryptographically secure (32+ chars)
- [ ] SSL certificates are valid and configured
- [ ] Firewall rules are properly configured
- [ ] Database access is restricted to application networks
- [ ] Monitoring access is IP-restricted

### Post-Deployment Security

- [ ] Change all default passwords
- [ ] Enable audit logging
- [ ] Configure intrusion detection
- [ ] Set up log monitoring
- [ ] Regular security updates
- [ ] Backup encryption

## 📈 Performance Tuning

### Resource Allocation

Adjust these based on your server specs:

```bash
# In docker-compose.prod.yml
# AuthServer: 2GB RAM, 2 CPU
# GameServer: 4GB RAM, 4 CPU
# BattleServer: 4GB RAM, 4 CPU
# MongoDB: 4GB RAM, 4 CPU
# Redis: 2GB RAM, 2 CPU
```

### Database Optimization

```bash
# MongoDB indexes
docker-compose exec mongodb-primary mongo gameapp_prod --eval "
  db.users.createIndex({'username': 1}, {unique: true});
  db.users.createIndex({'email': 1}, {unique: true});
  db.players.createIndex({'userId': 1});
"
```

### Cache Configuration

```bash
# Redis memory optimization
# Set maxmemory policy in redis configuration
maxmemory-policy allkeys-lru
```

## 🗄️ Backup and Recovery

### Automated Backups

Backups are automatically created during rolling updates. Manual backup:

```bash
# Database backup
docker-compose exec mongodb-primary mongodump --out /tmp/backup-$(date +%Y%m%d)

# Configuration backup
cp -r /opt/gameapp/production /opt/gameapp/backup-$(date +%Y%m%d)
```

### Recovery Procedures

```bash
# Restore from backup
./scripts/rollback.sh --backup /path/to/backup

# Manual database restore
docker-compose exec mongodb-primary mongorestore --drop /path/to/backup
```

## 🚨 Troubleshooting

### Common Issues

#### Services Won't Start

```bash
# Check logs
docker-compose logs service-name

# Check resource usage
docker stats

# Check network connectivity
docker network ls
```

#### High Memory Usage

```bash
# Check container memory usage
docker stats --no-stream

# Restart high-memory containers
docker-compose restart service-name
```

#### Database Connection Issues

```bash
# Check MongoDB replica set status
docker-compose exec mongodb-primary mongo --eval "rs.status()"

# Check Redis connectivity
docker-compose exec redis-master redis-cli ping
```

#### SSL Certificate Issues

```bash
# Check certificate expiry
openssl x509 -in nginx/ssl/fullchain.pem -text -noout | grep "Not After"

# Renew certificates
sudo certbot renew
```

### Emergency Procedures

#### Complete System Recovery

```bash
# Stop all services
docker-compose down

# Clean up containers and volumes
docker system prune -a
docker volume prune

# Restore from backup
./scripts/rollback.sh --backup /path/to/latest/backup

# Restart services
docker-compose up -d
```

#### Database Recovery

```bash
# Stop applications
docker-compose stop authserver-1 authserver-2 gameserver-1 gameserver-2 battleserver-1 battleserver-2

# Restore database
docker-compose exec mongodb-primary mongorestore --drop /path/to/backup

# Restart applications
docker-compose start authserver-1 authserver-2 gameserver-1 gameserver-2 battleserver-1 battleserver-2
```

## 📞 Support and Contacts

### Team Contacts

- **Platform Team**: platform@gameapp.com
- **Database Team**: database@gameapp.com
- **Security Team**: security@gameapp.com
- **On-Call**: oncall@gameapp.com

### Documentation

- **API Documentation**: https://docs.gameapp.com/api
- **Runbooks**: https://docs.gameapp.com/runbooks
- **Architecture**: https://docs.gameapp.com/architecture

### Emergency Escalation

1. **Check monitoring dashboard** for system status
2. **Review recent deployments** in deployment history
3. **Check alert manager** for active alerts
4. **Contact on-call engineer** if critical
5. **Initiate rollback** if necessary

---

## 📝 Deployment Checklist

### Pre-Deployment

- [ ] Environment variables configured
- [ ] SSL certificates installed
- [ ] Database credentials updated
- [ ] Monitoring configured
- [ ] Backup strategy tested
- [ ] Rollback procedure tested

### Deployment

- [ ] Rolling update executed successfully
- [ ] Health checks passing
- [ ] Monitoring metrics normal
- [ ] No active alerts
- [ ] Performance within acceptable range

### Post-Deployment

- [ ] System monitoring for 24 hours
- [ ] User feedback collected
- [ ] Performance metrics reviewed
- [ ] Documentation updated
- [ ] Team notified of successful deployment

---

**🎉 Congratulations! Your GameApp production environment is now ready!**

For additional support, please refer to our comprehensive documentation or contact the platform team.
