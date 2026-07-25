using System;
using System.Collections.Generic;
using GEmuera.Core.Compatibility;

namespace MinorShift.Emuera.Compatibility
{
	/// <summary>
	/// The narrow legacy-facing policy for Snake-only parser and VM behavior.
	/// The policy is immutable for the lifetime of one legacy session.
	/// </summary>
	internal interface ISnakeCompatibilityPolicy
	{
		bool IsEnabled { get; }
		bool UsesParserDiagnostics { get; }
		bool AllowsUserDefinedVariableResolution { get; }
		bool AllowsPrivateArguments { get; }
		bool AllowsExtraCallArguments { get; }
		bool AllowsScopedVariablePreRegistration { get; }
		bool ContinuesAfterStartupFault { get; }
		bool UsesFastDisplayRefresh { get; }
		bool UsesLazyResourceIndex { get; }
	}

	/// <summary>
	/// The narrow legacy-facing policy for eraFL input and display differences.
	/// Game-specific parsing and recovery algorithms remain in the pure Core
	/// eraFL module; this interface only decides whether they are reachable.
	/// </summary>
	internal interface IEraFlCompatibilityPolicy
	{
		bool IsEnabled { get; }
		bool UsesExtendedDisplayHistory { get; }
		string TaskStartRoomLookupFunction { get; }
		string GMapQuestType { get; }
		bool IsOmittedDefaultArgument(char currentToken);
		bool IsPointerInputMetadataOption(string optionText);
		int NormalizePointerButtonResult(int mouseButton);
		string NormalizePointerIntegerSubmission(string input, int mouseButton, bool waitingForInteger);
		bool ShouldSubmitBlankPointerStringInput(int mouseButton, bool waitingForString);
		bool TryRecoverQuestStartRoomIndex(
			string functionName,
			long returnedRoomIndex,
			string requestedRoomTag,
			long mapId,
			string questType,
			string[,] mapData,
			out long recoveredRoomIndex);
		bool TryPopulateGMapRoomData(long mapId, string[,] mapData, IReadOnlyList<EraFlGMapNode> nodes);
		bool TryParseGMapDataTableFromXml(
			string schemaXml,
			string dataXml,
			out System.Data.DataTable table,
			out IReadOnlyList<EraFlGMapNode> nodes);
	}

	/// <summary>
	/// Legacy bridge DTO for eraFL GMAP node data. The VM sees this typed
	/// contract instead of the Core module's implementation detail.
	/// </summary>
	internal sealed class EraFlGMapNode
	{
		public EraFlGMapNode(long nodeId, string nodeName, string pathList)
		{
			NodeId = nodeId;
			NodeName = nodeName ?? string.Empty;
			PathList = pathList ?? string.Empty;
		}

		public long NodeId { get; }
		public string NodeName { get; }
		public string PathList { get; }
	}

	/// <summary>
	/// Immutable module composition consumed by the process-wide legacy runtime.
	/// The legacy engine cannot run two VMs concurrently yet, but every parser,
	/// view, and resource decision reads this session-bound object instead of a
	/// mutable global profile enum or a game-name branch.
	/// </summary>
	internal sealed class LegacyCompatibilityProfile
	{
		private readonly ISet<string> hiddenInstructionNames;
		private readonly ISet<string> hiddenFunctionNames;
		private readonly ISet<string> scopedInstructionNames;
		private readonly bool scopedVariableInstructionsEnabled;

		internal LegacyCompatibilityProfile(
			string profileId,
			CompatibilityPlan plan,
			bool scopedVariableInstructionsEnabled,
			ISnakeCompatibilityPolicy snake,
			IEraFlCompatibilityPolicy eraFl,
			IEnumerable<string> hiddenInstructionNames,
			IEnumerable<string> hiddenFunctionNames,
			IEnumerable<string> scopedInstructionNames)
		{
			ProfileId = profileId;
			Plan = plan;
			this.scopedVariableInstructionsEnabled = scopedVariableInstructionsEnabled;
			Snake = snake;
			EraFl = eraFl;
			this.hiddenInstructionNames = new HashSet<string>(hiddenInstructionNames, StringComparer.Ordinal);
			this.hiddenFunctionNames = new HashSet<string>(hiddenFunctionNames, StringComparer.Ordinal);
			this.scopedInstructionNames = new HashSet<string>(scopedInstructionNames, StringComparer.Ordinal);
		}

		public string ProfileId { get; }
		public CompatibilityPlan Plan { get; }
		public ISnakeCompatibilityPolicy Snake { get; }
		public IEraFlCompatibilityPolicy EraFl { get; }
		/// <summary>
		/// Frozen session input for the optional Snake <c>VARI</c>/<c>VARS</c>
		/// instruction surface. This is intentionally not read from the mutable
		/// legacy Config singleton while the parser registry is being built.
		/// </summary>
		public bool ScopedVariableInstructionsEnabled => scopedVariableInstructionsEnabled;
		/// <summary>
		/// Identity of the registry surface projected from this profile. The Core
		/// plan hash alone is insufficient because the scoped-variable setting
		/// changes parser-visible names without changing its module closure.
		/// </summary>
		public string RegistrySurfaceHash => Plan.CanonicalHash
			+ ":scoped-variable-instructions="
			+ (scopedVariableInstructionsEnabled ? "enabled" : "disabled");
		public bool UsesExtendedDisplayHistory => Snake.IsEnabled || EraFl.UsesExtendedDisplayHistory;
		public bool UsesLazyResourceIndex => Snake.UsesLazyResourceIndex || EraFl.IsEnabled;

		public bool IsInstructionVisible(string instructionName)
		{
			if (string.IsNullOrWhiteSpace(instructionName))
				return false;

			string name = instructionName.Trim().ToUpperInvariant();
			if (!hiddenInstructionNames.Contains(name))
				return !scopedInstructionNames.Contains(name) || scopedVariableInstructionsEnabled;
			return false;
		}

		public bool IsFunctionVisible(string functionName)
		{
			if (string.IsNullOrWhiteSpace(functionName))
				return false;
			return !hiddenFunctionNames.Contains(functionName.Trim());
		}

		public static LegacyCompatibilityProfile Create(
			CompatibilityPlan plan,
			bool scopedVariableInstructionsEnabled)
		{
			if (plan == null)
				throw new ArgumentNullException(nameof(plan));

			return LegacyCompatibilityModuleCatalog.Compose(plan, scopedVariableInstructionsEnabled);
		}

		public static LegacyCompatibilityProfile CreateForProfile(
			string profileId,
			bool scopedVariableInstructionsEnabled)
		{
			CompatibilityPlan plan = BuiltInDialectCatalog.CreateLegacySessionPlan(profileId);
			return Create(plan, scopedVariableInstructionsEnabled);
		}

	}
}
