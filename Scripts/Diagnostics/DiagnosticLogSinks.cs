using System;
using System.Text;
using System.Threading;
using Godot;

namespace gEmuera.Diagnostics
{
	/// <summary>
	/// 企业级说明：日志 Sink 层只负责接收已通过路由和限流的记录，写入内存 ring buffer 和 Godot 控制台。
	/// 不做文件 I/O，不构造 message，不感知业务模块。
	/// Android 热路径只操作预分配数组和 Interlocked 计数，避免 GC。
	/// </summary>
	public static class DiagnosticLogSinks
	{
		static RuntimeDiagnosticsConfig _config;
		static long _overwrittenTotal;
		static readonly object _ringLock = new object();
		static DiagnosticLogRecord[] _ring;
		static int _ringStart;
		static int _ringCount;
		static int _ringCapacity;
		static int _mirrorNonErrorToGodot;

		public static void Initialize(RuntimeDiagnosticsConfig config)
		{
			_config = config ?? RuntimeDiagnosticsConfig.CreateDefault();
			if (!_config.LoggingEnabled)
			{
				_ringCapacity = 0;
				_ring = null;
				_ringStart = 0;
				_ringCount = 0;
				_overwrittenTotal = 0;
				_mirrorNonErrorToGodot = 0;
				return;
			}
			_ringCapacity = Math.Max(64, _config.LoggingDiagnosticRingCapacity);
			_ring = new DiagnosticLogRecord[_ringCapacity];
			_ringStart = 0;
			_ringCount = 0;
			_overwrittenTotal = 0;
			_mirrorNonErrorToGodot = _config.LoggingMirrorNonErrorToGodot ? 1 : 0;
		}

		public static int RingCapacity => _ringCapacity;
		public static int RingCount => Volatile.Read(ref _ringCount);
		public static long OverwrittenTotal => Interlocked.Read(ref _overwrittenTotal);

		public static void SetMirrorNonErrorToGodot(bool value)
		{
			Volatile.Write(ref _mirrorNonErrorToGodot, value ? 1 : 0);
		}

		/// <summary>
		/// 将单条记录同时写入 ring buffer 和 Godot 控制台（如果等级/策略允许）。
		/// 调用方已确保通过限流和开关检查，本方法只做最低开销的写入。
		/// </summary>
		public static void Write(in DiagnosticLogRecord record)
		{
			if (_config == null || !_config.LoggingEnabled)
				return;
			AppendToRing(record);
			if (ShouldMirrorToGodot(record.Level))
				WriteToGodotConsole(record);
		}

		static void AppendToRing(in DiagnosticLogRecord record)
		{
			lock (_ringLock)
			{
				if (_ring == null)
					return;
				if (_ringCount >= _ringCapacity)
				{
					_ring[_ringStart] = record;
					_ringStart = (_ringStart + 1) % _ringCapacity;
					Interlocked.Increment(ref _overwrittenTotal);
				}
				else
				{
					int index = (_ringStart + _ringCount) % _ringCapacity;
					_ring[index] = record;
					_ringCount++;
				}
			}
		}

		static bool ShouldMirrorToGodot(EmueraLogLevel level)
		{
			return level >= EmueraLogLevel.Error || Volatile.Read(ref _mirrorNonErrorToGodot) != 0;
		}

		static void WriteToGodotConsole(in DiagnosticLogRecord record)
		{
			string message = record.FormatForGodot();
			switch (record.Level)
			{
				case EmueraLogLevel.Warn:
					GD.PushWarning(message);
					break;
				case EmueraLogLevel.Error:
					 GD.PushError(message);
					break;
				default:
					GD.Print(message);
					break;
			}
		}

		public static DiagnosticLogRecord[] Snapshot()
		{
			lock (_ringLock)
			{
				if (_ring == null || _ringCount == 0)
					return Array.Empty<DiagnosticLogRecord>();
				var result = new DiagnosticLogRecord[_ringCount];
				for (int i = 0; i < result.Length; i++)
					result[i] = _ring[(_ringStart + i) % _ringCapacity];
				return result;
			}
		}
	}
}
