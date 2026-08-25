using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FdeTutor.Contracts.Api;
using FdeTutor.Contracts.Events;

namespace FdeTutor.Api.Content;

public sealed class S083ContentProvider
{
    private static readonly string[] ExpectedNodeFiles =
    [
        "assessments.json",
        "citations.json",
        "competencies.json",
        "content.html",
        "node.json",
        "pedagogy.json",
    ];

    private readonly JsonDocument manifest;
    private readonly JsonDocument graph;
    private readonly JsonDocument node;
    private readonly JsonDocument pedagogy;
    private readonly string sourceHtml;
    private readonly string packageRoot;

    public S083ContentProvider(
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var configuredRoot = configuration["ContentPackage:Root"];
        packageRoot = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", "content-package"))
            : Path.GetFullPath(configuredRoot);

        manifest = Load(Path.Combine(packageRoot, "manifest.json"));
        graph = Load(Path.Combine(packageRoot, "graph.json"));
        node = Load(Path.Combine(packageRoot, "nodes", "S083", "node.json"));
        pedagogy = Load(Path.Combine(packageRoot, "nodes", "S083", "pedagogy.json"));
        sourceHtml = File.ReadAllText(
            Path.Combine(packageRoot, "nodes", "S083", "content.html"));

        ContentRevision = RequiredString(manifest.RootElement, "content_revision");
        var nodeRevision = RequiredString(node.RootElement.GetProperty("source"), "commit");
        var sourceCommit = RequiredString(
            manifest.RootElement.GetProperty("source"),
            "commit");
        if (!StringComparer.Ordinal.Equals(sourceCommit, nodeRevision) ||
            !StringComparer.Ordinal.Equals(
                sourceCommit,
                RequiredString(graph.RootElement, "source_commit")))
        {
            throw new InvalidOperationException(
                "The S083 node and graph must use the manifest source commit.");
        }

        ValidateHashes();
        ValidateLearningInvariants();

        var offeringStatus = RequiredString(manifest.RootElement, "offering_status");
        var technicalEvidence =
            environment.IsEnvironment("TechnicalEvidence") &&
            configuration.GetValue("Deployment:EvidenceOnly", false);
        if (!environment.IsDevelopment() &&
            !environment.IsEnvironment("Testing") &&
            !technicalEvidence)
        {
            ValidateOfferingReadiness(offeringStatus);
        }
    }

    public string ContentRevision { get; }

    public S083ContentResponse GetPublicContent(bool sourceAbsentRecall = false)
    {
        var pedagogyRoot = pedagogy.RootElement;
        var vocabulary = pedagogyRoot
            .GetProperty("vocabulary")
            .EnumerateArray()
            .Select(item => new VocabularyItemResponse(
                RequiredString(item, "term"),
                RequiredString(item, "definition")))
            .ToArray();
        var coldStart = pedagogyRoot
            .GetProperty("cold_start_elicitations")
            .EnumerateArray()
            .Select(item => new PromptResponse(
                RequiredString(item, "source_node_id"),
                RequiredString(item, "prompt")))
            .ToArray();
        var priming = pedagogyRoot
            .GetProperty("priming_prompts")
            .EnumerateArray()
            .Select((item, index) => new PromptResponse(
                $"prime-{index + 1}",
                item.GetString() ?? throw new InvalidOperationException("A priming prompt is empty.")))
            .ToArray();
        var assessment = pedagogyRoot.GetProperty("assessment_posture");
        var authenticTransfer = pedagogyRoot.GetProperty("authentic_transfer");

        return new S083ContentResponse(
            "S083",
            ContentRevision,
            sourceAbsentRecall,
            RequiredString(node.RootElement, "title"),
            pedagogyRoot.GetProperty("expected_duration_minutes").GetInt32(),
            sourceAbsentRecall
                ? "Complete the due changed-context retrieval without instructional cues."
                : RequiredString(pedagogyRoot, "organizer"),
            sourceAbsentRecall ? [] : vocabulary,
            sourceAbsentRecall ? string.Empty : RequiredString(pedagogyRoot, "expectation_prompt"),
            sourceAbsentRecall ? [] : coldStart,
            sourceAbsentRecall ? [] : priming,
            sourceAbsentRecall
                ? string.Empty
                : RequiredString(pedagogyRoot.GetProperty("unpaid_remedy"), "prompt"),
            sourceAbsentRecall
                ? string.Empty
                : RequiredString(pedagogyRoot, "comparison_prompt"),
            new AuthenticTransferContractResponse(
                sourceAbsentRecall
                    ? string.Empty
                    : RequiredString(authenticTransfer, "prompt"),
                RequiredString(authenticTransfer, "artifact_classification"),
                RequiredString(authenticTransfer, "pilot_restriction")),
            assessment.GetProperty("assessment_bearing").GetBoolean(),
            RequiredString(assessment, "mastery_effect"));
    }

    public CriterionResponse GetCriterion()
    {
        var elements = pedagogy.RootElement
            .GetProperty("reveals")
            .GetProperty("four_element_criterion")
            .GetProperty("elements")
            .EnumerateArray()
            .Select(item => item.GetString() ??
                throw new InvalidOperationException("A criterion element is empty."))
            .ToArray();

        return new CriterionResponse("S083", ContentRevision, elements);
    }

    public string GetSourceHtml() => sourceHtml;

    public IReadOnlySet<string> GetExpectedNamedResponseIds(string eventType) =>
        eventType switch
        {
            LearnerEventTypes.PrerequisiteRecallAttempted => pedagogy.RootElement
                .GetProperty("cold_start_elicitations")
                .EnumerateArray()
                .Select(item => RequiredString(item, "source_node_id"))
                .ToHashSet(StringComparer.Ordinal),
            LearnerEventTypes.PrimingResponseSubmitted => pedagogy.RootElement
                .GetProperty("priming_prompts")
                .EnumerateArray()
                .Select((_, index) => $"prime-{index + 1}")
                .ToHashSet(StringComparer.Ordinal),
            _ => throw new ArgumentOutOfRangeException(
                nameof(eventType),
                eventType,
                "The event type does not accept a named response set."),
        };

    public void EnsureRevision(string revision)
    {
        if (!MatchesRevision(revision))
        {
            throw new ContentRevisionMismatchException(ContentRevision, revision);
        }
    }

    public bool MatchesRevision(string revision) =>
        StringComparer.Ordinal.Equals(ContentRevision, revision);

    private static JsonDocument Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Required S083 content package file was not found.", path);
        }

        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private void ValidateHashes()
    {
        var packageNode = manifest.RootElement
            .GetProperty("nodes")
            .EnumerateArray()
            .Single(item => RequiredString(item, "id") == "S083");
        var hashes = packageNode.GetProperty("hashes");
        var hashEntries = hashes.EnumerateObject()
            .ToDictionary(item => item.Name, item => item.Value.GetString(), StringComparer.Ordinal);
        if (!hashEntries.Keys.Order(StringComparer.Ordinal).SequenceEqual(
                ExpectedNodeFiles,
                StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "The manifest must contain exactly the six canonical S083 node files.");
        }

        var nodeRoot = Path.GetFullPath(Path.Combine(packageRoot, "nodes", "S083"));
        foreach (var file in ExpectedNodeFiles)
        {
            var path = Path.GetFullPath(Path.Combine(nodeRoot, file));
            if (!path.StartsWith(
                    $"{nodeRoot}{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The S083 manifest path escapes its node directory: '{file}'.");
            }

            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"The manifest references missing S083 file '{file}'.");
            }

            var actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
            if (!StringComparer.Ordinal.Equals(actual, hashEntries[file]))
            {
                throw new InvalidOperationException(
                    $"The S083 content hash does not match '{file}'.");
            }
        }

        var graphPath = Path.Combine(packageRoot, "graph.json");
        var graphHash = Convert.ToHexStringLower(
            SHA256.HashData(File.ReadAllBytes(graphPath)));
        if (!StringComparer.Ordinal.Equals(
                graphHash,
                RequiredString(manifest.RootElement, "graph_hash")))
        {
            throw new InvalidOperationException("The S083 graph hash does not match.");
        }

        var manifestRoot = manifest.RootElement;
        var source = manifestRoot.GetProperty("source");
        var revisionParts = new List<string>
        {
            $"schema_version={RequiredString(manifestRoot, "schema_version")}",
            $"source_commit={RequiredString(source, "commit")}",
            $"upstream_hve_revision={OptionalString(source, "upstream_hve_revision")}",
            $"graph_hash={graphHash}",
            $"assessment_bank_version={RequiredString(manifestRoot, "assessment_bank_version")}",
            $"namespace_policy_version={RequiredString(manifestRoot, "namespace_policy_version")}",
            $"platform_freshness_policy_version={RequiredString(manifestRoot, "platform_freshness_policy_version")}",
            $"policy_version={RequiredString(manifestRoot, "policy_version")}",
            $"minimum_runtime_version={RequiredString(manifestRoot, "minimum_runtime_version")}",
        };
        revisionParts.AddRange(
            hashEntries
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => $"node:{item.Key}={item.Value}"));
        var expectedRevision = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", revisionParts))));
        if (!StringComparer.Ordinal.Equals(expectedRevision, ContentRevision))
        {
            throw new InvalidOperationException(
                "The package content revision does not match its immutable digest.");
        }
    }

    private void ValidateLearningInvariants()
    {
        if (node.RootElement.GetProperty("assessment_bearing").GetBoolean())
        {
            throw new InvalidOperationException("S083 must remain non-assessment-bearing.");
        }

        if (node.RootElement.GetProperty("criteria").EnumerateArray().Any(criterion =>
                criterion.GetProperty("assessable").GetBoolean() ||
                RequiredString(criterion, "assesses_namespace") == "platform"))
        {
            throw new InvalidOperationException(
                "S083 criteria must be non-assessable and cannot assess platform instances.");
        }

        var unpaid = pedagogy.RootElement.GetProperty("unpaid_remedy");
        var locks = unpaid.GetProperty("locks")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToHashSet(StringComparer.Ordinal);
        if (RequiredString(unpaid, "required_event") != "UnpaidRemedyRecorded" ||
            !locks.Contains("four_element_criterion") ||
            !locks.Contains("paid_proposal_improvement"))
        {
            throw new InvalidOperationException(
                "The S083 unpaid-remedy lock is incomplete.");
        }

        if (sourceHtml.Contains("<script", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(sourceHtml, @"\son[a-z]+\s*=", RegexOptions.IgnoreCase))
        {
            throw new InvalidOperationException(
                "The authored S083 HTML contains executable markup.");
        }
    }

    private void ValidateOfferingReadiness(string offeringStatus)
    {
        if (!StringComparer.Ordinal.Equals(offeringStatus, "OFFERING_APPROVED"))
        {
            throw new InvalidOperationException(
                "A development-only content package cannot run outside Development or Testing.");
        }

        if (!manifest.RootElement
                .GetProperty("source")
                .GetProperty("canonical_owner_confirmed")
                .GetBoolean())
        {
            throw new InvalidOperationException(
                "The canonical content owner is not confirmed.");
        }

        if (RequiredString(node.RootElement, "freshness_status") != "CURRENT" ||
            !TryDate(node.RootElement, "platform_instance_verified_on", out _) ||
            !TryDate(node.RootElement, "platform_verify_before", out var verifyBefore) ||
            verifyBefore < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            throw new InvalidOperationException(
                "Platform-bearing S083 content is not verified for the current offering.");
        }
    }

    private static bool TryDate(
        JsonElement element,
        string propertyName,
        out DateOnly value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               DateOnly.TryParse(property.GetString(), out value);
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidOperationException(
                $"Content package property '{propertyName}' is required.");
        }

        return property.GetString()!;
    }

    private static string OptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
}

public sealed class ContentRevisionMismatchException(
    string expected,
    string actual)
    : Exception($"Content revision mismatch. Expected '{expected}', received '{actual}'.")
{
    public string Expected { get; } = expected;

    public string Actual { get; } = actual;
}
