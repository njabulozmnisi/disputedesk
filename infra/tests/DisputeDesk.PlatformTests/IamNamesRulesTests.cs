namespace DisputeDesk.PlatformTests;

/// <summary>
/// Story 8.16 criteria 2 to 4, offline: how IamNames reads the policy templates and the HCL around them, each bound on
/// both sides, and how it looks names up, against a small reference written here instead of AWS's.
/// </summary>
public sealed class IamNamesRulesTests
{
    private const string File1 = "modules/one/policy.json.tftpl";
    private const string File2 = "modules/two/policy.json.tftpl";

    /// <summary>A valid statement, so a case's own problem is not hidden by "no action" or "no condition key".</summary>
    private const string ValidStatement = """{ "Effect": "Allow", "Action": "ssm:SendCommand", "Resource": "${arn}", "Condition": { "Bool": { "aws:SecureTransport": "true" } } }""";

    private static readonly ServiceReference Reference = new(
        new HashSet<string> { "ssm", "s3", "kms" },
        new Dictionary<string, string[]>
        {
            ["ssm"] = ["SendCommand", "GetParametersByPath", "GetParameter"],
            ["s3"] = ["GetObject", "PutObject"],
        },
        ["aws:SourceArn", "aws:RequestTag/${TagKey}", "kms:EncryptionContext:${EncryptionContextKey}"]);

    // ---- criterion 2: the templates

    [Fact]
    public void Read_ValidTemplates_ReadsEachDistinctNameOncePerFile()
    {
        // File2 has no condition key, as the compute trust policy has none: the rule is across all templates together.
        string first = Policy("""
            { "Action": "ssm:SendCommand", "Condition": { "StringEquals": { "kms:ViaService": "x", "aws:SourceAccount": "${account_id}" }, "Bool": { "aws:SecureTransport": "false" } } },
            { "NotAction": ["s3:*", "ssm:SendCommand"] }
            """);
        string second = Policy("""{ "Action": ["ssm:SendCommand", "ssm:Get?arameter"] }""");

        PolicyNames names = ReadTree((File1, first), (File2, second));

        Assert.Empty(names.Problems);
        Assert.Equal(
            [new("ssm:SendCommand", File1), new("s3:*", File1), new("ssm:SendCommand", File2), new("ssm:Get?arameter", File2)],
            names.Actions);
        Assert.Equal([new("kms:ViaService", File1), new("aws:SourceAccount", File1), new("aws:SecureTransport", File1)], names.ConditionKeys);
    }

    [Theory]
    // Not valid JSON: a placeholder outside a JSON string.
    [InlineData("""{ "Version": "2012-10-17", "Statement": [{ "Action": "ssm:SendCommand", "Resource": ${resources} }] }""", "not valid JSON")]
    [InlineData("""[]""", "not a JSON object")]
    // The top-level keys: one more, one fewer, one twice.
    [InlineData("""{ "Version": "2012-10-17", "Id": "x", "Statement": [] }""", "the top-level keys are [Id, Statement, Version]")]
    [InlineData("""{ "Version": "2012-10-17" }""", "the top-level keys are [Version]")]
    [InlineData("""{ "Version": "2012-10-17", "Version": "2012-10-17", "Statement": [] }""", "the top-level keys are [Statement, Version, Version]")]
    // Version: the older language version, and a number.
    [InlineData("""{ "Version": "2008-10-17", "Statement": [] }""", "Version is \"2008-10-17\", not \"2012-10-17\"")]
    [InlineData("""{ "Version": 2012, "Statement": [] }""", "Version is 2012, not \"2012-10-17\"")]
    // Statement and its elements in a shape the check does not read.
    [InlineData("""{ "Version": "2012-10-17", "Statement": { "Action": "ssm:SendCommand" } }""", "Statement is not an array")]
    [InlineData("""{ "Version": "2012-10-17", "Statement": ["ssm:SendCommand"] }""", "a statement that is not an object")]
    [InlineData("""{ "Version": "2012-10-17", "Statement": [{ "Action": 5 }] }""", "the action 5 is not <service>:<name>")]
    [InlineData("""{ "Version": "2012-10-17", "Statement": [{ "NotAction": [["ssm:SendCommand"]] }] }""", "the action [\"ssm:SendCommand\"] is not <service>:<name>")]
    [InlineData("""{ "Version": "2012-10-17", "Statement": [{ "Condition": "aws:SecureTransport" }] }""", "a Condition that is not an object")]
    [InlineData("""{ "Version": "2012-10-17", "Statement": [{ "Condition": { "Bool": "aws:SecureTransport" } }] }""", "the Condition operator Bool is not an object of condition keys")]
    public void Read_TemplateOutsideItsShape_FailsNamingTheFile(string template, string expected)
    {
        PolicyNames names = ReadTree((File1, template), (File2, Policy(ValidStatement)));

        Assert.Contains(names.Problems, problem => problem.StartsWith($"{File1}: ", StringComparison.Ordinal) && problem.Contains(expected, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("ssm:SendCommand")]
    [InlineData("s3:*")]
    [InlineData("ssm:Get?arameter")]
    [InlineData("a-b-1:X9")]
    public void Read_ActionInsideItsBound_IsRead(string action)
    {
        PolicyNames names = ReadTree((File1, Policy(ValidStatement, $$"""{ "Action": "{{action}}" }""")));

        Assert.Empty(names.Problems);
        Assert.Contains(new NameInFile(action, File1), names.Actions);
    }

    [Theory]
    [InlineData("SSM:SendCommand")] // upper case in the service
    [InlineData("ssm_x:SendCommand")] // an underscore in the service
    [InlineData("ssm:Send-Command")] // a hyphen in the name
    [InlineData("ssm:Send Command")]
    [InlineData("ssm:${name}")]
    [InlineData("ssm:")]
    [InlineData(":SendCommand")]
    [InlineData("ssm")]
    [InlineData("ssm:Send:Command")]
    [InlineData("ssm:SendCommand\\n")] // a final newline, which $ would let through
    public void Read_ActionOutsideItsBound_FailsNamingItAndTheFile(string action)
    {
        PolicyNames names = ReadTree((File1, Policy(ValidStatement, $$"""{ "Action": ["{{action}}"] }""")));

        Assert.Equal([$"{File1}: the action \"{action}\" is not <service>:<name> (service [a-z0-9-]+, name [A-Za-z0-9*?]+)"], names.Problems);
    }

    [Theory]
    [InlineData("aws:SourceArn")]
    [InlineData("kms:EncryptionContext:PARAMETER_ARN")]
    [InlineData("aws:RequestTag/env-1.x_y")]
    public void Read_ConditionKeyInsideItsBound_IsRead(string key)
    {
        PolicyNames names = ReadTree((File1, Policy(ValidStatement, $$"""{ "Condition": { "StringEquals": { "{{key}}": "x" } } }""")));

        Assert.Empty(names.Problems);
        Assert.Contains(new NameInFile(key, File1), names.ConditionKeys);
    }

    [Theory]
    [InlineData("SourceArn")] // no colon
    [InlineData("aws:")]
    [InlineData(":SourceArn")]
    [InlineData("aws:Source Arn")]
    [InlineData("aws:${key}")]
    [InlineData("aws:Source*")]
    [InlineData("aws:SourceArn\\n")]
    public void Read_ConditionKeyOutsideItsBound_FailsNamingItAndTheFile(string key)
    {
        PolicyNames names = ReadTree((File1, Policy(ValidStatement, $$"""{ "Condition": { "StringEquals": { "{{key}}": "x" } } }""")));

        string expected = key.Replace("\\n", "\n", StringComparison.Ordinal);
        Assert.Equal([$"{File1}: the condition key \"{expected}\" is outside [A-Za-z0-9._/-]+(:[A-Za-z0-9._/-]+)+"], names.Problems);
    }

    [Fact]
    public void Read_NoTemplate_FailsForNoActionAndNoConditionKey()
    {
        string tree = Directory.CreateTempSubdirectory("disputedesk-iam-rules-").FullName;
        try
        {
            PolicyNames names = IamNames.Read(tree);

            Assert.Equal([$"no IAM action found in a *.json.tftpl file under {tree}", $"no condition key found in a *.json.tftpl file under {tree}"], names.Problems);
        }
        finally
        {
            Directory.Delete(tree, recursive: true);
        }
    }

    [Fact]
    public void Read_TemplatesWithActionsButNoConditionKey_FailsForNoConditionKey()
    {
        PolicyNames names = ReadTree((File1, Policy("""{ "Action": "ssm:SendCommand" }""")), (File2, Policy("""{ "Action": "s3:*" }""")));

        Assert.Single(names.Problems);
        Assert.StartsWith("no condition key found", names.Problems[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Read_TemplateUnderDotTerraform_IsNotRead()
    {
        PolicyNames names = ReadTree((File1, Policy(ValidStatement)), (".terraform/modules/x/policy.json.tftpl", "not JSON"));

        Assert.Empty(names.Problems);
    }

    // ---- criterion 2: the HCL text rules

    [Theory]
    [InlineData("modules/one/main.tf", "  policy = templatefile(\"${path.module}/deploy-policy.json.tftpl\", {")]
    [InlineData("modules/one/main.tf", "  assume_role_policy = templatefile(\"${path.module}/trust-policy.json.tftpl\", {")]
    [InlineData("modules/one/main.tf", "  policy_document = templatefile( \"${path.module}/p.json.tftpl\", {")]
    // Attributes the anchored rule leaves out.
    [InlineData("modules/one/main.tf", "  block_public_policy     = true")]
    [InlineData("modules/one/main.tf", "      origin_protocol_policy = \"http-only\"")]
    [InlineData("modules/one/main.tf", "    viewer_protocol_policy   = \"https-only\"")]
    [InlineData("modules/one/main.tf", "  policy_name = \"${var.name}-transaction-search\"")]
    [InlineData("modules/one/main.tf", "  policy_arn = \"arn:aws:iam::aws:policy/${each.key}\"")]
    // The deploy document's content, the one jsonencode( allowed.
    [InlineData("modules/deploy-role/main.tf", "  content = jsonencode({")]
    public void CheckTerraformFiles_AllowedLine_Passes(string file, string line)
    {
        Assert.Empty(CheckHcl(file, line));
    }

    [Theory]
    [InlineData("modules/one/main.tf", "  policy = jsonencode({", new[] { "modules/one/main.tf:1: a policy attribute that does not continue with templatefile( of a .json.tftpl file", "modules/one/main.tf:1: jsonencode( outside the deploy document's content" })]
    [InlineData("modules/one/main.tf", "  policy=jsonencode({", new[] { "modules/one/main.tf:1: a policy attribute that does not continue with templatefile( of a .json.tftpl file", "modules/one/main.tf:1: jsonencode( outside the deploy document's content" })]
    // jsonencode( on the next line: the anchored rule fails the attribute line.
    [InlineData("modules/one/main.tf", "  policy =\n    jsonencode({", new[] { "modules/one/main.tf:1: a policy attribute that does not continue with templatefile( of a .json.tftpl file", "modules/one/main.tf:2: jsonencode( outside the deploy document's content" })]
    [InlineData("modules/one/main.tf", "  policy = templatefile(\"${path.module}/p.json\", {", new[] { "modules/one/main.tf:1: a policy attribute that does not continue with templatefile( of a .json.tftpl file" })]
    [InlineData("modules/one/main.tf", "  policy_document = file(\"${path.module}/p.json.tftpl\")", new[] { "modules/one/main.tf:1: a policy attribute that does not continue with templatefile( of a .json.tftpl file" })]
    [InlineData("modules/one/main.tf", "data \"aws_iam_policy_document\" \"x\" {", new[] { "modules/one/main.tf:1: an aws_iam_policy_document" })]
    [InlineData("modules/one/main.tf", "  assume_role_policy = data.aws_iam_policy_document.x.json", new[] { "modules/one/main.tf:1: a policy attribute that does not continue with templatefile( of a .json.tftpl file", "modules/one/main.tf:1: an aws_iam_policy_document" })]
    // content = jsonencode( anywhere but the deploy-role module, and another jsonencode( in it.
    [InlineData("modules/one/main.tf", "  content = jsonencode({", new[] { "modules/one/main.tf:1: jsonencode( outside the deploy document's content" })]
    [InlineData("modules/deploy-role/main.tf", "  value = jsonencode({", new[] { "modules/deploy-role/main.tf:1: jsonencode( outside the deploy document's content" })]
    public void CheckTerraformFiles_RefusedLine_FailsNamingFileAndLine(string file, string text, string[] expected)
    {
        Assert.Equal(expected, CheckHcl(file, text));
    }

    // ---- criterion 3: actions

    [Theory]
    [InlineData("ssm:SendCommand")]
    [InlineData("ssm:sendcommand")] // case-insensitive
    [InlineData("s3:*")]
    [InlineData("ssm:*Command")]
    [InlineData("ssm:SendComman?")] // ? is exactly one character
    [InlineData("ssm:GetParameter*")] // * may be no character
    public void Check_KnownAction_Passes(string action)
    {
        (List<string> lines, List<string> failures) = IamNames.Check(Names(actions: [action]), Reference);

        Assert.Empty(failures);
        Assert.Equal([$"ok  action {action}  ({File1})"], lines);
    }

    [Theory]
    [InlineData("ssm:SendComand", "no such action in the service reference for ssm")]
    [InlineData("ssm:SendComma?", "no such action in the service reference for ssm")] // ? is one character, not two
    [InlineData("ssm:SendCommand?", "no such action in the service reference for ssm")] // ? is one character, not none
    [InlineData("ec2:RunInstances", "the service reference has no service ec2")]
    [InlineData("kms:Decrypt", "the service file for kms was not fetched")]
    public void Check_UnknownAction_FailsNamingItAndTheFile(string action, string reason)
    {
        (List<string> lines, List<string> failures) = IamNames.Check(Names(actions: [action]), Reference);

        Assert.Empty(lines);
        Assert.Equal([$"action {action} in {File1}: {reason}"], failures);
    }

    // ---- criterion 4: condition keys

    [Theory]
    [InlineData("aws:SourceArn")]
    [InlineData("AWS:SOURCEARN")]
    [InlineData("aws:RequestTag/env")] // ${TagKey} is any non-empty text without ':'
    [InlineData("kms:EncryptionContext:PARAMETER_ARN")]
    public void Check_KnownConditionKey_Passes(string key)
    {
        (List<string> lines, List<string> failures) = IamNames.Check(Names(keys: [key]), Reference);

        Assert.Empty(failures);
        Assert.Equal([$"ok  condition key {key}  ({File1})"], lines);
    }

    [Theory]
    [InlineData("token.actions.githubusercontent.com:aud")]
    [InlineData("token.actions.githubusercontent.com:sub")]
    [InlineData("TOKEN.ACTIONS.GITHUBUSERCONTENT.COM:SUB")]
    public void Check_OidcKey_PassesMarkedNotVerified(string key)
    {
        (List<string> lines, List<string> failures) = IamNames.Check(Names(keys: [key]), Reference);

        Assert.Empty(failures);
        Assert.Equal([$"ok  condition key {key}  ({File1})  not verified against the service reference: an OIDC federation key in the IAM User Guide"], lines);
    }

    [Theory]
    [InlineData("aws:SourceArnX")]
    [InlineData("aws:RequestTag/")] // ${TagKey} must not be empty
    [InlineData("aws:RequestTag/a:b")] // nor hold ':'
    [InlineData("token.actions.githubusercontent.com:sub2")]
    [InlineData("token.actions.githubusercontent.com:iss")]
    public void Check_UnknownConditionKey_FailsNamingItAndTheFile(string key)
    {
        (List<string> lines, List<string> failures) = IamNames.Check(Names(keys: [key]), Reference);

        Assert.Empty(lines);
        Assert.Equal([$"condition key {key} in {File1}: in neither the global condition keys nor a fetched service's keys"], failures);
    }

    private static string Policy(params string[] statements) =>
        $$"""{ "Version": "2012-10-17", "Statement": [{{string.Join(", ", statements)}}] }""";

    private static PolicyNames Names(string[]? actions = null, string[]? keys = null) =>
        new([.. (actions ?? []).Select(name => new NameInFile(name, File1))], [.. (keys ?? []).Select(name => new NameInFile(name, File1))], []);

    private static PolicyNames ReadTree(params (string Path, string Text)[] files)
    {
        string tree = WriteTree(files);
        try
        {
            return IamNames.Read(tree);
        }
        finally
        {
            Directory.Delete(tree, recursive: true);
        }
    }

    private static List<string> CheckHcl(string file, string text)
    {
        string tree = WriteTree((file, text));
        try
        {
            return IamNames.CheckTerraformFiles(tree);
        }
        finally
        {
            Directory.Delete(tree, recursive: true);
        }
    }

    private static string WriteTree(params (string Path, string Text)[] files)
    {
        string tree = Directory.CreateTempSubdirectory("disputedesk-iam-rules-").FullName;
        foreach ((string path, string text) in files)
        {
            string target = Path.Combine(tree, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? tree);
            File.WriteAllText(target, text);
        }

        return tree;
    }
}
