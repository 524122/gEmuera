using System;
using System.Collections.Generic;
using System.Text;
using MinorShift.Emuera.GameData.Expression;

namespace MinorShift.Emuera.GameData.Function
{
	internal sealed class FunctionMethodTerm : IOperandTerm
	{
		public FunctionMethodTerm(FunctionMethod meth, IOperandTerm[] args)
			: base(meth.ReturnType)
		{
			method = meth;
			arguments = args;
		}

		private FunctionMethod method;
		private IOperandTerm[] arguments;

        public override long GetIntValue(ExpressionMediator exm)
        {
			if (GetEraType() == EraType.Float)
				return (Int64)GetFloatValue(exm);
			return method.GetIntValue(exm, arguments);
        }
        public override string GetStrValue(ExpressionMediator exm)
        {
			return method.GetStrValue(exm, arguments);
        }
        public override double GetFloatValue(ExpressionMediator exm)
        {
			// 直接转发 GetFloatValue（方法重写它时），避免 GetReturnValue 先分配 SingleTerm 再取回的额外开销。
			// 语义等价：基类 GetReturnValue 对 Float 返回类型内部就是调用 GetFloatValue 构造 SingleTerm。
			// 少数 Float 方法只重写 GetReturnValue 而未重写 GetFloatValue（TOFLOAT/GETMETHF/GETVARF/
			// SQL_READER_GET_FLOAT/SQL_EXECUTE_SCALAR_FLOAT/SQL_P_EXECUTE_SCALAR_FLOAT/DT_CELL_GETF），
			// 基类 GetFloatValue 会抛"未实现"；此时回退 GetReturnValue 路径，与旧实现完全一致。
			return HasFloatValueOverride(method)
				? method.GetFloatValue(exm, arguments)
				: method.GetReturnValue(exm, arguments).GetFloatValue(exm);
        }

		// 方法具体类型是否 override 了 FunctionMethod.GetFloatValue(exm, arguments)。
		// 用 ConcurrentDictionary 静态缓存：每种 FunctionMethod 子类型只反射判定一次，
		// 热路径命中为无锁 O(1) 查表。
		static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, bool> floatOverrideCache =
			new System.Collections.Concurrent.ConcurrentDictionary<Type, bool>();
		static bool HasFloatValueOverride(FunctionMethod method)
		{
			return floatOverrideCache.GetOrAdd(method.GetType(), static t =>
				t.GetMethod("GetFloatValue", new[] { typeof(ExpressionMediator), typeof(IOperandTerm[]) })?.DeclaringType != typeof(FunctionMethod));
		}
		public override SingleTerm GetValue(ExpressionMediator exm)
		{
			return method.GetReturnValue(exm, arguments);
		}
		
        public override IOperandTerm Restructure(ExpressionMediator exm)
        {
			if (method.HasUniqueRestructure)
			{
				if (method.UniqueRestructure(exm, arguments) && method.CanRestructure)
					return GetValue(exm);
				return this;
			}
			bool argIsConst = true;
			for(int i = 0; i< arguments.Length;i++)
			{
				if(arguments[i] == null)
					continue;
				arguments[i] = arguments[i].Restructure(exm);
				argIsConst &= arguments[i] is SingleTerm;
			}
			if ((method.CanRestructure) && (argIsConst))
				return GetValue(exm);
			return this;
			
        }
        
	}
}
