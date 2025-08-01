#!/bin/bash

# GameApp Production Rollback Script
# This script performs a quick rollback to the previous working version

set -euo pipefail

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
COMPOSE_FILE="$PROJECT_DIR/docker-compose.prod.yml"
LOG_FILE="/var/log/gameapp/rollback.log"
BACKUP_DIR="/opt/gameapp/backups"
DEPLOYMENT_HISTORY="$PROJECT_DIR/deployment-history.log"
MAX_RETRIES=3

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
    log ERROR "Rollback failed! Check logs at $LOG_FILE"
    exit 1
}

# Show usage
usage() {
    echo "Usage: $0 [OPTIONS]"
    echo "Options:"
    echo "  -t, --tag TAG        Specific image tag to rollback to"
    echo "  -b, --backup PATH    Specific backup to restore from"
    echo "  -f, --force          Force rollback without confirmation"
    echo "  -h, --help           Show this help message"
    echo ""
    echo "Examples:"
    echo "  $0                   # Rollback to previous version"
    echo "  $0 -t v1.2.3         # Rollback to specific tag"
    echo "  $0 -b /path/backup   # Restore from specific backup"
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
            sleep 5
        fi
    done

    log ERROR "$service health check failed after $MAX_RETRIES attempts"
    return 1
}

# Get previous deployment info
get_previous_deployment() {
    if [ ! -f "$DEPLOYMENT_HISTORY" ]; then
        log ERROR "No deployment history found at $DEPLOYMENT_HISTORY"
        return 1
    fi

    # Get the second-to-last successful deployment
    local previous_deployment=$(tail -n 2 "$DEPLOYMENT_HISTORY" | head -n 1)

    if [ -z "$previous_deployment" ]; then
        log ERROR "No previous deployment found in history"
        return 1
    fi

    # Extract image tag from deployment record
    echo "$previous_deployment" | grep -o "Image tag: [^ ]*" | cut -d' ' -f3
}

# Get latest backup
get_latest_backup() {
    if [ -f "$BACKUP_DIR/latest_backup.txt" ]; then
        cat "$BACKUP_DIR/latest_backup.txt"
    else
        # Find the most recent backup directory
        find "$BACKUP_DIR" -maxdepth 1 -type d -name "gameapp_backup_*" | sort | tail -n 1
    fi
}

# Rollback services
rollback_services() {
    local target_tag=$1

    log INFO "Rolling back services to tag: $target_tag"

    # Set the rollback tag
    export IMAGE_TAG="$target_tag"

    # Stop all application services
    log INFO "Stopping application services..."
    docker-compose -f "$COMPOSE_FILE" stop authserver-1 authserver-2 gameserver-1 gameserver-2 battleserver-1 battleserver-2

    # Pull the target images
    log INFO "Pulling rollback images..."
    docker-compose -f "$COMPOSE_FILE" pull authserver-1 authserver-2 gameserver-1 gameserver-2 battleserver-1 battleserver-2

    # Start services in reverse order (most critical first)
    log INFO "Starting AuthServer instances..."
    docker-compose -f "$COMPOSE_FILE" up -d authserver-1 authserver-2
    sleep 10

    log INFO "Starting GameServer instances..."
    docker-compose -f "$COMPOSE_FILE" up -d gameserver-1 gameserver-2
    sleep 10

    log INFO "Starting BattleServer instances..."
    docker-compose -f "$COMPOSE_FILE" up -d battleserver-1 battleserver-2
    sleep 10

    # Wait for services to be healthy
    log INFO "Waiting for services to become healthy..."
    sleep 30

    # Perform health checks
    health_check "AuthServer" "http://localhost/api/auth/health" || error_exit "AuthServer health check failed after rollback"
    health_check "Monitoring" "http://localhost/api/monitoring/health" || error_exit "Monitoring health check failed after rollback"

    log INFO "Service rollback completed successfully"
}

# Restore configuration from backup
restore_configuration() {
    local backup_path=$1

    if [ ! -d "$backup_path" ]; then
        log ERROR "Backup directory not found: $backup_path"
        return 1
    fi

    log INFO "Restoring configuration from backup: $backup_path"

    # Backup current configuration before restore
    local current_backup="$PROJECT_DIR/current_backup_$(date +%Y%m%d_%H%M%S)"
    mkdir -p "$current_backup"
    cp "$COMPOSE_FILE" "$current_backup/"
    cp -r "$PROJECT_DIR/nginx" "$current_backup/" 2>/dev/null || true

    # Restore configuration files
    if [ -f "$backup_path/docker-compose.prod.yml" ]; then
        cp "$backup_path/docker-compose.prod.yml" "$COMPOSE_FILE"
        log INFO "Restored docker-compose.prod.yml"
    fi

    if [ -d "$backup_path/nginx" ]; then
        rm -rf "$PROJECT_DIR/nginx"
        cp -r "$backup_path/nginx" "$PROJECT_DIR/"
        log INFO "Restored nginx configuration"
    fi

    if [ -d "$backup_path/monitoring" ]; then
        rm -rf "$PROJECT_DIR/monitoring"
        cp -r "$backup_path/monitoring" "$PROJECT_DIR/"
        log INFO "Restored monitoring configuration"
    fi

    log INFO "Configuration restoration completed"
}

# Restore database from backup
restore_database() {
    local backup_path=$1

    log WARN "Database restoration is a destructive operation!"
    read -p "Are you sure you want to restore the database? (yes/no): " -r

    if [[ ! $REPLY =~ ^[Yy][Ee][Ss]$ ]]; then
        log INFO "Database restoration cancelled"
        return 0
    fi

    log INFO "Restoring database from backup..."

    # Find backup file
    local backup_file=$(find "$backup_path" -name "backup_*.tar.gz" | head -n 1)

    if [ -z "$backup_file" ]; then
        log WARN "No database backup file found in $backup_path"
        return 1
    fi

    # Copy backup to MongoDB container
    docker cp "$backup_file" "$(docker-compose -f "$COMPOSE_FILE" ps -q mongodb-primary):/tmp/"

    # Extract and restore
    local backup_filename=$(basename "$backup_file")
    docker-compose -f "$COMPOSE_FILE" exec -T mongodb-primary bash -c "
        cd /tmp &&
        tar -xzf $backup_filename &&
        mongorestore --username $MONGODB_ROOT_USERNAME --password $MONGODB_ROOT_PASSWORD --authenticationDatabase admin --drop backup_*/
    "

    log INFO "Database restoration completed"
}

# Interactive rollback selection
interactive_rollback() {
    log INFO "Available rollback options:"
    echo ""
    echo "1. Rollback to previous deployment"
    echo "2. Rollback to specific image tag"
    echo "3. Restore from backup"
    echo "4. Cancel"
    echo ""

    read -p "Please select an option (1-4): " -r choice

    case $choice in
        1)
            local previous_tag=$(get_previous_deployment)
            if [ -n "$previous_tag" ]; then
                log INFO "Previous deployment tag: $previous_tag"
                read -p "Rollback to this version? (y/n): " -r
                if [[ $REPLY =~ ^[Yy]$ ]]; then
                    rollback_services "$previous_tag"
                fi
            else
                log ERROR "Could not determine previous deployment"
            fi
            ;;
        2)
            read -p "Enter image tag to rollback to: " -r target_tag
            if [ -n "$target_tag" ]; then
                rollback_services "$target_tag"
            fi
            ;;
        3)
            local latest_backup=$(get_latest_backup)
            if [ -n "$latest_backup" ]; then
                log INFO "Latest backup: $latest_backup"
                read -p "Restore from this backup? (y/n): " -r
                if [[ $REPLY =~ ^[Yy]$ ]]; then
                    restore_configuration "$latest_backup"
                    restore_database "$latest_backup"
                    docker-compose -f "$COMPOSE_FILE" restart
                fi
            else
                log ERROR "No backup found"
            fi
            ;;
        4)
            log INFO "Rollback cancelled"
            exit 0
            ;;
        *)
            log ERROR "Invalid option"
            exit 1
            ;;
    esac
}

# Main rollback function
main() {
    local target_tag=""
    local backup_path=""
    local force_rollback=false

    # Parse command line arguments
    while [[ $# -gt 0 ]]; do
        case $1 in
            -t|--tag)
                target_tag="$2"
                shift 2
                ;;
            -b|--backup)
                backup_path="$2"
                shift 2
                ;;
            -f|--force)
                force_rollback=true
                shift
                ;;
            -h|--help)
                usage
                exit 0
                ;;
            *)
                log ERROR "Unknown option: $1"
                usage
                exit 1
                ;;
        esac
    done

    log INFO "Starting GameApp rollback process..."

    # Check if compose file exists
    if [ ! -f "$COMPOSE_FILE" ]; then
        error_exit "Compose file not found: $COMPOSE_FILE"
    fi

    # If no specific options provided, run interactive mode
    if [ -z "$target_tag" ] && [ -z "$backup_path" ] && [ "$force_rollback" = false ]; then
        interactive_rollback
        return
    fi

    # Confirmation unless forced
    if [ "$force_rollback" = false ]; then
        log WARN "This will rollback the production environment!"
        read -p "Are you sure you want to continue? (yes/no): " -r
        if [[ ! $REPLY =~ ^[Yy][Ee][Ss]$ ]]; then
            log INFO "Rollback cancelled"
            exit 0
        fi
    fi

    # Perform specific rollback
    if [ -n "$backup_path" ]; then
        restore_configuration "$backup_path"
        restore_database "$backup_path"
        docker-compose -f "$COMPOSE_FILE" restart
    elif [ -n "$target_tag" ]; then
        rollback_services "$target_tag"
    else
        # Default to previous deployment
        local previous_tag=$(get_previous_deployment)
        if [ -n "$previous_tag" ]; then
            rollback_services "$previous_tag"
        else
            error_exit "Could not determine rollback target"
        fi
    fi

    # Record rollback in history
    echo "$(date '+%Y-%m-%d %H:%M:%S') - Rollback completed - Target: ${target_tag:-$backup_path}" >> "$DEPLOYMENT_HISTORY"

    log INFO "Rollback completed successfully!"

    # Show final status
    log INFO "Current service status:"
    docker-compose -f "$COMPOSE_FILE" ps
}

# Cleanup function
cleanup() {
    log INFO "Cleaning up..."
}

# Set trap for cleanup
trap cleanup EXIT

# Check requirements
if [ "$EUID" -ne 0 ]; then
    log WARN "Not running as root. Make sure you have Docker permissions."
fi

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
