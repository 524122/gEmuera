using System;
using System.Collections.Generic;

namespace MinorShift.Emuera.GameData.Variable
{
	/// <summary>
	/// 1D 变量用の疎配列。Emuera では巨大な配列でも実際に触る要素が少ないゲームが多いため、
	/// Android の初期メモリを抑える目的で snake 実装と同じ「未設定要素は既定値」を採用する。
	/// 2D/3D は既存コードが CLR 多次元配列契約に強く依存するため、本クラスは 1D 専用に留める。
	/// </summary>
	internal sealed class SparseArray<T>
	{
		readonly T defaultValue;
		Dictionary<long, T> data;
		int logicalLength;

		public SparseArray(T defaultValue = default)
		{
			this.defaultValue = defaultValue;
			data = new Dictionary<long, T>();
		}

		public int Length
		{
			get { return logicalLength; }
			set { logicalLength = value; }
		}

		public int Count
		{
			get { return data.Count; }
		}

		public IEnumerable<KeyValuePair<long, T>> Entries
		{
			get { return data; }
		}

		public T this[long index]
		{
			get
			{
				if (data.TryGetValue(index, out T value))
					return value;
				return defaultValue;
			}
			set
			{
				if (EqualityComparer<T>.Default.Equals(value, defaultValue))
					data.Remove(index);
				else
					data[index] = value;
			}
		}

		public void Clear()
		{
			data.Clear();
		}

		public bool ContainsIndex(long index)
		{
			return data.ContainsKey(index);
		}

		public void Remove(long index)
		{
			data.Remove(index);
		}

		public void FromArray(T[] source)
		{
			data.Clear();
			if (source == null)
				return;
			for (int i = 0; i < source.Length; i++)
				this[i] = source[i];
			logicalLength = source.Length;
		}

		public T[] ToArray()
		{
			return ToArray(logicalLength);
		}

		public T[] ToArray(int length)
		{
			T[] result = new T[length];
			for (int i = 0; i < length; i++)
				result[i] = this[i];
			return result;
		}

		public void CopyFrom(IList<T> source)
		{
			data.Clear();
			if (source == null)
				return;
			int count = Math.Min(source.Count, logicalLength);
			for (int i = 0; i < count; i++)
				this[i] = source[i];
		}

		public void CopyTo(T[] destination)
		{
			if (destination == null)
				return;
			int count = Math.Min(destination.Length, logicalLength);
			for (int i = 0; i < count; i++)
				destination[i] = this[i];
		}

		public void Shift(int offset, T fillValue, int start, int count)
		{
			if (offset == 0 || count <= 0)
				return;

			int end = Math.Min(start + count, logicalLength);
			Dictionary<long, T> shifted = new Dictionary<long, T>();
			foreach (KeyValuePair<long, T> pair in data)
			{
				long key = pair.Key;
				if (key < start || key >= end)
				{
					shifted[key] = pair.Value;
					continue;
				}

				long newKey = key + offset;
				if (newKey >= start && newKey < end)
					shifted[newKey] = pair.Value;
			}

			data = shifted;
			if (!EqualityComparer<T>.Default.Equals(fillValue, defaultValue))
			{
				if (offset > 0)
				{
					long fillEnd = Math.Min(start + offset, end);
					for (long i = start; i < fillEnd; i++)
						data[i] = fillValue;
				}
				else
				{
					long fillStart = Math.Max(start, end + offset);
					for (long i = fillStart; i < end; i++)
						data[i] = fillValue;
				}
			}
		}

		public void RemoveRange(int start, int count)
		{
			if (count <= 0)
				return;
			int end = Math.Min(start + count, logicalLength);
			Dictionary<long, T> compacted = new Dictionary<long, T>();
			foreach (KeyValuePair<long, T> pair in data)
			{
				long key = pair.Key;
				if (key < start)
					compacted[key] = pair.Value;
				else if (key >= end)
					compacted[key - count] = pair.Value;
			}
			data = compacted;
		}

		public void Sort(bool ascending, int start, int count)
		{
			int end = Math.Min(start + count, logicalLength);
			if (end <= start)
				return;
			T[] values = new T[end - start];
			for (int i = 0; i < values.Length; i++)
				values[i] = this[start + i];
			Array.Sort(values);
			if (!ascending)
				Array.Reverse(values);
			for (int i = 0; i < values.Length; i++)
				this[start + i] = values[i];
		}
	}
}
