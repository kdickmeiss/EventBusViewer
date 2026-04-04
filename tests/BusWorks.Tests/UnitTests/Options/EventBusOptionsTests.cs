using BusWorks.Options;
using Xunit;

namespace BusWorks.Tests.UnitTests.Options;

public class BusWorksOptionsTests
{
    [Fact]
    public void EventBusOptions_Defaults_AreCorrect()
    {
        var options = new BusWorksOptions();

        Assert.Equal(EventBusAuthenticationType.ConnectionString, options.AuthenticationType);
        Assert.Null(options.ConnectionString);
        Assert.Null(options.ManagedIdentity);
        Assert.Null(options.ApplicationRegistration);
        Assert.Null(options.AzureCli);
        Assert.Equal(10, options.MaxConcurrentCalls);
        Assert.Equal(8, options.MaxConcurrentSessions);
        Assert.Equal(1, options.MaxConcurrentCallsPerSession);
    }

    [Theory]
    [InlineData(EventBusAuthenticationType.ManagedIdentity)]
    [InlineData(EventBusAuthenticationType.ApplicationRegistration)]
    [InlineData(EventBusAuthenticationType.AzureCli)]
    public void AuthenticationType_CanBeConfigured(EventBusAuthenticationType authenticationType)
    {
        var options = new BusWorksOptions { AuthenticationType = authenticationType };

        Assert.Equal(authenticationType, options.AuthenticationType);
    }

    [Fact]
    public void MaxConcurrentCalls_CanBeOverridden()
    {
        var options = new BusWorksOptions { MaxConcurrentCalls = 20 };

        Assert.Equal(20, options.MaxConcurrentCalls);
    }

    [Fact]
    public void MaxConcurrentSessions_CanBeOverridden()
    {
        var options = new BusWorksOptions { MaxConcurrentSessions = 16 };

        Assert.Equal(16, options.MaxConcurrentSessions);
    }

    [Fact]
    public void MaxConcurrentCallsPerSession_CanBeOverridden()
    {
        var options = new BusWorksOptions { MaxConcurrentCallsPerSession = 2 };

        Assert.Equal(2, options.MaxConcurrentCallsPerSession);
    }

    [Theory]
    [InlineData(EventBusAuthenticationType.ConnectionString, 0)]
    [InlineData(EventBusAuthenticationType.ManagedIdentity, 1)]
    [InlineData(EventBusAuthenticationType.ApplicationRegistration, 2)]
    [InlineData(EventBusAuthenticationType.AzureCli, 3)]
    public void AuthenticationType_IntegerValue_IsStable(EventBusAuthenticationType authenticationType, int expectedValue)
    {
        int value = (int)authenticationType;

        Assert.Equal(expectedValue, value);
    }

    [Fact]
    public void AuthenticationType_HasExactlyFourValues()
    {
        int count = Enum.GetValues<EventBusAuthenticationType>().Length;

        Assert.Equal(4, count);
    }

    [Fact]
    public void ConnectionStringOptions_Default_ConnectionString_IsEmpty()
    {
        var options = new ConnectionStringOptions();

        Assert.Equal(string.Empty, options.ConnectionString);
    }

    [Fact]
    public void ConnectionStringOptions_ConnectionString_CanBeSet()
    {
        const string value = "Endpoint=sb://my-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc123";

        var options = new ConnectionStringOptions { ConnectionString = value };

        Assert.Equal(value, options.ConnectionString);
    }

    [Fact]
    public void ManagedIdentityOptions_Defaults_AreCorrect()
    {
        var options = new ManagedIdentityOptions();

        Assert.Equal(string.Empty, options.FullyQualifiedNamespace);
        Assert.Null(options.ClientId);
    }

    [Fact]
    public void ManagedIdentityOptions_AllValuesPreserved()
    {
        var options = new ManagedIdentityOptions
        {
            FullyQualifiedNamespace = "my-namespace.servicebus.windows.net",
            ClientId = "00000000-0000-0000-0000-000000000001"
        };

        Assert.Equal("my-namespace.servicebus.windows.net", options.FullyQualifiedNamespace);
        Assert.Equal("00000000-0000-0000-0000-000000000001", options.ClientId);
    }

    [Fact]
    public void ApplicationRegistrationOptions_Defaults_AreCorrect()
    {
        var options = new ApplicationRegistrationOptions();

        Assert.Equal(string.Empty, options.FullyQualifiedNamespace);
        Assert.Equal(string.Empty, options.TenantId);
        Assert.Equal(string.Empty, options.ClientId);
        Assert.Equal(string.Empty, options.ClientSecret);
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

        Assert.Equal("my-namespace.servicebus.windows.net", options.FullyQualifiedNamespace);
        Assert.Equal("00000000-0000-0000-0000-000000000001", options.TenantId);
        Assert.Equal("00000000-0000-0000-0000-000000000002", options.ClientId);
        Assert.Equal("super-secret", options.ClientSecret);
    }

    [Fact]
    public void AzureCliOptions_Default_FullyQualifiedNamespace_IsEmpty()
    {
        var options = new AzureCliOptions();

        Assert.Equal(string.Empty, options.FullyQualifiedNamespace);
    }

    [Fact]
    public void AzureCliOptions_FullyQualifiedNamespace_CanBeSet()
    {
        var options = new AzureCliOptions { FullyQualifiedNamespace = "my-namespace.servicebus.windows.net" };

        Assert.Equal("my-namespace.servicebus.windows.net", options.FullyQualifiedNamespace);
    }
}
