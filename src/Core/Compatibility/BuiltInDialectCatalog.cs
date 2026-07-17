namespace GEmuera.Core.Compatibility;

/// <summary>
/// Minimal built-in module declarations used by the migration facade. They are
/// compatibility-plan inputs only; legacy parser registrations remain owned by
/// the old runtime until the M2/M3 evidence gates are closed.
/// </summary>
public static class BuiltInDialectCatalog
{
    // These are declaration-only port type ids from the reviewed static
    // dialect surface. They intentionally do not provide policy values or
    // legacy Parser/VM bindings; a plan still requires an explicit immutable
    // BehaviorPortSnapshot for every behavior it elects to expose.
    private static readonly string[] SnakePortTypeIds =
    {
        "IExtraArgumentPolicy",
        "IPrivateArgumentShapePolicy",
        "IEffectiveDisplayConfigurationProjection",
        "IDisplayRefreshTimingPolicy",
        "IExpressionFunctionCatalog",
        "IInstructionCatalogBuilder",
        "IStartupFaultPolicy",
        "IUserVariableResolutionPolicy",
        "IParserDiagnosticsSinkPolicy",
        "IResourceLazyIndexPolicy",
    };

    public static DialectModuleCatalog CreateLegacyBaseline()
    {
        var catalog = new DialectModuleCatalog();
        catalog.Register(new DeclaredDialectModule(
            new DialectModuleDefinition("gemuera.v24", "1.0.0", 1),
            Array.Empty<IDialectContribution>()));
        catalog.Register(new DeclaredDialectModule(
            new DialectModuleDefinition(
                "game.snake",
                "1.0.0",
                1,
                dependencies: new[]
                {
                    new ModuleDependencySnapshot("gemuera.v24", "[1.0.0,2.0.0)"),
                },
                portTypeIds: SnakePortTypeIds),
            Array.Empty<IDialectContribution>()));
        catalog.Register(new DeclaredDialectModule(
            EraFlCompatibilityModule.CreateDefinition(),
            Array.Empty<IDialectContribution>()));
        return catalog;
    }

    /// <summary>
    /// Legacy launcher profile projection. It deliberately lists roots rather
    /// than a pre-expanded closure, so <see cref="DialectModuleCatalog"/>
    /// remains the single owner of dependency and version validation.
    /// </summary>
    public static CompatibilityProfileCatalog CreateLegacyProfileCatalog()
    {
        var catalog = new CompatibilityProfileCatalog();
        catalog.Register(new CompatibilityProfileDefinition(
            "v24pure",
            new[] { "gemuera.v24" }));
        catalog.Register(new CompatibilityProfileDefinition(
            "snake",
            new[] { "game.snake" }));
        catalog.Register(EraFlCompatibilityModule.CreateProfile());
        return catalog;
    }

    private sealed class DeclaredDialectModule : IDialectModule
    {
        public DeclaredDialectModule(
            DialectModuleDefinition definition,
            IReadOnlyList<IDialectContribution> contributions)
        {
            Definition = definition;
            Contributions = contributions;
        }

        public DialectModuleDefinition Definition { get; }
        public IReadOnlyList<IDialectContribution> Contributions { get; }
    }
}
