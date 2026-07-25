#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GEmuera.Core.Compatibility;

namespace MinorShift.Emuera.Compatibility
{
	/// <summary>
	/// A legacy bridge module owns only the profile-specific surface that can be
	/// projected onto the existing Parser/VM handler implementation.
	/// </summary>
	internal interface ILegacyCompatibilityModule
	{
		string ModuleId { get; }
		void Declare(LegacyCompatibilityProfileBuilder builder);
		void Apply(LegacyCompatibilityProfileBuilder builder);
	}

	/// <summary>
	/// Composes the legacy bridge from the exact frozen Core module closure.
	/// The complete legacy handler tables remain implementation detail; only
	/// these module declarations decide their parser-visible surfaces.
	/// </summary>
	internal static class LegacyCompatibilityModuleCatalog
	{
		private const string V24ModuleId = "gemuera.v24";
		private const string SnakeModuleId = "game.snake";
		private const string EraFlModuleId = "game.erafl";

		private static readonly ILegacyCompatibilityModule[] modules =
		{
			new LegacyV24CompatibilityModule(),
			new LegacySnakeCompatibilityModule(),
			new LegacyEraFlCompatibilityModule(),
		};

		private static readonly IReadOnlyDictionary<string, ILegacyCompatibilityModule> modulesById =
			new ReadOnlyDictionary<string, ILegacyCompatibilityModule>(
				modules.ToDictionary(module => module.ModuleId, StringComparer.Ordinal));

		private static readonly IReadOnlyDictionary<string, ISet<string>> expectedModuleClosures =
			new ReadOnlyDictionary<string, ISet<string>>(
				new Dictionary<string, ISet<string>>(StringComparer.Ordinal)
				{
					["v24pure"] = new HashSet<string>(StringComparer.Ordinal) { V24ModuleId },
					["snake"] = new HashSet<string>(StringComparer.Ordinal) { V24ModuleId, SnakeModuleId },
					["erafl"] = new HashSet<string>(StringComparer.Ordinal) { V24ModuleId, EraFlModuleId },
				});

		public static LegacyCompatibilityProfile Compose(
			CompatibilityPlan plan,
			bool scopedVariableInstructionsEnabled)
		{
			if (!expectedModuleClosures.ContainsKey(plan.ProfileId))
			{
				throw new InvalidOperationException(
					$"Compatibility profile '{plan.ProfileId}' has no legacy module composition.");
			}
			ISet<string> expectedModules = expectedModuleClosures[plan.ProfileId];

			var selectedModules = new HashSet<string>(
				plan.Dialect.Modules.Select(module => module.ModuleId),
				StringComparer.Ordinal);
			if (!selectedModules.SetEquals(expectedModules))
			{
				throw new InvalidOperationException(
					$"Legacy profile '{plan.ProfileId}' does not match its exact dialect module closure.");
			}

			var builder = new LegacyCompatibilityProfileBuilder(plan, scopedVariableInstructionsEnabled);
			foreach (ILegacyCompatibilityModule module in modules)
				module.Declare(builder);
			foreach (DialectModuleSnapshot selectedModule in plan.Dialect.Modules)
			{
				if (!modulesById.ContainsKey(selectedModule.ModuleId))
				{
					throw new InvalidOperationException(
						$"Legacy profile '{plan.ProfileId}' selected unsupported module '{selectedModule.ModuleId}'.");
				}
				ILegacyCompatibilityModule module = modulesById[selectedModule.ModuleId];
				module.Apply(builder);
			}
			return builder.Build();
		}
	}

	internal sealed class LegacyCompatibilityProfileBuilder
	{
		private readonly CompatibilityPlan plan;
		private readonly HashSet<string> hiddenInstructionNames = new HashSet<string>(StringComparer.Ordinal);
		private readonly HashSet<string> hiddenFunctionNames = new HashSet<string>(StringComparer.Ordinal);
		private readonly HashSet<string> scopedInstructionNames = new HashSet<string>(StringComparer.Ordinal);
		private ISnakeCompatibilityPolicy snake = DisabledSnakeCompatibilityPolicy.Instance;
		private IEraFlCompatibilityPolicy eraFl = DisabledEraFlCompatibilityPolicy.Instance;

		private readonly bool scopedVariableInstructionsEnabled;

		public LegacyCompatibilityProfileBuilder(
			CompatibilityPlan plan,
			bool scopedVariableInstructionsEnabled)
		{
			this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
			this.scopedVariableInstructionsEnabled = scopedVariableInstructionsEnabled;
		}

		public void DeclareInstructionNames(IEnumerable<string> names)
		{
			foreach (string name in names)
				hiddenInstructionNames.Add(name);
		}

		public void DeclareFunctionNames(IEnumerable<string> names)
		{
			foreach (string name in names)
				hiddenFunctionNames.Add(name);
		}

		public void DeclareScopedInstructionNames(IEnumerable<string> names)
		{
			foreach (string name in names)
				scopedInstructionNames.Add(name);
		}

		public void ExposeInstructionNames(IEnumerable<string> names)
		{
			foreach (string name in names)
				hiddenInstructionNames.Remove(name);
		}

		public void ExposeFunctionNames(IEnumerable<string> names)
		{
			foreach (string name in names)
				hiddenFunctionNames.Remove(name);
		}

		public void SetSnakePolicy(ISnakeCompatibilityPolicy policy)
		{
			snake = policy ?? throw new ArgumentNullException(nameof(policy));
		}

		public void SetEraFlPolicy(IEraFlCompatibilityPolicy policy)
		{
			eraFl = policy ?? throw new ArgumentNullException(nameof(policy));
		}

		public LegacyCompatibilityProfile Build()
		{
			return new LegacyCompatibilityProfile(
				plan.ProfileId,
				plan,
				scopedVariableInstructionsEnabled,
				snake,
				eraFl,
				hiddenInstructionNames,
				hiddenFunctionNames,
				scopedInstructionNames);
		}
	}

	internal sealed class LegacyV24CompatibilityModule : ILegacyCompatibilityModule
	{
		public string ModuleId => "gemuera.v24";
		public void Declare(LegacyCompatibilityProfileBuilder builder) { }
		public void Apply(LegacyCompatibilityProfileBuilder builder) { }
	}

	internal sealed class LegacySnakeCompatibilityModule : ILegacyCompatibilityModule
	{
		private static readonly IReadOnlyCollection<string> ScopedInstructionNames =
			Array.AsReadOnly(new[] { "VARI", "VARS" });

		private static readonly IReadOnlyCollection<string> InstructionNames =
			Array.AsReadOnly(new[]
			{
				"BITMAP_CACHE_ENABLE", "BREAKBUTTON", "CALLSHARP", "CLEARBGIMAGE", "DT_COLUMN_OPTIONS",
				"HTML_PRINT_ISLAND", "HTML_PRINT_ISLAND_CLEAR", "HTML_PRINTC", "HTML_PRINTLC", "PLAYBGM",
				"PLAYSOUND", "PRINTFORMN", "PRINTFORMSN", "PRINTN", "PRINTSN", "PRINTVN", "REMOVEBGIMAGE",
				"SET_SKIA_QUALITY", "SET_TEXT_DRAWING_MODE", "SETBGIMAGE", "SETBGMVOLUME", "SETSOUNDVOLUME",
				"SKIPLOG", "STOPBGM", "STOPSOUND", "STRICT_FONT_FALLBACK", "TEXT_BGC_OFF", "TEXT_BGC_ON",
				"TOOLTIP_CUSTOM", "TOOLTIP_FORMAT", "TOOLTIP_IMG", "TOOLTIP_SETFONT", "TOOLTIP_SETFONTSIZE",
				"UPDATECHECK", "VARI", "VARS",
			});

		private static readonly IReadOnlyCollection<string> FunctionNames =
			Array.AsReadOnly(new[] { "陥落状態", "陷落状态" });

		public string ModuleId => "game.snake";

		public void Declare(LegacyCompatibilityProfileBuilder builder)
		{
			builder.DeclareInstructionNames(InstructionNames);
			builder.DeclareFunctionNames(FunctionNames);
			builder.DeclareScopedInstructionNames(ScopedInstructionNames);
		}

		public void Apply(LegacyCompatibilityProfileBuilder builder)
		{
			builder.ExposeInstructionNames(InstructionNames);
			builder.ExposeFunctionNames(FunctionNames);
			builder.SetSnakePolicy(LegacySnakeCompatibilityPolicy.Instance);
		}
	}

	internal sealed class LegacyEraFlCompatibilityModule : ILegacyCompatibilityModule
	{
		public string ModuleId => EraFlCompatibilityModule.ModuleId;
		public void Declare(LegacyCompatibilityProfileBuilder builder) { }
		public void Apply(LegacyCompatibilityProfileBuilder builder)
		{
			builder.SetEraFlPolicy(LegacyEraFlCompatibilityPolicy.Instance);
		}
	}

	internal sealed class DisabledSnakeCompatibilityPolicy : ISnakeCompatibilityPolicy
	{
		public static readonly DisabledSnakeCompatibilityPolicy Instance = new DisabledSnakeCompatibilityPolicy();
		public bool IsEnabled => false;
		public bool UsesParserDiagnostics => false;
		public bool AllowsUserDefinedVariableResolution => false;
		public bool AllowsPrivateArguments => false;
		public bool AllowsExtraCallArguments => false;
		public bool AllowsScopedVariablePreRegistration => false;
		public bool ContinuesAfterStartupFault => false;
		public bool UsesFastDisplayRefresh => false;
		public bool UsesLazyResourceIndex => false;
	}

	internal sealed class LegacySnakeCompatibilityPolicy : ISnakeCompatibilityPolicy
	{
		public static readonly LegacySnakeCompatibilityPolicy Instance = new LegacySnakeCompatibilityPolicy();
		public bool IsEnabled => true;
		public bool UsesParserDiagnostics => true;
		public bool AllowsUserDefinedVariableResolution => true;
		public bool AllowsPrivateArguments => true;
		public bool AllowsExtraCallArguments => true;
		public bool AllowsScopedVariablePreRegistration => true;
		public bool ContinuesAfterStartupFault => true;
		public bool UsesFastDisplayRefresh => true;
		public bool UsesLazyResourceIndex => true;
	}

	internal sealed class DisabledEraFlCompatibilityPolicy : IEraFlCompatibilityPolicy
	{
		public static readonly DisabledEraFlCompatibilityPolicy Instance = new DisabledEraFlCompatibilityPolicy();
		public bool IsEnabled => false;
		public bool UsesExtendedDisplayHistory => false;
		public string TaskStartRoomLookupFunction => string.Empty;
		public string GMapQuestType => string.Empty;
		public bool IsOmittedDefaultArgument(char currentToken) => false;
		public bool IsPointerInputMetadataOption(string optionText) => false;
		public int NormalizePointerButtonResult(int mouseButton) => mouseButton;
		public string NormalizePointerIntegerSubmission(string input, int mouseButton, bool waitingForInteger) => input ?? string.Empty;
		public bool ShouldSubmitBlankPointerStringInput(int mouseButton, bool waitingForString) => false;
		public bool TryRecoverQuestStartRoomIndex(string functionName, long returnedRoomIndex, string requestedRoomTag, long mapId, string questType, string[,] mapData, out long recoveredRoomIndex)
		{
			recoveredRoomIndex = returnedRoomIndex;
			return false;
		}
		public bool TryPopulateGMapRoomData(long mapId, string[,] mapData, IReadOnlyList<EraFlGMapNode> nodes) => false;
		public bool TryParseGMapDataTableFromXml(string schemaXml, string dataXml, out System.Data.DataTable table, out IReadOnlyList<EraFlGMapNode> nodes)
		{
			table = new System.Data.DataTable();
			nodes = Array.Empty<EraFlGMapNode>();
			return false;
		}
	}

	internal sealed class LegacyEraFlCompatibilityPolicy : IEraFlCompatibilityPolicy
	{
		public static readonly LegacyEraFlCompatibilityPolicy Instance = new LegacyEraFlCompatibilityPolicy();
		public bool IsEnabled => true;
		public bool UsesExtendedDisplayHistory => true;
		public string TaskStartRoomLookupFunction => EraFlCompatibilityModule.TaskStartRoomLookupFunction;
		public string GMapQuestType => EraFlCompatibilityModule.GMapQuestType;
		public bool IsOmittedDefaultArgument(char currentToken) => EraFlCompatibilityModule.IsOmittedDefaultArgument(currentToken);
		public bool IsPointerInputMetadataOption(string optionText) => EraFlCompatibilityModule.IsPointerInputMetadataOption(optionText);
		public int NormalizePointerButtonResult(int mouseButton) => EraFlCompatibilityModule.NormalizePointerButtonResult(mouseButton);
		public string NormalizePointerIntegerSubmission(string input, int mouseButton, bool waitingForInteger) =>
			EraFlCompatibilityModule.NormalizePointerIntegerSubmission(input, mouseButton, waitingForInteger);
		public bool ShouldSubmitBlankPointerStringInput(int mouseButton, bool waitingForString) =>
			EraFlCompatibilityModule.ShouldSubmitBlankPointerStringInput(mouseButton, waitingForString);
		public bool TryRecoverQuestStartRoomIndex(string functionName, long returnedRoomIndex, string requestedRoomTag, long mapId, string questType, string[,] mapData, out long recoveredRoomIndex) =>
			EraFlCompatibilityModule.TryRecoverQuestStartRoomIndex(
				functionName,
				returnedRoomIndex,
				requestedRoomTag,
				mapId,
				questType,
				mapData,
				out recoveredRoomIndex);
		public bool TryPopulateGMapRoomData(long mapId, string[,] mapData, IReadOnlyList<EraFlGMapNode> nodes)
		{
			if (nodes == null)
				return false;
			var coreNodes = new List<EraFlCompatibilityModule.GMapNodeData>(nodes.Count);
			foreach (EraFlGMapNode node in nodes)
				coreNodes.Add(new EraFlCompatibilityModule.GMapNodeData(node.NodeId, node.NodeName, node.PathList));
			return EraFlCompatibilityModule.TryPopulateGMapRoomData(mapId, mapData, coreNodes);
		}
		public bool TryParseGMapDataTableFromXml(string schemaXml, string dataXml, out System.Data.DataTable table, out IReadOnlyList<EraFlGMapNode> nodes)
		{
			if (!EraFlCompatibilityModule.TryParseGMapDataTableFromXml(schemaXml, dataXml,
				out System.Data.DataTable? parsedTable,
				out IReadOnlyList<EraFlCompatibilityModule.GMapNodeData> coreNodes)
				|| parsedTable == null)
			{
				table = new System.Data.DataTable();
				nodes = Array.Empty<EraFlGMapNode>();
				return false;
			}
			table = parsedTable;
			var projected = new List<EraFlGMapNode>(coreNodes.Count);
			foreach (EraFlCompatibilityModule.GMapNodeData node in coreNodes)
				projected.Add(new EraFlGMapNode(node.NodeId, node.NodeName ?? string.Empty, node.PathList ?? string.Empty));
			nodes = projected.AsReadOnly();
			return true;
		}
	}
}
