using System;
using System.Collections.Generic;

namespace MinorShift.Emuera.GameProc
{
	internal sealed class ExecutionContext
	{
		public FunctionLabelLine Function { get; }
		public long[] LocalIntegers { get; }
		public string[] LocalStrings { get; }
		public double[] LocalFloats { get; }
		public long[] ArgIntegers { get; set; }
		public string[] ArgStrings { get; set; }
		public double[] ArgFloats { get; set; }

		ExecutionContext parent;
		readonly List<ExecutionContext> children = new List<ExecutionContext>();

		public ExecutionContext(FunctionLabelLine func, ExecutionContext parent)
		{
			Function = func;
			this.parent = parent;
			parent?.children.Add(this);

			int localLen = func.LocalLength;
			int localsLen = func.LocalsLength;
			int localFloatLen = func.LocalFloatLength;
			int argLen = func.ArgLength;
			int argsLen = func.ArgsLength;
			int argFloatLen = func.ArgFloatLength;

			var idDict = GlobalStatic.IdentifierDictionary;
			if (idDict != null)
			{
				int defaultLocal = idDict.getLocalDefaultSize("LOCAL");
				int defaultLocals = idDict.getLocalDefaultSize("LOCALS");
				int defaultArg = idDict.getLocalDefaultSize("ARG");
				int defaultArgs = idDict.getLocalDefaultSize("ARGS");
				int defaultLocalF = idDict.getLocalDefaultSize("LOCALF");
				int defaultArgF = idDict.getLocalDefaultSize("ARGF");

				if (localLen <= 0)
					localLen = defaultLocal;
				if (localsLen <= 0)
					localsLen = defaultLocals;
				if (argLen <= 0)
					argLen = defaultArg;
				else if (argLen < defaultArg)
					argLen = defaultArg;
				if (argsLen <= 0)
					argsLen = defaultArgs;
				else if (argsLen < defaultArgs)
					argsLen = defaultArgs;
				if (localFloatLen <= 0)
					localFloatLen = defaultLocalF;
				if (argFloatLen <= 0)
					argFloatLen = defaultArgF;
				else if (argFloatLen < defaultArgF)
					argFloatLen = defaultArgF;
			}
			else
			{
				if (localLen <= 0)
					localLen = 1000;
				if (localsLen <= 0)
					localsLen = 100;
				if (argLen <= 0)
					argLen = 1000;
				else if (argLen < 1000)
					argLen = 1000;
				if (argsLen <= 0)
					argsLen = 100;
				else if (argsLen < 100)
					argsLen = 100;
			}

			// 局部变量数组归属到调用上下文，避免同一个 LocalVariableToken 在递归/嵌套调用中共享运行期存储。
			LocalIntegers = new long[Math.Max(localLen, 0)];
			LocalStrings = new string[Math.Max(localsLen, 0)];
			LocalFloats = new double[Math.Max(localFloatLen, 0)];
			ArgIntegers = new long[Math.Max(argLen, 0)];
			ArgStrings = new string[Math.Max(argsLen, 0)];
			ArgFloats = new double[Math.Max(argFloatLen, 0)];
		}

		public ExecutionContext Parent
		{
			get { return parent; }
		}

		public void Dispose()
		{
			parent?.children.Remove(this);
			foreach (ExecutionContext child in children)
				child.parent = null;
			children.Clear();
		}
	}
}
