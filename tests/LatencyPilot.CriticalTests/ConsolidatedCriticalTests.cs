using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
internal sealed class AuditCaseAttribute : Attribute;

[TestClass]
public sealed class ConsolidatedCriticalTests
{
    [TestMethod]
    public async Task RunAllCriticalContractsWithinPermanentTestBudget()
    {
        var failures = new List<string>();
        var cases = typeof(ConsolidatedCriticalTests).Assembly.GetTypes()
            .Where(static type => type.Namespace == "LatencyPilot.CriticalTests")
            .SelectMany(static type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(static method => method.GetCustomAttribute<AuditCaseAttribute>() is not null)
                .Select(method => (Type: type, Method: method)))
            .OrderBy(static item => item.Type.FullName, StringComparer.Ordinal)
            .ThenBy(static item => item.Method.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.IsGreaterThan(0, cases.Length, "Consolidated test suite discovered no audit cases.");
        foreach (var item in cases)
        {
            try
            {
                if (item.Method.GetParameters().Length != 0)
                    throw new InvalidOperationException("AuditCase methods must be parameterless after consolidation.");
                var instance = item.Method.IsStatic ? null : Activator.CreateInstance(item.Type);
                var returned = item.Method.Invoke(instance, null);
                switch (returned)
                {
                    case Task task:
                        await task.ConfigureAwait(false);
                        break;
                    case ValueTask valueTask:
                        await valueTask.ConfigureAwait(false);
                        break;
                    case null:
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported audit-case return type {returned.GetType().FullName}.");
                }
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                failures.Add($"{item.Type.Name}.{item.Method.Name}: {exception.InnerException.GetType().Name}: {exception.InnerException.Message}");
            }
            catch (Exception exception)
            {
                failures.Add($"{item.Type.Name}.{item.Method.Name}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        if (failures.Count > 0)
            Assert.Fail("Critical contract failures:\n" + string.Join("\n", failures));
    }
}
