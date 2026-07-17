using System.Collections.ObjectModel;

namespace GEmuera.Core.Compatibility;

/// <summary>
/// 内置 eraFL 兼容包。模块以数据和确定性策略为主，只暴露冻结计划需要的
/// 输入以及 legacy bridge 消费的窄决策；不读取游戏文件、不加载程序集，也不执行 ERB。
/// </summary>
public static class EraFlCompatibilityModule
{
    public const string ModuleId = "game.erafl";
    public const string ModuleVersion = "1.0.0";
    public const string BaseModuleId = "gemuera.v24";
    public const string SaveProfileId = "gemuera.erafl";

    public const string InputOmittedDefaultArgumentBehavior = "input.omitted-default-argument.v1";
    public const string PointerBlankIntegerBehavior = "input.pointer-blank-integer.v1";
    public const string MarkupUnknownAngleTextBehavior = "markup.unknown-angle-text.v1";
    public const string DisplayDynamicScopeBehavior = "display.dynamic-scope-classifier.v1";
    public const string ResourceDynamicSpriteBehavior = "resource.dynamic-sprite-roots.v1";

    public const string InputArgumentPortType = "IEraFlInputArgumentPolicy";
    public const string PointerInputPortType = "IEraFlPointerInputPolicy";
    public const string MarkupPortType = "IEraFlMarkupPolicy";
    public const string DisplayScopePortType = "IEraFlDisplayScopePolicy";
    public const string ResourcePortType = "IEraFlResourcePolicy";

    public const string MarkupDivCapability = "markup.div-v2.v1";
    public const string MarkupDualImageCapability = "markup.image-dual-src.v1";
    public const string DynamicMapCapability = "display.dynamic-map-transaction.v1";
    public const string PointerButtonCapability = "input.pointer-button.v1";
    public const string DynamicSpriteCapability = "resource.dynamic-sprite.v1";

    private static readonly ReadOnlyCollection<string> RequiredCapabilities =
        Array.AsReadOnly(new[]
        {
            MarkupDivCapability,
            MarkupDualImageCapability,
            DynamicMapCapability,
            PointerButtonCapability,
            DynamicSpriteCapability,
        });

    private static readonly ReadOnlyCollection<BehaviorPortSnapshot> DefaultBehaviorPorts =
        Array.AsReadOnly(new[]
        {
            new BehaviorPortSnapshot(
                InputOmittedDefaultArgumentBehavior,
                InputArgumentPortType,
                PortContractKind.PolicyDecision,
                ModuleId,
                "input.argument-policy.v1",
                ModuleId,
                "DIA-ERAFL-INPUT-001"),
            new BehaviorPortSnapshot(
                PointerBlankIntegerBehavior,
                PointerInputPortType,
                PortContractKind.PolicyDecision,
                ModuleId,
                "input.pointer-policy.v1",
                ModuleId,
                "DIA-ERAFL-INPUT-002"),
            new BehaviorPortSnapshot(
                MarkupUnknownAngleTextBehavior,
                MarkupPortType,
                PortContractKind.PolicyDecision,
                ModuleId,
                "markup.angle-text-policy.v1",
                ModuleId,
                "DIA-ERAFL-MARKUP-001"),
            new BehaviorPortSnapshot(
                DisplayDynamicScopeBehavior,
                DisplayScopePortType,
                PortContractKind.PolicyDecision,
                ModuleId,
                "display.scope-policy.v1",
                ModuleId,
                "DIA-ERAFL-DISPLAY-001"),
            new BehaviorPortSnapshot(
                ResourceDynamicSpriteBehavior,
                ResourcePortType,
                PortContractKind.PolicyDecision,
                ModuleId,
                "resource.sprite-root-policy.v1",
                ModuleId,
                "DIA-ERAFL-RESOURCE-001"),
        });

    public static IReadOnlyList<string> RequiredCapabilityIds => RequiredCapabilities;
    public static IReadOnlyList<BehaviorPortSnapshot> DefaultPorts => DefaultBehaviorPorts;

    public static DialectModuleDefinition CreateDefinition()
    {
        return new DialectModuleDefinition(
            ModuleId,
            ModuleVersion,
            1,
            dependencies: new[]
            {
                new ModuleDependencySnapshot(BaseModuleId, "[1.0.0,2.0.0)"),
            },
            portTypeIds: new[]
            {
                InputArgumentPortType,
                PointerInputPortType,
                MarkupPortType,
                DisplayScopePortType,
                ResourcePortType,
            });
    }

    public static CompatibilityProfileDefinition CreateProfile()
    {
        return new CompatibilityProfileDefinition(
            "erafl",
            new[] { ModuleId },
            defaultPorts: DefaultPorts,
            requiredCapabilityIds: RequiredCapabilities,
            defaultSaveProfileId: SaveProfileId);
    }

    /// <summary>
    /// eraFL 在省略第一个默认字符串时会把 INPUTS 写成前导逗号。该策略只
    /// 处理词法当前位置，因此可以在 legacy 表达式解析器创建参数对象前消费。
    /// </summary>
    public static bool IsOmittedDefaultArgument(char currentToken)
    {
        return currentToken == ',';
    }

    /// <summary>
    /// 在 eraFL 的整数等待契约中，空白右键/中键表示“没有选中的按钮”。左键
    /// 和非指针输入仍保留普通空字符串语义，避免改变 v24 的输入行为。
    /// </summary>
    public static string NormalizePointerIntegerSubmission(
        string? input,
        int mouseButton,
        bool waitingForInteger)
    {
        if (!waitingForInteger || !string.IsNullOrEmpty(input) || mouseButton is 0 or 1)
            return input ?? string.Empty;
        return "-1";
    }

    public static bool UsesExtendedDisplayHistory(string? profileId)
    {
        return string.Equals(profileId, "erafl", StringComparison.OrdinalIgnoreCase)
            || string.Equals(profileId, "snake", StringComparison.OrdinalIgnoreCase);
    }
}
