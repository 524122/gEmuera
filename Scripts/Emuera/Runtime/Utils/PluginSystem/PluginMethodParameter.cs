using MinorShift.Emuera.GameData.Expression;

namespace MinorShift.Emuera.Runtime.Utils.PluginSystem
{
	public class PluginMethodParameter
	{
		public PluginMethodParameter(string initialValue)
		{
			isString = true;
			strValue = initialValue;
		}

		public PluginMethodParameter(long initialValue)
		{
			isString = false;
			intValue = initialValue;
		}

		public PluginMethodParameter(double initialValue)
		{
			isString = false;
			isFloat = true;
			floatValue = initialValue;
		}

		public bool isString;
		public bool isFloat;
		public string strValue;
		public long intValue;
		public double floatValue;
	}

	internal static class PluginMethodParameterBuilder
	{
		internal static PluginMethodParameter ConvertTerm(IOperandTerm term, ExpressionMediator exm)
		{
			if (term.IsString)
				return new PluginMethodParameter(term.GetStrValue(exm));
			if (term.IsFloat)
				return new PluginMethodParameter(term.GetFloatValue(exm));
			return new PluginMethodParameter(term.GetIntValue(exm));
		}
	}
}
