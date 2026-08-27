using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac.Features.Indexed;
using StackExchange.Redis;
using Viv.Apex.Core.Entity.Dto.Account;
using Viv.Apex.Core.Entity.Vo.Account;
using Viv.Apex.Core.Interface;
using Viv.Apex.Core.IRepository;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Contracts.Options;
using Viv.Delusion;
using Viv.Delusion.Generic;
using Viv.Elysia.Interface;
using Viv.Entity.Database.Apex;
using Viv.Entity.Enums;
using Viv.Redis;

namespace Viv.Apex.Tests
{
    /// <summary>可预设 GetByPhoneAsync/GetAsync 返回值的用户仓储替身。</summary>
    public sealed class StubUserRepository : IUserRepository
    {
        public AtUser? UserByPhone { get; set; }

        public AtUser? UserById { get; set; }

        public string? LastPhone { get; private set; }

        public EmUserType LastUserType { get; private set; }

        public Task<bool> AddAsync(AtUser user) => throw new NotImplementedException();

        public Task<bool> UpdateAsync(AtUser user) => throw new NotImplementedException();

        public Task<bool> DeleteAsync(long userId) => throw new NotImplementedException();

        public Task<bool> SoftDeleteAsync(long userId) => throw new NotImplementedException();

        public Task<AtUser?> GetAsync(long userId)
        {
            LastPhone = null;
            return Task.FromResult(UserById);
        }

        public Task<AtUser?> GetByPhoneAsync(string phone, EmUserType userType)
        {
            LastPhone = phone;
            LastUserType = userType;
            return Task.FromResult(UserByPhone);
        }

        public Task<PagedList<AtUser>> GetPagedListAsync(IApiPagedRequest request) => throw new NotImplementedException();
    }

    /// <summary>令牌服务替身——记录 GenerateToken 调用，可预设返回值。</summary>
    public sealed class StubTokenService : ITokenService
    {
        public string GeneratedToken { get; set; } = "access-token";

        public TokenOptions Options { get; set; } = new TokenOptions { ExpireMinutes = 30 };

        public int GenerateCount { get; private set; }

        public TokenPayload? LastPayload { get; private set; }

        public TokenOptions GetOptions() => Options;

        public string GenerateToken(TokenPayload payload)
        {
            GenerateCount++;
            LastPayload = payload;
            return GeneratedToken;
        }

        public bool ValidateToken(string token) => true;

        public TokenPayload? ParseToken(string token) => null;
    }

    /// <summary>Redis 服务替身——仅记录 GetAsync/AddAsync 调用，其余成员未实现。</summary>
    public sealed class StubRedisService : IRedisService
    {
        public object? Session { get; set; }

        public List<(string Key, object Value)> Added { get; } = new();

        public TimeSpan? LastExpire { get; private set; }

        public Task<T> GetAsync<T>(string key) => Task.FromResult((T)Session!);

        public Task<bool> AddAsync(string key, object value, TimeSpan expire)
        {
            Added.Add((key, value));
            LastExpire = expire;
            return Task.FromResult(true);
        }

        // ── 未使用的接口成员（同步版，全部未实现） ──────────
        public bool Add(string key, object value, TimeSpan expire) => throw new NotImplementedException();
        public bool Add(string key, object value, int seconds = 600) => throw new NotImplementedException();
        public bool DelayExpire(string key, TimeSpan delayTime) => throw new NotImplementedException();
        public bool Exist(string key) => throw new NotImplementedException();
        public object Get(string key) => throw new NotImplementedException();
        public T Get<T>(string key) => throw new NotImplementedException();
        public long Publish(RedisChannel channel, object message) => throw new NotImplementedException();
        public long Remove(List<string> keyList) => throw new NotImplementedException();
        public bool Remove(string key) => throw new NotImplementedException();
        public bool SetKeyExpire(string key, TimeSpan expire) => throw new NotImplementedException();
        public bool HashSet(string key, string field, object value) => throw new NotImplementedException();
        public T HashGet<T>(string key, string field) => throw new NotImplementedException();
        public bool HashExist(string key, string field) => throw new NotImplementedException();
        public long HashRemove(string key, params string[] fields) => throw new NotImplementedException();
        public Dictionary<string, T> HashGetAll<T>(string key) => throw new NotImplementedException();
        public long ListPush(string key, object value) => throw new NotImplementedException();
        public object ListPop(string key) => throw new NotImplementedException();
        public List<T> ListRange<T>(string key, long start = 0, long stop = -1) => throw new NotImplementedException();
        public long ListRemove(string key, object value, long count = 0) => throw new NotImplementedException();
        public bool AcquireLock(string lockKey, TimeSpan expire, string? lockHolderId = null, bool isReentrant = true) => throw new NotImplementedException();
        public bool ReleaseLock(string lockKey, string? lockHolderId = null, bool enableReentrant = true) => throw new NotImplementedException();
        public bool ForceReleaseLock(string lockKey) => throw new NotImplementedException();

        // ── 未使用的接口成员（异步版，全部未实现） ──────────
        public Task<bool> AddAsync(string key, object value, int seconds = 600) => throw new NotImplementedException();
        public Task<bool> DelayExpireAsync(string key, TimeSpan delayTime) => throw new NotImplementedException();
        public Task<bool> ExistAsync(string key) => throw new NotImplementedException();
        public Task<object> GetAsync(string key) => throw new NotImplementedException();
        public Task<long> PublishAsync(RedisChannel channel, object message) => throw new NotImplementedException();
        public Task<long> RemoveAsync(List<string> keyList) => throw new NotImplementedException();
        public Task<bool> RemoveAsync(string key) => throw new NotImplementedException();
        public Task<bool> SetKeyExpireAsync(string key, TimeSpan expire) => throw new NotImplementedException();
        public void Subscribe<T>(RedisChannel channel, Action<T> action) => throw new NotImplementedException();
        public Task SubscribeAsync<T>(RedisChannel channel, Action<T> action) => throw new NotImplementedException();
        public void Unsubscribe(RedisChannel channel) => throw new NotImplementedException();
        public void UnsubscribeAll() => throw new NotImplementedException();
        public Task UnsubscribeAllAsync() => throw new NotImplementedException();
        public Task UnsubscribeAsync(RedisChannel channel) => throw new NotImplementedException();
        public Task<bool> HashSetAsync(string key, string field, object value) => throw new NotImplementedException();
        public Task<T> HashGetAsync<T>(string key, string field) => throw new NotImplementedException();
        public Task<bool> HashExistAsync(string key, string field) => throw new NotImplementedException();
        public Task<long> HashRemoveAsync(string key, params string[] fields) => throw new NotImplementedException();
        public Task<Dictionary<string, T>> HashGetAllAsync<T>(string key) => throw new NotImplementedException();
        public Task<long> ListPushAsync(string key, object value) => throw new NotImplementedException();
        public Task<object> ListPopAsync(string key) => throw new NotImplementedException();
        public Task<List<T>> ListRangeAsync<T>(string key, long start = 0, long stop = -1) => throw new NotImplementedException();
        public Task<long> ListRemoveAsync(string key, object value, long count = 0) => throw new NotImplementedException();
        public Task<bool> AcquireLockAsync(string lockKey, TimeSpan expire, string? lockHolderId = null, bool isReentrant = true) => throw new NotImplementedException();
        public Task<bool> ReleaseLockAsync(string lockKey, string? lockHolderId = null, bool isReentrant = true) => throw new NotImplementedException();
        public Task<bool> ForceReleaseLockAsync(string lockKey) => throw new NotImplementedException();
    }

    /// <summary>登录实现替身——可预设 LoginAsync 返回值。</summary>
    public sealed class StubLoginContract : ILoginContract
    {
        public FuncResult<LoginOutput>? LoginResult { get; set; }

        public Task<FuncResult<LoginOutput>> LoginAsync(LoginRequest request)
            => Task.FromResult(LoginResult ?? FuncResult<LoginOutput>.Failed("未设置登录结果"));

        public Task<FuncResult<LoginOutput>> RefreshTokenAsync(RefreshRequest request)
            => throw new NotImplementedException();

        public Task<bool> LogoutAsync(LoginoutRequest request)
            => throw new NotImplementedException();
    }

    /// <summary>Autofac IIndex 替身——按字典提供登录实现。</summary>
    public sealed class FakeLoginIndex : IIndex<EmUserType, ILoginContract>
    {
        private readonly Dictionary<EmUserType, ILoginContract> _map;

        public FakeLoginIndex(Dictionary<EmUserType, ILoginContract> map) => _map = map;

        public ILoginContract this[EmUserType key] => _map[key];

        public bool TryGetValue(EmUserType key, out ILoginContract value)
        {
            if (_map.TryGetValue(key, out var v))
            {
                value = v;
                return true;
            }

            value = null!;
            return false;
        }
    }
}
