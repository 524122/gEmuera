# 源码索引

> **生成文件。** 此索引覆盖仓库自有的 `Scripts/`、`src/Core/`、`test/` 与 `tools/core-contracts/` C# 源文件，以及 `scenes/` 场景资产（列出挂载脚本）；跳过 `bin/`、`obj/`、`.godot/`、`android/build/`、`addons/gdUnit4/` 和 `node_modules/`。
>
> 重新生成：`powershell -NoProfile -ExecutionPolicy Bypass -File docs/gEmueraCodeWiki/Update-SourceMap.ps1`。声明名由轻量正则提取，用于定位，不等同于公开 API 或完整调用图。

## 使用方式

- 先用本页按目录/文件定位，再结合其他 Wiki 页面和 CodeGraph 确认调用链。
- `partial` 类型的成员分布在多个文件；请同时查看同名的所有文件。
- 需要修改 ERB 语义时，先读 [`03-Legacy-Interpreter.md`](03-Legacy-Interpreter.md) 与 [`../../ERBAPI.md`](../../ERBAPI.md)。

## `Scripts/AnimatedWebpSpriteFrames.cs`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/AnimatedWebpSpriteFrames.cs` | `AnimatedWebpFrameSequence, Frame, AnimatedWebpSpriteFrames, RawFrame, Entry` |

## `Scripts/ColorMatrixGPU.cs`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/ColorMatrixGPU.cs` | `ColorMatrixGPU` |

## `Scripts/Diagnostics`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/Diagnostics/DiagnosticLogExporter.cs` | `DiagnosticLogExporter` |
| `Scripts/Diagnostics/DiagnosticLogRecord.cs` | `DiagnosticLogRecord` |
| `Scripts/Diagnostics/DiagnosticLogRouter.cs` | `DiagnosticLogRouter, RateLimitBucket` |
| `Scripts/Diagnostics/DiagnosticLogSinks.cs` | `DiagnosticLogSinks` |
| `Scripts/Diagnostics/InputReplayBuffer.cs` | `InputReplayBuffer, ReplayEntry` |
| `Scripts/Diagnostics/RuntimeDiagnosticsConfig.cs` | `RuntimeDiagnosticsConfig, QuickDebugModuleSwitches, DebugModelProfile, CategorySwitches` |
| `Scripts/Diagnostics/RuntimeDiagnosticsConfigLoader.cs` | `RuntimeDiagnosticsConfigLoader, LoadResult` |
| `Scripts/Diagnostics/RuntimeDiagnosticsConfigWriter.cs` | `RuntimeDiagnosticsConfigWriter` |
| `Scripts/Diagnostics/RuntimeDiagnosticsPanel.cs` | `RuntimeDiagnosticsPanel, FloatingDiagnosticsHost` |
| `Scripts/Diagnostics/RuntimeTomlParser.cs` | `RuntimeTomlParser, ParseResult, ParseError` |
| `Scripts/Diagnostics/SaveLogOperationTrail.cs` | `SaveLogOperationTrail, OperationEntry` |

## `Scripts/Emuera`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/Emuera/_Library/GDI.cs` | `TernaryRasterOperations, StockObjects, StretchMode, GDI` |
| `Scripts/Emuera/_Library/LangManager.cs` | `LangManager` |
| `Scripts/Emuera/_Library/SFMT.cs` | `MTRandom` |
| `Scripts/Emuera/_Library/Sys.cs` | `Sys` |
| `Scripts/Emuera/_Library/WinInput.cs` | `WinInput, MouseButtons` |
| `Scripts/Emuera/_Library/WinmmTimer.cs` | `WinmmTimer` |
| `Scripts/Emuera/Compatibility/LegacyCompatibilityModules.cs` | `ILegacyCompatibilityModule, LegacyCompatibilityModuleCatalog, LegacyCompatibilityProfileBuilder, LegacyV24CompatibilityModule, LegacySnakeCompatibilityModule, LegacyEraFlCompatibilityModule, DisabledSnakeCompatibilityPolicy, LegacySnakeCompatibilityPolicy, DisabledEraFlCompatibilityPolicy, LegacyEraFlCompatibilityPolicy` |
| `Scripts/Emuera/Compatibility/LegacyCompatibilityProfile.cs` | `ISnakeCompatibilityPolicy, IEraFlCompatibilityPolicy, EraFlGMapNode, LegacyCompatibilityProfile` |
| `Scripts/Emuera/Config/Config.cs` | `Config, StrIgnoreCaseComparer` |
| `Scripts/Emuera/Config/ConfigCode.cs` | `DisplayWarningFlag, ReduceArgumentOnLoadFlag, TextDrawingMode, RenderingBackend, UseLanguage, TextEditorType, ConfigCode` |
| `Scripts/Emuera/Config/ConfigData.cs` | `ConfigData` |
| `Scripts/Emuera/Config/ConfigItem.cs` | `AConfigItem, ConfigItem` |
| `Scripts/Emuera/Config/JSONConfig.cs` | `JSONConfig` |
| `Scripts/Emuera/Config/JSONConfigData.cs` | `JSONConfigData` |
| `Scripts/Emuera/Config/KeyMacro.cs` | `KeyMacro` |
| `Scripts/Emuera/Content/AContentFile.cs` | `AContentFile` |
| `Scripts/Emuera/Content/AContentItem.cs` | `—` |
| `Scripts/Emuera/Content/AppContents.cs` | `AppContents, LazySpriteDefinition` |
| `Scripts/Emuera/Content/ConstImage.cs` | `AbstractImage, ConstImage` |
| `Scripts/Emuera/Content/CroppedImage.cs` | `AContentItem, ASprite, ASpriteSingle, SpriteG, SpriteF, SpriteAnime, AnimeFrame` |
| `Scripts/Emuera/Content/FontMeasureCache.cs` | `FontMeasureCache, CacheKey` |
| `Scripts/Emuera/Content/FontModel.cs` | `FontModel` |
| `Scripts/Emuera/Content/GraphicsImage.cs` | `GraphicsImage` |
| `Scripts/Emuera/CtrlZ.cs` | `CtrlZ` |
| `Scripts/Emuera/GameData/ConstantData.cs` | `CharacterStrData, CharacterIntData, ConstantData, LazyErdNameData, CsvFieldRange, CharacterTemplate` |
| `Scripts/Emuera/GameData/DefineMacro.cs` | `DefineMacro` |
| `Scripts/Emuera/GameData/EraType.cs` | `EraType, EraTypeHelper` |
| `Scripts/Emuera/GameData/Expression/CaseExpression.cs` | `CaseExpressionType, CaseExpression` |
| `Scripts/Emuera/GameData/Expression/ExpressionMediator.cs` | `ExpressionMediator` |
| `Scripts/Emuera/GameData/Expression/ExpressionParser.cs` | `ArgsEndWith, TermEndWith, ExpressionParser, TermStack, StackEntry` |
| `Scripts/Emuera/GameData/Expression/IOperandTerm.cs` | `IOperandTerm` |
| `Scripts/Emuera/GameData/Expression/OperatorCode.cs` | `OperatorCode, OperatorManager` |
| `Scripts/Emuera/GameData/Expression/OperatorMethod.cs` | `OperatorMethod, OperatorMethodManager, PlusIntInt, PlusStrStr, MinusIntInt, MultIntInt, MultStrInt, DivIntInt, ModIntInt, EqualIntInt, EqualStrStr, NotEqualIntInt, NotEqualStrStr, GreaterIntInt, GreaterStrStr, LessIntInt, LessStrStr, GreaterEqualIntInt, GreaterEqualStrStr, LessEqualIntInt, LessEqualStrStr, AndIntInt, OrIntInt, XorIntInt, NandIntInt, NorIntInt, BitAndIntInt, BitOrIntInt, BitXorIntInt, RightShiftIntInt, LeftShiftIntInt, PlusInt, MinusInt, NotInt, BitNotInt, IncrementInt, DecrementInt, IncrementAfterInt, DecrementAfterInt, TernaryIntIntInt, TernaryIntStrStr, TernaryIntFloatFloat, PlusFloatFloat, MinusFloatFloat, MultFloatFloat, DivFloatFloat, EqualFloatFloat, NotEqualFloatFloat, LessFloatFloat, GreaterFloatFloat, LessEqualFloatFloat, GreaterEqualFloatFloat, PlusMixedFloat, MinusMixedFloat, MultMixedFloat, DivMixedFloat, EqualMixedFloat, NotEqualMixedFloat, LessMixedFloat, GreaterMixedFloat, LessEqualMixedFloat, GreaterEqualMixedFloat, MinusFloat` |
| `Scripts/Emuera/GameData/Expression/SafeArithmetic.cs` | `SafeArithmetic` |
| `Scripts/Emuera/GameData/Expression/Term.cs` | `NullTerm, SingleTerm, StrFormTerm, VariadicArgTerm` |
| `Scripts/Emuera/GameData/Function/Creator.cs` | `FunctionMethodCreator` |
| `Scripts/Emuera/GameData/Function/Creator.Method.cs` | `FunctionMethodCreator, GetcharaMethod, GetspcharaMethod, CsvStrDataMethod, CsvcstrMethod, CsvDataMethod, FindcharaMethod, ExistCsvMethod, VarsizeMethod, CheckfontMethod, CheckdataMethod, CheckdataStrMethod, FindFilesMethod, IsSkipMethod, MesSkipMethod, GetColorMethod, GetFocusColorMethod, GetBGColorMethod, GetStyleMethod, GetFontMethod, BarStringMethod, CurrentAlignMethod, CurrentRedrawMethod, ColorFromNameMethod, ColorFromRGBMethod, GetRefMethod, MoneyStrMethod, GetPrintCPerLineMethod, PrintCLengthMethod, GetSaveNosMethod, GettimeMethod, GettimesMethod, GetmsMethod, GetSecondMethod, RandMethod, MaxMethod, AbsMethod, PowerMethod, SqrtMethod, CbrtMethod, LogMethod, ExpMethod, SignMethod, GetLimitMethod, SumArrayMethod, MatchMethod, GroupMatchMethod, NosamesMethod, AllsamesMethod, MaxArrayMethod, GetbitMethod, GetnumMethod, GetnumBMethod, GetPalamLVMethod, GetExpLVMethod, FindElementMethod, InRangeMethod, InRangeArrayMethod, ArrayMultiSortMethod, ArrayMultiSortExMethod, StrlenMethod, StrlenuMethod, SubstringMethod, SubstringuMethod, StrfindMethod, StrCountMethod, ToStrMethod, ToIntMethod, ToFloatMethod, StrFormType, StrChangeStyleMethod, LineIsEmptyMethod, ReplaceMethod, UnicodeMethod, UnicodeByteMethod, ConvertIntMethod, IsNumericMethod, EscapeMethod, EncodeToUniMethod, CharAtMethod, GetLineStrMethod, StrFormMethod, StrFormCheckMethod, JoinMethod, GetConfigMethod, HtmlGetPrintedStrMethod, HtmlPopPrintingStrMethod, HtmlToPlainTextMethod, HtmlEscapeMethod, HtmlStringLenMethod, HtmlSubstringMethod, HtmlStringLinesMethod, GraphicsStateMethod, GraphicsGetColorMethod, GraphicsSetColorMethod, GraphicsClearLowAlphaMethod, GraphicsSetBrushMethod, GraphicsSetFontMethod, GraphicsSetPenMethod, SpriteStateMethod, SpriteSetPosMethod, SpriteGetColorMethod, ClientSizeMethod, GraphicsCreateMethod, GraphicsCreateFromFileMethod, GraphicsDisposeMethod, SpriteCreateMethod, SpriteDisposeMethod, GraphicsClearMethod, GraphicsFillRectangleMethod, GraphicsDrawGMethod, GraphicsDrawGWithMaskMethod, GraphicsDrawSpriteMethod, SpriteAnimeCreateMethod, SpriteAnimeAddFrameMethod, CBGClearMethod, CBGRemoveRangeMethod, CBGClearButtonMethod, CBGRemoveBMapMethod, CBGSetGraphicsMethod, CBGSetBMapGMethod, CBGSetCIMGMethod, CBGSETButtonSpriteMethod, GetKeyStateMethod, MousePosMethod, IsActiveMethod, SetAnimeTimerMethod, GetAnimeTimerMethod, ExistSoundMethod, ExistsImageLayerMethod, GetLineYMethod, ExistFunctionMethod, ExistFileMethod, GetCsvNoByNameMethod, GetSoundOrBgmInfoMethod, IsPlayingSoundMethod, SoundControlMethod, IsPlayingBgmMethod, BgmControlMethod, GetTextDrawingModeMethod, GetSkiaQualityMethod, SnakeFallenStateMethod, SequenceInputMethod, DisableInputMacroMethod, EnableInputMacroMethod, GetPlatformMethod, V24TrigMethod, V24RoundMathMethod, ArgLengthMethod, UncheckedMathMethod, BitSetMethod, BitGetMethod, BitToggleMethod, BitIndexOfFirstMethod, SaveTextMethod, LoadTextMethod, GraphicsSaveMethod, GraphicsLoadMethod, GraphicsGetFontMethod, GraphicsGetTextSizeMethod, GraphicsDrawLineMethod, GraphicsDrawStringMethod, GraphicsDashStyleMethod, GraphicsRotateMethod, PolygonPointAddMethod, PolygonPointClearMethod, PolygonDrawMethod, PolygonFillMethod, SpriteCreateFromFileMethod, SpriteDisposeAllMethod, SetTextBoxMethod, GetTextBoxMethod, MoveTextBoxMethod, ResumeTextBoxMethod, MouseButtonMethod, VarSetExMethod, RegexpMatchMethod, ExistMethMethod, EnumNameMethod, EType, EAction, GraphicsDrawGWithRotateMethod, GetDisplayLineMethod, GetDoingFunctionMethod, GetMethMethod, GetMethFMethod, GetMethSMethod, EvalMethod, EvalFMethod, EvalSMethod, MatchAllMethod, MatchAllExMethod, ExistVarMethod, SetVarMethod, IsDefinedMethod, ClearMemoryMethod, GetMemoryUsageMethod, OutputLogMethod, ErdNameMethod, ToStrfMethod, EnumFilesMethod, GetVarMethod, GetVarSMethod, GetVarFMethod, BitmapCacheEnableMethod, FlowInputMethod, FlowInputsMethod, HotkeyStateInitMethod, HotkeyStateMethod` |
| `Scripts/Emuera/GameData/Function/Creator.Method.DT.cs` | `FunctionMethodCreator, DtCreateMethod, DtExistMethod, DtReleaseMethod, DtClearMethod, DtNoCaseMethod, DtColumnAddMethod, DtColumnExistMethod, DtColumnRemoveMethod, DtColumnLengthMethod, DtColumnNamesMethod, DtRowAddMethod, DtRowSetMethod, DtRowRemoveMethod, DtRowLengthMethod, DtCellGetMethod, DtCellGetfMethod, DtCellGetsMethod, DtCellIsNullMethod, DtCellSetMethod, DtCellSetfMethod, DtSelectMethod, DtToXmlMethod, DtFromXmlMethod` |
| `Scripts/Emuera/GameData/Function/Creator.Method.Map.cs` | `FunctionMethodCreator, MapCreateMethod, MapExistMethod, MapReleaseMethod, MapSetMethod, MapHasMethod, MapRemoveMethod, MapClearMethod, MapSizeMethod, MapGetMethod, MapGetKeysMethod, MapValuesMethod, MapToStringMethod, MapFromStringMethod, MapToXmlMethod, MapFromXmlMethod, MapMergeMethod, MapRemoveIfMethod, MapFindKeyMethod` |
| `Scripts/Emuera/GameData/Function/Creator.Method.Sql.cs` | `FunctionMethodCreator, SqlConnectionOpenMethod, SqlConnectMethod, SqlDisconnectMethod, SqlExecuteNonQueryMethod, SqlExecuteReaderMethod, SqlReaderReadMethod, SqlReaderGetIntMethod, SqlReaderGetFloatMethod, SqlReaderIsNullMethod, SqlReaderCloseMethod, SqlPExecuteNonQueryMethod, SqlPExecuteReaderMethod, SqlExecuteScalarLongMethod, SqlExecuteScalarFloatMethod, SqlPExecuteScalarLongMethod, SqlPExecuteScalarFloatMethod, SqlEscapeMethod, SqlExecuteScalarStringMethod, SqlPExecuteScalarStringMethod, SqlReaderGetStringMethod, SqlImportMapXmlMethod, SqlImportDtXmlMethod, SqlImportXmlCustomMethod, SqlExportMapXmlMethod, SqlExportDtXmlMethod` |
| `Scripts/Emuera/GameData/Function/Creator.Method.Xml.cs` | `FunctionMethodCreator, XmlDocumentMethod, XmlReleaseMethod, XmlToStrMethod, XmlGetMethod, XmlSetMethod, XmlAddNodeMethod, XmlRemoveNodeMethod, XmlReplaceMethod` |
| `Scripts/Emuera/GameData/Function/DialectFunctionContracts.cs` | `DialectFunctionContracts` |
| `Scripts/Emuera/GameData/Function/FunctionMethod.cs` | `FunctionMethod, DialectFunctionMethod` |
| `Scripts/Emuera/GameData/Function/FunctionMethodTerm.cs` | `FunctionMethodTerm` |
| `Scripts/Emuera/GameData/Function/RuntimeDataStore.cs` | `RuntimeDataStore` |
| `Scripts/Emuera/GameData/Function/UserDefinedMethodTerm.cs` | `SuperUserDefinedMethodTerm, UserDefinedMethodTerm, UserDefinedRefMethodTerm, UserDefinedRefMethodNoArgTerm` |
| `Scripts/Emuera/GameData/Function/UserDefinedRefMethod.cs` | `UserDefinedRefMethod` |
| `Scripts/Emuera/GameData/GameBase.cs` | `GameBase` |
| `Scripts/Emuera/GameData/IdentifierDictionary.cs` | `IdentifierDictionary, DefinedNameType` |
| `Scripts/Emuera/GameData/ParserMediator.cs` | `ParserMediator, ParserWarning` |
| `Scripts/Emuera/GameData/StrForm.cs` | `StrForm, FormattedStringMethod, FormatCurlyBrace, FormatPercent, FormatYenAt` |
| `Scripts/Emuera/GameData/Variable/CharacterData.cs` | `CharacterData` |
| `Scripts/Emuera/GameData/Variable/ElementRefInfo.cs` | `ElementRefInfo` |
| `Scripts/Emuera/GameData/Variable/NullRefTerm.cs` | `NullRefTerm` |
| `Scripts/Emuera/GameData/Variable/SparseArray.cs` | `SparseArray` |
| `Scripts/Emuera/GameData/Variable/VariableCode.cs` | `VariableCode` |
| `Scripts/Emuera/GameData/Variable/VariableData.cs` | `VariableData` |
| `Scripts/Emuera/GameData/Variable/VariableDescriptor.cs` | `VariableKind, VariableDimension, VariableAttribute, VariableDescriptor, VariableDescriptorTable` |
| `Scripts/Emuera/GameData/Variable/VariableEvaluator.cs` | `VariableEvaluator` |
| `Scripts/Emuera/GameData/Variable/VariableIdentifier.cs` | `VariableIdentifier` |
| `Scripts/Emuera/GameData/Variable/VariableLocal.cs` | `VariableLocal` |
| `Scripts/Emuera/GameData/Variable/VariableParser.cs` | `VariableParser` |
| `Scripts/Emuera/GameData/Variable/VariableStrArgTerm.cs` | `VariableStrArgTerm` |
| `Scripts/Emuera/GameData/Variable/VariableTerm.cs` | `VariableTerm, FixedVariableTerm, VariableNoArgTerm` |
| `Scripts/Emuera/GameData/Variable/VariableToken.cs` | `VariableToken, CharaVariableToken, UserDefinedVariableToken, UserDefinedCharaVariableToken, ReferenceToken, LocalVariableToken, VariableData, IntVariableToken, FloatVariableToken, Int1DVariableToken, Int2DVariableToken, Int3DVariableToken, StrVariableToken, Str1DVariableToken, Str2DVariableToken, Str3DVariableToken, CharaIntVariableToken, CharaInt1DVariableToken, CharaStrVariableToken, CharaStr1DVariableToken, CharaInt2DVariableToken, CharaStr2DVariableToken, ConstantToken, IntConstantToken, StrConstantToken, Int1DConstantToken, Str1DConstantToken, PseudoVariableToken, RandToken, CompatiRandToken, CHARANUM_Token, LASTLOAD_TEXT_Token, LASTLOAD_VERSION_Token, LASTLOAD_NO_Token, LINECOUNT_Token, WINDOW_TITLE_Token, MONEYLABEL_Token, DRAWLINESTR_Token, EmptyStrToken, EmptyIntToken, Debug__FILE__Token, Debug__FUNCTION__Token, Debug__LINE__Token, ISTIMEOUTToken, __INT_MAX__Token, __INT_MIN__Token, EMUERA_VERSIONToken, LocalInt1DVariableToken, LocalFloat1DVariableToken, LocalStr1DVariableToken, StaticInt1DVariableToken, StaticInt2DVariableToken, StaticInt3DVariableToken, StaticStr1DVariableToken, StaticStr2DVariableToken, StaticStr3DVariableToken, PrivateInt1DVariableToken, PrivateInt2DVariableToken, PrivateInt3DVariableToken, PrivateStr1DVariableToken, PrivateStr2DVariableToken, PrivateStr3DVariableToken, ReferenceIntScalarToken, ReferenceFloatScalarToken, ReferenceStrScalarToken, ReferenceInt1DToken, ReferenceInt2DToken, ReferenceInt3DToken, ReferenceFloat1DToken, ReferenceFloat2DToken, ReferenceFloat3DToken, ReferenceStr1DToken, ReferenceStr2DToken, ReferenceStr3DToken, UserDefinedCharaInt1DVariableToken, UserDefinedCharaStr1DVariableToken, UserDefinedCharaInt2DVariableToken, UserDefinedCharaStr2DVariableToken, StaticFloat1DVariableToken, StaticFloat2DVariableToken, StaticFloat3DVariableToken, PrivateFloat1DVariableToken, PrivateFloat2DVariableToken, PrivateFloat3DVariableToken, UserDefinedCharaFloat1DVariableToken, UserDefinedCharaFloat2DVariableToken` |
| `Scripts/Emuera/GameProc/ErbLoader.cs` | `ErbLoader, PPState` |
| `Scripts/Emuera/GameProc/ExecutionContext.cs` | `ExecutionContext` |
| `Scripts/Emuera/GameProc/Function/Argument.cs` | `Argument, ExpressionsArgument, VoidArgument, ErrorArgument, ExpressionArgument, ExpressionArrayArgument, MixedIntegerExprTerm, SpPrintShapeArgument, SpPrintImgArgument, SpPrintVArgument, SpTimesArgument, SpBarArgument, SpSwapCharaArgument, SpSwapVarArgument, SpVarsizeArgument, SpSaveDataArgument, SpTInputsArgument, SortOrder, SpSortcharaArgument, SpCallFArgment, SpCallArgment, SpCallSharpArgment, SnakeVariArgument, SnakeVarsArgument, SpDtColumnOptionsArgument, OptionType, SpSetBgImageArgument, SpSetImageLayerArgument, SpForNextArgment, SpPowerArgument, CaseArgument, PrintDataArgument, StrDataArgument, MethodArgument, BitArgument, SpVarSetArgument, SpCVarSetArgument, SpButtonArgument, SpColorArgument, SpColorAlphaArgument, SpSplitArgument, SpHtmlSplitArgument, SpGetIntArgument, SpArrayControlArgument, SpArrayShiftArgument, SpArraySortArgument, SpCopyArrayArgument, SpSaveVarArgument, RefArgument, OneInputArgument, OneInputsArgument, SpSetArgument, SpSetArrayArgument` |
| `Scripts/Emuera/GameProc/Function/ArgumentBuilder.cs` | `ArgumentBuilder, ArgumentParser, SP_PRINT_IMG_ArgumentBuilder, SP_PRINT_SHAPE_ArgumentBuilder, SP_DT_COLUMN_OPTIONS_ArgumentBuilder, SP_PRINTV_ArgumentBuilder, SP_TIMES_ArgumentBuilder, FORM_STR_ANY_ArgumentBuilder, VOID_ArgumentBuilder, STR_ArgumentBuilder, FORM_STR_ArgumentBuilder, SP_VAR_ArgumentBuilder, SP_SORTCHARA_ArgumentBuilder, SP_SORT_ARRAY_ArgumentBuilder, SP_CALL_ArgumentBuilder, CASE_ArgumentBuilder, SP_SET_ArgumentBuilder, METHOD_ArgumentBuilder, SP_INPUTS_ArgumentBuilder, INT_EXPRESSION_ArgumentBuilder, INT_ANY_ArgumentBuilder, STR_EXPRESSION_ArgumentBuilder, EXPRESSION_ArgumentBuilder, SP_BAR_ArgumentBuilder, SP_SWAP_ArgumentBuilder, SP_SAVEDATA_ArgumentBuilder, SP_TINPUT_ArgumentBuilder, SP_TINPUTS_ArgumentBuilder, SP_FOR_NEXT_ArgumentBuilder, SP_POWER_ArgumentBuilder, SP_SWAPVAR_ArgumentBuilder, VAR_INT_ArgumentBuilder, VAR_STR_ArgumentBuilder, BIT_ARG_ArgumentBuilder, SP_VAR_SET_ArgumentBuilder, SP_CVAR_SET_ArgumentBuilder, SP_BUTTON_ArgumentBuilder, SP_COLOR_ArgumentBuilder, SP_COLOR_ALPHA_ArgumentBuilder, SP_SPLIT_ArgumentBuilder, SP_HTMLSPLIT_ArgumentBuilder, SP_SETBGIMAGE_ArgumentBuilder, SP_SETIMAGELAYERL_ArgumentBuilder, SP_GETINT_ArgumentBuilder, SP_CONTROL_ARRAY_ArgumentBuilder, SP_SHIFT_ARRAY_ArgumentBuilder, SP_SAVEVAR_ArgumentBuilder, SP_SAVECHARA_ArgumentBuilder, SP_REF_ArgumentBuilder, SP_INPUT_ArgumentBuilder, SP_COPY_ARRAY_Arguments, Expressions_ArgumentBuilder` |
| `Scripts/Emuera/GameProc/Function/ArgumentParser.cs` | `ArgumentParser` |
| `Scripts/Emuera/GameProc/Function/BuiltInFunctionCode.cs` | `FunctionCode` |
| `Scripts/Emuera/GameProc/Function/FunctionArgType.cs` | `FunctionArgType` |
| `Scripts/Emuera/GameProc/Function/FunctionIdentifier.cs` | `FunctionIdentifier` |
| `Scripts/Emuera/GameProc/Function/Instraction.Child.cs` | `FunctionIdentifier, PRINT_Instruction, PRINT_DATA_Instruction, HTML_PRINT_Instruction, HTML_TAGSPLIT_Instruction, PRINT_IMG_Instruction, PRINT_RECT_Instruction, PRINT_SPACE_Instruction, CUSTOMDRAWLINE_Instruction, DEBUGPRINT_Instruction, DEBUGCLEAR_Instruction, METHOD_Instruction, SET_Instruction, REUSELASTLINE_Instruction, CLEARLINE_Instruction, STRLEN_Instruction, SETBIT_Instruction, WAIT_Instruction, WAITANYKEY_Instruction, INPUTANY_Instruction, TWAIT_Instruction, INPUT_Instruction, INPUTS_Instruction, ONEINPUT_Instruction, ONEINPUTS_Instruction, BINPUT_Instruction, BINPUTS_Instruction, ONEBINPUT_Instruction, ONEBINPUTS_Instruction, TINPUT_Instruction, TINPUTS_Instruction, CALLF_Instruction, BAR_Instruction, TIMES_Instruction, ADDCHARA_Instruction, ADDVOIDCHARA_Instruction, SWAPCHARA_Instruction, COPYCHARA_Instruction, ADDCOPYCHARA_Instruction, SORTCHARA_Instruction, RESETCOLOR_Instruction, RESETBGCOLOR_Instruction, FONTBOLD_Instruction, FONTITALIC_Instruction, FONTREGULAR_Instruction, VARSET_Instruction, CVARSET_Instruction, RANDOMIZE_Instruction, INITRAND_Instruction, DUMPRAND_Instruction, SAVEGLOBAL_Instruction, LOADGLOBAL_Instruction, RESETDATA_Instruction, RESETGLOBAL_Instruction, SAVECHARA_Instruction, LOADCHARA_Instruction, SAVEVAR_Instruction, LOADVAR_Instruction, DELDATA_Instruction, DO_NOTHING_Instruction, SNAKE_COMPAT_NOOP_Instruction, RawArgBuilder, SNAKE_ARGS_ArgumentBuilder, SNAKE_SETIMAGELAYER_ArgumentBuilder, SNAKE_SKIA_ArgumentBuilder, SNAKE_TEXT_BGC_ON_Instruction, SNAKE_TEXT_BGC_OFF_Instruction, SNAKE_HTML_PRINT_ArgumentBuilder, SNAKE_CALLSHARP_ArgumentBuilder, SNAKE_CALLSHARP_Instruction, SNAKE_SETBGIMAGE_Instruction, V24_SETBGIMAGE_Instruction, SNAKE_CLEARBGIMAGE_Instruction, SNAKE_REMOVEBGIMAGE_Instruction, SETIMAGELAYER_Instruction, SETIMAGELAYERL_Instruction, CLEARIMAGELAYER_Instruction, CLEARIMAGELAYER_ALL_Instruction, SNAKE_PLAYSOUND_Instruction, SNAKE_STOPSOUND_Instruction, SNAKE_PLAYBGM_Instruction, SNAKE_STOPBGM_Instruction, SNAKE_SETVOLUME_Instruction, SNAKE_HTML_PRINTC_Instruction, SNAKE_HTML_PRINT_ISLAND_Instruction, SNAKE_HTML_PRINT_ISLAND_CLEAR_Instruction, SNAKE_UPDATECHECK_Instruction, SETANIMETIMER_Instruction, SNAKE_UI_SETTING_Instruction, SNAKE_SKIPLOG_Instruction, BREAKBUTTON_Instruction, SNAKE_DT_COLUMN_OPTIONS_Instruction, SNAKE_VARI_Instruction, SNAKE_VARI_ArgumentBuilder, SNAKE_TOOLTIP_SETFONT_Instruction, SNAKE_TOOLTIP_INT_Instruction, REF_Instruction, TOOLTIP_SETCOLOR_Instruction, TOOLTIP_SETDELAY_Instruction, TOOLTIP_SETDURATION_Instruction, INPUTMOUSEKEY_Instruction, AWAIT_Instruction, BEGIN_Instruction, FORCE_BEGIN_Instruction, SAVELOADGAME_Instruction, REPEAT_Instruction, WHILE_Instruction, SIF_Instruction, ELSEIF_Instruction, ENDIF_Instruction, IF_Instruction, SELECTCASE_Instruction, RETURNFORM_Instruction, RETURN_Instruction, CATCH_Instruction, RESTART_Instruction, BREAK_Instruction, CONTINUE_Instruction, REND_Instruction, WEND_Instruction, LOOP_Instruction, RETURNF_Instruction, CALLS_Instruction, CALL_Instruction, CALLEVENT_Instruction, GOTO_Instruction` |
| `Scripts/Emuera/GameProc/Function/Instruction.cs` | `AbstractInstruction` |
| `Scripts/Emuera/GameProc/HeaderFileLoader.cs` | `HeaderFileLoader` |
| `Scripts/Emuera/GameProc/InputRequest.cs` | `InputType, InputRequest` |
| `Scripts/Emuera/GameProc/LabelDictionary.cs` | `LabelDictionary` |
| `Scripts/Emuera/GameProc/LogicalLine.cs` | `LogicalLine, InvalidLine, InstructionLine, NullLine, InvalidLabelLine, FunctionLabelLine, GotoLabelLine` |
| `Scripts/Emuera/GameProc/LogicalLineParser.cs` | `LogicalLineParser` |
| `Scripts/Emuera/GameProc/Process.CalledFunction.cs` | `UserDefinedFunctionArgument, EraFlQuestStartLookupContext, CalledFunction` |
| `Scripts/Emuera/GameProc/Process.cs` | `Process` |
| `Scripts/Emuera/GameProc/Process.LazyLoading.cs` | `Process, LazyStatus` |
| `Scripts/Emuera/GameProc/Process.ScriptProc.cs` | `Process` |
| `Scripts/Emuera/GameProc/Process.State.cs` | `SystemStateCode, BeginType, ProcessState` |
| `Scripts/Emuera/GameProc/Process.SystemProc.cs` | `Process` |
| `Scripts/Emuera/GameProc/SelectCaseJumpTable.cs` | `SelectCaseJumpTable` |
| `Scripts/Emuera/GameProc/UserDefinedFunction.cs` | `UserDifinedFunctionDataArgType, UserDefinedFunctionData` |
| `Scripts/Emuera/GameProc/UserDefinedVariable.cs` | `UserDefinedVariableData, DimLineWC` |
| `Scripts/Emuera/GameView/AConsoleDisplayPart.cs` | `AConsoleDisplayPart, AConsoleColoredPart` |
| `Scripts/Emuera/GameView/ButtonStringCreator.cs` | `ButtonPrimitive, ButtonStringCreator` |
| `Scripts/Emuera/GameView/ConsoleButtonString.cs` | `ConsoleButtonString` |
| `Scripts/Emuera/GameView/ConsoleDisplayLine.cs` | `DisplayLineLastState, DisplayLineAlignment, ConsoleDisplayLine` |
| `Scripts/Emuera/GameView/ConsoleDivPart.cs` | `BoxDirection, StyledBoxModel, ConsoleDivPart` |
| `Scripts/Emuera/GameView/ConsoleImagePart.cs` | `ConsoleImagePart` |
| `Scripts/Emuera/GameView/ConsoleShapePart.cs` | `ConsoleShapePart, ConsoleRectangleShapePart, ConsoleSpacePart, ConsoleErrorShapePart` |
| `Scripts/Emuera/GameView/ConsoleStyledString.cs` | `DisplayMode, ConsoleStyledString` |
| `Scripts/Emuera/GameView/EmueraConsole.cs` | `ConsoleState, ConsoleRedraw, ChangedEventArgs, DisplayLineList, EmueraConsole, ClientBackGroundImage` |
| `Scripts/Emuera/GameView/EmueraConsole.Print.cs` | `EmueraConsole` |
| `Scripts/Emuera/GameView/HotkeyState.cs` | `HotkeyState, Eval, Command` |
| `Scripts/Emuera/GameView/HtmlManager.cs` | `HtmlManager, HtmlTagInfo, HtmlAnalzeStateFontTag, HtmlAnalzeStateButtonTag, HtmlDivTag, HtmlAnalzeState` |
| `Scripts/Emuera/GameView/MixedNum.cs` | `MixedNum` |
| `Scripts/Emuera/GameView/PrintStringBuffer.cs` | `PrintStringBuffer` |
| `Scripts/Emuera/GameView/StringMeasure.cs` | `StringMeasure` |
| `Scripts/Emuera/GameView/StringStyle.cs` | `FontVerticalAlign, StringStyle` |
| `Scripts/Emuera/GlobalStatic.cs` | `GlobalStatic` |
| `Scripts/Emuera/Modern/Script/Functions/ModernSqlManager.cs` | `ModernSqlManager, ReaderContext` |
| `Scripts/Emuera/Program.cs` | `EmueraCoreProfile, Program` |
| `Scripts/Emuera/Runtime/Utils/PluginSystem/IPluginMethod.cs` | `IPluginMethod` |
| `Scripts/Emuera/Runtime/Utils/PluginSystem/PluginManager.cs` | `PluginManager, BuiltinPluginMethod, ReflectionPluginMethod` |
| `Scripts/Emuera/Runtime/Utils/PluginSystem/PluginManifestAbstract.cs` | `PluginManifestAbstract` |
| `Scripts/Emuera/Runtime/Utils/PluginSystem/PluginMethodParameter.cs` | `PluginMethodParameter, PluginMethodParameterBuilder` |
| `Scripts/Emuera/Runtime/Utils/SnakeSqlManager.cs` | `SnakeSqlManager, ReaderContext` |
| `Scripts/Emuera/Runtime/Utils/SqliteRuntime.cs` | `SqliteRuntime` |
| `Scripts/Emuera/Sub/EmueraException.cs` | `EmueraException, ExeEE, CodeEE, IdentifierNotFoundCodeEE, NotImplCodeEE, FileEE, ScriptPosition` |
| `Scripts/Emuera/Sub/EraBinaryDataReader.cs` | `EraSaveFileType, EraSaveDataType, Ebdb, EraBDConst, EraBinaryDataReader, EraBinaryDataReader1808` |
| `Scripts/Emuera/Sub/EraBinaryDataWriter.cs` | `EraBinaryDataWriter` |
| `Scripts/Emuera/Sub/EraDataStream.cs` | `EraDataState, EraDataResult, EraDataReader, EraDataWriter` |
| `Scripts/Emuera/Sub/EraStreamReader.cs` | `EraStreamReader` |
| `Scripts/Emuera/Sub/LexicalAnalyzer.cs` | `LexEndWith, FormStrEndWith, StrEndWith, LexAnalyzeFlag, LexicalAnalyzer` |
| `Scripts/Emuera/Sub/Preload.cs` | `Preload` |
| `Scripts/Emuera/Sub/StringStream.cs` | `StringStream` |
| `Scripts/Emuera/Sub/SubWord.cs` | `SubWord, TripleSymbolSubWord, CurlyBraceSubWord, PercentSubWord, YenAtSubWord` |
| `Scripts/Emuera/Sub/Word.cs` | `Word, NullWord, IdentifierWord, LiteralIntegerWord, LiteralFloatWord, LiteralStringWord, OperatorWord, SymbolWord, StrFormWord, TermWord, MacroWord` |
| `Scripts/Emuera/Sub/WordCollection.cs` | `WordCollection` |

## `Scripts/EmueraContent.AndroidSpriteAnime.cs`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/EmueraContent.AndroidSpriteAnime.cs` | `EmueraContent, AndroidCroppedAtlasTextureEntry` |

## `Scripts/EmueraContent.Canvas.cs`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/EmueraContent.Canvas.cs` | `EmueraContent, ConsoleRenderBackend, ConsoleButtonHit, CanvasImageRenderInfo, CanvasImageOverlay, CanvasDivOverlay, ConsoleRenderSurface, ConsoleRenderStats, EscapedHitLineComparer, ConsoleTextRenderPlanEntry, ConsoleTextRenderPlan` |

## `Scripts/EmueraContent.cs`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/EmueraContent.cs` | `EmueraContent, ConsoleLineLayoutEntry, CanvasOverlayKey, ConsoleFontMetricsEntry, PureImageFallbackLine, GraphicsImageTextureCacheEntry, CbgRenderEntry, SpriteAnimeFrameLayoutInfo, AnimatedSpriteFrameCacheEntry, ConsoleColorRectPart, ConsoleTextPart, UiDiagnosticOverlay, OverlayRect` |

## `Scripts/EmueraContent.M0.cs`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/EmueraContent.M0.cs` | `EmueraContent` |

## `Scripts/EmueraImage.cs`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/EmueraImage.cs` | `EmueraImage, ImageSourceState` |

## `Scripts/EmueraMain.cs`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/EmueraMain.cs` | `EmueraMain, GpuWorkItem, TextRenderItem` |

## `Scripts/EmueraThread.cs`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/EmueraThread.cs` | `EmueraThread, PendingInput` |

## `Scripts/FirstWindow.cs`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/FirstWindow.cs` | `FirstWindow, LauncherGameCategory, LauncherGameSource, LauncherGameEntry, LauncherTab` |

## `Scripts/FrameRateHelper.cs`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/FrameRateHelper.cs` | `FrameRateHelper` |

## `Scripts/GenericUtils.cs`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/GenericUtils.cs` | `EmueraLogLevel, EmueraLogCategory, EmueraDisplayScrollMode, GenericUtils, UiEnvelope, ConsoleRenderSamplingWindow, DisplayBridgeSamplingWindow, SnakeAudioState, SnakeAudioInfo` |

## `Scripts/GodotHost`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/GodotHost/AppBootstrap.cs` | `AppBootstrap` |
| `Scripts/GodotHost/EmueraGpuRenderComponent.cs` | `EmueraGpuRenderComponent` |
| `Scripts/GodotHost/EmueraLifecycleComponent.cs` | `EmueraLifecycleComponent` |
| `Scripts/GodotHost/EmueraStartupComponent.cs` | `EmueraStartupComponent` |
| `Scripts/GodotHost/EmueraStartupOverlayView.cs` | `EmueraStartupOverlayView` |
| `Scripts/GodotHost/EmueraTextRenderComponent.cs` | `EmueraTextRenderComponent` |
| `Scripts/GodotHost/GameContentProbe.cs` | `GameContentProbe, GameCompatibilityDetector` |
| `Scripts/GodotHost/LegacySessionBackend.cs` | `LegacySessionBackend` |
| `Scripts/GodotHost/LegacySessionLaunchRegistry.cs` | `LegacySessionLaunchConfiguration, LegacySessionLaunchRegistry, LaunchKey` |
| `Scripts/GodotHost/LegacyThreadQuiescence.cs` | `LegacyThreadQuiescence` |
| `Scripts/GodotHost/PlatformGateway.cs` | `PlatformGateway` |
| `Scripts/GodotHost/PrototypeAudioBridge.cs` | `PrototypeAudioEffectKind, PrototypeAudioBridgeState, PrototypeAudioEffectCommand, PrototypeAudioBridge, AudioVoiceSlot` |
| `Scripts/GodotHost/PrototypeCommandPanel.cs` | `PrototypeCommandPanel` |
| `Scripts/GodotHost/PrototypeInputBridge.cs` | `PrototypeInputActionKind, PrototypeInputRequestKind, PrototypeInputActionDto, PrototypeInputRequestDto, PrototypeInputBridge` |
| `Scripts/GodotHost/PrototypeResourceBridge.cs` | `PrototypeResourceBridge, Projection` |
| `Scripts/GodotHost/PrototypeRuntimeNode.cs` | `PrototypeRuntimeNode` |
| `Scripts/GodotHost/PrototypeStatusView.cs` | `PrototypeStatusView` |
| `Scripts/GodotHost/RendererRuntimeIdentity.cs` | `RendererRuntimeIdentity` |

## `Scripts/M0`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/M0/LegacyDisplayObservation.cs` | `LegacyDisplayRect, LegacyDisplayPoint, LegacyDisplayCoverageEvidence, LegacyDisplayFeatureCoverage, LegacyDisplayBackendEvidence, LegacyDisplayViewportEvidence, LegacyDisplayHitProbe, LegacyDisplayHitEvidence, LegacyScreenshotEvidence, LegacyDisplayObservation` |
| `Scripts/M0/LegacyInputReplayDriver.cs` | `LegacyReplayTickKind, LegacyReplayTick, LegacyInputReplayDriver` |
| `Scripts/M0/LegacyRunnerConfig.cs` | `LegacyRunnerInput, LegacyRunnerSessionTarget, LegacyRunnerConfig` |
| `Scripts/M0/LegacyRunnerDeterminism.cs` | `LegacyRunnerDeterminism` |
| `Scripts/M0/LegacyRunnerHost.cs` | `InProcessSessionCycleSample, LegacyRunnerHost` |
| `Scripts/M0/LegacyRunnerReportWriter.cs` | `LegacyRunnerReportWriter` |
| `Scripts/M0/LegacySettlementTracker.cs` | `LegacySettlementTracker` |
| `Scripts/M0/LegacyTraceEvent.cs` | `LegacyTraceCategory, LegacyTraceThreadOwner, LegacyTraceOrderingPoint, LegacyTraceCompletionMode, LegacyTracePayload, LegacyTraceRunnerPayload, LegacyTraceClockPayload, LegacyTraceRngPayload, LegacyTraceWaitPayload, LegacyTraceInputPayload, LegacyTraceDisplayPayload, LegacyTraceEffectPayload, LegacyTraceErrorPayload, LegacyTraceEvent, LegacyTraceSnapshot, LegacySemanticTraceSnapshot` |
| `Scripts/M0/LegacyTraceRecorder.cs` | `LegacyTraceRecorder, LegacyTrace` |

## `Scripts/MultiLanguage.cs`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/MultiLanguage.cs` | `MultiLanguage` |

## `Scripts/Panels`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/Panels/Inputpad.cs` | `Inputpad` |
| `Scripts/Panels/OptionWindow.cs` | `OptionWindow` |
| `Scripts/Panels/QuickButtons.cs` | `QuickButtons` |
| `Scripts/Panels/SafeAreaApplicator.cs` | `SafeAreaApplicator, SafeInsets` |
| `Scripts/Panels/Scalepad.cs` | `Scalepad` |
| `Scripts/Panels/VirtualCursor.cs` | `VirtualCursor` |

## `Scripts/PerformanceBenchmark.cs`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/PerformanceBenchmark.cs` | `PerformanceBenchmark, BenchmarkResult` |

## `Scripts/ResolutionHelper.cs`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/ResolutionHelper.cs` | `ResolutionHelper` |

## `Scripts/SpriteDebugNotifier.cs`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/SpriteDebugNotifier.cs` | `SpriteDebugNotifier` |

## `Scripts/SpriteDebugViewer.cs`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/SpriteDebugViewer.cs` | `SpriteDebugViewer` |

## `Scripts/SpriteManager.cs`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/SpriteManager.cs` | `SpriteManager, SpriteInfo, TextureInfo, CallbackInfo, TextureInfoOtherThread, AsyncTextureLoadRequest, AsyncTextureLoadResult` |

## `Scripts/uEmuera`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/uEmuera/Application.cs` | `Application` |
| `Scripts/uEmuera/Drawing.cs` | `Bitmap, BitmapTexture, GodotFileAccessReadStream, BitmapRenderTexture, GraphicsUnit, Graphics, Brush, SolidBrush, Pen, DashStyle, DashCap, FontStyle, FontFamily, Font, Color, Point, Size, Rectangle, RectangleF, ImageAttributes, StringFormatFlags, StringFormat, CharacterRange` |
| `Scripts/uEmuera/Forms.cs` | `FormWindowState, Timer, TextFormatFlags, TextRenderer, DialogResult, MessageBoxButtons, MessageBox, ScrollBar, Control, PictureBox, ToolTip, TextBox` |
| `Scripts/uEmuera/Media.cs` | `Hand, Asterisk` |
| `Scripts/uEmuera/partial/AConsoleColoredPart.cs` | `AConsoleColoredPart` |
| `Scripts/uEmuera/partial/EmueraConsole.cs` | `EmueraConsole` |
| `Scripts/uEmuera/Properties.cs` | `ResourceManager, Resources` |
| `Scripts/uEmuera/Utils.cs` | `Logger, Utils, DirListing` |
| `Scripts/uEmuera/VisualBasic.cs` | `VbStrConv, Strings` |
| `Scripts/uEmuera/Window.cs` | `DebugDialog, MainWindow` |

## `Scripts/VirtualCursor.cs`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `Scripts/Panels/VirtualCursor.cs` | `VirtualCursor` |

## `src/Core/Application`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `src/Core/Application/GameSession.cs` | `GameSessionState, GameSessionOptions, GameSessionSnapshot, GameSession, RuntimeSwitchResult, CoreApplicationRuntime` |

## `src/Core/Compatibility`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `src/Core/Compatibility/BuiltInDialectCatalog.cs` | `BuiltInDialectCatalog, DeclaredDialectModule` |
| `src/Core/Compatibility/CompatibilityDescriptorRoute.cs` | `CompatibilityDescriptorRoute` |
| `src/Core/Compatibility/CompatibilityPlanSnapshot.cs` | `CompatibilityPlanSnapshot, ContractText, ContractCollections` |
| `src/Core/Compatibility/CompatibilityProfileCatalog.cs` | `CompatibilityProfileDefinition, CompatibilityProfileCatalog` |
| `src/Core/Compatibility/DialectModuleSnapshot.cs` | `ModuleDependencySnapshot, DialectModuleSnapshot, PortContractKind, BehaviorPortSnapshot` |
| `src/Core/Compatibility/DialectPlanConsumer.cs` | `DialectPlanConsumer` |
| `src/Core/Compatibility/DialectRuntime.cs` | `IDialectModule, DialectModuleDefinition, IDialectContribution, IInstructionContribution, IFunctionContribution, InstructionDescriptor, FunctionDescriptor, InstructionRegistryBuilder, FunctionRegistryBuilder, DialectModuleCatalog, RegisteredDialectModule, DialectPlan, CompatibilityPlan, CompatibilityPlanBuilder, SemanticVersionRange` |
| `src/Core/Compatibility/EraFlCompatibilityModule.cs` | `EraFlCompatibilityModule, GMapNodeData` |
| `src/Core/Compatibility/GameCompatibilityResolver.cs` | `GameCompatibilityConfidence, GameCompatibilityResolutionStatus, GameBaseProbeEvidence, GameCompatibilityAnchorEvidence, GameCompatibilityProbeEvidence, GameCompatibilityResolution, BuiltInGameCompatibilityResolver` |
| `src/Core/Compatibility/LegacyCompatibilityPlanConsumption.cs` | `LegacyCompatibilityConsumptionSnapshot, LegacyCompatibilityPlanConsumption` |

## `src/Core/Display`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `src/Core/Display/DisplayDtos.cs` | `DisplayRect, DisplayPoint, DisplayEffectSequence, DisplayPartKind, DisplayPlacement, DisplayLineAlignment, DisplayScrollIntent, DisplayBarrierKind, DisplayProvenance, DisplayScroll, DisplayStyle, DisplayInteraction, DisplayPart, DisplayTextPart, DisplayImagePart, DisplayShapePart, DisplayOpaquePart, DisplayDiv, DisplayLine, DisplayDataOnlyPatch, DisplayTransactionItem, DisplayLineItem, DisplayDataOnlyItem, DisplayOrderBarrier, DisplayBarrierItem, DisplayTransaction, IDisplayTeeObserver, DisplayTee, DisplayCanonicalHash` |

## `src/Core/Experiments`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `src/Core/Experiments/CooperativeVmExperimentRunner.cs` | `CooperativeVmExperimentRunner` |
| `src/Core/Experiments/M6ExperimentContracts.cs` | `M6FeatureFlags, M6EvidenceStatus, M6ExperimentIdentity, M6PerformanceThresholds, M6ExperimentDefinition, M6MetricSample, M6MetricPercentiles, M6MetricSeries, M6CooperativeVmStep, M6DualRunContract, ExperimentContractText` |
| `src/Core/Experiments/PrototypeScheduler.cs` | `PrototypeSchedulerMode, PrototypeStepStatus, PrototypeStepResult, PrototypeScheduler, PrototypeRendererExperimentGate` |
| `src/Core/Experiments/YieldabilityAudit.cs` | `YieldabilityPathCategory, YieldabilityDisposition, YieldabilityAuditRecord, YieldabilityAuditEvaluation, YieldabilityAuditInventory` |

## `src/Core/Governance`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `src/Core/Governance/M7GovernanceContracts.cs` | `RemovalLifecycleStatus, RemovalInventoryEntry, ReleaseCycle, RollbackDrill, RemovalApproval, RemovalPacket` |
| `src/Core/Governance/RuntimeReleaseLedger.cs` | `RuntimeLeaseKind, RuntimeLeaseKey, RuntimeLedgerSnapshot, RuntimeLease, RuntimeReleaseLedger` |

## `src/Core/Parsing`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `src/Core/Parsing/ErbParsing.cs` | `ContentToken, SourceSpan, CoreDiagnosticSeverity, CoreDiagnostic, ErbLineKind, ErbInstruction, ErbLogicalLine, ErbParseResult, ErbParser, ExpressionNode, LiteralExpression, IdentifierExpression, BinaryExpression, CoreLiteralValue, CoreLiteralKind` |

## `src/Core/Ports`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `src/Core/Ports/CompletionDispatcher.cs` | `IPortOwnerScheduler, CompletionDispatchStatus, CompletionDispatchResult, CompletionDispatcher` |
| `src/Core/Ports/InputStorageContracts.cs` | `InputDeviceKind, PointerButton, NormalizedAction, InputSubmissionStatus, InputCoordinator, StorageContentToken, ImportJournalState, ImportJournal, PortLeaseState, DatabaseOperationLease, LifecycleSignal, AudioGenerationGate, EraFlCapabilityPack` |
| `src/Core/Ports/PortAdapters.cs` | `IPortAdapter, PortSessionContext, IPortSessionAdapterFactory, IPortSessionAdapters, PortSessionScope` |
| `src/Core/Ports/PortManifest.cs` | `FallbackAdapterKind, FallbackAdapterDescriptor, PortManifestEntry, PortManifest, PortContractTextExtensions` |
| `src/Core/Ports/PortManifestDefaults.cs` | `M5PortManifest` |
| `src/Core/Ports/PortPrimitives.cs` | `PortTypeId, CapabilityId, PortOperationKey, PortOwnerKind, PortOwner, PortCompletionMode, PortCancellationMode, PortTimeoutMode, PortTimeout, PortPayloadLimits, PortErrorCode, PortFault, PortCancellationContract, PortRequest, PortCompletion, PortContractText` |
| `src/Core/Ports/RuntimePortAdapters.cs` | `RuntimeStorageAdapter, RuntimeLifecycleAdapter` |
| `src/Core/Ports/RuntimePortHub.cs` | `RuntimePortManifestBuilder, RuntimePortStamp, RuntimePortHub, InlinePortOwnerScheduler` |

## `src/Core/Resources`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `src/Core/Resources/PixelFormat.cs` | `PixelFormat, PixelFormatInfo, PixelColor, PixelOffset, PixelRect` |
| `src/Core/Resources/PixelStore.cs` | `PixelStore, SurfaceEntry, PixelStoreSnapshot` |
| `src/Core/Resources/PixelSurface.cs` | `PixelHandle, PixelRevision, PixelSurface, PixelSurfaceSnapshot` |
| `src/Core/Resources/ResourceCatalog.cs` | `ResourceKey, ResourceDescriptor, ResourceCatalog, MemoryBudgetSnapshot, MemoryBudget, MemoryReservation, ResourceProjectionState, ResourceUploadDescriptor, ResourceUploadDecision, ResourceBridgeLedger` |
| `src/Core/Resources/ResourceRuntime.cs` | `ResourceAdmissionStatus, ResourceLease, ResourceRuntimeEntry, ResourceRuntime` |
| `src/Core/Resources/SourceToken.cs` | `SourceToken` |

## `src/Core/Runtime`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `src/Core/Runtime/ErbExecution.cs` | `VmCompletionMode, VmExecutionState, VmStepStopReason, VmStepBudget, VmOperationId, VmFault, VmEffect, VmDisplayEffect, VmInputEffect, VmPortEffect, VmApplicationEffect, VmEffectContract, VmStepResult, VmCompletion, ErbInterpreterModuleSupport, ErbInterpreterKey, ErbInterpreterDescriptor, IErbInterpreterFactory, IErbInterpreterCatalog, ErbInterpreterCatalog, InterpreterRegistration, FrozenErbInterpreterCatalog, ErbInterpreterContext, IErbInterpreterHost, ResumableErbInterpreterHost, VmContractException, InterpreterContractText` |
| `src/Core/Runtime/LegacyCoreAdapter.cs` | `CoreSessionBoundary, ILegacyCoreAdapter, LegacyCoreAdapter` |

## `src/Core/Save`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `src/Core/Save/SaveCodec.cs` | `SaveProfileId, SaveSnapshot, ISaveCodec, DeterministicSaveCodec` |
| `src/Core/Save/SaveService.cs` | `SaveTransactionState, SaveCandidate, ISaveBlobStore, FileSaveBlobStore, SaveService` |

## `src/Core/Session`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `src/Core/Session/LegacySessionFacade.cs` | `ILegacySessionBackend, LegacySessionSwitchResult, LegacySessionFacade, BackendSnapshot` |
| `src/Core/Session/SessionCoordinator.cs` | `SessionSwitchStatus, SessionPreparationStatus, SessionSelection, SessionSwitchResult, SessionPreparationResult, SessionCandidate, SessionSwitchLease, SessionCoordinator, ContractTextForSession` |
| `src/Core/Session/SessionGeneration.cs` | `SessionGeneration, SessionOperationId, SessionStamp, SessionGenerationClock, SessionCompletionGuard` |

## `src/Core/State`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `src/Core/State/VariableStore.cs` | `VariableScope, CoreValueKind, VariableKey, CoreValue, VariableSnapshot, VariableCandidate, VariableStore` |

## `tools/core-contracts`

| 文件 | 识别到的类型声明 |
| --- | --- |
| `tools/core-contracts/M2DisplayContracts.cs` | `M2DisplayContracts, RecordingObserver` |
| `tools/core-contracts/M3M7ContractSmoke.cs` | `M3M7ContractSmoke, QueuedOwnerScheduler` |
| `tools/core-contracts/Program.cs` | `—` |
| `tools/core-contracts/TestModules.cs` | `TestDialectModule, TestInstructionContribution, TestFunctionContribution, TestInterpreterFactory, MutableDescriptorInterpreterFactory, DescriptorIgnoringTestInterpreterFactory, TestInterpreterHost, ResumableHostMode, ResumableTestInterpreterHost, ResumableTestInterpreterFactory, InterpreterTestDescriptors, TestLegacyBackend` |

## `scenes/`

| 场景资产 | 根节点类型 | 挂载脚本（ext_resource） |
| --- | --- | --- |
| `scenes/Inputpad.tscn` | `Control` | `res://Scripts/Panels/Inputpad.cs` |
| `scenes/OptionWindow.tscn` | `Control` | `res://Scripts/Panels/OptionWindow.cs` |
| `scenes/QuickButtons.tscn` | `CanvasLayer` | `res://Scripts/Panels/QuickButtons.cs` |
| `scenes/RuntimeDiagnosticsPanel.tscn` | `PanelContainer` | `res://Scripts/Diagnostics/RuntimeDiagnosticsPanel.cs` |
| `scenes/Scalepad.tscn` | `Control` | `res://Scripts/Panels/Scalepad.cs` |
| `scenes/VirtualCursor.tscn` | `CanvasLayer` | `res://Scripts/Panels/VirtualCursor.cs` |

