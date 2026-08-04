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
			// 直接转发 GetFloatValue，避免 GetReturnValue 先分配 SingleTerm 再取回的额外开销。
			// 语义等价：GetReturnValue 对 Float 返回类型内部就是调用 GetFloatValue 构造 SingleTerm。
			return method.GetFloatValue(exm, arguments);
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
