using System;
using System.Collections.Generic;
using System.Text;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.Sub;

namespace MinorShift.Emuera.GameData.Function
{
	internal abstract class FunctionMethod
	{
		public EraType ReturnType { get; protected set; }
		/// <summary>
		/// 数学系函数（MAX/MIN/ABS/POWER/SQRT 等）在任一参数为 Float 时，返回类型可动态切换为 Float。
		/// FunctionMethodTerm 依赖该标志做动态类型解析（与 snake 参考实现一致）。
		/// </summary>
		public bool CanReturnFloat { get; protected set; }
		protected EraType[] argumentTypeArray;
		protected string Name { get; private set; }

		//引数の数・型が一致するかどうかのテスト
		//正しくない場合はエラーメッセージを返す。
		//引数の数が不定である場合や引数の省略を許す場合にはoverrideすること。
		public virtual string CheckArgumentType(string name, IOperandTerm[] arguments)
		{
			if (arguments.Length != argumentTypeArray.Length)
				return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum0, name);
			for (int i = 0; i < argumentTypeArray.Length; i++)
			{
				if (arguments[i] == null)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNotNullable0, name, i+1);
				if (argumentTypeArray[i] != arguments[i].GetEraType())
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, i + 1);
			}
			return null;
		}
		
		//Argumentが全て定数の時にMethodを解体してよいかどうか。RANDやCharaを参照するものなどは不可
		public bool CanRestructure { get; protected set; }

		//FunctionMethodが固有のRestructure()を持つかどうか
		public bool HasUniqueRestructure { get; protected set; }

		//実際の計算。
		public virtual Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments) { throw new ExeEE("戻り値の型が違う or 未実装"); }
		public virtual string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments) { throw new ExeEE("戻り値の型が違う or 未実装"); }
		public virtual double GetFloatValue(ExpressionMediator exm, IOperandTerm[] arguments) { throw new ExeEE("戻り値の型が違う or 未実装"); }
		public virtual SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			if (ReturnType == EraType.Integer)
				return new SingleTerm(GetIntValue(exm, arguments));
			else if (ReturnType == EraType.Float)
				return new SingleTerm(GetFloatValue(exm, arguments));
			else
				return new SingleTerm(GetStrValue(exm, arguments));
		}

		protected bool MatchesArgumentType(int index, IOperandTerm argument)
		{
			return argumentTypeArray[index] == argument.GetEraType();
		}

		/// <summary>
		/// 戻り値は全体をRestructureできるかどうか
		/// </summary>
		/// <param name="exm"></param>
		/// <param name="arguments"></param>
		/// <returns></returns>
		public virtual bool UniqueRestructure(ExpressionMediator exm, IOperandTerm[] arguments)
		{ throw new ExeEE("未実装？"); }


		internal void SetMethodName(string name)
		{
			Name = name;
		}
	}

	/// <summary>
	/// Profile-scoped projection for legacy methods whose public ERB contract differs
	/// between v24 and Snake. The legacy implementation remains the execution owner;
	/// this immutable wrapper freezes the selected argument surface when the registry
	/// is projected, so argument validation does not read the profile on every call.
	/// </summary>
	internal sealed class DialectFunctionMethod : FunctionMethod
	{
		private readonly FunctionMethod inner;
		private readonly Func<string, IOperandTerm[], string> checker;
		private readonly Func<IOperandTerm[], IOperandTerm[]> normalizer;

		internal DialectFunctionMethod(
			FunctionMethod inner,
			EraType[] declaredArgumentTypes,
			Func<string, IOperandTerm[], string> checker,
			Func<IOperandTerm[], IOperandTerm[]> normalizer = null)
		{
			this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
			this.checker = checker ?? throw new ArgumentNullException(nameof(checker));
			this.normalizer = normalizer;
			ReturnType = inner.ReturnType;
			CanReturnFloat = inner.CanReturnFloat;
			argumentTypeArray = declaredArgumentTypes;
			CanRestructure = inner.CanRestructure;
			HasUniqueRestructure = inner.HasUniqueRestructure;
		}

		private IOperandTerm[] Prepare(IOperandTerm[] arguments)
		{
			return normalizer == null ? arguments : normalizer(arguments);
		}

		public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		{
			return checker(name, arguments);
		}

		public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			return inner.GetIntValue(exm, Prepare(arguments));
		}

		public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			return inner.GetStrValue(exm, Prepare(arguments));
		}

		public override double GetFloatValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			return inner.GetFloatValue(exm, Prepare(arguments));
		}

		public override SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			return inner.GetReturnValue(exm, Prepare(arguments));
		}

		public override bool UniqueRestructure(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			return inner.UniqueRestructure(exm, Prepare(arguments));
		}
	}
}
