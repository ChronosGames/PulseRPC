#!/bin/bash

# GameApp Production Rolling Update Script
# This script performs a zero-downtime rolling update of GameApp services

set -euo pipefail

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
COMPOSE_FILE="$PROJECT_DIR/docker-compose.prod.yml"
LOG_FILE="/var/log/gameapp/rolling-update.log"
BACKUP_DIR="/opt/gameapp/backups"
MAX_RETRIES=3
HEALTH_CHECK_TIMEOUT=60

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Logging function
log() {
    local level=$1
    shift
    local message="$*"
    local timestamp=$(date '+%Y-%m-%d %H:%M:%S')

    case $level in
        INFO)  echo -e "${GREEN}[INFO]${NC} $message" ;;
        WARN)  echo -e "${YELLOW}[WARN]${NC} $message" ;;
        ERROR) echo -e "${RED}[ERROR]${NC} $message" ;;
        DEBUG) echo -e "${BLUE}[DEBUG]${NC} $message" ;;
    esac

    # Also log to file
    mkdir -p "$(dirname "$LOG_FILE")"
    echo "[$timestamp] [$level] $message" >> "$LOG_FILE"
}

# Error handler
error_exit() {
    log ERROR "$1"
    log ERROR "Rolling update failed! Check logs at $LOG_FILE"
    exit 1
}

# Health check function
health_check() {
    local service=$1
    local endpoint=$2
    local retries=0

    log INFO "Performing health check for $service..."

    while [ $retries -lt $MAX_RETRIES ]; do
        if curl -f -s --max-time 10 "$endpoint" > /dev/null 2>&1; then
            log INFO "$service health check passed"
            return 0
        fi

        retries=$((retries + 1))
        log WARN "$service health check failed (attempt $retries/$MAX_RETRIES)"

        if [ $retries -lt $MAX_RETRIES ]; then
            sleep 10
        fi
    done

    log ERROR "$service health check failed after $MAX_RETRIES attempts"
    return 1
}

# Wait for service to be healthy
wait_for_service() {
    local service=$1
    local port=$2
    local path=${3:-"/health"}
    local timeout=${4:-$HEALTH_CHECK_TIMEOUT}

    log INFO "Waiting for $service to become healthy..."

    local start_time=$(date +%s)
    while true; do
        local current_time=$(date +%s)
        local elapsed=$((current_time - start_time))

        if [ $elapsed -ge $timeout ]; then
            log ERROR "Timeout waiting for $service to become healthy"
            return 1
        fi

        if docker-compose -f "$COMPOSE_FILE" ps "$service" | grep -q "healthy\|Up"; then
            if health_check "$service" "http://localhost:$port$path"; then
                log INFO "$service is healthy and ready"
                return 0
            fi
        fi

        sleep 5
    done
}

# Create backup
create_backup() {
    log INFO "Creating backup before update..."

    local backup_timestamp=$(date +"%Y%m%d_%H%M%S")
    local backup_path="$BACKUP_DIR/gameapp_backup_$backup_timestamp"

    mkdir -p "$backup_path"

    # Backup current configuration
    cp "$COMPOSE_FILE" "$backup_path/"
    cp -r "$PROJECT_DIR/nginx" "$backup_path/" 2>/dev/null || true
    cp -r "$PROJECT_DIR/monitoring" "$backup_path/" 2>/dev/null || true

    # Backup database
    log INFO "Creating database backup..."
    docker-compose -f "$COMPOSE_FILE" exec -T mongodb-primary mongodump \
        --username "$MONGODB_ROOT_USERNAME" \
        --password "$MONGODB_ROOT_PASSWORD" \
        --authenticationDatabase admin \
        --out "/tmp/backup_$backup_timestamp" || true

    docker-compose -f "$COMPOSE_FILE" exec -T mongodb-primary tar -czf \
        "/tmp/backup_$backup_timestamp.tar.gz" \
        "/tmp/backup_$backup_timestamp" || true

    # Copy backup from container
    docker cp "$(docker-compose -f "$COMPOSE_FILE" ps -q mongodb-primary):/tmp/backup_$backup_timestamp.tar.gz" \
        "$backup_path/" || true

    log INFO "Backup created at $backup_path"
    echo "$backup_path" > "$BACKUP_DIR/latest_backup.txt"
}

# Update service with zero downtime
update_service() {
    local service_base=$1
    local instances=("${@:2}")

    log INFO "Starting rolling update for $service_base..."

    for instance in "${instances[@]}"; do
        log INFO "Updating $instance..."

        # Pull new image
        docker-compose -f "$COMPOSE_FILE" pull "$instance"

        # Stop old container
        docker-compose -f "$COMPOSE_FILE" stop "$instance"
        docker-compose -f "$COMPOSE_FILE" rm -f "$instance"

        # Start new container
        docker-compose -f "$COMPOSE_FILE" up -d "$instance"

        # Wait for service to be healthy
        case $service_base in
            "authserver")
                wait_for_service "$instance" "5000" "/api/auth/health"
                ;;
            "gameserver")
                wait_for_service "$instance" "7000" "/health"
                ;;
            "battleserver")
                wait_for_service "$instance" "8000" "/health"
                ;;
        esac

        log INFO "$instance updated successfully"

        # Brief pause between instances
        sleep 10
    done

    log INFO "$service_base rolling update completed"
}

# Main update process
main() {
    log INFO "Starting GameApp rolling update..."
    log INFO "Using compose file: $COMPOSE_FILE"

    # Check if compose file exists
    if [ ! -f "$COMPOSE_FILE" ]; then
        error_exit "Compose file not found: $COMPOSE_FILE"
    fi

    # Check if required environment variables are set
    if [ -z "${IMAGE_TAG:-}" ]; then
        error_exit "IMAGE_TAG environment variable not set"
    fi

    log INFO "Updating to image tag: $IMAGE_TAG"

    # Create backup
    create_backup

    # Pull all new images first
    log INFO "Pulling new Docker images..."
    docker-compose -f "$COMPOSE_FILE" pull || error_exit "Failed to pull new images"

    # Update services in order (least critical first)
    log INFO "Starting service updates..."

    # Update AuthServer instances (with load balancer, zero downtime)
    update_service "authserver" "authserver-1" "authserver-2"

    # Brief pause between service types
    sleep 15

    # Update GameServer instances
    update_service "gameserver" "gameserver-1" "gameserver-2"

    # Brief pause between service types
    sleep 15

    # Update BattleServer instances
    update_service "battleserver" "battleserver-1" "battleserver-2"

    # Final health checks
    log INFO "Performing final system health checks..."

    health_check "AuthServer" "http://localhost/api/auth/health" || error_exit "AuthServer final health check failed"
    health_check "Monitoring" "http://localhost/api/monitoring/health" || error_exit "Monitoring final health check failed"

    # Cleanup old images
    log INFO "Cleaning up old Docker images..."
    docker image prune -f || log WARN "Failed to cleanup old images"

    # Update deployment record
    echo "$(date '+%Y-%m-%d %H:%M:%S') - Rolling update completed successfully - Image tag: $IMAGE_TAG" >> "$PROJECT_DIR/deployment-history.log"

    log INFO "Rolling update completed successfully!"
    log INFO "All services are running with image tag: $IMAGE_TAG"

    # Show final status
    log INFO "Current service status:"
    docker-compose -f "$COMPOSE_FILE" ps
}

# Cleanup function
cleanup() {
    log INFO "Cleaning up..."
    # Add any cleanup logic here
}

# Set trap for cleanup
trap cleanup EXIT

# Check if running as root
if [ "$EUID" -ne 0 ]; then
    log WARN "Not running as root. Make sure you have Docker permissions."
fi

# Check if Docker is available
if ! command -v docker &> /dev/null; then
    error_exit "Docker command not found"
fi

if ! command -v docker-compose &> /dev/null; then
    error_exit "docker-compose command not found"
fi

# Change to project directory
cd "$PROJECT_DIR"

# Source environment variables if .env file exists
if [ -f .env ]; then
    set -a
    source .env
    set +a
    log INFO "Loaded environment variables from .env"
fi

# Run main function
main "$@"
