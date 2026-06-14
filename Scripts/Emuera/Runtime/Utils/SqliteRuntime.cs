using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Data.Sqlite;

namespace MinorShift.Emuera.Runtime.Utils
{
	internal static class SqliteRuntime
	{
		static bool initialized;
		static bool resolverInstalled;
		static readonly object initLock = new object();

		public static void EnsureInitialized()
		{
			if (initialized)
				return;
			lock (initLock)
			{
				if (initialized)
					return;

				GenericUtils.Info("Initializing SQLite runtime...", EmueraLogCategory.SQL);
				GenericUtils.Info($"Platform: {Godot.OS.GetName()}, BaseDirectory: {AppContext.BaseDirectory}", EmueraLogCategory.SQL);

				try
				{
					InstallNativeResolver();
					SQLitePCL.Batteries_V2.Init();
					initialized = true;
					GenericUtils.Info("SQLite runtime initialized successfully", EmueraLogCategory.SQL);
				}
				catch (Exception ex)
				{
					GenericUtils.Error($"Failed to initialize SQLite runtime: {FormatException(ex)}", EmueraLogCategory.SQL);
					throw;
				}
			}
		}

		static void InstallNativeResolver()
		{
			if (resolverInstalled)
				return;

			resolverInstalled = true;
			try
			{
				Assembly providerAssembly = Assembly.Load("SQLitePCLRaw.provider.e_sqlite3");
				NativeLibrary.SetDllImportResolver(providerAssembly, ResolveSqliteNativeLibrary);
			}
			catch (InvalidOperationException)
			{
				// Another startup path already installed a resolver for the provider assembly.
			}
			catch
			{
				// Let SQLitePCLRaw use its default loader; callers will report the concrete failure.
			}
		}

		static IntPtr ResolveSqliteNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
		{
			if (!IsSqliteNativeName(libraryName))
				return IntPtr.Zero;

			GenericUtils.Debug($"Resolving SQLite native library: {libraryName}", EmueraLogCategory.SQL);

			foreach (string candidate in GetNativeLibraryCandidates())
			{
				GenericUtils.Debug($"Trying candidate: {candidate}", EmueraLogCategory.SQL);
				if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out IntPtr handle))
				{
					GenericUtils.Info($"Successfully loaded SQLite native library from: {candidate}", EmueraLogCategory.SQL);
					return handle;
				}
				if (NativeLibrary.TryLoad(candidate, out handle))
				{
					GenericUtils.Info($"Successfully loaded SQLite native library from: {candidate}", EmueraLogCategory.SQL);
					return handle;
				}
			}

			GenericUtils.Error($"Failed to load SQLite native library: {libraryName}", EmueraLogCategory.SQL);
			return IntPtr.Zero;
		}

		static bool IsSqliteNativeName(string libraryName)
		{
			return string.Equals(libraryName, "e_sqlite3", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(libraryName, "libe_sqlite3.so", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(libraryName, "e_sqlite3.dll", StringComparison.OrdinalIgnoreCase);
		}

		static IEnumerable<string> GetNativeLibraryCandidates()
		{
			yield return "e_sqlite3";
			yield return "libe_sqlite3.so";

			foreach (string dir in GetNativeSearchDirectories())
			{
				if (string.IsNullOrWhiteSpace(dir))
					continue;
				yield return Path.Combine(dir, "libe_sqlite3.so");
				yield return Path.Combine(dir, "e_sqlite3.dll");
			}
		}

		static IEnumerable<string> GetNativeSearchDirectories()
		{
			// 标准路径
			yield return AppContext.BaseDirectory;
			yield return Path.GetDirectoryName(typeof(SqliteRuntime).Assembly.Location);
			yield return Directory.GetCurrentDirectory();

			// Android 特定路径
			if (Godot.OS.GetName() == "Android")
			{
				// 尝试从可执行路径推断应用目录
				string execPath = Godot.OS.GetExecutablePath();
				if (!string.IsNullOrEmpty(execPath))
				{
					// APK 路径通常是 /data/app/<package>/base.apk
					string appDir = Path.GetDirectoryName(execPath);
					if (!string.IsNullOrEmpty(appDir))
					{
						// lib 目录在 /data/app/<package>/lib/arm64
						yield return Path.Combine(appDir, "lib", "arm64");
						yield return Path.Combine(appDir, "lib");
					}
				}

				// 标准 Android native library 路径
				yield return "/data/data/com.godot.game/lib"; // Godot 默认包名
				yield return "/data/data/com.godotengine.gemuera/lib"; // 可能的自定义包名

				// APK 内部路径
				yield return Path.Combine(AppContext.BaseDirectory, "lib", "arm64-v8a");
				yield return Path.Combine(AppContext.BaseDirectory, "lib");

				// 系统库路径
				yield return "/system/lib64";
				yield return "/system/lib";
				yield return "/data/local/tmp";
			}

			// Linux LD_LIBRARY_PATH
			string ldPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
			if (!string.IsNullOrWhiteSpace(ldPath))
			{
				foreach (string dir in ldPath.Split(':'))
				{
					if (!string.IsNullOrWhiteSpace(dir))
						yield return dir;
				}
			}
		}

		public static string NormalizeConnectionString(string connectionString, string baseDirectory)
		{
			if (string.IsNullOrWhiteSpace(connectionString))
				return "Data Source=:memory:";

			try
			{
				var builder = new SqliteConnectionStringBuilder(connectionString);
				string dataSource = builder.DataSource;
				if (!string.IsNullOrWhiteSpace(dataSource)
					&& dataSource != ":memory:"
					&& dataSource.IndexOf(":", StringComparison.Ordinal) < 0
					&& !Path.IsPathRooted(dataSource)
					&& !string.IsNullOrWhiteSpace(baseDirectory))
				{
					string path = Path.Combine(baseDirectory, dataSource.Replace('/', Path.DirectorySeparatorChar));
					string dir = Path.GetDirectoryName(path);
					if (!string.IsNullOrEmpty(dir))
						Directory.CreateDirectory(dir);
					builder.DataSource = path;
				}
				return builder.ConnectionString;
			}
			catch
			{
				return connectionString;
			}
		}

		public static string FormatException(Exception exception)
		{
			var builder = new StringBuilder();
			for (Exception current = exception; current != null; current = current.InnerException)
			{
				if (builder.Length > 0)
					builder.Append(" -> ");
				builder.Append(current.GetType().Name);
				if (!string.IsNullOrEmpty(current.Message))
				{
					builder.Append(": ");
					builder.Append(current.Message);
				}
			}
			return builder.ToString();
		}
	}
}
