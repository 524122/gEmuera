using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

internal static class Program
{
    private const string FunctionIdentifierTypeName = "MinorShift.Emuera.GameProc.Function.FunctionIdentifier";
    private const string FunctionCreatorTypeName = "MinorShift.Emuera.GameData.Function.FunctionMethodCreator";

    private static int Main()
    {
        try
        {
            string root = FindProjectRoot();
            var v24 = ReadUpstreamSnapshot(Path.Combine(root, "artifacts", "upstream-v24-build", "bin", "release-naudio", "Emuera.dll"));
            var snake = ReadUpstreamSnapshot(Path.Combine(root, "artifacts", "upstream-snake-build", "bin", "release", "Emuera.dll"));
            var currentV24 = ReadCurrentSnapshot("v24pure");
            var currentSnake = ReadCurrentSnapshot("snake");
            var v24Comparison = Compare(v24, currentV24);
            var snakeComparison = Compare(snake, currentSnake);
            var report = new ReflectionReport(
                v24Comparison,
                snakeComparison,
                FunctionBehaviorMatrix.Compare(v24.FunctionObjects, currentV24.FunctionObjects, v24Comparison.Functions.Mismatches.Select(item => item.Key)),
                FunctionBehaviorMatrix.Compare(snake.FunctionObjects, currentSnake.FunctionObjects, snakeComparison.Functions.Mismatches.Select(item => item.Key)));

            WriteProfile("v24", report.V24, report.V24FunctionBehavior);
            WriteProfile("snake", report.Snake, report.SnakeFunctionBehavior);
            WriteReport(root, report);
            return report.TotalMismatchCount == 0 ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 2;
        }
    }

    private static void WriteProfile(string profile, ProfileComparison comparison, FunctionBehaviorComparison behavior)
    {
        Console.WriteLine($"{profile}: instructions={comparison.Instructions.ExpectedCount}, functions={comparison.Functions.ExpectedCount}, instructionMismatches={comparison.Instructions.Mismatches.Count}, functionDeclarationMismatches={comparison.Functions.Mismatches.Count}, functionBehaviorMismatches={behavior.Mismatches.Count}, functionBehaviorUncovered={behavior.Uncovered.Count}");
        foreach (var mismatch in comparison.Instructions.Mismatches.Concat(comparison.Functions.Mismatches))
            Console.WriteLine($"{profile}/{mismatch.Kind}/{mismatch.Key}: expected={mismatch.Expected}; current={mismatch.Actual}");
    }

    private static void WriteReport(string root, ReflectionReport report)
    {
        string path = Path.Combine(root, "docs", "NewFrameworkDesign", "generated", "legacy-dialect-reflection-diff.json");
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json + Environment.NewLine);
        Console.WriteLine($"report={path}");
    }

    private static Snapshot ReadUpstreamSnapshot(string assemblyPath)
    {
        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException("Built upstream assembly was not found.", assemblyPath);
        using var context = new UpstreamLoadContext(assemblyPath);
        Assembly assembly = context.LoadFromAssemblyPath(assemblyPath);
        InitializeUpstreamJsonConfig(assembly);
        return ReadSnapshot(assembly, null);
    }

    private static Snapshot ReadCurrentSnapshot(string profileId)
    {
        Assembly assembly = typeof(EmueraContent).Assembly;
        Type profileType = RequiredType(assembly, "MinorShift.Emuera.Compatibility.LegacyCompatibilityProfile");
        MethodInfo create = profileType.GetMethod("CreateForProfile", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(bool) }, null)
            ?? throw new InvalidOperationException("LegacyCompatibilityProfile.CreateForProfile was not found.");
        object profile = create.Invoke(null, new object[] { profileId, true })
            ?? throw new InvalidOperationException("LegacyCompatibilityProfile.CreateForProfile returned null.");
        return ReadSnapshot(assembly, profile);
    }

    private static void InitializeUpstreamJsonConfig(Assembly assembly)
    {
        Type? configType = assembly.GetType("MinorShift.Emuera.Runtime.Config.JSON.JSONConfig", false);
        Type? dataType = assembly.GetType("MinorShift.Emuera.Runtime.Config.JSON.JSONConfigData", false);
        if (configType is null || dataType is null)
            return;
        FieldInfo? data = configType.GetField("Data", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (data is not null && data.GetValue(null) is null)
            data.SetValue(null, Activator.CreateInstance(dataType, nonPublic: true));
    }

    private static Snapshot ReadSnapshot(Assembly assembly, object? profile)
    {
        Type instructionType = RequiredType(assembly, FunctionIdentifierTypeName);
        Type functionType = RequiredType(assembly, FunctionCreatorTypeName);
        var instructions = ReadRegistry(instructionType, "GetInstructionNameDic", profile)
            .Where(pair => GetMemberValue(pair.Value, "Method") is null)
            .ToDictionary(pair => pair.Key, pair => DescribeInstruction(pair.Value), StringComparer.Ordinal);
        var functionObjects = ReadRegistry(functionType, "GetMethodList", profile)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var functions = functionObjects.ToDictionary(pair => pair.Key, pair => DescribeFunction(pair.Value), StringComparer.Ordinal);
        return new Snapshot(instructions, functions, functionObjects);
    }

    private static IEnumerable<KeyValuePair<string, object>> ReadRegistry(Type owner, string methodName, object? profile)
    {
        MethodInfo? method = owner.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .SingleOrDefault(candidate => candidate.Name == methodName && candidate.GetParameters().Length == (profile is null ? 0 : 1));
        if (method is null)
            throw new InvalidOperationException($"{owner.FullName}.{methodName} was not found for the selected profile.");
        object registry = method.Invoke(null, profile is null ? null : new[] { profile })
            ?? throw new InvalidOperationException($"{owner.FullName}.{methodName} returned null.");
        if (registry is not IEnumerable entries)
            throw new InvalidOperationException($"{owner.FullName}.{methodName} did not return an enumerable registry.");
        foreach (object entry in entries)
        {
            Type type = entry.GetType();
            string key = type.GetProperty("Key")?.GetValue(entry) as string ?? throw new InvalidOperationException("Registry key was not a string.");
            object value = type.GetProperty("Value")?.GetValue(entry) ?? throw new InvalidOperationException($"Registry value was null for '{key}'.");
            yield return new KeyValuePair<string, object>(key, value);
        }
    }

    private static string DescribeInstruction(object identifier)
    {
        object? builder = GetMemberValue(identifier, "ArgBuilder") ?? GetMemberValue(GetMemberValue(identifier, "Instruction"), "ArgBuilder");
        if (builder is null)
            return "custom:" + (GetMemberValue(identifier, "Instruction")?.GetType().Name ?? identifier.GetType().Name);
        return "builder:" + DescribeArgumentShape(builder);
    }

    private static string DescribeArgumentShape(object builder)
    {
        object? types = GetMemberValue(builder, "argumentTypeArray");
        object? nullable = GetMemberValue(builder, "nullableArgumentArray");
        object? minArg = GetMemberValue(builder, "minArg");
        object? argAny = GetMemberValue(builder, "argAny");
        string values = types is Array array
            ? string.Join(",", array.Cast<object?>().Select((value, index) => IsNullable(nullable, index) ? "optional" : DescribeEraType(value)))
            : "custom";
        return $"{values};min={minArg ?? "default"};any={argAny ?? false}";
    }

    private static bool IsNullable(object? nullable, int index)
    {
        if (nullable is not Array values || index >= values.Length)
            return false;
        return values.GetValue(index) is bool isNullable && isNullable;
    }

    private static string DescribeFunction(object method)
    {
        object? returnType = GetMemberValue(method, "ReturnType");
        object? types = GetMemberValue(method, "argumentTypeArray");
        string arguments = types is Array array ? string.Join(",", array.Cast<object?>().Select(DescribeEraType)) : "custom";
        return $"return={DescribeEraType(returnType)};args={arguments}";
    }

    private static string DescribeEraType(object? value)
    {
        if (value is null) return "optional";
        if (value is Type type)
            return type == typeof(long) ? "Integer" : type == typeof(double) ? "Float" : type == typeof(string) ? "String" : type == typeof(void) ? "Void" : type.Name;
        return value.ToString() ?? value.GetType().Name;
    }

    private static object? GetMemberValue(object? instance, string name)
    {
        if (instance is null) return null;
        for (Type? current = instance.GetType(); current is not null; current = current.BaseType)
        {
            PropertyInfo? property = current.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property is not null) return property.GetValue(instance);
            FieldInfo? field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field is not null) return field.GetValue(instance);
        }
        return null;
    }

    private static ProfileComparison Compare(Snapshot expected, Snapshot current) => new(
        CompareSurface("instruction", expected.Instructions, current.Instructions),
        CompareSurface("function", expected.Functions, current.Functions));

    private static SurfaceComparison CompareSurface(string kind, IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string> current)
    {
        var mismatches = new List<Mismatch>();
        foreach (var pair in expected)
        {
            if (!current.TryGetValue(pair.Key, out string? actual)) mismatches.Add(new Mismatch(kind, pair.Key, pair.Value, "<missing>"));
            else if (!string.Equals(pair.Value, actual, StringComparison.Ordinal)) mismatches.Add(new Mismatch(kind, pair.Key, pair.Value, actual));
        }
        return new SurfaceComparison(expected.Count, current.Count, mismatches.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray());
    }

    private static Type RequiredType(Assembly assembly, string fullName)
    {
        Type? exact = assembly.GetType(fullName, false);
        if (exact is not null)
            return exact;

        string shortName = fullName[(fullName.LastIndexOf('.') + 1)..];
        Type[] matches = assembly.GetTypes().Where(type => type.Name == shortName).ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException($"Missing unambiguous type '{fullName}' in {assembly.Location}.");
    }

    private static string FindProjectRoot()
    {
        string path = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(path, "gemuera-c#.sln"))) path = Directory.GetParent(path)?.FullName ?? throw new DirectoryNotFoundException("Could not locate the project root.");
        return path;
    }

    private sealed class UpstreamLoadContext : AssemblyLoadContext, IDisposable
    {
        private readonly AssemblyDependencyResolver _resolver;
        public UpstreamLoadContext(string assemblyPath) : base(true) => _resolver = new AssemblyDependencyResolver(assemblyPath);
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string? path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }
        public void Dispose() => Unload();
    }

    private sealed record Snapshot(
        IReadOnlyDictionary<string, string> Instructions,
        IReadOnlyDictionary<string, string> Functions,
        IReadOnlyDictionary<string, object> FunctionObjects);
    private sealed record Mismatch(string Kind, string Key, string Expected, string Actual);
    private sealed record SurfaceComparison(int ExpectedCount, int CurrentCount, IReadOnlyList<Mismatch> Mismatches);
    private sealed record ProfileComparison(SurfaceComparison Instructions, SurfaceComparison Functions);
    private sealed record ReflectionReport(
        ProfileComparison V24,
        ProfileComparison Snake,
        FunctionBehaviorComparison V24FunctionBehavior,
        FunctionBehaviorComparison SnakeFunctionBehavior)
    {
        public int TotalMismatchCount => V24.Instructions.Mismatches.Count
            + Snake.Instructions.Mismatches.Count
            + V24FunctionBehavior.Mismatches.Count
            + SnakeFunctionBehavior.Mismatches.Count
            + V24FunctionBehavior.Uncovered.Count
            + SnakeFunctionBehavior.Uncovered.Count;
    }
}
