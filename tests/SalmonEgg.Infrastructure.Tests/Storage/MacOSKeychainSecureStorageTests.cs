using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public sealed class MacOSKeychainSecureStorageTests
{
    private const int ErrSecSuccess = 0;
    private const int ErrSecDuplicateItem = -25299;
    private const int ErrSecItemNotFound = -25300;

    [Fact]
    public async Task SaveAsync_StoresSecretAsKeychainData()
    {
        var keychain = new RecordingKeychainApi();
        var storage = new MacOSKeychainSecureStorage(keychain);

        await storage.SaveAsync("salmonegg/config/profile/token", "secret-token");

        var call = Assert.Single(keychain.AddCalls);
        Assert.Equal("SalmonEgg", call.ServiceName);
        Assert.NotEqual("salmonegg/config/profile/token", call.AccountName);
        Assert.Equal("secret-token", Encoding.UTF8.GetString(call.Password));
    }

    [Fact]
    public async Task SaveAsync_WhenItemExists_UpdatesExistingPassword()
    {
        var keychain = new RecordingKeychainApi();
        var storage = new MacOSKeychainSecureStorage(keychain);

        await storage.SaveAsync("salmonegg/config/profile/token", "first-token");
        await storage.SaveAsync("salmonegg/config/profile/token", "second-token");

        var update = Assert.Single(keychain.UpdateCalls);
        Assert.Equal("second-token", Encoding.UTF8.GetString(update.Password));
        Assert.Equal("second-token", await storage.LoadAsync("salmonegg/config/profile/token"));
    }

    [Fact]
    public async Task LoadAsync_WhenItemMissing_ReturnsNull()
    {
        var keychain = new RecordingKeychainApi();
        var storage = new MacOSKeychainSecureStorage(keychain);

        var value = await storage.LoadAsync("salmonegg/config/profile/token");

        Assert.Null(value);
    }

    [Fact]
    public async Task DeleteAsync_WhenItemExists_RemovesKeychainItem()
    {
        var keychain = new RecordingKeychainApi();
        var storage = new MacOSKeychainSecureStorage(keychain);
        await storage.SaveAsync("salmonegg/config/profile/token", "secret-token");

        await storage.DeleteAsync("salmonegg/config/profile/token");

        Assert.Null(await storage.LoadAsync("salmonegg/config/profile/token"));
        Assert.Single(keychain.DeleteCalls);
    }

    [Fact]
    public async Task SaveAsync_WhenKeychainFails_ThrowsSecureStorageUnavailableException()
    {
        var keychain = new RecordingKeychainApi { AddStatus = -25291 };
        var storage = new MacOSKeychainSecureStorage(keychain);

        var ex = await Assert.ThrowsAsync<SecureStorageUnavailableException>(
            () => storage.SaveAsync("salmonegg/config/profile/token", "secret-token"));

        Assert.Contains("macOS Keychain", ex.Message, StringComparison.Ordinal);
        Assert.Contains("-25291", ex.Message, StringComparison.Ordinal);
    }

    private sealed class RecordingKeychainApi : MacOSKeychainSecureStorage.IKeychainApi
    {
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private int _nextItemId = 1;

        public int AddStatus { get; set; } = ErrSecSuccess;

        public List<AddCall> AddCalls { get; } = new();

        public List<UpdateCall> UpdateCalls { get; } = new();

        public List<IntPtr> DeleteCalls { get; } = new();

        public int AddGenericPassword(string serviceName, string accountName, byte[] password, out IntPtr itemRef)
        {
            AddCalls.Add(new AddCall(serviceName, accountName, password));
            itemRef = IntPtr.Zero;
            if (AddStatus != ErrSecSuccess)
            {
                return AddStatus;
            }

            if (_entries.ContainsKey(accountName))
            {
                return ErrSecDuplicateItem;
            }

            itemRef = new IntPtr(_nextItemId++);
            _entries[accountName] = new Entry(itemRef, password);
            return ErrSecSuccess;
        }

        public int FindGenericPassword(string serviceName, string accountName, out byte[] password, out IntPtr itemRef)
        {
            if (!_entries.TryGetValue(accountName, out var entry))
            {
                password = Array.Empty<byte>();
                itemRef = IntPtr.Zero;
                return ErrSecItemNotFound;
            }

            password = entry.Password;
            itemRef = entry.ItemRef;
            return ErrSecSuccess;
        }

        public int UpdatePassword(IntPtr itemRef, byte[] password)
        {
            UpdateCalls.Add(new UpdateCall(itemRef, password));
            foreach (var pair in _entries)
            {
                if (pair.Value.ItemRef == itemRef)
                {
                    _entries[pair.Key] = pair.Value with { Password = password };
                    return ErrSecSuccess;
                }
            }

            return ErrSecItemNotFound;
        }

        public int DeleteItem(IntPtr itemRef)
        {
            DeleteCalls.Add(itemRef);
            foreach (var pair in _entries)
            {
                if (pair.Value.ItemRef == itemRef)
                {
                    _entries.Remove(pair.Key);
                    return ErrSecSuccess;
                }
            }

            return ErrSecItemNotFound;
        }

        public void ReleaseItem(IntPtr itemRef)
        {
        }
    }

    private readonly record struct AddCall(string ServiceName, string AccountName, byte[] Password);

    private readonly record struct UpdateCall(IntPtr ItemRef, byte[] Password);

    private readonly record struct Entry(IntPtr ItemRef, byte[] Password);
}
