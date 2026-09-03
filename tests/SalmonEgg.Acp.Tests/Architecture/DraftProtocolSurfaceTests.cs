using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests.Architecture;

/// <summary>
/// Guards the compile-time marking of the ACP v2 draft public surface.
/// </summary>
/// <remarks>
/// <para>
/// AGENTS.md forbids production entry points from silently sending incompletely implemented draft
/// wire. The SDK models v2 contracts ahead of the runtime, so the compensating control is
/// <see cref="ExperimentalAttribute"/> keyed by <see cref="AcpDraftProtocol.DiagnosticId"/>: naming a
/// draft type is an error unless the consumer opts in explicitly.
/// </para>
/// <para>
/// These tests are not decoration. The SDK and its test project both put that diagnostic id in
/// <c>NoWarn</c> - forced, because the JSON source generator emits code naming every registered draft
/// type and no <c>#pragma</c> reaches generated code - which means the compiler cannot police the
/// marking from inside. Everything the compiler stopped checking is checked here instead.
/// </para>
/// </remarks>
public sealed class DraftProtocolSurfaceTests
{
    /// <summary>
    /// The <see cref="AcpJsonContext"/> members whose signatures name a draft type, pinned by name
    /// and by count.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the one place <see cref="ExperimentalAttribute"/> cannot reach. The JSON source
    /// generator emits a public <c>JsonTypeInfo&lt;T&gt;</c> property per registered type and does not
    /// copy attributes onto it, so a consumer can obtain and use a draft contract through the context
    /// without ever naming the draft type - measured end to end: zero diagnostics, successful
    /// round-trip. Making the context internal is not available while the DTOs are public
    /// (CS0053: inconsistent accessibility), and dropping the registrations does not help either,
    /// because the draft subclasses stay reachable through <see cref="SessionUpdate"/>'s polymorphic
    /// metadata and the generator emits properties for them anyway.
    /// </para>
    /// <para>
    /// So the channel is documented and pinned rather than pretended away. A 27th entry - any new
    /// draft registration - turns this red and forces the author to decide deliberately, instead of
    /// widening an unmeasured hole. Closing it properly means moving the draft discriminators into a
    /// hand-written converter and the registrations into an internal context, with the DTOs
    /// internalized in the same change; tracked separately.
    /// </para>
    /// </remarks>
    private static readonly string[] PinnedSerializationContextBypass =
    [
        "AgentWholeMessageUpdate",
        "AgentWholeThoughtUpdate",
        "CommandPermissionSubject",
        "DiffChange",
        "DiffPatch",
        "Icon",
        "ListDiffChange",
        "McpHttpCapabilities",
        "PlanItemsUpdateContent",
        "PlanUpdateContent",
        "PromptAudioCapabilities",
        "PromptEmbeddedContextCapabilities",
        "PromptImageCapabilities",
        "RequestPermissionSubject",
        "SessionWorkState",
        "StateSessionUpdate",
        "StructuredDiff",
        "TerminalAuthCapabilities",
        "TerminalOutput",
        "TerminalOutputChunkSessionUpdate",
        "TerminalSessionUpdate",
        "TextCommandInput",
        "ToolCallContentChunkUpdate",
        "ToolCallPermissionSubject",
        "UserWholeMessageUpdate",
        "V2PlanUpdate",
    ];


    [Fact]
    public void ManifestDraftClassification_MatchesTheExperimentalAttributes()
    {
        var manifest = PublicSurfaceManifest.Load();
        var marked = DraftTypes()
            .Select(static type => type.FullName ?? type.Name)
            .ToHashSet(StringComparer.Ordinal);

        // Complement in both directions, never a count or a second list. A gate written as
        // "the draft list equals the marked list" passes when a new draft type is added to neither,
        // which is precisely the mistake worth catching.
        var markedButNotClassified = marked.Except(manifest.DraftTypeNames).OrderBy(static n => n, StringComparer.Ordinal).ToArray();
        var classifiedButNotMarked = manifest.DraftTypeNames.Except(marked).OrderBy(static n => n, StringComparer.Ordinal).ToArray();

        Assert.True(
            markedButNotClassified.Length == 0,
            "Types carry [Experimental] but are recorded as stable in PublicSurface.Types.txt. "
            + "Change the tag to 'draft': " + string.Join(", ", markedButNotClassified));
        Assert.True(
            classifiedButNotMarked.Length == 0,
            "Types are recorded as 'draft' in PublicSurface.Types.txt but carry no "
            + $"[Experimental({nameof(AcpDraftProtocol)}.{nameof(AcpDraftProtocol.DiagnosticId)})], so consumers get no "
            + "compile-time signal: " + string.Join(", ", classifiedButNotMarked));
    }

    [Fact]
    public void EveryDraftMarking_UsesTheSingleAgreedDiagnosticId()
    {
        // One id for the whole draft surface is load-bearing, not tidiness: a type marked
        // [Experimental] is immune to the diagnostic of *any* id, so a second id would let one draft
        // family reference another while both look independently gated. It also keeps the two NoWarn
        // entries sufficient - a stray id would fail the SDK build rather than gate anything.
        var wrong = DraftTypes()
            .Select(static type => (Type: type, Id: type.GetCustomAttribute<ExperimentalAttribute>(inherit: false)!.DiagnosticId))
            .Where(static marking => !string.Equals(marking.Id, AcpDraftProtocol.DiagnosticId, StringComparison.Ordinal))
            .Select(static marking => $"{marking.Type.FullName}: {marking.Id}")
            .ToArray();

        Assert.True(
            wrong.Length == 0,
            $"Draft markings must all use {AcpDraftProtocol.DiagnosticId}: " + string.Join(", ", wrong));
    }

    [Fact]
    public void DraftMarking_CarriesTheDiagnosticTextAndDocumentationLink()
    {
        // The CLI prints Message and UrlFormat verbatim; a consumer building from a terminal sees
        // nothing else. Left at their defaults, the diagnostic would say only "for evaluation
        // purposes only", omitting the fact that decides whether to use the type at all: no live
        // client negotiates v2, so nothing built on it can reach a real Agent.
        var missing = DraftTypes()
            .Select(static type => (Type: type, Attribute: type.GetCustomAttribute<ExperimentalAttribute>(inherit: false)!))
            .Where(static marking =>
                !string.Equals(marking.Attribute.Message, AcpDraftProtocol.Message, StringComparison.Ordinal)
                || !string.Equals(marking.Attribute.UrlFormat, AcpDraftProtocol.UrlFormat, StringComparison.Ordinal))
            .Select(static marking => marking.Type.FullName ?? marking.Type.Name)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"Draft markings must reuse {nameof(AcpDraftProtocol)}.{nameof(AcpDraftProtocol.Message)} and "
            + $"{nameof(AcpDraftProtocol.UrlFormat)} so the diagnostic reads the same everywhere: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void DocumentationLink_ResolvesToASectionOfThePackagedReadme()
    {
        // The link is the only route from the diagnostic to an explanation, and it ships inside the
        // package as PackageReadmeFile. A renamed heading would rot it silently.
        var fragment = AcpDraftProtocol.UrlFormat[(AcpDraftProtocol.UrlFormat.IndexOf('#', StringComparison.Ordinal) + 1)..];
        Assert.NotEmpty(fragment);

        var anchors = HeadingAnchors(File.ReadAllLines(FindPackageReadme()));

        Assert.Contains(fragment, anchors, StringComparer.Ordinal);
        Assert.Contains(fragment, AcpDraftProtocol.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftDiagnostic_IsSilencedPerProjectAndNeverRepositoryWide()
    {
        // Dropping either entry breaks its own build, so drift is already fail-closed there. What is
        // not fail-closed, and what this actually guards, is the tempting fix for that build break:
        // moving the id into the repository-wide props. That compiles everything green while
        // silencing the marker for SalmonEgg.Application, Infrastructure and Presentation.Core too -
        // exactly the production entry points AGENTS.md says must not reach draft wire unannounced.
        // The blast radius of a NoWarn is invisible at its use site, so it is asserted here.
        foreach (var project in new[]
                 {
                     Path.Combine("src", "SalmonEgg.Acp", "SalmonEgg.Acp.csproj"),
                     Path.Combine("tests", "SalmonEgg.Acp.Tests", "SalmonEgg.Acp.Tests.csproj"),
                 })
        {
            Assert.Contains(AcpDraftProtocol.DiagnosticId, SuppressedDiagnostics(project), StringComparer.Ordinal);
        }

        var repositoryWide = SuppressedDiagnostics("Directory.Build.props");
        Assert.DoesNotContain(AcpDraftProtocol.DiagnosticId, repositoryWide, StringComparer.Ordinal);
    }

    private static string[] SuppressedDiagnostics(string relativeProjectPath) =>
        [.. XDocument.Load(FindRepositoryFile(relativeProjectPath))
            .Descendants()
            .Where(static element => element.Name.LocalName == "NoWarn")
            .SelectMany(static element => element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Select(static id => id.Trim())];

    [Fact]
    public void StableSurface_ExposesDraftTypesOnlyThroughThePinnedSerializationContext()
    {
        var draft = DraftTypes().ToHashSet();
        var expectedBypass = PinnedSerializationContextBypass.ToHashSet(StringComparer.Ordinal);
        var observedBypass = new HashSet<string>(StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (var type in typeof(AcpClient).Assembly.GetExportedTypes())
        {
            if (draft.Contains(type))
            {
                continue;
            }

            foreach (var (member, signature) in ConsumerVisibleSignatures(type))
            {
                if (!signature.Any(part => MentionsDraftType(part, draft)))
                {
                    continue;
                }

                if (type == typeof(AcpJsonContext) && expectedBypass.Contains(member))
                {
                    observedBypass.Add(member);
                    continue;
                }

                violations.Add($"{type.FullName}.{member}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Stable exported types must not name a draft type in a consumer-visible signature - the "
            + "marking only fires on the draft type itself, so a stable member reaching one hands it "
            + "over with no diagnostic at all: " + string.Join(", ", violations.Order(StringComparer.Ordinal)));

        // Stale entries matter as much as new ones: a pin nobody reaches is a pin that stopped
        // describing anything, and it would silently absorb a future member of the same name.
        var stale = expectedBypass.Except(observedBypass).OrderBy(static n => n, StringComparer.Ordinal).ToArray();
        Assert.True(
            stale.Length == 0,
            $"{nameof(PinnedSerializationContextBypass)} lists {nameof(AcpJsonContext)} members that no longer "
            + "expose a draft type. Remove them: " + string.Join(", ", stale));
    }

    [Fact]
    public void DraftMarking_IsDeclaredOnTypesOnly()
    {
        // The manifest classifies types, so a member-level marking would be invisible to the
        // classification gate above. Rather than let that be a silent blind spot, adding one fails
        // here and forces the manifest format to grow a member column first.
        var members = new List<string>();
        foreach (var type in typeof(AcpClient).Assembly.GetExportedTypes())
        {
            foreach (var member in type.GetMembers(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (member is Type)
                {
                    continue;
                }

                if (member.GetCustomAttribute<ExperimentalAttribute>(inherit: false) is not null)
                {
                    members.Add($"{type.FullName}.{member.Name}");
                }
            }
        }

        Assert.True(
            members.Count == 0,
            "Draft marking is type-level by design; PublicSurface.Types.txt has no member column to "
            + "record these, so the classification gate cannot see them: " + string.Join(", ", members));
    }

    [Fact]
    public void DraftUpdateContracts_AreExactlyTheV2OnlySessionUpdateSurface()
    {
        // Ties the two mechanisms together instead of leaving them as parallel facts that happen to
        // agree today. "This type is draft" and "this discriminator only exists in v2" are the same
        // statement about a session update, so they are asserted as one equivalence: a v2-only entry
        // whose type is not marked would ship unannounced, and a marked type sitting in the v1 surface
        // would mean a stable connection binds a draft contract.
        var draft = DraftTypes().ToHashSet();
        var v1Surface = SessionUpdateWireSurface.Entries
            .Where(static entry => entry.Surface.HasFlag(SessionUpdateWireSurface.Surfaces.V1))
            .ToArray();
        var v2Only = SessionUpdateWireSurface.Entries
            .Where(static entry => entry.Surface == SessionUpdateWireSurface.Surfaces.V2)
            .ToArray();

        var unmarkedDraftSurface = v2Only
            .Where(entry => !draft.Contains(entry.UpdateType))
            .Select(static entry => entry.Discriminator)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            unmarkedDraftSurface.Length == 0,
            "These sessionUpdate discriminators exist only in v2, so the contracts they bind are draft "
            + "surface and must be marked: " + string.Join(", ", unmarkedDraftSurface));

        var markedStableSurface = v1Surface
            .Where(entry => draft.Contains(entry.UpdateType))
            .Select(static entry => entry.Discriminator)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            markedStableSurface.Length == 0,
            "These discriminators are part of the stable v1 surface, so a v1 connection binds them - "
            + "their contracts cannot be draft: " + string.Join(", ", markedStableSurface));
    }

    private static IEnumerable<Type> DraftTypes() =>
        typeof(AcpClient).Assembly
            .GetExportedTypes()
            .Where(static type => type.GetCustomAttribute<ExperimentalAttribute>(inherit: false) is not null);

    /// <summary>
    /// Enumerates the signatures a consumer of the assembly can bind to, member by member.
    /// </summary>
    /// <remarks>
    /// Protected members count: a consumer deriving from an unsealed wire root reaches them. Property
    /// and event accessors are excluded because their declaring member is already reported, and
    /// reporting both would double every hit; operators stay in, since they are a real conversion
    /// route to a draft type.
    /// </remarks>
    private static IEnumerable<(string Member, Type[] Signature)> ConsumerVisibleSignatures(Type type)
    {
        const BindingFlags Scope = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var accessors = new HashSet<MethodInfo>();
        foreach (var property in type.GetProperties(Scope))
        {
            accessors.UnionWith(property.GetAccessors(true));
        }

        foreach (var @event in type.GetEvents(Scope))
        {
            foreach (var accessor in new[] { @event.GetAddMethod(true), @event.GetRemoveMethod(true), @event.GetRaiseMethod(true) })
            {
                if (accessor is not null)
                {
                    accessors.Add(accessor);
                }
            }
        }

        foreach (var property in type.GetProperties(Scope))
        {
            if (property.GetAccessors(true).Any(static accessor => IsConsumerVisible(accessor)))
            {
                yield return (property.Name, [property.PropertyType]);
            }
        }

        foreach (var field in type.GetFields(Scope).Where(static field => IsConsumerVisible(field)))
        {
            yield return (field.Name, [field.FieldType]);
        }

        foreach (var @event in type.GetEvents(Scope))
        {
            if (@event.EventHandlerType is { } handler && @event.GetAddMethod(true) is { } add && IsConsumerVisible(add))
            {
                yield return (@event.Name, [handler]);
            }
        }

        foreach (var method in type.GetMethods(Scope).Where(method => !accessors.Contains(method) && IsConsumerVisible(method)))
        {
            yield return (method.Name, [method.ReturnType, .. method.GetParameters().Select(static parameter => parameter.ParameterType)]);
        }

        foreach (var constructor in type.GetConstructors(Scope).Where(static constructor => IsConsumerVisible(constructor)))
        {
            yield return ($".ctor({constructor.GetParameters().Length})", [.. constructor.GetParameters().Select(static parameter => parameter.ParameterType)]);
        }
    }

    private static bool IsConsumerVisible(MethodBase method) =>
        method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

    private static bool IsConsumerVisible(FieldInfo field) =>
        field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;

    private static bool MentionsDraftType(Type? type, IReadOnlySet<Type> draft)
    {
        if (type is null)
        {
            return false;
        }

        if (type.HasElementType)
        {
            return MentionsDraftType(type.GetElementType(), draft);
        }

        // Generic arguments are the route that matters here: JsonTypeInfo<StateSessionUpdate> hands
        // over a draft contract while naming only a stable generic type.
        return draft.Contains(type) || type.GetGenericArguments().Any(argument => MentionsDraftType(argument, draft));
    }

    /// <summary>
    /// Anchors of the document's headings, as GitHub derives them.
    /// </summary>
    /// <remarks>
    /// Fenced blocks are skipped: this document contains a <c>#pragma warning disable SEACP002</c>
    /// sample, and a naive "starts with #" scan would read it as a heading and mint an anchor that
    /// does not exist.
    /// </remarks>
    private static string[] HeadingAnchors(IEnumerable<string> lines)
    {
        var anchors = new List<string>();
        var fenced = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            if (!fenced && line.StartsWith("#", StringComparison.Ordinal))
            {
                anchors.Add(GitHubAnchor(line));
            }
        }

        return [.. anchors];
    }

    private static string GitHubAnchor(string heading)
    {
        var text = heading.TrimStart('#').Trim().ToLowerInvariant();
        var anchor = new System.Text.StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
            {
                anchor.Append(character);
            }
            else if (character == ' ')
            {
                anchor.Append('-');
            }
        }

        return anchor.ToString();
    }

    private static string FindPackageReadme() =>
        FindRepositoryFile(Path.Combine("src", "SalmonEgg.Acp", "README.md"));

    private static string FindRepositoryFile(string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            relativePath));
        if (File.Exists(path))
        {
            return path;
        }

        throw new FileNotFoundException($"Repository file was not found at {path}.", path);
    }
}
