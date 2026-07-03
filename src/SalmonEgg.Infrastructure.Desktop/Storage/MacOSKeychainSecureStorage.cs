using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class MacOSKeychainSecureStorage : ISecureStorage
{
    private const int ErrSecSuccess = 0;
    private const int ErrSecDuplicateItem = -25299;
    private const int ErrSecItemNotFound = -25300;
    private const string ServiceName = "SalmonEgg";

    private readonly IKeychainApi _keychain;

    public MacOSKeychainSecureStorage()
        : this(new NativeKeychainApi())
    {
    }

    internal MacOSKeychainSecureStorage(IKeychainApi keychain)
    {
        _keychain = keychain ?? throw new ArgumentNullException(nameof(keychain));
    }

    public Task SaveAsync(string key, string value)
    {
        ValidateKey(key);
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var account = GetKeyHash(key);
        var password = Encoding.UTF8.GetBytes(value);
        var status = _keychain.AddGenericPassword(ServiceName, account, password, out var itemRef);
        _keychain.ReleaseItem(itemRef);

        if (status == ErrSecDuplicateItem)
        {
            status = _keychain.FindGenericPassword(ServiceName, account, out _, out itemRef);
            if (status == ErrSecSuccess)
            {
                try
                {
                    status = _keychain.UpdatePassword(itemRef, password);
                }
                finally
                {
                    _keychain.ReleaseItem(itemRef);
                }
            }
        }

        ThrowIfFailed(status, "save");
        return Task.CompletedTask;
    }

    public Task<string?> LoadAsync(string key)
    {
        ValidateKey(key);

        var account = GetKeyHash(key);
        var status = _keychain.FindGenericPassword(ServiceName, account, out var password, out var itemRef);
        _keychain.ReleaseItem(itemRef);

        if (status == ErrSecItemNotFound)
        {
            return Task.FromResult<string?>(null);
        }

        ThrowIfFailed(status, "load");
        return Task.FromResult<string?>(Encoding.UTF8.GetString(password));
    }

    public Task DeleteAsync(string key)
    {
        ValidateKey(key);

        var account = GetKeyHash(key);
        var status = _keychain.FindGenericPassword(ServiceName, account, out _, out var itemRef);
        if (status == ErrSecItemNotFound)
        {
            return Task.CompletedTask;
        }

        if (status == ErrSecSuccess)
        {
            try
            {
                status = _keychain.DeleteItem(itemRef);
            }
            finally
            {
                    _keychain.ReleaseItem(itemRef);
            }
        }
        else
        {
            _keychain.ReleaseItem(itemRef);
        }

        ThrowIfFailed(status, "delete");
        return Task.CompletedTask;
    }

    private static string GetKeyHash(string key)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void ThrowIfFailed(int status, string operation)
    {
        if (status == ErrSecSuccess)
        {
            return;
        }

        throw new SecureStorageUnavailableException(
            $"macOS Keychain failed to {operation} SalmonEgg credentials. OSStatus={status}.");
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentNullException(nameof(key));
        }
    }

    internal interface IKeychainApi
    {
        int AddGenericPassword(string serviceName, string accountName, byte[] password, out IntPtr itemRef);

        int FindGenericPassword(string serviceName, string accountName, out byte[] password, out IntPtr itemRef);

        int UpdatePassword(IntPtr itemRef, byte[] password);

        int DeleteItem(IntPtr itemRef);

        void ReleaseItem(IntPtr itemRef);
    }

    internal sealed class NativeKeychainApi : IKeychainApi
    {
        public int AddGenericPassword(string serviceName, string accountName, byte[] password, out IntPtr itemRef)
        {
            var service = Encoding.UTF8.GetBytes(serviceName);
            var account = Encoding.UTF8.GetBytes(accountName);
            return SecKeychainAddGenericPassword(
                IntPtr.Zero,
                (uint)service.Length,
                service,
                (uint)account.Length,
                account,
                (uint)password.Length,
                password,
                out itemRef);
        }

        public int FindGenericPassword(string serviceName, string accountName, out byte[] password, out IntPtr itemRef)
        {
            var service = Encoding.UTF8.GetBytes(serviceName);
            var account = Encoding.UTF8.GetBytes(accountName);
            var status = SecKeychainFindGenericPassword(
                IntPtr.Zero,
                (uint)service.Length,
                service,
                (uint)account.Length,
                account,
                out var passwordLength,
                out var passwordData,
                out itemRef);

            if (status != ErrSecSuccess)
            {
                password = Array.Empty<byte>();
                return status;
            }

            password = new byte[passwordLength];
            if (passwordLength > 0)
            {
                Marshal.Copy(passwordData, password, 0, checked((int)passwordLength));
            }

            SecKeychainItemFreeContent(IntPtr.Zero, passwordData);
            return status;
        }

        public int UpdatePassword(IntPtr itemRef, byte[] password)
            => SecKeychainItemModifyAttributesAndData(
                itemRef,
                IntPtr.Zero,
                (uint)password.Length,
                password);

        public int DeleteItem(IntPtr itemRef)
            => SecKeychainItemDelete(itemRef);

        public void ReleaseItem(IntPtr itemRef)
        {
            if (itemRef != IntPtr.Zero)
            {
                CFRelease(itemRef);
            }
        }

        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        private static extern int SecKeychainAddGenericPassword(
            IntPtr keychain,
            uint serviceNameLength,
            byte[] serviceName,
            uint accountNameLength,
            byte[] accountName,
            uint passwordLength,
            byte[] passwordData,
            out IntPtr itemRef);

        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        private static extern int SecKeychainFindGenericPassword(
            IntPtr keychain,
            uint serviceNameLength,
            byte[] serviceName,
            uint accountNameLength,
            byte[] accountName,
            out uint passwordLength,
            out IntPtr passwordData,
            out IntPtr itemRef);

        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        private static extern int SecKeychainItemModifyAttributesAndData(
            IntPtr itemRef,
            IntPtr attrList,
            uint length,
            byte[] data);

        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        private static extern int SecKeychainItemDelete(IntPtr itemRef);

        [DllImport("/System/Library/Frameworks/Security.framework/Security")]
        private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern void CFRelease(IntPtr cf);
    }
}
