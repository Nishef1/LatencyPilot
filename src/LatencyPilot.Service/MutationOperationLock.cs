using System.Security.AccessControl;
using System.Security.Principal;

namespace LatencyPilot.Service;

/// <summary>
/// Serializes machine mutation, recovery and retained-restore operations across
/// LatencyPilot processes. A Windows mutex is released by the kernel if its owner
/// process/thread dies; SQLite CAS remains the second concurrency layer.
/// Newly created lock objects use an explicit DACL granting access only to SYSTEM
/// and Administrators. Existing objects are reopened with ACL-read rights and
/// rejected unless that same trust boundary is still present.
/// </summary>
internal sealed class MutationOperationLock : IDisposable
{
    private const string MutexName = @"Global\LatencyPilot.MutationOperation.v1";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly SecurityIdentifier LocalSystemSid =
        new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier AdministratorsSid =
        new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private const MutexRights RequiredTrustedRights =
        MutexRights.Synchronize |
        MutexRights.Modify |
        MutexRights.ReadPermissions;

    private readonly Mutex mutex;
    private bool ownsMutex;
    private bool disposed;

    private MutationOperationLock(Mutex mutex)
    {
        this.mutex = mutex;
        ownsMutex = true;
    }

    internal static MutationOperationLock Acquire() => Acquire(DefaultTimeout);

    internal static MutationOperationLock Acquire(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var mutex = CreateMutex();
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(timeout);
            }
            catch (AbandonedMutexException)
            {
                // The previous owner died. Windows transfers ownership to this
                // thread; actual journal + device state is still re-read by the
                // mutation/recovery caller before any subsequent write.
                acquired = true;
            }

            if (!acquired)
            {
                throw new TimeoutException(
                    "Another LatencyPilot mutation/recovery operation owns the machine mutation lock. Retry only after that operation completes or recovery inspects its journal state.");
            }

            return new MutationOperationLock(mutex);
        }
        catch
        {
            if (!acquired)
            {
                mutex.Dispose();
            }
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (ownsMutex)
        {
            ownsMutex = false;
            mutex.ReleaseMutex();
        }
        mutex.Dispose();
    }

    private static Mutex CreateMutex()
    {
        try
        {
            var expectedSecurity = CreateExpectedSecurity();
            var initialHandle = MutexAcl.Create(
                initiallyOwned: false,
                MutexName,
                out var createdNew,
                expectedSecurity);

            if (createdNew)
            {
                try
                {
                    EnsureExpectedAccessControl(initialHandle);
                    return initialHandle;
                }
                catch
                {
                    initialHandle.Dispose();
                    throw;
                }
            }

            // Keep the first handle alive while requesting a handle that can
            // inspect the existing object's DACL. This avoids a destroy/recreate
            // race if the other process releases its last handle concurrently.
            try
            {
                var verifiedHandle = MutexAcl.OpenExisting(
                    MutexName,
                    MutexRights.Synchronize |
                    MutexRights.Modify |
                    MutexRights.ReadPermissions);
                try
                {
                    EnsureExpectedAccessControl(verifiedHandle);
                    return verifiedHandle;
                }
                catch
                {
                    verifiedHandle.Dispose();
                    throw;
                }
            }
            finally
            {
                initialHandle.Dispose();
            }
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException(
                "The global mutation lock exists but LatencyPilot cannot open and verify its restricted ACL. " +
                "Mutation and recovery stay fail-closed until the conflicting object is gone or its permissions are restored.",
                exception);
        }
    }

    private static MutexSecurity CreateExpectedSecurity()
    {
        var security = new MutexSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new MutexAccessRule(
            LocalSystemSid,
            MutexRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new MutexAccessRule(
            AdministratorsSid,
            MutexRights.FullControl,
            AccessControlType.Allow));
        return security;
    }

    private static void EnsureExpectedAccessControl(Mutex mutex)
    {
        var rules = mutex
            .GetAccessControl()
            .GetAccessRules(
                includeExplicit: true,
                includeInherited: true,
                targetType: typeof(SecurityIdentifier))
            .OfType<MutexAccessRule>()
            .ToArray();

        if (rules.Length == 0 ||
            rules.Any(static rule =>
                rule.IsInherited ||
                rule.AccessControlType != AccessControlType.Allow))
        {
            throw new InvalidOperationException(
                "The global mutation lock ACL is not the restricted LatencyPilot ACL; mutation and recovery remain fail-closed.");
        }

        foreach (var rule in rules)
        {
            if (rule.IdentityReference is not SecurityIdentifier sid ||
                (!sid.Equals(LocalSystemSid) && !sid.Equals(AdministratorsSid)))
            {
                throw new InvalidOperationException(
                    "The global mutation lock grants access outside SYSTEM/Administrators; mutation and recovery remain fail-closed.");
            }
        }

        if (!HasRequiredRights(rules, LocalSystemSid) ||
            !HasRequiredRights(rules, AdministratorsSid))
        {
            throw new InvalidOperationException(
                "The global mutation lock does not grant the required synchronization and ACL-read rights to SYSTEM and Administrators; mutation and recovery remain fail-closed.");
        }
    }

    private static bool HasRequiredRights(
        IReadOnlyList<MutexAccessRule> rules,
        SecurityIdentifier identity)
    {
        var rights = rules
            .Where(rule =>
                rule.AccessControlType == AccessControlType.Allow &&
                rule.IdentityReference is SecurityIdentifier sid &&
                sid.Equals(identity))
            .Aggregate(
                (MutexRights)0,
                static (current, rule) => current | rule.MutexRights);
        return (rights & RequiredTrustedRights) == RequiredTrustedRights;
    }
}
