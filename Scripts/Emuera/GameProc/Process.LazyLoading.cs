using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MinorShift.Emuera.Sub;

namespace MinorShift.Emuera.GameProc
{
	internal sealed partial class Process
	{
		private readonly Dictionary<string, List<string>> lazyLoadingTable =
			new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, HashSet<string>> lazyLoadingFileToFunctions =
			new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, long> lazyLoadingFilesTable =
			new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

		public readonly HashSet<string> LazyLoadingFiles =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		public readonly HashSet<string> DeletedFiles =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		public readonly HashSet<string> ChangedFiles =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		int lazyLoadingRuntimeBatchCount = 0;
		int lazyLoadingRuntimeFileCount = 0;
		int lazyLoadingRuntimeElapsedMs = 0;
		int lazyLoadingRuntimeMaxMs = 0;
		string lazyLoadingRuntimeMaxFunction = "";

		static string cachedLazyLoadingSourceDir;
		static string cachedLazyLoadingWorkingDir;

		static string LazyLoadingDataFilePath { get { return Path.Combine(GetLazyLoadingWorkingDir(), "lazyloading.bin"); } }
		static string LazyLoadingFilesFilePath { get { return Path.Combine(GetLazyLoadingWorkingDir(), "lazyloadingfiles.bin"); } }
		static string LazyLoadingConfigFilePath { get { return Path.Combine(Program.ExeDir, "lazyloading.cfg"); } }

		const uint LazyMagicNumber = 0x4C415A59;
		const uint LazyVersion = 3;
		const int LazyRuntimeSlowLoadThresholdMs = 50;

		public enum LazyStatus
		{
			Disabled,
			NoLazy,
			BuildTable,
			Loaded,
			Error,
			UpdateTable,
		}

		public LazyStatus LazyCurrentLazyStatus = LazyStatus.Disabled;

		public void ResetLazyLoadingState()
		{
			lazyLoadingTable.Clear();
			lazyLoadingFileToFunctions.Clear();
			lazyLoadingFilesTable.Clear();
			LazyLoadingFiles.Clear();
			DeletedFiles.Clear();
			ChangedFiles.Clear();
			LazyCurrentLazyStatus = LazyStatus.Disabled;
		}

		/// <summary>
		/// Invalidates the static Android working-directory memo used by lazy
		/// loading.  The table itself is instance-owned, but the memo can survive
		/// a process-wide canary switch and otherwise point a new candidate at the
		/// previous game's fallback directory.
		/// </summary>
		internal static void ResetCanarySessionState()
		{
			cachedLazyLoadingSourceDir = null;
			cachedLazyLoadingWorkingDir = null;
		}

		public bool TryLazyLoadErb(string functionName)
		{
			if (LazyCurrentLazyStatus == LazyStatus.Disabled)
				return false;
			if (!lazyLoadingTable.TryGetValue(functionName, out List<string> files) || files.Count == 0)
				return false;

			List<string> filesToLoad = new List<string>(files);
			var loader = new ErbLoader(console, exm, this);
			int start = Environment.TickCount;
			if (loader.LoadErbsAsync(filesToLoad, labelDic, true).GetAwaiter().GetResult())
			{
				int elapsedMs = Environment.TickCount - start;
				RecordLazyLoadingRuntime(functionName, filesToLoad.Count, elapsedMs);
				LogSlowLazyLoadingRuntime(functionName, filesToLoad.Count, elapsedMs, true);
				RemoveLazyLoadingEntriesForFiles(filesToLoad);
				return true;
			}

			LogSlowLazyLoadingRuntime(functionName, filesToLoad.Count, Environment.TickCount - start, false);
			console.PrintSystemLine("LazyLoading: failed to load ERB for @" + functionName);
			return false;
		}

		public void ResetLazyLoadingRuntimeStats()
		{
			lazyLoadingRuntimeBatchCount = 0;
			lazyLoadingRuntimeFileCount = 0;
			lazyLoadingRuntimeElapsedMs = 0;
			lazyLoadingRuntimeMaxMs = 0;
			lazyLoadingRuntimeMaxFunction = "";
		}

		public void LogLazyLoadingRuntimeStats(string phase)
		{
			if (lazyLoadingRuntimeBatchCount == 0)
				return;
			GenericUtils.Info(
				$"[LOADSAVE] {phase} lazyload: batches={lazyLoadingRuntimeBatchCount}, files={lazyLoadingRuntimeFileCount}, total={lazyLoadingRuntimeElapsedMs}ms, max={lazyLoadingRuntimeMaxMs}ms @{lazyLoadingRuntimeMaxFunction}");
		}

		private void RecordLazyLoadingRuntime(string functionName, int fileCount, int elapsedMs)
		{
			lazyLoadingRuntimeBatchCount++;
			lazyLoadingRuntimeFileCount += fileCount;
			lazyLoadingRuntimeElapsedMs += elapsedMs;
			if (elapsedMs > lazyLoadingRuntimeMaxMs)
			{
				lazyLoadingRuntimeMaxMs = elapsedMs;
				lazyLoadingRuntimeMaxFunction = functionName;
			}
		}

		private static void LogSlowLazyLoadingRuntime(string functionName, int fileCount, int elapsedMs, bool success)
		{
			if (elapsedMs < LazyRuntimeSlowLoadThresholdMs)
				return;
			GenericUtils.Warn(EmueraLogCategory.Load, () =>
				$"[LOADSAVE] lazy ERB runtime load slow: function=@{functionName}, files={fileCount}, elapsed={elapsedMs}ms, success={success}, platform={Godot.OS.GetName()}");
		}

		public bool PreloadEventLoadLazyErbs()
		{
			if (LazyCurrentLazyStatus == LazyStatus.Disabled || LazyCurrentLazyStatus == LazyStatus.NoLazy)
				return false;
			if (lazyLoadingTable.Count == 0)
				return false;

			List<string> filesToLoad = lazyLoadingTable
				.Where(pair => IsEventLoadHotLazyLabel(pair.Key))
				.SelectMany(pair => pair.Value)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			if (filesToLoad.Count == 0)
				return false;

			int start = Environment.TickCount;
			var loader = new ErbLoader(console, exm, this);
			if (loader.LoadErbsAsync(filesToLoad, labelDic, true).GetAwaiter().GetResult())
			{
				int elapsed = Environment.TickCount - start;
				RecordLazyLoadingRuntime("EVENTLOAD_PRELOAD", filesToLoad.Count, elapsed);
				RemoveLazyLoadingEntriesForFiles(filesToLoad);
				GenericUtils.Info($"[LOADSAVE] EVENTLOAD lazy preload: {filesToLoad.Count} files, {elapsed}ms");
				return true;
			}

			console.PrintSystemLine("LazyLoading: failed to preload EVENTLOAD ERB files");
			return false;
		}

		private static bool IsEventLoadHotLazyLabel(string functionName)
		{
			if (string.IsNullOrEmpty(functionName))
				return false;
			if (!functionName.StartsWith("M_KOJO", StringComparison.OrdinalIgnoreCase))
				return false;
			return functionName.IndexOf("FLAGSETTING", StringComparison.OrdinalIgnoreCase) >= 0
				|| functionName.IndexOf("KOJO_VERSION", StringComparison.OrdinalIgnoreCase) >= 0
				|| functionName.IndexOf("CUSTOM_TALENT", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private void RemoveLazyLoadingEntriesForFiles(IReadOnlyCollection<string> loadedFiles)
		{
			foreach (string file in loadedFiles)
			{
				string relative = RelativeErbPath(file);
				string normalizedFull = NormalizeFullPath(ErbPath(relative));
				if (lazyLoadingFileToFunctions.TryGetValue(relative, out HashSet<string> functions))
				{
					foreach (string functionName in functions)
					{
						if (!lazyLoadingTable.TryGetValue(functionName, out List<string> paths))
							continue;
						paths.RemoveAll(path => string.Equals(NormalizeFullPath(path), normalizedFull, StringComparison.OrdinalIgnoreCase));
						if (paths.Count == 0)
							lazyLoadingTable.Remove(functionName);
					}
					lazyLoadingFileToFunctions.Remove(relative);
				}
				else
				{
					foreach (string functionName in lazyLoadingTable.Keys.ToList())
					{
						List<string> paths = lazyLoadingTable[functionName];
						paths.RemoveAll(path => string.Equals(NormalizeFullPath(path), normalizedFull, StringComparison.OrdinalIgnoreCase));
						if (paths.Count == 0)
							lazyLoadingTable.Remove(functionName);
					}
				}
				LazyLoadingFiles.Remove(normalizedFull);
			}
		}

		private void AddLazyLoadingEntry(string functionName, string fileName)
		{
			// lazy 表需要同时支持“按函数找文件”和“按文件删除所有函数映射”。
			// 运行时命中一个角色 ERB 后，如果只保存 function -> files，就必须扫描整张表；
			// 上千角色文件在手机端会把一次按需加载放大成明显尖峰，所以这里维护反向索引。
			if (string.IsNullOrEmpty(functionName) || string.IsNullOrEmpty(fileName))
				return;

			string relative = NormalizeRelativePath(fileName);
			string fullPath = ErbPath(relative);
			if (!lazyLoadingTable.TryGetValue(functionName, out List<string> paths))
			{
				paths = new List<string>();
				lazyLoadingTable.Add(functionName, paths);
			}
			if (!ContainsIgnoreCase(paths, fullPath))
				paths.Add(fullPath);

			if (!lazyLoadingFileToFunctions.TryGetValue(relative, out HashSet<string> functions))
			{
				functions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				lazyLoadingFileToFunctions.Add(relative, functions);
			}
			functions.Add(functionName);

			LazyLoadingFiles.Add(NormalizeFullPath(fullPath));
		}

		private static bool ContainsIgnoreCase(List<string> values, string value)
		{
			for (int i = 0; i < values.Count; i++)
			{
				if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
					return true;
			}
			return false;
		}

		public bool IsFunctionInLazyLoadingTable(string functionName)
		{
			if (LazyCurrentLazyStatus == LazyStatus.Disabled)
				return false;
			return lazyLoadingTable.ContainsKey(functionName);
		}

		public bool IsLazyLoadingFile(string path)
		{
			return LazyLoadingFiles.Contains(NormalizeFullPath(path));
		}

		private List<string> LoadLazyLoadingFolders()
		{
			if (!uEmuera.Utils.FileExists(LazyLoadingConfigFilePath))
			{
				console.PrintSystemLine("LazyLoading: lazyloading.cfg not found; using normal full load");
				return null;
			}

			try
			{
				var result = new List<string>();
				foreach (string line in uEmuera.Utils.ReadAllLines(LazyLoadingConfigFilePath, Encoding.UTF8))
				{
					string value = NormalizeRelativePath(line.Trim());
					if (value.Length != 0 && !value.StartsWith(";"))
						result.Add(value);
				}
				return result;
			}
			catch (Exception e)
			{
				console.PrintSystemLine("LazyLoading: failed to read lazyloading.cfg: " + e.Message);
				return null;
			}
		}

		public void LoadLazyLoadingTable(List<KeyValuePair<string, string>> erbFiles)
		{
			ResetLazyLoadingState();

			if (!uEmuera.Utils.FileExists(LazyLoadingConfigFilePath))
				return;
			if (!File.Exists(LazyLoadingDataFilePath) || !File.Exists(LazyLoadingFilesFilePath))
			{
				RebuildLazyLoadingIndex(erbFiles);
				return;
			}

			try
			{
				HashSet<string> files = GetLazyFiles(erbFiles);

				using (var metaStream = new FileStream(LazyLoadingFilesFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
				using (var metaReader = new BinaryReader(metaStream, Encoding.UTF8))
				{
					if (metaReader.ReadUInt32() != LazyMagicNumber || metaReader.ReadUInt32() != LazyVersion)
					{
						RebuildLazyLoadingIndex(erbFiles);
						return;
					}

					int fileCount = metaReader.ReadInt32();
					for (int i = 0; i < fileCount; i++)
					{
						string name = NormalizeRelativePath(metaReader.ReadString());
						long lastWrite = metaReader.ReadInt64();
						string path = ErbPath(name);

						if (!uEmuera.Utils.FileExists(path))
						{
							DeletedFiles.Add(name);
							continue;
						}

						if (GetLazyFileTimestamp(path) != lastWrite)
							ChangedFiles.Add(name);
						else
							lazyLoadingFilesTable[name] = lastWrite;
					}
				}

				files.ExceptWith(lazyLoadingFilesTable.Keys);
				ChangedFiles.UnionWith(files);

				using (var dataStream = new FileStream(LazyLoadingDataFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
				using (var dataReader = new BinaryReader(dataStream, Encoding.UTF8))
				{
					if (dataReader.ReadUInt32() != LazyMagicNumber || dataReader.ReadUInt32() != LazyVersion)
					{
						RebuildLazyLoadingIndex(erbFiles);
						return;
					}

					int funcCount = dataReader.ReadInt32();
					for (int i = 0; i < funcCount; i++)
					{
						string funcName = dataReader.ReadString();
						string fileName = NormalizeRelativePath(dataReader.ReadString());
						if (ChangedFiles.Contains(fileName) || DeletedFiles.Contains(fileName))
							continue;

						AddLazyLoadingEntry(funcName, fileName);
					}
				}
			}
			catch (Exception e)
			{
				console.PrintSystemLine("LazyLoading: failed to read index table: " + e.Message);
				RebuildLazyLoadingIndex(erbFiles);
				return;
			}

			LazyCurrentLazyStatus =
				ChangedFiles.Count != 0 || DeletedFiles.Count != 0 ? LazyStatus.UpdateTable : LazyStatus.Loaded;
		}

		private void RebuildLazyLoadingIndex(List<KeyValuePair<string, string>> erbFiles)
		{
			lazyLoadingTable.Clear();
			lazyLoadingFileToFunctions.Clear();
			lazyLoadingFilesTable.Clear();
			LazyLoadingFiles.Clear();
			DeletedFiles.Clear();
			ChangedFiles.Clear();

			if (IsAndroid() && TryBuildLazyLoadingTableFromLabels(erbFiles))
				return;

			LazyCurrentLazyStatus = LazyStatus.BuildTable;
		}

		private HashSet<string> GetLazyFiles(IEnumerable<KeyValuePair<string, string>> erbFiles)
		{
			List<string> paths = LoadLazyLoadingFolders();
			if (paths == null || paths.Count == 0)
				return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var pair in erbFiles)
			{
				string relative = NormalizeRelativePath(pair.Key);
				foreach (string path in paths)
				{
					if (relative.StartsWith(path, StringComparison.OrdinalIgnoreCase))
					{
						files.Add(relative);
						break;
					}
				}
			}
			return files;
		}

		private bool TryBuildLazyLoadingTableFromLabels(List<KeyValuePair<string, string>> erbFiles)
		{
			HashSet<string> files = GetLazyFiles(erbFiles);
			if (files.Count == 0)
			{
				LazyCurrentLazyStatus = LazyStatus.NoLazy;
				return true;
			}

			var validLabels = new List<KeyValuePair<string, string>>();
			var validFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				foreach (string relative in files)
				{
					string path = ErbPath(relative);
					if (!uEmuera.Utils.FileExists(path))
						continue;
					// 标签扫描只建立 function -> ERB 文件映射；首次真正命中时仍走完整 ERB 解析、
					// setLabelsArg/checkScript，避免为了启动速度跳过原核心语义检查。
					if (!TryScanLazyFileLabels(path, relative, validLabels, out bool canLazyLoad))
						return false;
					if (canLazyLoad)
						validFiles.Add(relative);
				}

				if (validLabels.Count == 0 || validFiles.Count == 0)
				{
					LazyCurrentLazyStatus = LazyStatus.NoLazy;
					return true;
				}

				EnsureLazyLoadingWorkingDir();
				using (var dataStream = new FileStream(LazyLoadingDataFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
				using (var dataWriter = new BinaryWriter(dataStream, Encoding.UTF8))
				{
					dataWriter.Write(LazyMagicNumber);
					dataWriter.Write(LazyVersion);
					dataWriter.Write(validLabels.Count);
					foreach (var label in validLabels)
					{
						dataWriter.Write(label.Key);
						dataWriter.Write(label.Value);
					}
				}

				WriteLazyFileMeta(validFiles);
				foreach (var label in validLabels)
				{
					AddLazyLoadingEntry(label.Key, label.Value);
					lazyLoadingFilesTable[label.Value] = GetLazyFileTimestamp(ErbPath(label.Value));
				}

				console.PrintSystemLine("LazyLoading: index table created from labels without full ERB load");
				LazyCurrentLazyStatus = LazyStatus.Loaded;
				return true;
			}
			catch (Exception e)
			{
				console.PrintSystemLine("LazyLoading: failed to create label-scan index table: " + e.Message);
				lazyLoadingTable.Clear();
				lazyLoadingFileToFunctions.Clear();
				lazyLoadingFilesTable.Clear();
				LazyLoadingFiles.Clear();
				return false;
			}
		}

		private bool TryScanLazyFileLabels(
			string path,
			string relativePath,
			List<KeyValuePair<string, string>> labels,
			out bool canLazyLoad)
		{
			canLazyLoad = true;
			var fileLabels = new List<string>();
			bool hasCurrentLabel = false;
			using (var reader = new EraStreamReader(Config.UseRenameFile && ParserMediator.RenameDic != null))
			{
				if (!reader.Open(path, relativePath))
					return false;

				StringStream line;
				while ((line = reader.ReadEnabledLine()) != null)
				{
					if (line.Current == '@')
					{
						string labelName = ReadLazyScanLabelName(line);
						hasCurrentLabel = false;
						if (string.IsNullOrEmpty(labelName))
							continue;
						if (IdentifierDictionary.IsEventLabelName(labelName))
						{
							canLazyLoad = false;
							break;
						}
						fileLabels.Add(labelName);
						hasCurrentLabel = true;
					}
					else if (line.Current == '#' && hasCurrentLabel)
					{
						string token = ReadLazyScanSharpToken(line);
						if (IsLazyScanMethodToken(token))
						{
							canLazyLoad = false;
							break;
						}
					}
				}
			}

			if (!canLazyLoad)
				return true;
			foreach (string label in fileLabels)
				labels.Add(new KeyValuePair<string, string>(label, NormalizeRelativePath(relativePath)));
			return true;
		}

		private static string ReadLazyScanLabelName(StringStream line)
		{
			// 这里仅用于 Android 首次建立 lazy 索引：只抽取 label 名和 #FUNCTION* 标记，
			// 真正命中 lazy 文件时仍会调用 ErbLoader 做完整解析、警告和语义检查。
			line.ShiftNext();
			string labelName = LexicalAnalyzer.ReadSingleIdentifier(line);
			if (Config.ICVariable && !string.IsNullOrEmpty(labelName))
				labelName = labelName.ToUpper();
			return labelName;
		}

		private static string ReadLazyScanSharpToken(StringStream line)
		{
			line.ShiftNext();
			string token = LexicalAnalyzer.ReadSingleIdentifier(line);
			if (Config.ICFunction && !string.IsNullOrEmpty(token))
				token = token.ToUpper();
			return token;
		}

		private static bool IsLazyScanMethodToken(string token)
		{
			return string.Equals(token, "FUNCTION", StringComparison.Ordinal)
				|| string.Equals(token, "FUNCTIONS", StringComparison.Ordinal)
				|| string.Equals(token, "FUNCTIONF", StringComparison.Ordinal);
		}

		public void SaveLazyLoadingList(List<FunctionLabelLine> labels, List<KeyValuePair<string, string>> erbFiles)
		{
			HashSet<string> files = GetLazyFiles(erbFiles);
			if (files.Count == 0)
			{
				LazyCurrentLazyStatus = LazyStatus.NoLazy;
				return;
			}

			foreach (FunctionLabelLine label in labels)
			{
				if (label.Position == null || !files.Contains(NormalizeRelativePath(label.Position.Filename)))
					continue;
				if (label.IsEvent || label.IsMethod)
					files.Remove(NormalizeRelativePath(label.Position.Filename));
			}

			try
			{
				EnsureLazyLoadingWorkingDir();
				var validLabels = labels
					.Where(label => label.Position != null && files.Contains(NormalizeRelativePath(label.Position.Filename)))
					.ToList();
				var metaFiles = new HashSet<string>(
					validLabels.Select(label => NormalizeRelativePath(label.Position.Filename)),
					StringComparer.OrdinalIgnoreCase);

				using (var dataStream = new FileStream(LazyLoadingDataFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
				using (var dataWriter = new BinaryWriter(dataStream, Encoding.UTF8))
				{
					dataWriter.Write(LazyMagicNumber);
					dataWriter.Write(LazyVersion);
					dataWriter.Write(validLabels.Count);
					foreach (FunctionLabelLine label in validLabels)
					{
						dataWriter.Write(label.LabelName);
						dataWriter.Write(NormalizeRelativePath(label.Position.Filename));
					}
				}

				WriteLazyFileMeta(metaFiles);
			}
			catch (Exception e)
			{
				console.PrintSystemLine("LazyLoading: failed to save index table: " + e.Message);
				LazyCurrentLazyStatus = LazyStatus.Error;
				return;
			}

			LazyCurrentLazyStatus = LazyStatus.Loaded;
		}

		public bool SavePartialLazyLoadingList(List<FunctionLabelLine> labels)
		{
			var validLabelsToAppend = new List<FunctionLabelLine>();
			var labelFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (string file in ChangedFiles.ToList())
			{
				bool valid = true;
				bool anyLabel = false;
				foreach (FunctionLabelLine label in labels)
				{
					if (label.Position == null || !string.Equals(NormalizeRelativePath(label.Position.Filename), file, StringComparison.OrdinalIgnoreCase))
						continue;

					anyLabel = true;
					if (label.IsEvent || label.IsMethod)
					{
						valid = false;
						break;
					}

					validLabelsToAppend.Add(label);
					labelFiles.Add(file);
				}

				if (!valid || !anyLabel)
					ChangedFiles.Remove(file);
			}

			if (ChangedFiles.Count == 0 && DeletedFiles.Count == 0)
			{
				LazyCurrentLazyStatus = LazyStatus.Loaded;
				return false;
			}

			try
			{
				var unchangedLabels = lazyLoadingTable
					.SelectMany(item => item.Value.Select(path => new KeyValuePair<string, string>(item.Key, RelativeErbPath(path))))
					.ToList();

				EnsureLazyLoadingWorkingDir();
				using (var dataStream = new FileStream(LazyLoadingDataFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
				using (var dataWriter = new BinaryWriter(dataStream, Encoding.UTF8))
				{
					dataWriter.Write(LazyMagicNumber);
					dataWriter.Write(LazyVersion);
					dataWriter.Write(validLabelsToAppend.Count + unchangedLabels.Count);

					foreach (FunctionLabelLine label in validLabelsToAppend)
					{
						dataWriter.Write(label.LabelName);
						dataWriter.Write(NormalizeRelativePath(label.Position.Filename));
					}

					foreach (var item in unchangedLabels)
					{
						dataWriter.Write(item.Key);
						dataWriter.Write(item.Value);
					}
				}

				var metaFiles = new HashSet<string>(lazyLoadingFilesTable.Keys, StringComparer.OrdinalIgnoreCase);
				metaFiles.UnionWith(labelFiles);
				WriteLazyFileMeta(metaFiles);
			}
			catch (Exception e)
			{
				console.PrintSystemLine("LazyLoading: failed to update index table: " + e.Message);
				LazyCurrentLazyStatus = LazyStatus.Error;
				return false;
			}

			LazyCurrentLazyStatus = LazyStatus.Loaded;
			return true;
		}

		private static void WriteLazyFileMeta(IEnumerable<string> files)
		{
			using (var metaStream = new FileStream(LazyLoadingFilesFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
			using (var metaWriter = new BinaryWriter(metaStream, Encoding.UTF8))
			{
				var list = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
					metaWriter.Write(LazyMagicNumber);
					metaWriter.Write(LazyVersion);
					metaWriter.Write(list.Count);
					foreach (string name in list)
					{
						metaWriter.Write(NormalizeRelativePath(name));
						metaWriter.Write(GetLazyFileTimestamp(ErbPath(name)));
					}
				}
			}

		private static string ErbPath(string relativePath)
		{
			return Path.Combine(Program.ErbDir, NormalizeRelativePath(relativePath));
		}

		private static string GetLazyLoadingWorkingDir()
		{
			string gameDir = !string.IsNullOrEmpty(Program.WorkingDir) ? Program.WorkingDir : Program.ExeDir;
			gameDir = uEmuera.Utils.NormalizePath(gameDir);

			if (string.Equals(cachedLazyLoadingSourceDir, gameDir, StringComparison.OrdinalIgnoreCase)
				&& !string.IsNullOrEmpty(cachedLazyLoadingWorkingDir))
				return cachedLazyLoadingWorkingDir;

			if (!IsAndroid())
				return CacheLazyLoadingWorkingDir(gameDir, gameDir);

			if (CanWriteLazyLoadingIndexTo(gameDir))
				return CacheLazyLoadingWorkingDir(gameDir, gameDir);

			string userRoot = Godot.OS.GetUserDataDir();
			if (string.IsNullOrEmpty(userRoot))
				userRoot = Godot.ProjectSettings.GlobalizePath("user://");
			string fallbackDir = Path.Combine(userRoot, "lazyloading", StablePathId(gameDir));
			return CacheLazyLoadingWorkingDir(gameDir, uEmuera.Utils.NormalizePath(fallbackDir));
		}

		private static string CacheLazyLoadingWorkingDir(string sourceDir, string workingDir)
		{
			cachedLazyLoadingSourceDir = sourceDir;
			cachedLazyLoadingWorkingDir = workingDir;
			return cachedLazyLoadingWorkingDir;
		}

		private static void EnsureLazyLoadingWorkingDir()
		{
			string dir = GetLazyLoadingWorkingDir();
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
				Directory.CreateDirectory(dir);
		}

		private static bool IsAndroid()
		{
			return string.Equals(Godot.OS.GetName(), "Android", StringComparison.OrdinalIgnoreCase);
		}

		private static bool CanWriteLazyLoadingIndexTo(string path)
		{
			if (string.IsNullOrEmpty(path))
				return false;
			string normalized = uEmuera.Utils.NormalizePath(path);
			if (normalized.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
				return false;
			if (!Directory.Exists(normalized))
				return false;

			string probePath = Path.Combine(normalized, ".lazyloading_write_test_" + Guid.NewGuid().ToString("N") + ".tmp");
			try
			{
				using (var stream = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1))
					stream.WriteByte(0);
				File.Delete(probePath);
				return true;
			}
			catch
			{
				try
				{
					if (File.Exists(probePath))
						File.Delete(probePath);
				}
				catch
				{
				}
				return false;
			}
		}

		private static string StablePathId(string path)
		{
			unchecked
			{
				ulong hash = 14695981039346656037UL;
				string value = (path ?? "").ToUpperInvariant();
				for (int i = 0; i < value.Length; i++)
				{
					hash ^= value[i];
					hash *= 1099511628211UL;
				}
				return hash.ToString("X16");
			}
		}

		private static long GetLazyFileTimestamp(string path)
		{
			if (IsAndroid())
				return uEmuera.Utils.GetLastWriteTimeKey(path);
			return File.GetLastWriteTime(path).ToFileTimeUtc();
		}

		private static string RelativeErbPath(string path)
		{
			string fullPath = Path.GetFullPath(path);
			string erbDir = Path.GetFullPath(Program.ErbDir);
			if (fullPath.StartsWith(erbDir, StringComparison.OrdinalIgnoreCase))
				return NormalizeRelativePath(fullPath.Substring(erbDir.Length));
			return NormalizeRelativePath(path);
		}

		private static string NormalizeRelativePath(string path)
		{
			return (path ?? "").Trim().Replace('\\', '/').TrimStart('/');
		}

		private static string NormalizeFullPath(string path)
		{
			if (string.IsNullOrEmpty(path))
				return "";
			return Path.GetFullPath(path).Replace('\\', '/');
		}
	}
}
