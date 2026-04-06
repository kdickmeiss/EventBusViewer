using BusWorks.Options;
using Shouldly;
using Xunit;

namespace BusWorks.Tests.UnitTests.Options;

[Trait("Category", "Unit")]
public sealed class BusWorksOptionsTests
{
    [Fact]
    public void EventBusOptions_Defaults_AreCorrect()
    {
        var options = new BusWorksOptions();

        options.AuthenticationType.ShouldBe(EventBusAuthenticationType.ConnectionString);
        options.ConnectionString.ShouldBeNull();
        options.ManagedIdentity.ShouldBeNull();
        options.ApplicationRegistration.ShouldBeNull();
        options.AzureCli.ShouldBeNull();
        options.MaxConcurrentCalls.ShouldBe(10);
        options.MaxConcurrentSessions.ShouldBe(8);
        options.MaxConcurrentCallsPerSession.ShouldBe(1);
        options.SessionIdleTimeout.ShouldBeNull();
    }

    [Theory]
    [InlineData(EventBusAuthenticationType.ManagedIdentity)]
    [InlineData(EventBusAuthenticationType.ApplicationRegistration)]
    [InlineData(EventBusAuthenticationType.AzureCli)]
    public void AuthenticationType_CanBeConfigured(EventBusAuthenticationType authenticationType)
    {
        var options = new BusWorksOptions { AuthenticationType = authenticationType };

        options.AuthenticationType.ShouldBe(authenticationType);
    }

    [Fact]
    public void MaxConcurrentCalls_CanBeOverridden()
    {
        var options = new BusWorksOptions { MaxConcurrentCalls = 20 };

        options.MaxConcurrentCalls.ShouldBe(20);
    }

    [Fact]
    public void MaxConcurrentSessions_CanBeOverridden()
    {
        var options = new BusWorksOptions { MaxConcurrentSessions = 16 };

        options.MaxConcurrentSessions.ShouldBe(16);
    }

    [Fact]
    public void MaxConcurrentCallsPerSession_CanBeOverridden()
    {
        var options = new BusWorksOptions { MaxConcurrentCallsPerSession = 2 };

        options.MaxConcurrentCallsPerSession.ShouldBe(2);
    }

    [Theory]
    [InlineData(EventBusAuthenticationType.ConnectionString, 0)]
    [InlineData(EventBusAuthenticationType.ManagedIdentity, 1)]
    [InlineData(EventBusAuthenticationType.ApplicationRegistration, 2)]
    [InlineData(EventBusAuthenticationType.AzureCli, 3)]
    public void AuthenticationType_IntegerValue_IsStable(EventBusAuthenticationType authenticationType,
        int expectedValue)
    {
        int value = (int)authenticationType;

        value.ShouldBe(expectedValue);
    }

    [Fact]
    public void AuthenticationType_HasExactlyFourValues()
    {
        int count = Enum.GetValues<EventBusAuthenticationType>().Length;

        count.ShouldBe(4);
    }

    [Fact]
    public void ConnectionStringOptions_Default_ConnectionString_IsEmpty()
    {
        var options = new ConnectionStringOptions();

        options.ConnectionString.ShouldBe(string.Empty);
    }

    [Fact]
    public void ConnectionStringOptions_ConnectionString_CanBeSet()
    {
        const string value =
            "Endpoint=sb://my-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc123";
        var options = new ConnectionStringOptions { ConnectionString = value };

        options.ConnectionString.ShouldBe(value);
    }

    [Fact]
    public void ManagedIdentityOptions_Defaults_AreCorrect()
    {
        var options = new ManagedIdentityOptions();

        options.FullyQualifiedNamespace.ShouldBe(string.Empty);
        options.ClientId.ShouldBeNull();
    }

    [Fact]
    public void ManagedIdentityOptions_AllValuesPreserved()
    {
        var options = new ManagedIdentityOptions
        {
            FullyQualifiedNamespace = "my-namespace.servicebus.windows.net",
            ClientId = "00000000-0000-0000-0000-000000000001"
        };

        options.FullyQualifiedNamespace.ShouldBe("my-namespace.servicebus.windows.net");
        options.ClientId.ShouldBe("00000000-0000-0000-0000-000000000001");
    }

    [Fact]
    public void ApplicationRegistrationOptions_Defaults_AreCorrect()
    {
        var options = new ApplicationRegistrationOptions();

        options.FullyQualifiedNamespace.ShouldBe(string.Empty);
        options.TenantId.ShouldBe(string.Empty);
        options.ClientId.ShouldBe(string.Empty);
        options.ClientSecret.ShouldBe(string.Empty);
    }

    [Fact]
    public void ApplicationRegistrationOptions_AllValuesPreserved()
    {
        var options = new ApplicationRegistrationOptions
        {
            FullyQualifiedNamespace = "my-namespace.servicebus.windows.net",
            TenantId = "00000000-0000-0000-0000-000000000001",
            ClientId = "00000000-0000-0000-0000-000000000002",
            ClientSecret = "super-secret"
        };

        options.FullyQualifiedNamespace.ShouldBe("my-namespace.servicebus.windows.net");
        options.TenantId.ShouldBe("00000000-0000-0000-0000-000000000001");
        options.ClientId.ShouldBe("00000000-0000-0000-0000-000000000002");
        options.ClientSecret.ShouldBe("super-secret");
    }

    [Fact]
    public void AzureCliOptions_Default_FullyQualifiedNamespace_IsEmpty()
    {
        var options = new AzureCliOptions();

        options.FullyQualifiedNamespace.ShouldBe(string.Empty);
    }

    [Fact]
    public void AzureCliOptions_FullyQualifiedNamespace_CanBeSet()
    {
        var options = new AzureCliOptions { FullyQualifiedNamespace = "my-namespace.servicebus.windows.net" };

        options.FullyQualifiedNamespace.ShouldBe("my-namespace.servicebus.windows.net");
    }
}
