using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using Xunit;

namespace SalmonEgg.Domain.Tests.Services;

public sealed class CredentialBindingResolverTests
{
    private const string Secret = "credential-canary-never-log";

    [Fact]
    public void Resolve_NoBinding_DoesNotGuessAnInjectionName()
    {
        var profile = CreateStdioProfile();

        var resolved = CredentialBindingResolver.Resolve(profile);

        Assert.True(resolved.IsSuccess);
        Assert.Null(resolved.ErrorKind);
        Assert.Null(resolved.Error);
        Assert.DoesNotContain(Secret, resolved.Value!.Environment.Values);
        Assert.Null(resolved.Value.HeaderValue);
    }

    [Theory]
    [InlineData(CredentialSource.Token)]
    [InlineData(CredentialSource.ApiKey)]
    public void Resolve_EnvironmentBinding_CopiesSnapshotWithoutMutatingProfile(CredentialSource source)
    {
        var profile = CreateStdioProfile();
        profile.Authentication = source == CredentialSource.Token
            ? new AuthenticationConfig { Token = Secret }
            : new AuthenticationConfig { ApiKey = Secret };
        profile.CredentialBinding = CredentialBindingPolicy.Create(profile, source, CredentialTarget.Environment, "AGENT_KEY");

        var resolved = CredentialBindingResolver.Resolve(profile);
        profile.StdioEnvironment["MODEL"] = "changed";
        profile.Authentication = null;

        Assert.True(resolved.IsSuccess);
        Assert.Equal(Secret, resolved.Value!.Environment["AGENT_KEY"]);
        Assert.Equal("small", resolved.Value.Environment["MODEL"]);
        Assert.False(profile.StdioEnvironment.ContainsKey("AGENT_KEY"));
        Assert.DoesNotContain(Secret, resolved.ToString());
        Assert.DoesNotContain(Secret, resolved.Value.ToString());
        Assert.DoesNotContain(Secret, profile.CredentialBinding.ToString());
    }

    [Theory]
    [InlineData(null, Secret)]
    [InlineData("Bearer", "Bearer " + Secret)]
    public void Resolve_HeaderBinding_UsesOnlyExplicitScheme(string? scheme, string expected)
    {
        var profile = CreateNetworkProfile();
        profile.CredentialBinding = CredentialBindingPolicy.Create(profile, CredentialSource.Token, CredentialTarget.Header, "Authorization", scheme);

        var resolved = CredentialBindingResolver.Resolve(profile);

        Assert.True(resolved.IsSuccess);
        Assert.Equal(expected, resolved.Value!.HeaderValue);
        Assert.Equal("Authorization", resolved.Value.HeaderName);
        Assert.Equal(new Uri(profile.ServerUrl), resolved.Value.Endpoint);
        Assert.DoesNotContain(Secret, resolved.Value.Environment.Values);
    }

    [Theory]
    [InlineData("https://other.example/acp")]
    [InlineData("https://agent.example/other")]
    [InlineData("https://agent.example/acp?tenant=other")]
    public void Resolve_DestinationChanged_RequiresExplicitRebinding(string destination)
    {
        var profile = CreateNetworkProfile();
        profile.CredentialBinding = CredentialBindingPolicy.Create(profile, CredentialSource.Token, CredentialTarget.Header, "X-Api-Key");
        profile.ServerUrl = destination;

        var resolved = CredentialBindingResolver.Resolve(profile);

        Assert.False(resolved.IsSuccess);
        Assert.Equal(CredentialBindingValidationError.DestinationChanged, resolved.ErrorKind);
        Assert.Contains("Bind the credential again", resolved.Error);
        Assert.DoesNotContain(Secret, resolved.Error);
    }

    [Fact]
    public void Resolve_CommandArgumentsOrEnvironmentChanged_RequiresRebinding()
    {
        var profile = CreateStdioProfile();
        profile.CredentialBinding = CredentialBindingPolicy.Create(profile, CredentialSource.Token, CredentialTarget.Environment, "AGENT_KEY");

        foreach (var change in new Action<ServerConfiguration>[]
        {
            copy => copy.StdioCommand = "other-agent",
            copy => copy.StdioArguments.Add("other-script"),
            copy => copy.StdioEnvironment["PATH"] = "/other/path",
        })
        {
            var copy = profile.Clone();
            change(copy);
            Assert.False(CredentialBindingResolver.Resolve(copy).IsSuccess);
        }
    }

    [Fact]
    public void Resolve_ClearedCredential_DoesNotFallBackToPlainEnvironment()
    {
        var profile = CreateStdioProfile();
        profile.StdioEnvironment["AGENT_KEY"] = "unrelated-parent-value";
        profile.CredentialBinding = CredentialBindingPolicy.Create(profile, CredentialSource.Token, CredentialTarget.Environment, "AGENT_KEY");
        profile.Authentication = null;

        var resolved = CredentialBindingResolver.Resolve(profile);

        Assert.False(resolved.IsSuccess);
        Assert.Null(resolved.Value);
        Assert.Equal(CredentialBindingValidationError.MissingCredential, resolved.ErrorKind);
        Assert.Contains("not set", resolved.Error);
    }

    [Theory]
    [InlineData("Host", null)]
    [InlineData("Sec-WebSocket-Protocol", null)]
    [InlineData("Acp-Connection-Id", null)]
    [InlineData("Header\r\nInjection", null)]
    [InlineData("Authorization", "Bearer\r\nOther:")]
    public void Resolve_InvalidHeaderMetadata_RejectsBeforeInjection(string name, string? scheme)
    {
        var profile = CreateNetworkProfile();
        profile.CredentialBinding = CredentialBindingPolicy.Create(profile, CredentialSource.Token, CredentialTarget.Header, name, scheme);

        var resolved = CredentialBindingResolver.Resolve(profile);

        Assert.False(resolved.IsSuccess);
        Assert.Equal(scheme is null ? CredentialBindingValidationError.InvalidHeaderName
            : CredentialBindingValidationError.InvalidHeaderScheme, resolved.ErrorKind);
        Assert.DoesNotContain(Secret, resolved.Error);
    }

    [Fact]
    public void Resolve_HeaderSecretWithNewline_RejectsWithoutEcho()
    {
        var profile = CreateNetworkProfile();
        profile.Authentication!.Token = Secret + "\r\nInjected: value";
        profile.CredentialBinding = CredentialBindingPolicy.Create(profile, CredentialSource.Token, CredentialTarget.Header, "Authorization");

        var resolved = CredentialBindingResolver.Resolve(profile);

        Assert.False(resolved.IsSuccess);
        Assert.Equal(CredentialBindingValidationError.UnsupportedCredentialCharacters, resolved.ErrorKind);
        Assert.DoesNotContain(Secret, resolved.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" AGENT_KEY")]
    [InlineData("AGENT_KEY\n")]
    [InlineData("A=B")]
    [InlineData("PATH")]
    [InlineData("PathExt")]
    public void Resolve_InvalidEnvironmentBinding_RejectsBeforeLaunching(string name)
    {
        var profile = CreateStdioProfile();
        profile.CredentialBinding = CredentialBindingPolicy.Create(profile, CredentialSource.Token, CredentialTarget.Environment, name);

        var resolved = CredentialBindingResolver.Resolve(profile);

        Assert.False(resolved.IsSuccess);
        Assert.DoesNotContain(Secret, resolved.Error);
    }

    [Theory]
    [InlineData("https://user:password@agent.example/acp")]
    [InlineData("https://agent.example/acp#fragment")]
    [InlineData("wss://agent.example/acp")]
    public void Resolve_HeaderEndpointOutsideTransportContract_RejectsBeforeConnecting(string endpoint)
    {
        var profile = CreateNetworkProfile();
        profile.ServerUrl = endpoint;
        profile.CredentialBinding = CredentialBindingPolicy.Create(profile, CredentialSource.Token, CredentialTarget.Header, "X-Key");

        Assert.False(CredentialBindingResolver.Resolve(profile).IsSuccess);
    }

    [Fact]
    public void Resolve_BindingWithUnknownSourceOrTarget_DoesNotFallBack()
    {
        var profile = CreateNetworkProfile();
        profile.CredentialBinding = CredentialBindingPolicy.Create(profile, (CredentialSource)9, CredentialTarget.Header, "X-Key");
        Assert.False(CredentialBindingResolver.Resolve(profile).IsSuccess);

        profile.CredentialBinding = CredentialBindingPolicy.Create(profile, CredentialSource.Token, (CredentialTarget)9, "X-Key");
        Assert.False(CredentialBindingResolver.Resolve(profile).IsSuccess);
    }

    [Fact]
    public void Resolve_EnvironmentNameAlias_ProducesOneUnambiguousSecretDestination()
    {
        var profile = CreateStdioProfile();
        profile.StdioEnvironment["agent_key"] = "old-value";
        profile.CredentialBinding = CredentialBindingPolicy.Create(profile, CredentialSource.Token, CredentialTarget.Environment, "AGENT_KEY");

        var resolved = CredentialBindingResolver.Resolve(profile).Value!;

        Assert.Equal(Secret, resolved.Environment["AGENT_KEY"]);
        Assert.False(resolved.Environment.ContainsKey("agent_key"));
    }

    [Fact]
    public void Clone_EditAndClear_DoNotMutateLoadedProfile()
    {
        var profile = CreateStdioProfile();
        profile.CredentialBinding = CredentialBindingPolicy.Create(profile, CredentialSource.Token, CredentialTarget.Environment, "AGENT_KEY");

        var copy = profile.Clone();
        copy.Authentication!.Token = "replacement";
        copy.StdioArguments.Add("other");
        copy.StdioEnvironment.Clear();
        copy.CredentialBinding = null;

        Assert.Equal(Secret, profile.Authentication!.Token);
        Assert.Empty(profile.StdioArguments);
        Assert.Single(profile.StdioEnvironment);
        Assert.NotNull(profile.CredentialBinding);
    }

    private static ServerConfiguration CreateStdioProfile() => new()
    {
        Transport = TransportType.Stdio,
        StdioCommand = "agent",
        StdioEnvironment = new() { ["MODEL"] = "small" },
        Authentication = new AuthenticationConfig { Token = Secret },
    };

    private static ServerConfiguration CreateNetworkProfile() => new()
    {
        Transport = TransportType.StreamableHttp,
        ServerUrl = "https://agent.example/acp",
        Authentication = new AuthenticationConfig { Token = Secret },
    };
}
