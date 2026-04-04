using BusWorks.Options;

namespace BusWorks.Tests.UnitTests.Options;

public class BusWorksOptionsTests
{
    [Test]
    public async Task EventBusOptions_Defaults_AreCorrect()
    {
        // ConnectionString is the enum's zero value — the implicit default when
        // no AuthenticationType is specified in appsettings.json.
        // All sub-option objects default to null; a missing block for the selected
        // auth type causes an InvalidOperationException at startup.
        var options = new BusWorksOptions();

        await Assert.That(options.AuthenticationType).IsEqualTo(EventBusAuthenticationType.ConnectionString);
        await Assert.That(options.ConnectionString).IsNull();
        await Assert.That(options.ManagedIdentity).IsNull();
        await Assert.That(options.ApplicationRegistration).IsNull();
        await Assert.That(options.AzureCli).IsNull();
        await Assert.That(options.MaxConcurrentCalls).IsEqualTo(10);
        await Assert.That(options.MaxConcurrentSessions).IsEqualTo(8);
        await Assert.That(options.MaxConcurrentCallsPerSession).IsEqualTo(1);
    }

    [Test]
    [Arguments(EventBusAuthenticationType.ManagedIdentity)]
    [Arguments(EventBusAuthenticationType.ApplicationRegistration)]
    [Arguments(EventBusAuthenticationType.AzureCli)]
    public async Task AuthenticationType_CanBeConfigured(EventBusAuthenticationType authenticationType)
    {
        var options = new BusWorksOptions { AuthenticationType = authenticationType };

        await Assert.That(options.AuthenticationType).IsEqualTo(authenticationType);
    }

    [Test]
    public async Task MaxConcurrentCalls_CanBeOverridden()
    {
        var options = new BusWorksOptions { MaxConcurrentCalls = 20 };

        await Assert.That(options.MaxConcurrentCalls).IsEqualTo(20);
    }

    [Test]
    public async Task MaxConcurrentSessions_CanBeOverridden()
    {
        var options = new BusWorksOptions { MaxConcurrentSessions = 16 };

        await Assert.That(options.MaxConcurrentSessions).IsEqualTo(16);
    }

    [Test]
    public async Task MaxConcurrentCallsPerSession_CanBeOverridden()
    {
        var options = new BusWorksOptions { MaxConcurrentCallsPerSession = 2 };

        await Assert.That(options.MaxConcurrentCallsPerSession).IsEqualTo(2);
    }

    [Test]
    [Arguments(EventBusAuthenticationType.ConnectionString, 0)]
    [Arguments(EventBusAuthenticationType.ManagedIdentity, 1)]
    [Arguments(EventBusAuthenticationType.ApplicationRegistration, 2)]
    [Arguments(EventBusAuthenticationType.AzureCli, 3)]
    public async Task AuthenticationType_IntegerValue_IsStable(EventBusAuthenticationType authenticationType, int expectedValue)
    {
        int value = (int)authenticationType;

        await Assert.That(value).IsEqualTo(expectedValue);
    }

    [Test]
    public async Task AuthenticationType_HasExactlyFourValues()
    {
        // Catches unreviewed additions that would alter the config contract.
        int count = Enum.GetValues<EventBusAuthenticationType>().Length;

        await Assert.That(count).IsEqualTo(4);
    }
    
    [Test]
    public async Task ConnectionStringOptions_Default_ConnectionString_IsEmpty()
    {
        var options = new ConnectionStringOptions();

        await Assert.That(options.ConnectionString).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ConnectionStringOptions_ConnectionString_CanBeSet()
    {
        const string value = "Endpoint=sb://my-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc123";

        var options = new ConnectionStringOptions { ConnectionString = value };

        await Assert.That(options.ConnectionString).IsEqualTo(value);
    }
    
    [Test]
    public async Task ManagedIdentityOptions_Defaults_AreCorrect()
    {
        // Null ClientId signals the system-assigned identity.
        // A non-null value selects a specific user-assigned identity by its client ID.
        var options = new ManagedIdentityOptions();

        await Assert.That(options.FullyQualifiedNamespace).IsEqualTo(string.Empty);
        await Assert.That(options.ClientId).IsNull();
    }

    [Test]
    public async Task ManagedIdentityOptions_AllValuesPreserved()
    {
        var options = new ManagedIdentityOptions
        {
            FullyQualifiedNamespace = "my-namespace.servicebus.windows.net",
            ClientId = "00000000-0000-0000-0000-000000000001"
        };

        await Assert.That(options.FullyQualifiedNamespace).IsEqualTo("my-namespace.servicebus.windows.net");
        await Assert.That(options.ClientId).IsEqualTo("00000000-0000-0000-0000-000000000001");
    }
    
    [Test]
    public async Task ApplicationRegistrationOptions_Defaults_AreCorrect()
    {
        var options = new ApplicationRegistrationOptions();

        await Assert.That(options.FullyQualifiedNamespace).IsEqualTo(string.Empty);
        await Assert.That(options.TenantId).IsEqualTo(string.Empty);
        await Assert.That(options.ClientId).IsEqualTo(string.Empty);
        await Assert.That(options.ClientSecret).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ApplicationRegistrationOptions_AllValuesPreserved()
    {
        var options = new ApplicationRegistrationOptions
        {
            FullyQualifiedNamespace = "my-namespace.servicebus.windows.net",
            TenantId = "00000000-0000-0000-0000-000000000001",
            ClientId = "00000000-0000-0000-0000-000000000002",
            ClientSecret = "super-secret"
        };

        await Assert.That(options.FullyQualifiedNamespace).IsEqualTo("my-namespace.servicebus.windows.net");
        await Assert.That(options.TenantId).IsEqualTo("00000000-0000-0000-0000-000000000001");
        await Assert.That(options.ClientId).IsEqualTo("00000000-0000-0000-0000-000000000002");
        await Assert.That(options.ClientSecret).IsEqualTo("super-secret");
    }
    
    [Test]
    public async Task AzureCliOptions_Default_FullyQualifiedNamespace_IsEmpty()
    {
        var options = new AzureCliOptions();

        await Assert.That(options.FullyQualifiedNamespace).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task AzureCliOptions_FullyQualifiedNamespace_CanBeSet()
    {
        var options = new AzureCliOptions { FullyQualifiedNamespace = "my-namespace.servicebus.windows.net" };

        await Assert.That(options.FullyQualifiedNamespace).IsEqualTo("my-namespace.servicebus.windows.net");
    }
}
