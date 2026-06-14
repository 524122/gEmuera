using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using MinorShift.Emuera.Sub;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.GameData;
using MinorShift.Emuera.GameData.Function;
using MinorShift.Emuera.GameProc.Function;

namespace MinorShift.Emuera.GameProc
{
	internal static class LogicalLineParser
	{
		private static readonly Regex SnakeDimfSizeFloatLiteralRegex =
			new Regex(@"(?<![A-Za-z0-9_])([+-]?\d+)\.\d+(?![A-Za-z0-9_])", RegexOptions.Compiled);

		static EraType GetFunctionReturnType(string token)
		{
			if (token == "FUNCTIONS")
				return EraType.String;
			if (token == "FUNCTIONF")
				return EraType.Float;
			return EraType.Integer;
		}

		public static bool ParseSharpLine(FunctionLabelLine label, StringStream st, ScriptPosition position, List<string> OnlyLabel)
		{
			st.ShiftNext();//'#'を飛ばす
			string token = LexicalAnalyzer.ReadSingleIdentifier(st);//#～自体にはマクロ非適用
			if (Config.ICFunction)
				token = token.ToUpper();
            //#行として不正な行でもAnalyzeに行って引っかかることがあるので、空の#～だけは先に弾く
            if (string.IsNullOrEmpty(token))
            {
                ParserMediator.Warn("解釈できない#行です", position, 1);
                return false;
            }
			try
			{
				WordCollection wc = null;
				switch (token)
				{
					case "SINGLE":
						if (label.IsMethod)
						{
							ParserMediator.Warn("式中関数では#SINGLEは機能しません", position, 1);
							break;
						}
						else if (!label.IsEvent)
						{
							ParserMediator.Warn("イベント関数以外では#SINGLEは機能しません", position, 1);
							break;
						}
						else if (label.IsSingle)
						{
							ParserMediator.Warn("#SINGLEが重複して使われています", position, 1);
							break;
						}
						else if (label.IsOnly)
						{
							ParserMediator.Warn("#ONLYが指定されたイベント関数では#SINGLEは機能しません", position, 1);
							break;
						}
						label.IsSingle = true;
						break;
					case "LATER":
						if (label.IsMethod)
						{
							ParserMediator.Warn("式中関数では#LATERは機能しません", position, 1);
							break;
						}
						else if (!label.IsEvent)
						{
							ParserMediator.Warn("イベント関数以外では#LATERは機能しません", position, 1);
							break;
						}
						else if (label.IsLater)
						{
							ParserMediator.Warn("#LATERが重複して使われています", position, 1);
							break;
						}
						else if (label.IsOnly)
						{
							ParserMediator.Warn("#ONLYが指定されたイベント関数では#LATERは機能しません", position, 1);
							break;
						}
						else if (label.IsPri)
							ParserMediator.Warn("#PRIと#LATERが重複して使われています(この関数は2度呼ばれます)", position, 1);
						label.IsLater = true;
						break;
					case "PRI":
						if (label.IsMethod)
						{
							ParserMediator.Warn("式中関数では#PRIは機能しません", position, 1);
							break;
						}
						else if (!label.IsEvent)
						{
							ParserMediator.Warn("イベント関数以外では#PRIは機能しません", position, 1);
							break;
						}
						else if (label.IsPri)
						{
							ParserMediator.Warn("#PRIが重複して使われています", position, 1);
							break;
						}
						else if (label.IsOnly)
						{
							ParserMediator.Warn("#ONLYが指定されたイベント関数では#PRIは機能しません", position, 1);
							break;
						}
						else if (label.IsLater)
							ParserMediator.Warn("#PRIと#LATERが重複して使われています(この関数は2度呼ばれます)", position, 1);
						label.IsPri = true;
						break;
					case "ONLY":
						if (label.IsMethod)
						{
							ParserMediator.Warn("式中関数では#ONLYは機能しません", position, 1);
							break;
						}
						else if (!label.IsEvent)
						{
							ParserMediator.Warn("イベント関数以外では#ONLYは機能しません", position, 1);
							break;
						}
						else if (label.IsOnly)
						{
							ParserMediator.Warn("#ONLYが重複して使われています", position, 1);
							break;
						}
						else if (OnlyLabel.Contains(label.LabelName))
							ParserMediator.Warn("このイベント関数\"@" + label.LabelName + "\"にはすでに#ONLYが宣言されています（この関数は実行されません）", position, 1);
						OnlyLabel.Add(label.LabelName);
						label.IsOnly = true;
						if (label.IsPri)
						{
							ParserMediator.Warn("このイベント関数には#PRIが宣言されていますが無視されます", position, 1);
							label.IsPri = false;
						}
						if (label.IsLater)
						{
							ParserMediator.Warn("このイベント関数には#LATERが宣言されていますが無視されます", position, 1);
							label.IsLater = false;
						}
						if (label.IsSingle)
						{
							ParserMediator.Warn("このイベント関数には#SINGLEが宣言されていますが無視されます", position, 1);
							label.IsSingle = false;
						}
						break;
					case "FUNCTION":
					case "FUNCTIONS":
					case "FUNCTIONF":
						if (!string.IsNullOrEmpty(label.LabelName) && char.IsDigit(label.LabelName[0]))
						{
							ParserMediator.Warn("#" + token + "属性は関数名が数字で始まる関数には指定できません", position, 1);
							label.IsError = true;
							label.ErrMes = "関数名が数字で始まっています";
							break;
						}
						if (label.IsMethod)
						{
							EraType requestedType = GetFunctionReturnType(token);
							if (label.MethodType == requestedType)
							{
								ParserMediator.Warn("関数" + label.LabelName + "にはすでに#" + token + "が宣言されています(この行は無視されます)", position, 1);
								return false;
							}
							if (label.MethodType == EraType.Integer && token == "FUNCTIONS")
								ParserMediator.Warn("関数" + label.LabelName + "にはすでに#FUNCTIONが宣言されています", position, 2);
							else if (label.MethodType == EraType.Integer && token == "FUNCTIONF")
								ParserMediator.Warn("関数" + label.LabelName + "にはすでに#FUNCTIONが宣言されています", position, 2);
							else if (label.MethodType == EraType.String && token == "FUNCTION")
								ParserMediator.Warn("関数" + label.LabelName + "にはすでに#FUNCTIONSが宣言されています", position, 2);
							else if (label.MethodType == EraType.String && token == "FUNCTIONF")
								ParserMediator.Warn("関数" + label.LabelName + "にはすでに#FUNCTIONSが宣言されています", position, 2);
							else if (label.MethodType == EraType.Float && token == "FUNCTION")
								ParserMediator.Warn("関数" + label.LabelName + "にはすでに#FUNCTIONFが宣言されています", position, 2);
							else if (label.MethodType == EraType.Float && token == "FUNCTIONS")
								ParserMediator.Warn("関数" + label.LabelName + "にはすでに#FUNCTIONFが宣言されています", position, 2);
							return false;
						}
						if (label.Depth == 0)
						{
							ParserMediator.Warn("システム関数に#" + token + "が指定されています", position, 2);
							return false;
						}
						label.IsMethod = true;
						label.Depth = 0;
						label.MethodType = GetFunctionReturnType(token);
						if (label.IsPri)
						{
							ParserMediator.Warn("式中関数では#PRIは機能しません", position, 1);
							label.IsPri = false;
						}
						if (label.IsLater)
						{
							ParserMediator.Warn("式中関数では#LATERは機能しません", position, 1);
							label.IsLater = false;
						}
						if (label.IsSingle)
						{
							ParserMediator.Warn("式中関数では#SINGLEは機能しません", position, 1);
							label.IsSingle = false;
						}
						if (label.IsOnly)
						{
							ParserMediator.Warn("式中関数では#ONLYは機能しません", position, 1);
							label.IsOnly = false;
						}
						break;
					case "LOCALSIZE":
					case "LOCALSSIZE":
					case "LOCALFSIZE":
						{
							wc = AnalyzeSharpArguments(st);
							if (wc.EOL)
							{
								ParserMediator.Warn("#" + token + "の後に有効な数値が指定されていません", position, 2);
								break;
							}
                            //イベント関数では指定しても無視される
                            if (label.IsEvent)
                            {
                                ParserMediator.Warn("イベント関数では#" + token + "による" + token.Substring(0, token.Length - 4)+ "のサイズ指定は無視されます", position, 1);
                                break;
                            }
							IOperandTerm arg = ExpressionParser.ReduceIntegerTerm(wc, TermEndWith.EoL);
                            if ((!(arg.Restructure(null) is SingleTerm sizeTerm)) || (sizeTerm.GetEraType() != EraType.Integer))
                            {
                                ParserMediator.Warn("#" + token + "の後に有効な定数式が指定されていません", position, 2);
                                break;
                            }
                            if (sizeTerm.Int <= 0)
							{
								ParserMediator.Warn("#" + token + "に0以下の値(" + sizeTerm.Int.ToString() + ")が与えられました。設定は無視されます", position, 1);
								break;
							}
							if (sizeTerm.Int >= Int32.MaxValue)
							{
								ParserMediator.Warn("#" + token + "に大きすぎる値(" + sizeTerm.Int.ToString() + ")が与えられました。設定は無視されます", position, 1);
								break;
							}
							int size = (int)sizeTerm.Int;
							if (token == "LOCALSIZE")
							{
								if (GlobalStatic.IdentifierDictionary.getLocalIsForbid("LOCAL"))
								{
									ParserMediator.Warn("#" + token + "が指定されていますが変数LOCALは使用禁止されています", position, 2);
									break;
								}
								if (label.LocalLength > 0)
									ParserMediator.Warn("この関数にはすでに#LOCALSIZEが定義されています。（以前の定義は無視されます）", position, 1);
								label.LocalLength = size;
							}
							else if (token == "LOCALSSIZE")
							{
								if (GlobalStatic.IdentifierDictionary.getLocalIsForbid("LOCALS"))
								{
									ParserMediator.Warn("#" + token + "が指定されていますが変数LOCALSは使用禁止されています", position, 2);
									break;
								}
								if (label.LocalsLength > 0)
									ParserMediator.Warn("この関数にはすでに#LOCALSSIZEが定義されています。（以前の定義は無視されます）", position, 1);
								label.LocalsLength = size;
							}
							else
							{
								if (GlobalStatic.IdentifierDictionary.getLocalIsForbid("LOCALF"))
								{
									ParserMediator.Warn("#" + token + "が指定されていますが変数LOCALFは使用禁止されています", position, 2);
									break;
								}
								if (label.LocalFloatLength > 0)
									ParserMediator.Warn("この関数にはすでに#LOCALFSIZEが定義されています。（以前の定義は無視されます）", position, 1);
								label.LocalFloatLength = size;
							}
						}
						break;
					case "DIM":
					case "DIMS":
					case "DIMF":
						{
							wc = AnalyzeSharpArguments(st, token);
							UserDefinedVariableData data = UserDefinedVariableData.Create(wc, token == "DIMS", token == "DIMF", true, position);
							if (!label.AddPrivateVariable(data))
							{
								ParserMediator.Warn("変数名" + data.Name + "は既に使用されています", position, 2);
								return false;
							}
							break;
						}
					case "REF":
					case "REFS":
					case "REFF":
						{
							wc = AnalyzeSharpArguments(st);
							bool isStr = token == "REFS";
							bool isFloat = token == "REFF";
							UserDefinedVariableData data = UserDefinedVariableData.CreateRefScalar(wc, isStr, isFloat, true, position);
							if (!label.AddPrivateVariable(data))
							{
								ParserMediator.Warn("変数名" + data.Name + "は既に使用されています", position, 2);
								return false;
							}
							break;
						}
					default:
						ParserMediator.Warn("解釈できない#行です", position, 1);
						break;
				}
				if (wc != null && !wc.EOL)
					ParserMediator.Warn("#の識別子の後に余分な文字があります", position, 1);
			}
			catch (Exception e)
			{
				ParserMediator.Warn(e.Message, position, 2);
				goto err;
			}
			return true;
		err:
			return false;
		}

		private static WordCollection AnalyzeSharpArguments(StringStream st)
		{
			// v24/snake 原核心只在需要参数的 # 行解析剩余内容。
			// #FUNCTION/#SINGLE 等无参数属性行不能提前词法分析，否则旧脚本里原本会被忽略的尾随内容会变成加载错误。
			return LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.AllowAssignment);
		}

		private static WordCollection AnalyzeSharpArguments(StringStream st, string token)
		{
			if (string.Equals(token, "DIMF", StringComparison.OrdinalIgnoreCase))
			{
				string source = NormalizeSnakeDimfSizeLiterals(st.Substring());
				return LexicalAnalyzer.Analyse(new StringStream(source), LexEndWith.EoL, LexAnalyzeFlag.AllowAssignment);
			}
			return AnalyzeSharpArguments(st);
		}

		private static string NormalizeSnakeDimfSizeLiterals(string source)
		{
			if (string.IsNullOrEmpty(source))
				return source;
			int assignmentIndex = source.IndexOf('=');
			if (assignmentIndex < 0)
				return SnakeDimfSizeFloatLiteralRegex.Replace(source, "$1");
			if (assignmentIndex == 0)
				return source;
			// #DIMF 的尺寸参数仍按整数数组长度处理；只兼容等号前的旧 snake 写法，
			// 等号后的浮点默认值必须保持原样，否则 3.14 会被错误截断成 3。
			string sizePart = SnakeDimfSizeFloatLiteralRegex.Replace(source.Substring(0, assignmentIndex), "$1");
			return sizePart + source.Substring(assignmentIndex);
		}

		public static LogicalLine ParseLine(string str, EmueraConsole console)
		{
			ScriptPosition position = new ScriptPosition();
			StringStream stream = new StringStream(str);
			return ParseLine(stream, position, console);
		}

		public static LogicalLine ParseLabelLine(StringStream stream, ScriptPosition position, EmueraConsole console)
		{
			bool isFunction = (stream.Current == '@');
			//int lineNo = position.LineNo;
			string labelName = "";
			string errMes = "";
			try
			{
				int warnLevel = -1;
                stream.ShiftNext();//@か$を除去
				WordCollection wc = LexicalAnalyzer.Analyse(stream, LexEndWith.EoL, LexAnalyzeFlag.AllowAssignment);
				if (wc.EOL || !(wc.Current is IdentifierWord))
				{
					errMes = "関数名が不正であるか存在しません";
					goto err;
				}
				labelName = ((IdentifierWord)wc.Current).Code;
				wc.ShiftNext();
				if (Config.ICVariable)
					labelName = labelName.ToUpper();
				GlobalStatic.IdentifierDictionary.CheckUserLabelName(ref errMes, ref warnLevel, isFunction, labelName);
				if (warnLevel >= 0)
				{
					if (warnLevel >= 2)
						goto err;
					ParserMediator.Warn(errMes, position, warnLevel);
				}
				if (!isFunction)//$ならこの時点で終了
				{
					if (!wc.EOL)
						ParserMediator.Warn("$で始まるラベルに引数が設定されています", position, 1);
					return new GotoLabelLine(position, labelName);
				}



				//labelName = LexicalAnalyzer.ReadString(stream, StrEndWith.LeftParenthesis_Bracket_Comma_Semicolon);
				//labelName = labelName.Trim();
				//if (Config.ICVariable)
				//    labelName = labelName.ToUpper();
				//GlobalStatic.IdentifierDictionary.CheckUserLabelName(ref errMes, ref warnLevel, isFunction, labelName);
				//if(warnLevel >= 0)
				//{
				//    if (warnLevel >= 2)
				//        goto err;
				//    ParserMediator.Warn(errMes, position, warnLevel);
				//}
				//if (!isFunction)//$ならこの時点で終了
				//{
				//    LexicalAnalyzer.SkipWhiteSpace(stream);
				//    if (!stream.EOS)
				//        ParserMediator.Warn("$で始まるラベルに引数が設定されています", position, 1);
				//    return new GotoLabelLine(position, labelName);
				//}

				////関数名部分に_renameを使えないように変更
				//if (ParserMediator.RenameDic != null && ((stream.ToString().IndexOf("[[") >= 0) && (stream.ToString().IndexOf("]]") >= 0)))
				//{
				//    string line = stream.ToString();
				//    foreach (KeyValuePair<string, string> pair in ParserMediator.RenameDic)
				//        line = line.Replace(pair.Key, pair.Value);
				//    stream = new StringStream(line);
				//}
				//WordCollection wc = null;
				//wc = LexicalAnalyzer.Analyse(stream, LexEndWith.EoL, LexAnalyzeFlag.AllowAssignment);
				if (Program.AnalysisMode)
					console.PrintC("@" + labelName, false);
				FunctionLabelLine funclabelLine = new FunctionLabelLine(position, labelName, wc);
				if (IdentifierDictionary.IsEventLabelName(labelName))
				{
					funclabelLine.IsEvent = true;
					funclabelLine.IsSystem = true;
					funclabelLine.Depth = 0;
				}
				else if (IdentifierDictionary.IsSystemLabelName(labelName))
				{
					funclabelLine.IsSystem = true;
					funclabelLine.Depth = 0;
				}
				return funclabelLine;
			}
			catch (CodeEE e)
			{
				errMes = e.Message;
			}
		err:
			uEmuera.Media.SystemSounds.Hand.Play();
			if (isFunction)
			{
				if(labelName.Length == 0)
					labelName = "<Error>";
				return new InvalidLabelLine(position, labelName, errMes);
			}
			return new InvalidLine(position, errMes);
		}
		
		
		public static LogicalLine ParseLine(StringStream stream, ScriptPosition position, EmueraConsole console)
		{
			//int lineNo = position.LineNo;
			string errMes;
			LexicalAnalyzer.SkipWhiteSpace(stream);//先頭のホワイトスペースを読み飛ばす
			if (stream.EOS)
				return null;
			//コメント行かどうかはここに来る前に判定しておく
			try
			{
				#region 前置インクリメント、デクリメント行
				if (stream.Current == '+' || stream.Current == '-')
				{
					char op = stream.Current;
					WordCollection wc = LexicalAnalyzer.Analyse(stream, LexEndWith.EoL, LexAnalyzeFlag.None);
                    if ((!(wc.Current is OperatorWord opWT)) || ((opWT.Code != OperatorCode.Increment) && (opWT.Code != OperatorCode.Decrement)))
                    {
                        if (op == '+')
                            errMes = "行が\'+\'から始まっていますが、インクリメントではありません";
                        else
                            errMes = "行が\'-\'から始まっていますが、デクリメントではありません";
                        goto err;
                    }
                    wc.ShiftNext();
					//token = EpressionParser.単語一個分取得(wc)
					//token非変数
					//token文字列形
					//token変更不可能
					//if (wc != EOS)
					//
					return new InstructionLine(position, FunctionIdentifier.SETFunction, opWT.Code, wc, null);
				}
				#endregion
				string idCode = LexicalAnalyzer.ReadFirstIdentifier(stream);
				if (idCode != null)
				{
					FunctionIdentifier func = GlobalStatic.IdentifierDictionary.GetFunctionIdentifier(idCode);
					//命令文
					if (func != null)//関数文
					{
						if (stream.EOS) //引数の無い関数
							return new InstructionLine(position, func, stream);
						if ((stream.Current != ';') && (stream.Current != ' ') && (stream.Current != '\t') && (!Config.SystemAllowFullSpace || (stream.Current != '　')))
						{
							if (stream.Current == '　')
								errMes = "命令で行が始まっていますが、命令の直後に半角スペース・タブ以外の文字が来ています(この警告はシステムオプション「" + Config.GetConfigName(ConfigCode.SystemAllowFullSpace) + "」により無視できます)";
							else
								errMes = "命令で行が始まっていますが、命令の直後に半角スペース・タブ以外の文字が来ています";
							goto err;
						}
						stream.ShiftNext();
						// 命令名と同名の変数への代入を優先する
						// VARS/VARI など snake 拡張命令名と同名の変数を使用するゲームへの対応
						// ※PRINTFORM = ... のような正当な命令呼び出しを誤判定しないよう、
						//   VARS/VARI のみに限定する
						LexicalAnalyzer.SkipWhiteSpace(stream);
						if (!stream.EOS && stream.Current == '='
						    && (func.Code == FunctionCode.VARS || func.Code == FunctionCode.VARI))
						{
							stream.Seek(0, System.IO.SeekOrigin.Begin);
							// Fall through to assignment parsing below
						}
						else
						{
							return new InstructionLine(position, func, stream);
						}
					}
				}
				LexicalAnalyzer.SkipWhiteSpace(stream);
				if (stream.EOS)
				{
					errMes = "解釈できない行です";
					goto err;
				}
				//命令行ではない→代入行のはず
				stream.Seek(0, System.IO.SeekOrigin.Begin);
				OperatorCode assignOP = OperatorCode.NULL;
				WordCollection wc1 = LexicalAnalyzer.Analyse(stream, LexEndWith.Operator, LexAnalyzeFlag.None);
				//if (idWT != null)
				//	wc1.Collection.Insert(0, idWT);
				try
				{
					assignOP = LexicalAnalyzer.ReadAssignmentOperator(stream);
				}
				catch(CodeEE)
				{
					errMes = "解釈できない行です";
					goto err;
				}
				//eramaker互換警告
				//stream.Jump(-1);
				//if ((stream.Current != ' ') && (stream.Current != '\t'))
				//{
				//	errMes = "変数で行が始まっていますが、演算子の直前に半角スペースまたはタブがありません";
				//	goto err;
				//}
				//stream.ShiftNext();


				if (assignOP == OperatorCode.Equal)
				{
					if (console != null)
						ParserMediator.Warn("代入演算子に\"==\"が使われています", position, 0);
					//"=="を代入文に使うのは本当はおかしいが結構使われているので仕様にする
					assignOP = OperatorCode.Assignment;
				}
				return new InstructionLine(position, FunctionIdentifier.SETFunction, assignOP, wc1, stream);
			err:
				return new InvalidLine(position, errMes);
			}
			catch (CodeEE e)
			{
				uEmuera.Media.SystemSounds.Hand.Play();
				return new InvalidLine(position, e.Message);
			}
		}
		
	}
}
