using System.Reflection;
using System.Collections;

internal static class FunctionBehaviorMatrix
{
    private static readonly object TermCacheGate = new();
    private static readonly Dictionary<Assembly, IReadOnlyList<Type>> LoadableTypeCache = new();
    private static readonly Dictionary<TermConstructorKey, ConstructorInfo> TermConstructorCache = new();

    public static FunctionBehaviorComparison Compare(
        IReadOnlyDictionary<string, object> expected,
        IReadOnlyDictionary<string, object> current,
        IEnumerable<string> keys)
    {
        var mismatches = new List<FunctionBehaviorMismatch>();
        var uncovered = new List<FunctionBehaviorUncovered>();
        int covered = 0;

        foreach (string key in keys.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
        {
            if (!expected.TryGetValue(key, out object? expectedMethod) || !current.TryGetValue(key, out object? currentMethod))
            {
                uncovered.Add(new FunctionBehaviorUncovered(key, "Function is missing from one of the compared registries."));
                continue;
            }

            bool hasExpectedCheck = TryGetCheckMethod(expectedMethod, out MethodInfo? expectedCheck, out string? expectedError);
            bool hasCurrentCheck = TryGetCheckMethod(currentMethod, out MethodInfo? currentCheck, out string? currentError);
            if (!hasExpectedCheck || !hasCurrentCheck)
            {
                uncovered.Add(new FunctionBehaviorUncovered(key, expectedError ?? currentError ?? "CheckArgumentType is unavailable."));
                continue;
            }

            if (!TryCreateCases(expectedMethod, currentMethod, expectedCheck!, currentCheck!, out IReadOnlyList<ArgumentCase>? cases, out string? caseError))
            {
                uncovered.Add(new FunctionBehaviorUncovered(key, caseError ?? "Could not construct the parameter matrix."));
                continue;
            }

            covered++;
            var caseMismatches = new List<FunctionBehaviorCaseMismatch>();
            foreach (ArgumentCase argumentCase in cases!)
            {
                ArgumentOutcome expectedOutcome = InvokeCheck(expectedMethod, expectedCheck!, key, argumentCase.Values);
                ArgumentOutcome currentOutcome = InvokeCheck(currentMethod, currentCheck!, key, argumentCase.Values);
                if (!string.Equals(expectedOutcome.Category, currentOutcome.Category, StringComparison.Ordinal))
                    caseMismatches.Add(new FunctionBehaviorCaseMismatch(argumentCase.Name, expectedOutcome.Category, currentOutcome.Category));
            }
            if (caseMismatches.Count > 0)
                mismatches.Add(new FunctionBehaviorMismatch(key, caseMismatches));
        }

        return new FunctionBehaviorComparison(covered, mismatches, uncovered);
    }

    private static bool TryGetCheckMethod(object method, out MethodInfo? checkMethod, out string? error)
    {
        checkMethod = method.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SingleOrDefault(candidate => candidate.Name == "CheckArgumentType" && candidate.GetParameters().Length == 2);
        error = checkMethod is null ? "CheckArgumentType(string, arguments) was not found." : null;
        return checkMethod is not null;
    }

    private static bool TryCreateCases(
        object expectedMethod,
        object currentMethod,
        MethodInfo expectedCheck,
        MethodInfo currentCheck,
        out IReadOnlyList<ArgumentCase>? cases,
        out string? error)
    {
        cases = null;
        error = null;
        if (!TryGetExpressionElement(expectedCheck.GetParameters()[1].ParameterType, out Type? expectedElement)
            || !TryGetExpressionElement(currentCheck.GetParameters()[1].ParameterType, out Type? currentElement))
        {
            error = "CheckArgumentType does not accept an expression array.";
            return false;
        }

        int maximumLength = Math.Clamp(Math.Max(GetDeclaredLength(expectedMethod), GetDeclaredLength(currentMethod)) + 2, 3, 9);
        bool supportsInteger = CanCreateTerm(expectedMethod.GetType().Assembly, expectedElement, ArgumentKind.Integer)
            && CanCreateTerm(currentMethod.GetType().Assembly, currentElement, ArgumentKind.Integer);
        bool supportsString = CanCreateTerm(expectedMethod.GetType().Assembly, expectedElement, ArgumentKind.String)
            && CanCreateTerm(currentMethod.GetType().Assembly, currentElement, ArgumentKind.String);
        bool supportsFloat = CanCreateTerm(expectedMethod.GetType().Assembly, expectedElement, ArgumentKind.Float)
            && CanCreateTerm(currentMethod.GetType().Assembly, currentElement, ArgumentKind.Float);
        if (!supportsInteger || !supportsString)
        {
            error = "The two assemblies do not share integer and string constant expression terms.";
            return false;
        }

        var replacements = new List<ArgumentKind> { ArgumentKind.String, ArgumentKind.Null };
        if (supportsFloat)
            replacements.Insert(1, ArgumentKind.Float);
        var generated = new List<ArgumentCase> { new("empty", Array.Empty<ArgumentKind>()) };
        for (int length = 1; length <= maximumLength; length++)
        {
            generated.Add(new ArgumentCase($"integer-{length}", Enumerable.Repeat(ArgumentKind.Integer, length).ToArray()));
            generated.Add(new ArgumentCase($"string-{length}", Enumerable.Repeat(ArgumentKind.String, length).ToArray()));
            if (supportsFloat)
                generated.Add(new ArgumentCase($"float-{length}", Enumerable.Repeat(ArgumentKind.Float, length).ToArray()));
            for (int index = 0; index < length; index++)
            {
                foreach (ArgumentKind replacement in replacements)
                {
                    ArgumentKind[] values = Enumerable.Repeat(ArgumentKind.Integer, length).ToArray();
                    values[index] = replacement;
                    generated.Add(new ArgumentCase($"integer-{length}-at-{index + 1}-{replacement.ToString().ToLowerInvariant()}", values));
                }
            }
        }

        foreach (ArgumentKind kind in new[] { ArgumentKind.Integer, ArgumentKind.String })
        {
            if (!CanCreateTerm(expectedMethod.GetType().Assembly, expectedElement!, kind)
                || !CanCreateTerm(currentMethod.GetType().Assembly, currentElement!, kind))
            {
                error = "No compatible constant expression constructor was found.";
                return false;
            }
        }

        cases = generated;
        return true;
    }

    private static int GetDeclaredLength(object method)
    {
        FieldInfo? field = FindField(method.GetType(), "argumentTypeArray");
        return field?.GetValue(method) is Array values ? values.Length : 0;
    }

    private static ArgumentOutcome InvokeCheck(object method, MethodInfo checkMethod, string key, IReadOnlyList<ArgumentKind> values)
    {
        try
        {
            object arguments = CreateArgumentCollection(
                method.GetType().Assembly,
                checkMethod.GetParameters()[1].ParameterType,
                values);
            object? result = checkMethod.Invoke(method, new object[] { key, arguments });
            return result is null ? ArgumentOutcome.Accepted : ArgumentOutcome.Rejected;
        }
        catch (TargetInvocationException exception)
        {
            return new ArgumentOutcome("fault:" + (exception.InnerException?.GetType().Name ?? exception.GetType().Name));
        }
        catch (Exception exception)
        {
            return new ArgumentOutcome("fault:" + exception.GetType().Name);
        }
    }

    private static bool CanCreateTerm(Assembly assembly, Type expressionType, ArgumentKind kind)
    {
        try
        {
            _ = CreateTerm(assembly, expressionType, kind);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetExpressionElement(Type collectionType, out Type elementType)
    {
        if (collectionType.IsArray)
        {
            Type? arrayElement = collectionType.GetElementType();
            if (arrayElement is null)
            {
                elementType = null!;
                return false;
            }
            elementType = arrayElement;
            return true;
        }
        if (collectionType.IsGenericType && collectionType.GetGenericTypeDefinition() == typeof(List<>))
        {
            elementType = collectionType.GetGenericArguments()[0];
            return true;
        }
        elementType = null!;
        return false;
    }

    private static object CreateArgumentCollection(Assembly assembly, Type collectionType, IReadOnlyList<ArgumentKind> values)
    {
        if (!TryGetExpressionElement(collectionType, out Type? expressionType))
            throw new InvalidOperationException("CheckArgumentType does not accept an expression collection.");
        if (collectionType.IsArray)
        {
            Array arguments = Array.CreateInstance(expressionType!, values.Count);
            for (int index = 0; index < values.Count; index++)
            {
                if (values[index] != ArgumentKind.Null)
                    arguments.SetValue(CreateTerm(assembly, expressionType!, values[index]), index);
            }
            return arguments;
        }

        var argumentsList = (IList)(Activator.CreateInstance(collectionType)
            ?? throw new InvalidOperationException("Could not construct CheckArgumentType's expression list."));
        foreach (ArgumentKind value in values)
            argumentsList.Add(value == ArgumentKind.Null ? null : CreateTerm(assembly, expressionType!, value));
        return argumentsList;
    }

    private static object CreateTerm(Assembly assembly, Type expressionType, ArgumentKind kind)
    {
        object value = kind switch
        {
            ArgumentKind.Integer => 1L,
            ArgumentKind.String => "x",
            ArgumentKind.Float => 1.5d,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        string[] names = kind switch
        {
            ArgumentKind.Integer => new[] { "SingleLongTerm", "SingleTerm" },
            ArgumentKind.String => new[] { "SingleStrTerm", "SingleTerm" },
            ArgumentKind.Float => new[] { "SingleFloatTerm", "SingleTerm" },
            _ => Array.Empty<string>(),
        };

        var key = new TermConstructorKey(assembly, expressionType, kind);
        ConstructorInfo? constructor;
        lock (TermCacheGate)
        {
            if (!TermConstructorCache.TryGetValue(key, out constructor))
            {
                foreach (string name in names)
                {
                    foreach (Type candidate in GetLoadableTypes(assembly).Where(type => type.Name == name && expressionType.IsAssignableFrom(type)))
                    {
                        constructor = candidate.GetConstructor(
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                            binder: null,
                            types: new[] { value.GetType() },
                            modifiers: null);
                        if (constructor is not null)
                            break;
                    }
                    if (constructor is not null)
                        break;
                }
                if (constructor is null)
                    throw new InvalidOperationException($"No {kind} expression term exists in '{assembly.GetName().Name}'.");
                TermConstructorCache.Add(key, constructor);
            }
        }
        return constructor.Invoke(new[] { value });
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        lock (TermCacheGate)
        {
            if (LoadableTypeCache.TryGetValue(assembly, out IReadOnlyList<Type>? cached))
                return cached;
            IReadOnlyList<Type> loaded;
            try
            {
                loaded = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                loaded = exception.Types.Where(type => type is not null).Cast<Type>().ToArray();
            }
            LoadableTypeCache.Add(assembly, loaded);
            return loaded;
        }
    }

    private static FieldInfo? FindField(Type type, string name)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            FieldInfo? field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field is not null)
                return field;
        }
        return null;
    }

    private enum ArgumentKind
    {
        Integer,
        String,
        Float,
        Null,
    }

    private sealed record ArgumentCase(string Name, IReadOnlyList<ArgumentKind> Values);
    private sealed record TermConstructorKey(Assembly Assembly, Type ExpressionType, ArgumentKind Kind);
    private sealed record ArgumentOutcome(string Category)
    {
        public static ArgumentOutcome Accepted { get; } = new("accepted");
        public static ArgumentOutcome Rejected { get; } = new("rejected");
    }
}

internal sealed record FunctionBehaviorCaseMismatch(string Case, string Expected, string Current);
internal sealed record FunctionBehaviorMismatch(string Key, IReadOnlyList<FunctionBehaviorCaseMismatch> Cases);
internal sealed record FunctionBehaviorUncovered(string Key, string Reason);
internal sealed record FunctionBehaviorComparison(
    int CoveredFunctionCount,
    IReadOnlyList<FunctionBehaviorMismatch> Mismatches,
    IReadOnlyList<FunctionBehaviorUncovered> Uncovered);
