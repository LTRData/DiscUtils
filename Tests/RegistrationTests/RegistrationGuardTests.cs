using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using DiscUtils;
using DiscUtils.Setup;
using RegistrationPlugin;
using Xunit;

namespace RegistrationTests;

public sealed class RegistrationGuardTests
{
    [Fact]
    public void RepeatedSuccessfulExplicitRegistrationRunsCallbackOnce()
    {
        var assembly = NewAssembly();
        var calls = 0;
        var type = assembly.GetName().Name!;
        Action register = () =>
        {
            calls++;
            VirtualDiskManager.RegisterVirtualDiskFactory(type, new[] { type }, new TestDiskFactory());
        };
        Parallel.For(0, 8, _ => SetupHelper.RegisterAssembly(assembly, register));
        Assert.Equal(1, calls);
        Assert.Equal(new[] { "test" }, VirtualDisk.GetSupportedDiskVariants(type));
    }

    [Fact]
    public void RecursiveRegistrationFromCallbackIsIgnoredAndOuterCallbackCompletes()
    {
        var assembly = NewAssembly();
        var completed = false;
        Action unexpected = () => throw new InvalidOperationException("Assembly callback ran again.");
        SetupHelper.RegisterAssembly(assembly, () =>
        {
            SetupHelper.RegisterAssembly(assembly, unexpected);
            SetupHelper.RegisterAssembly(assembly);
            completed = true;
        });
        SetupHelper.RegisterAssembly(assembly, unexpected);
        Assert.True(completed);
    }

    [Fact]
    public void FailedCallbackLeavesPartialRegistrationAndSubsequentAttemptsDoNotRetry()
    {
        var assembly = NewAssembly();
        var type = assembly.GetName().Name!;
        var calls = 0;
        var failure = new InvalidOperationException("Failure after registering the first provider.");
        Action register = () =>
        {
            calls++;
            VirtualDiskManager.RegisterVirtualDiskFactory(type, new[] { type }, new TestDiskFactory());
            throw failure;
        };
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => SetupHelper.RegisterAssembly(assembly, register)));
        Assert.Equal(new[] { "test" }, VirtualDisk.GetSupportedDiskVariants(type));
        SetupHelper.RegisterAssembly(assembly, register);
        SetupHelper.RegisterAssembly(assembly);
        SetupHelper.RegisterAssembly(assembly,
            () => VirtualDiskManager.RegisterVirtualDiskFactory(type + "Later", new[] { type + "Later" }, new TestDiskFactory()));
        Assert.Equal(1, calls);
        Assert.DoesNotContain(type + "Later", VirtualDiskManager.SupportedDiskTypes);
        Assert.Contains(type, VirtualDiskManager.SupportedDiskFormats);
    }

    // Each scenario needs a fresh assembly identity because successful and failed registrations persist.
    private static Assembly NewAssembly() => AssemblyBuilder.DefineDynamicAssembly(
        new AssemblyName("RegistrationGuard_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
}
