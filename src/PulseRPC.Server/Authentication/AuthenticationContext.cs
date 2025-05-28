using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using PulseRPC.Authentication;

namespace PulseRPC.Server.Authentication
{
    /// <summary>
    /// 统一认证上下文实现
    /// </summary>
    public class AuthenticationContext : IAuthenticationContext
    {
        private readonly object _syncLock = new object();
        private readonly ConcurrentDictionary<string, object> _properties = new ConcurrentDictionary<string, object>();

        private AuthenticationType _type = AuthenticationType.None;
        private string? _identity;
        private string? _name;
        private string? _token;
        private DateTime? _authenticationTime;
        private ClaimsPrincipal? _principal;
        private string[]? _scopes;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="connectionId">连接ID</param>
        public AuthenticationContext(string connectionId)
        {
            ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        }

        /// <inheritdoc />
        public string ConnectionId { get; }

        /// <inheritdoc />
        public bool IsAuthenticated
        {
            get
            {
                lock (_syncLock)
                {
                    return _type != AuthenticationType.None &&
                           !string.IsNullOrEmpty(_identity) &&
                           !string.IsNullOrEmpty(_name);
                }
            }
        }

        /// <inheritdoc />
        public AuthenticationType Type
        {
            get
            {
                lock (_syncLock)
                {
                    return _type;
                }
            }
        }

        /// <inheritdoc />
        public string? Identity
        {
            get
            {
                lock (_syncLock)
                {
                    return _identity;
                }
            }
        }

        /// <inheritdoc />
        public string? Name
        {
            get
            {
                lock (_syncLock)
                {
                    return _name;
                }
            }
        }

        /// <inheritdoc />
        public string? Token
        {
            get
            {
                lock (_syncLock)
                {
                    return _token;
                }
            }
        }

        /// <inheritdoc />
        public DateTime? AuthenticationTime
        {
            get
            {
                lock (_syncLock)
                {
                    return _authenticationTime;
                }
            }
        }

        /// <inheritdoc />
        public ClaimsPrincipal? Principal
        {
            get
            {
                lock (_syncLock)
                {
                    return _principal;
                }
            }
        }

        /// <inheritdoc />
        public IDictionary<string, object> Properties => _properties;

        /// <inheritdoc />
        public string[]? Scopes
        {
            get
            {
                lock (_syncLock)
                {
                    return _scopes?.ToArray(); // 返回副本避免外部修改
                }
            }
        }

        /// <inheritdoc />
        public void SetClientAuthentication(string userId, string username, string? token = null, ClaimsPrincipal? principal = null)
        {
            if (string.IsNullOrEmpty(userId)) throw new ArgumentException("用户ID不能为空", nameof(userId));
            if (string.IsNullOrEmpty(username)) throw new ArgumentException("用户名不能为空", nameof(username));

            lock (_syncLock)
            {
                _type = AuthenticationType.Client;
                _identity = userId;
                _name = username;
                _token = token;
                _authenticationTime = DateTime.UtcNow;
                _principal = principal;
                _scopes = null; // 客户端认证不使用scopes
            }
        }

        /// <inheritdoc />
        public void SetServiceAuthentication(string serviceId, string serviceName, string token, string[]? scopes = null, ClaimsPrincipal? principal = null)
        {
            if (string.IsNullOrEmpty(serviceId)) throw new ArgumentException("服务ID不能为空", nameof(serviceId));
            if (string.IsNullOrEmpty(serviceName)) throw new ArgumentException("服务名不能为空", nameof(serviceName));
            if (string.IsNullOrEmpty(token)) throw new ArgumentException("令牌不能为空", nameof(token));

            lock (_syncLock)
            {
                _type = AuthenticationType.Service;
                _identity = serviceId;
                _name = serviceName;
                _token = token;
                _authenticationTime = DateTime.UtcNow;
                _principal = principal;
                _scopes = scopes?.ToArray(); // 创建副本
            }
        }

        /// <inheritdoc />
        public void Clear()
        {
            lock (_syncLock)
            {
                _type = AuthenticationType.None;
                _identity = null;
                _name = null;
                _token = null;
                _authenticationTime = null;
                _principal = null;
                _scopes = null;
                _properties.Clear();
            }
        }

        /// <inheritdoc />
        public bool HasScope(string scope)
        {
            if (string.IsNullOrEmpty(scope)) return false;

            lock (_syncLock)
            {
                if (_scopes == null) return false;
                return _scopes.Contains(scope, StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <inheritdoc />
        public bool IsInRole(string role)
        {
            if (string.IsNullOrEmpty(role)) return false;

            lock (_syncLock)
            {
                // 优先检查Claims
                if (_principal?.IsInRole(role) == true)
                {
                    return true;
                }

                // 检查Scopes（对于服务间认证）
                if (_type == AuthenticationType.Service)
                {
                    return HasScope($"role:{role}");
                }

                return false;
            }
        }

        /// <summary>
        /// 从现有ISessionContext迁移到新的认证上下文（兼容性方法）
        /// </summary>
        public static AuthenticationContext FromSessionContext(string connectionId, PulseRPC.ISessionContext sessionContext)
        {
            var authContext = new AuthenticationContext(connectionId);

            if (sessionContext.IsAuthenticated && sessionContext.UserId.HasValue && !string.IsNullOrEmpty(sessionContext.Username))
            {
                authContext.SetClientAuthentication(
                    sessionContext.UserId.Value.ToString(),
                    sessionContext.Username,
                    sessionContext.Token
                );

                // 复制属性
                foreach (var prop in sessionContext.Properties)
                {
                    authContext.Properties[prop.Key] = prop.Value;
                }
            }

            return authContext;
        }
    }
}
