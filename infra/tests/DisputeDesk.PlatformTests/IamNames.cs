using System.Text.Json;
using System.Text.RegularExpressions;

namespace DisputeDesk.PlatformTests;

/// <summary>An IAM action or condition key, and the policy template it was read from, relative to the scanned folder.</summary>
internal sealed record NameInFile(string Name, string File);

/// <summary>The distinct actions and condition keys read from every policy template, and what could not be read.</summary>
internal sealed record PolicyNames(IReadOnlyList<NameInFile> Actions, IReadOnlyList<NameInFile> ConditionKeys, IReadOnlyList<string> Problems);

/// <summary>
/// Every IAM policy under infra/terraform is a *.json.tftpl file rendered with templatefile(). Its names are
/// read here with System.Text.Json and looked up in AWS's service reference (story 7.16). Only names are checked: not
/// whether a policy is sufficient or least privilege, how IAM evaluates it, whether a key applies to the action beside
/// it, how much a wildcard grants, resource ARNs, principals or the managed policies attached in modules/compute.
/// </summary>
internal static partial class IamNames
{
    /// <summary>The current IAM policy language version (AWS IAM, "IAM JSON policy elements: Version").</summary>
    public const string PolicyVersion = "2012-10-17";

    /// <summary>
    /// The GitHub OIDC provider's two keys: documented in the IAM User Guide ("IAM and AWS STS condition context keys",
    /// #condition-keys-wif) but not listed in the service reference, so accepted as they are and printed as not verified.
    /// </summary>
    public static readonly string[] OidcKeys = ["token.actions.githubusercontent.com:aud", "token.actions.githubusercontent.com:sub"];

    /// <summary>The one jsonencode( allowed: the deploy document's content, which is an SSM document, not an IAM policy.</summary>
    private const string SsmDocumentFile = "modules/deploy-role/main.tf";

    /// <summary>Reads every action and condition key from the *.json.tftpl files under the folder.</summary>
    public static PolicyNames Read(string terraformDirectory)
    {
        List<NameInFile> actions = [];
        List<NameInFile> conditionKeys = [];
        List<string> problems = [];

        foreach (string file in FilesUnder(terraformDirectory, "*.json.tftpl"))
        {
            ReadTemplate(File.ReadAllText(file), Relative(terraformDirectory, file), actions, conditionKeys, problems);
        }

        // Across all templates together: the compute trust policy, for one, has no condition key.
        if (actions.Count == 0)
        {
            problems.Add($"no IAM action found in a *.json.tftpl file under {terraformDirectory}");
        }

        if (conditionKeys.Count == 0)
        {
            problems.Add($"no condition key found in a *.json.tftpl file under {terraformDirectory}");
        }

        return new([.. actions.Distinct()], [.. conditionKeys.Distinct()], problems);
    }

    /// <summary>
    /// The only text rules over HCL, each naming the file and line: a policy attribute is templatefile() of a
    /// .json.tftpl file, jsonencode( appears only in the deploy document's content, and no aws_iam_policy_document.
    /// </summary>
    public static List<string> CheckTerraformFiles(string terraformDirectory)
    {
        List<string> problems = [];
        foreach (string file in FilesUnder(terraformDirectory, "*.tf"))
        {
            string relative = Relative(terraformDirectory, file);
            string[] lines = File.ReadAllLines(file);
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                string where = $"{relative}:{index + 1}";

                if (PolicyAttribute().IsMatch(line) && !PolicyFromTemplate().IsMatch(line))
                {
                    problems.Add($"{where}: a policy attribute that does not continue with templatefile( of a .json.tftpl file");
                }

                bool isSsmDocumentContent = relative == SsmDocumentFile && SsmDocumentContent().IsMatch(line);
                if (line.Contains("jsonencode(", StringComparison.Ordinal) && !isSsmDocumentContent)
                {
                    problems.Add($"{where}: jsonencode( outside the deploy document's content");
                }

                if (line.Contains("aws_iam_policy_document", StringComparison.Ordinal))
                {
                    problems.Add($"{where}: an aws_iam_policy_document");
                }
            }
        }

        return problems;
    }

    /// <summary>
    /// Looks up every name in the reference: one line per distinct name and file that passes, one failure per name that
    /// does not, each naming the name and its file. IAM action names and condition keys are case-insensitive (AWS IAM,
    /// "IAM JSON policy elements: Action" and "Condition").
    /// </summary>
    public static (List<string> Lines, List<string> Failures) Check(PolicyNames names, ServiceReference reference)
    {
        List<string> lines = [];
        List<string> failures = [];

        foreach (NameInFile action in names.Actions)
        {
            string service = action.Name[..action.Name.IndexOf(':', StringComparison.Ordinal)];
            Regex pattern = ActionPattern(action.Name[(service.Length + 1)..]);

            if (!reference.Services.Contains(service))
            {
                failures.Add($"action {action.Name} in {action.File}: the service reference has no service {service}");
            }
            else if (!reference.ActionsByService.TryGetValue(service, out string[]? serviceActions))
            {
                failures.Add($"action {action.Name} in {action.File}: the service file for {service} was not fetched");
            }
            else if (!serviceActions.Any(pattern.IsMatch))
            {
                failures.Add($"action {action.Name} in {action.File}: no such action in the service reference for {service}");
            }
            else
            {
                lines.Add($"ok  action {action.Name}  ({action.File})");
            }
        }

        foreach (NameInFile key in names.ConditionKeys)
        {
            if (reference.ConditionKeyNames.Any(referenceKey => ConditionKeyPattern(referenceKey).IsMatch(key.Name)))
            {
                lines.Add($"ok  condition key {key.Name}  ({key.File})");
            }
            else if (OidcKeys.Contains(key.Name, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add($"ok  condition key {key.Name}  ({key.File})  not verified against the service reference: an OIDC federation key in the IAM User Guide");
            }
            else
            {
                failures.Add($"condition key {key.Name} in {key.File}: in neither the global condition keys nor a fetched service's keys");
            }
        }

        return (lines, failures);
    }

    /// <summary>The files matching the pattern under the folder, outside any .terraform folder, in a stable order.</summary>
    public static IEnumerable<string> FilesUnder(string directory, string pattern) =>
        Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
            .Where(file => !Relative(directory, file).Split('/').Contains(".terraform"))
            .Order(StringComparer.Ordinal);

    private static string Relative(string directory, string file) => Path.GetRelativePath(directory, file).Replace('\\', '/');

    private static void ReadTemplate(string text, string file, List<NameInFile> actions, List<NameInFile> conditionKeys, List<string> problems)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            problems.Add($"{file}: not valid JSON ({exception.Message})");
            return;
        }

        using (document)
        {
            ReadPolicy(document.RootElement, file, actions, conditionKeys, problems);
        }
    }

    private static void ReadPolicy(JsonElement policy, string file, List<NameInFile> actions, List<NameInFile> conditionKeys, List<string> problems)
    {
        if (policy.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{file}: not a JSON object");
            return;
        }

        string[] topLevelKeys = [.. policy.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)];
        if (!topLevelKeys.SequenceEqual(["Statement", "Version"]))
        {
            problems.Add($"{file}: the top-level keys are [{string.Join(", ", topLevelKeys)}], not exactly Version and Statement");
            return;
        }

        JsonElement version = policy.GetProperty("Version");
        if (version.ValueKind != JsonValueKind.String || version.GetString() != PolicyVersion)
        {
            problems.Add($"{file}: Version is {version.GetRawText()}, not \"{PolicyVersion}\"");
        }

        JsonElement statements = policy.GetProperty("Statement");
        if (statements.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"{file}: Statement is not an array");
            return;
        }

        foreach (JsonElement statement in statements.EnumerateArray())
        {
            if (statement.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{file}: a statement that is not an object");
                continue;
            }

            foreach (JsonProperty element in statement.EnumerateObject())
            {
                if (element.Name is "Action" or "NotAction")
                {
                    ReadActions(element.Value, file, actions, problems);
                }
                else if (element.Name == "Condition")
                {
                    ReadConditionKeys(element.Value, file, conditionKeys, problems);
                }
            }
        }
    }

    /// <summary>An Action or NotAction: one string, or an array of strings, each &lt;service&gt;:&lt;name&gt;.</summary>
    private static void ReadActions(JsonElement value, string file, List<NameInFile> actions, List<string> problems)
    {
        IEnumerable<JsonElement> values = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [value];
        foreach (JsonElement action in values)
        {
            string? name = action.ValueKind == JsonValueKind.String ? action.GetString() : null;
            if (name is null || !ActionBound().IsMatch(name))
            {
                problems.Add($"{file}: the action {action.GetRawText()} is not <service>:<name> (service [a-z0-9-]+, name [A-Za-z0-9*?]+)");
                continue;
            }

            actions.Add(new(name, file));
        }
    }

    /// <summary>A Condition: an object of operators, each an object whose keys are the condition keys.</summary>
    private static void ReadConditionKeys(JsonElement condition, string file, List<NameInFile> conditionKeys, List<string> problems)
    {
        if (condition.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{file}: a Condition that is not an object");
            return;
        }

        foreach (JsonProperty conditionOperator in condition.EnumerateObject())
        {
            if (conditionOperator.Value.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{file}: the Condition operator {conditionOperator.Name} is not an object of condition keys");
                continue;
            }

            foreach (JsonProperty key in conditionOperator.Value.EnumerateObject())
            {
                if (!ConditionKeyBound().IsMatch(key.Name))
                {
                    problems.Add($"{file}: the condition key \"{key.Name}\" is outside [A-Za-z0-9._/-]+(:[A-Za-z0-9._/-]+)+");
                    continue;
                }

                conditionKeys.Add(new(key.Name, file));
            }
        }
    }

    /// <summary>An action's name as a pattern: * any characters, ? exactly one (AWS IAM, "IAM JSON policy elements: Action").</summary>
    private static Regex ActionPattern(string name) =>
        new("^" + Regex.Escape(name).Replace(@"\*", ".*", StringComparison.Ordinal).Replace(@"\?", ".", StringComparison.Ordinal) + @"\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A reference key name as a pattern: each ${...} segment, such as ${TagKey}, is any non-empty text without ':'.</summary>
    private static Regex ConditionKeyPattern(string referenceKey) =>
        new("^" + string.Join("[^:]+", Placeholder().Split(referenceKey).Select(Regex.Escape)) + @"\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // \z, not $: $ also matches before a final newline.
    [GeneratedRegex(@"^[a-z0-9-]+:[A-Za-z0-9*?]+\z")]
    private static partial Regex ActionBound();

    [GeneratedRegex(@"^[A-Za-z0-9._/-]+(:[A-Za-z0-9._/-]+)+\z")]
    private static partial Regex ConditionKeyBound();

    [GeneratedRegex(@"\$\{[^}]*\}")]
    private static partial Regex Placeholder();

    // Anchored at the start of the line, so block_public_policy and the *_protocol_policy attributes do not match.
    [GeneratedRegex(@"^\s*(policy|assume_role_policy|policy_document)\s*=")]
    private static partial Regex PolicyAttribute();

    [GeneratedRegex(@"^\s*(policy|assume_role_policy|policy_document)\s*=\s*templatefile\(\s*""[^""]*\.json\.tftpl""")]
    private static partial Regex PolicyFromTemplate();

    [GeneratedRegex(@"^\s*content\s*=\s*jsonencode\(")]
    private static partial Regex SsmDocumentContent();
}
