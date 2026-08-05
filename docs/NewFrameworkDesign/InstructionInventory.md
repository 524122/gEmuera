# 指令与内置函数机械库存快照

本文同时固定上游工作区和旧 gEmuera 的“名称集合证据”，用于评审迁移范围和设计生成器。它不是行为兼容证明：参数绑定、返回值、异常、显示副作用、等待点和时序仍须差分。

## 快照身份

本工作区的 `.git` 不能由当前命令行解析为有效仓库，因此暂时不能记录 commit；改用文件 SHA-256 防止把不同源码版本的计数混在一起。

| 源文件 | SHA-256 | 抽取结果 |
| --- | --- | --- |
| `XEmuera/XEmuera/Emuera/GameProc/Function/FunctionIdentifier.cs` | `ACD7D12A527774A296F94D3D35E7057BDD1C5E91CC480337030E74A3256440DD` | 203 个显式 `addFunction(FunctionCode.X, ...)` 唯一引用；另有 PRINT/PRINTDATA 系列循环注册 |
| `XEmuera/XEmuera/Emuera/GameProc/Function/BuiltInFunctionCode.cs` | `800971EDE3BBE88BE86E31B32E8F169E580B5C2DA604206A4D15CB90B1D6B25E` | `FunctionCode` 中 285 个唯一名称 |
| `XEmuera/XEmuera/Emuera/GameData/Function/Creator.cs` | `70547491BFB9EABFD4E25A5C3AE8D4A70F42F67E7FBB2BAC39E22497F60D21C2` | 248 个公开字典键，映射到 166 个唯一实现类 |

计数口径必须保持分开：285 是语句 code 枚举，不等于 285 个显式注册调用；248 是表达式内置函数字典，不应和语句指令相加后宣称“全部支持”。

## 旧 gEmuera 集合差异

当前 [M0-DIA-01 机器库存](generated/dialect-inventory.json) 对同一源码身份重新按“所有显式 `addFunction`/`addPrintFunction`/`addPrintDataFunction` 调用点”计数，得到 326 个指令注册、327 个 FunctionCode、360 个 Creator 公开键。对应 SHA-256：FunctionIdentifier `0674DD19C63F07A72328F986C862EC197AFD59FD5EC423EF7264C51B16A93629`，BuiltInFunctionCode `436F7DEFEDCE67BF456854BE172DB7F6E2FA87ECBD49ADFEEDD180386E108986`，Creator `B76A7E638FB4269C00830A544877E2190EE1C41E52FFD62CFC459A8ABC2BF381`。旧“240/358”是较窄正则与较早人工快照口径，遗漏 ArgumentBuilder 类注册和后来加入的两个 Snake 中文 alias，现由机器报告取代；不同口径不得混算。

旧 FunctionCode 相对本上游快照至少多出以下 40 个名称：

```text
BITMAP_CACHE_ENABLE, BREAKBUTTON, CALLSHARP, CALLSTR, CLEARBGIMAGE,
CLEARIMAGELAYER, CLEARIMAGELAYER_ALL, DT_COLUMN_OPTIONS, HTML_PRINT_ISLAND,
HTML_PRINT_ISLAND_CLEAR, HTML_PRINTC, HTML_PRINTLC, JUMPSTR, PRINTFORMN,
PRINTFORMSN, PRINTN, PRINTSN, PRINTVN, REMOVEBGIMAGE, SET_SKIA_QUALITY,
SET_TEXT_DRAWING_MODE, SETBGIMAGE, SETIMAGELAYER, STRICT_FONT_FALLBACK,
TEXT_BGC_OFF, TEXT_BGC_ON, TINPUTNF, TINPUTSNF, TONEINPUTNF, TONEINPUTSNF,
TOOLTIP_CUSTOM, TOOLTIP_FORMAT, TOOLTIP_IMG, TOOLTIP_SETFONT,
TOOLTIP_SETFONTSIZE, TRYCALLSTR, TRYCCALLSTR, TRYCJUMPSTR, TRYJUMPSTR,
VARI, VARS
```

旧 Creator 相对本上游快照有 110 个额外公开键，覆盖 float、SQL、HOTKEY、MOUSEBUTTON、图形、多媒体、Map/DT 和 Snake 控制：

```text
ACOS, ARGLEN, ASIN, ATAN, BGMCONTROL, BITGET, BITINDEXOFFIRST,
BITMAP_CACHE_ENABLE, BITSET, BITTOGGLE, CBGSETCIMG, CEIL, COS,
DT_CELL_GETF, DT_CELL_SETF, DT_COLUMN_NAMES, DT_FROMXML, DT_TOXML,
EVAL, EVALF, EVALS, EXISTMETH, EXISTSIMAGELAYER, FLOOR, FLOWINPUT,
FLOWINPUTS, G_POLYGON_DRAW, G_POLYGON_FILL, G_POLYGON_POINT_ADD,
G_POLYGON_POINT_CLEAR, GCLEARLOWALPHA, GDASHSTYLE, GDRAWLINE,
GDRAWPOLYGON, GDRAWPOLYGONADDPOINT, GDRAWPOLYGONCLEARPOINT, GDRAWSTRING,
GET_SKIA_QUALITY, GET_TEXT_DRAWING_MODE, GETANIMETIMER, GETARGCOUNT,
GETCSVNOBYCALLNAME, GETCSVNOBYMASTERNAME, GETCSVNOBYNAME,
GETCSVNOBYNICKNAME, GETDOINGFUNCTION, GETMETH, GETMETHF, GETMETHS,
GETPLATFORM, GETSOUNDORBGMINFO, GETVARF, GFILLPOLYGON, GGETPEN,
GGETPENWIDTH, HOTKEY_STATE, HOTKEY_STATE_INIT, HTML_STRINGLINES,
ISPLAYINGBGM, ISPLAYINGSOUND, MAP_FINDKEY, MAP_FROMSTRING, MAP_MERGE,
MAP_REMOVEIF, MAP_TOSTRING, MAP_VALUES, MATCHALL, MATCHALLEX, MOUSEB,
MOUSEBUTTON, MOVETEXTBOX, OUTPUTLOG, RESUMETEXTBOX, ROUND, SIN,
SOUNDCONTROL, SPRITECREATEFROMFILE, SPRITEDISPOSEALL, SQL_CONNECT,
SQL_CONNECTION_OPEN, SQL_DISCONNECT, SQL_ESCAPE, SQL_EXECUTE_NONQUERY,
SQL_EXECUTE_READER, SQL_EXECUTE_SCALAR_FLOAT, SQL_EXECUTE_SCALAR_LONG,
SQL_EXECUTE_SCALAR_STRING, SQL_EXPORT_DT_XML, SQL_EXPORT_MAP_XML,
SQL_IMPORT_DT_XML, SQL_IMPORT_MAP_XML, SQL_IMPORT_XML_CUSTOM,
SQL_P_EXECUTE_NONQUERY, SQL_P_EXECUTE_READER, SQL_P_EXECUTE_SCALAR_FLOAT,
SQL_P_EXECUTE_SCALAR_LONG, SQL_P_EXECUTE_SCALAR_STRING, SQL_READER_CLOSE,
SQL_READER_GET_FLOAT, SQL_READER_GET_LONG, SQL_READER_GET_STRING,
SQL_READER_ISNULL, SQL_READER_READ, TAN, TOFLOAT, TOSTRF, UNCHECKED_ADD,
UNCHECKED_MUL, UNCHECKED_NEG, UNCHECKED_SUB
```

这是名称差集而非最终分类：例如旧工程可能保留上游已有键但改变实现/参数，也可能同时暴露兼容别名。生成器必须输出 upstream、legacy、target 三集合及交集行为差分，不能只保护原 285/248 集合。

## 语句指令：203 个显式注册引用

以下集合来自 `addFunction(FunctionCode.<NAME>, ...)` 的静态唯一匹配；排序仅便于 diff，不表示执行优先级。

```text
ADDCHARA, ADDCOPYCHARA, ADDDEFCHARA, ADDSPCHARA, ADDVOIDCHARA, ALIGNMENT,
ARRAYCOPY, ARRAYREMOVE, ARRAYSHIFT, ARRAYSORT, ASSERT, AWAIT, BAR, BARL,
BEGIN, BINPUT, BINPUTS, BREAK, CALL, CALLEVENT, CALLF, CALLFORM, CALLFORMF,
CALLTRAIN, CASE, CASEELSE, CATCH, CLEARBIT, CLEARLINE, CLEARTEXTBOX,
CONTINUE, COPYCHARA, CUPCHECK, CUSTOMDRAWLINE, CVARSET, DATA, DATAFORM,
DATALIST, DEBUGCLEAR, DEBUGPRINT, DEBUGPRINTFORM, DEBUGPRINTFORML,
DEBUGPRINTL, DELALLCHARA, DELCHARA, DELDATA, DO, DOTRAIN, DRAWLINE,
DRAWLINEFORM, DUMPRAND, ELSE, ELSEIF, ENCODETOUNI, ENDCATCH, ENDDATA,
ENDFUNC, ENDIF, ENDLIST, ENDNOSKIP, ENDSELECT, FONTBOLD, FONTITALIC,
FONTREGULAR, FONTSTYLE, FOR, FORCE_BEGIN, FORCE_QUIT,
FORCE_QUIT_AND_RESTART, FORCEKANA, FORCEWAIT, FUNC, GETTIME, GOTO,
GOTOFORM, HTML_PRINT, HTML_TAGSPLIT, IF, INITRAND, INPUT, INPUTANY,
INPUTMOUSEKEY, INPUTS, INVERTBIT, JUMP, JUMPFORM, LOADCHARA, LOADDATA,
LOADGAME, LOADGLOBAL, LOADVAR, LOOP, NEXT, NOSKIP, ONEBINPUT, ONEBINPUTS,
ONEINPUT, ONEINPUTS, OUTPUTLOG, PICKUPCHARA, PLAYBGM, PLAYSOUND, POWER,
PRINT_ABL, PRINT_EXP, PRINT_IMG, PRINT_ITEM, PRINT_MARK, PRINT_PALAM,
PRINT_RECT, PRINT_SHOPITEM, PRINT_SPACE, PRINT_TALENT, PRINTBUTTON,
PRINTBUTTONC, PRINTBUTTONLC, PRINTCPERLINE, PRINTPLAIN, PRINTPLAINFORM,
PUTFORM, QUIT, QUIT_AND_RESTART, RANDOMIZE, REDRAW, REF, REFBYNAME, REND,
REPEAT, RESET_STAIN, RESETBGCOLOR, RESETCOLOR, RESETDATA, RESETGLOBAL,
RESTART, RETURN, RETURNF, RETURNFORM, REUSELASTLINE, SAVECHARA, SAVEDATA,
SAVEGAME, SAVEGLOBAL, SAVENOS, SAVEVAR, SELECTCASE, SETBGCOLOR,
SETBGCOLORBYNAME, SETBGMVOLUME, SETBIT, SETCOLOR, SETCOLORBYNAME, SETFONT,
SETSOUNDVOLUME, SIF, SKIPDISP, SKIPLOG, SORTCHARA, SPLIT, STOPBGM,
STOPCALLTRAIN, STOPSOUND, STRDATA, STRLEN, STRLENFORM, STRLENFORMU,
STRLENU, SWAP, SWAPCHARA, THROW, TIMES, TINPUT, TINPUTS, TONEINPUT,
TONEINPUTS, TOOLTIP_SETCOLOR, TOOLTIP_SETDELAY, TOOLTIP_SETDURATION,
TRYCALL, TRYCALLF, TRYCALLFORM, TRYCALLFORMF, TRYCALLLIST, TRYCCALL,
TRYCCALLFORM, TRYCGOTO, TRYCGOTOFORM, TRYCJUMP, TRYCJUMPFORM, TRYGOTO,
TRYGOTOFORM, TRYGOTOLIST, TRYJUMP, TRYJUMPFORM, TRYJUMPLIST, TWAIT,
UPCHECK, UPDATECHECK, VARSET, VARSIZE, WAIT, WAITANYKEY, WEND, WHILE
```

## FunctionCode：285 个枚举名称

比上述 203 多出的主要是由构造器循环展开的 PRINT/PRINTDATA 变体，以及 `SET` 等需要沿注册逻辑继续判定的 code。目标生成器必须执行或等价解析这些循环，不能只做一次正则匹配。

```text
ADDCHARA, ADDCOPYCHARA, ADDDEFCHARA, ADDSPCHARA, ADDVOIDCHARA, ALIGNMENT,
ARRAYCOPY, ARRAYREMOVE, ARRAYSHIFT, ARRAYSORT, ASSERT, AWAIT, BAR, BARL,
BEGIN, BINPUT, BINPUTS, BREAK, CALL, CALLEVENT, CALLF, CALLFORM, CALLFORMF,
CALLTRAIN, CASE, CASEELSE, CATCH, CLEARBIT, CLEARLINE, CLEARTEXTBOX,
CONTINUE, COPYCHARA, CUPCHECK, CUSTOMDRAWLINE, CVARSET, DATA, DATAFORM,
DATALIST, DEBUGCLEAR, DEBUGPRINT, DEBUGPRINTFORM, DEBUGPRINTFORML,
DEBUGPRINTL, DELALLCHARA, DELCHARA, DELDATA, DO, DOTRAIN, DRAWLINE,
DRAWLINEFORM, DUMPRAND, ELSE, ELSEIF, ENCODETOUNI, ENDCATCH, ENDDATA,
ENDFUNC, ENDIF, ENDLIST, ENDNOSKIP, ENDSELECT, FONTBOLD, FONTITALIC,
FONTREGULAR, FONTSTYLE, FOR, FORCE_BEGIN, FORCE_QUIT,
FORCE_QUIT_AND_RESTART, FORCEKANA, FORCEWAIT, FUNC, GETTIME, GOTO,
GOTOFORM, HTML_PRINT, HTML_TAGSPLIT, IF, INITRAND, INPUT, INPUTANY,
INPUTMOUSEKEY, INPUTS, INVERTBIT, JUMP, JUMPFORM, LOADCHARA, LOADDATA,
LOADGAME, LOADGLOBAL, LOADVAR, LOOP, NEXT, NOSKIP, ONEBINPUT, ONEBINPUTS,
ONEINPUT, ONEINPUTS, OUTPUTLOG, PICKUPCHARA, PLAYBGM, PLAYSOUND, POWER,
PRINT, PRINT_ABL, PRINT_EXP, PRINT_IMG, PRINT_ITEM, PRINT_MARK, PRINT_PALAM,
PRINT_RECT, PRINT_SHOPITEM, PRINT_SPACE, PRINT_TALENT, PRINTBUTTON,
PRINTBUTTONC, PRINTBUTTONLC, PRINTC, PRINTCD, PRINTCK, PRINTCPERLINE,
PRINTD, PRINTDATA, PRINTDATAD, PRINTDATADL, PRINTDATADW, PRINTDATAK,
PRINTDATAKL, PRINTDATAKW, PRINTDATAL, PRINTDATAW, PRINTDL, PRINTDW,
PRINTFORM, PRINTFORMC, PRINTFORMCD, PRINTFORMCK, PRINTFORMD, PRINTFORMDL,
PRINTFORMDW, PRINTFORMK, PRINTFORMKL, PRINTFORMKW, PRINTFORML, PRINTFORMLC,
PRINTFORMLCD, PRINTFORMLCK, PRINTFORMS, PRINTFORMSD, PRINTFORMSDL,
PRINTFORMSDW, PRINTFORMSK, PRINTFORMSKL, PRINTFORMSKW, PRINTFORMSL,
PRINTFORMSW, PRINTFORMW, PRINTK, PRINTKL, PRINTKW, PRINTL, PRINTLC,
PRINTLCD, PRINTLCK, PRINTPLAIN, PRINTPLAINFORM, PRINTS, PRINTSD, PRINTSDL,
PRINTSDW, PRINTSINGLE, PRINTSINGLED, PRINTSINGLEFORM, PRINTSINGLEFORMD,
PRINTSINGLEFORMK, PRINTSINGLEFORMS, PRINTSINGLEFORMSD, PRINTSINGLEFORMSK,
PRINTSINGLEK, PRINTSINGLES, PRINTSINGLESD, PRINTSINGLESK, PRINTSINGLEV,
PRINTSINGLEVD, PRINTSINGLEVK, PRINTSK, PRINTSKL, PRINTSKW, PRINTSL,
PRINTSW, PRINTV, PRINTVD, PRINTVDL, PRINTVDW, PRINTVK, PRINTVKL,
PRINTVKW, PRINTVL, PRINTVW, PRINTW, PUTFORM, QUIT, QUIT_AND_RESTART,
RANDOMIZE, REDRAW, REF, REFBYNAME, REND, REPEAT, RESET_STAIN,
RESETBGCOLOR, RESETCOLOR, RESETDATA, RESETGLOBAL, RESTART, RETURN, RETURNF,
RETURNFORM, REUSELASTLINE, SAVECHARA, SAVEDATA, SAVEGAME, SAVEGLOBAL,
SAVENOS, SAVEVAR, SELECTCASE, SET, SETBGCOLOR, SETBGCOLORBYNAME,
SETBGMVOLUME, SETBIT, SETCOLOR, SETCOLORBYNAME, SETFONT, SETSOUNDVOLUME,
SIF, SKIPDISP, SKIPLOG, SORTCHARA, SPLIT, STOPBGM, STOPCALLTRAIN,
STOPSOUND, STRDATA, STRLEN, STRLENFORM, STRLENFORMU, STRLENU, SWAP,
SWAPCHARA, THROW, TIMES, TINPUT, TINPUTS, TONEINPUT, TONEINPUTS,
TOOLTIP_SETCOLOR, TOOLTIP_SETDELAY, TOOLTIP_SETDURATION, TRYCALL,
TRYCALLF, TRYCALLFORM, TRYCALLFORMF, TRYCALLLIST, TRYCCALL, TRYCCALLFORM,
TRYCGOTO, TRYCGOTOFORM, TRYCJUMP, TRYCJUMPFORM, TRYGOTO, TRYGOTOFORM,
TRYGOTOLIST, TRYJUMP, TRYJUMPFORM, TRYJUMPLIST, TWAIT, UPCHECK,
UPDATECHECK, VARSET, VARSIZE, WAIT, WAITANYKEY, WEND, WHILE
```

## Creator：248 个公开函数键

以下按领域分组，组别是文档导航，不改变源码字典的全局命名空间。

| 领域 | 公共键 |
| --- | --- |
| 角色/CSV | `GETCHARA`, `GETSPCHARA`, `CSVNAME`, `CSVCALLNAME`, `CSVNICKNAME`, `CSVMASTERNAME`, `CSVCSTR`, `CSVBASE`, `CSVABL`, `CSVMARK`, `CSVEXP`, `CSVRELATION`, `CSVTALENT`, `CSVCFLAG`, `CSVEQUIP`, `CSVJUEL`, `FINDCHARA`, `FINDLASTCHARA`, `EXISTCSV` |
| 状态/配置/文件 | `VARSIZE`, `CHKFONT`, `CHKDATA`, `ISSKIP`, `MOUSESKIP`, `MESSKIP`, `GETCOLOR`, `GETDEFCOLOR`, `GETFOCUSCOLOR`, `GETBGCOLOR`, `GETDEFBGCOLOR`, `GETSTYLE`, `GETFONT`, `BARSTR`, `CURRENTALIGN`, `CURRENTREDRAW`, `COLOR_FROMNAME`, `COLOR_FROMRGB`, `CHKVARDATA`, `CHKCHARADATA`, `CHKGLOBALDATA`, `FIND_VARDATA`, `FIND_CHARADATA`, `MONEYSTR`, `PRINTCPERLINE`, `PRINTCLENGTH`, `SAVENOS`, `GETCONFIG`, `GETCONFIGS`, `EXISTFILE`, `EXISTVAR`, `ISDEFINED` |
| 时间/数值/数组 | `GETTIME`, `GETTIMES`, `GETMILLISECOND`, `GETSECOND`, `RAND`, `MIN`, `MAX`, `ABS`, `POWER`, `SQRT`, `CBRT`, `LOG`, `LOG10`, `EXPONENT`, `SIGN`, `LIMIT`, `SUMARRAY`, `SUMCARRAY`, `MATCH`, `CMATCH`, `GROUPMATCH`, `NOSAMES`, `ALLSAMES`, `MAXARRAY`, `MAXCARRAY`, `MINARRAY`, `MINCARRAY`, `GETBIT`, `GETNUM`, `GETPALAMLV`, `GETEXPLV`, `FINDELEMENT`, `FINDLASTELEMENT`, `INRANGE`, `INRANGEARRAY`, `INRANGECARRAY`, `GETNUMB`, `ARRAYMSORT`, `ARRAYMSORTEX` |
| 字符串/HTML | `STRLENS`, `STRLENSU`, `SUBSTRING`, `SUBSTRINGU`, `STRFIND`, `STRFINDU`, `STRCOUNT`, `TOSTR`, `TOINT`, `TOUPPER`, `TOLOWER`, `TOHALF`, `TOFULL`, `LINEISEMPTY`, `REPLACE`, `UNICODE`, `UNICODEBYTE`, `CONVERT`, `ISNUMERIC`, `ESCAPE`, `ENCODETOUNI`, `CHARATU`, `GETLINESTR`, `STRFORM`, `STRJOIN`, `HTML_GETPRINTEDSTR`, `HTML_POPPRINTINGSTR`, `HTML_TOPLAINTEXT`, `HTML_ESCAPE`, `HTML_STRINGLEN`, `HTML_SUBSTRING` |
| 窗口/输入/文本文件 | `CLIENTWIDTH`, `CLIENTHEIGHT`, `GETKEY`, `GETKEYTRIGGERED`, `MOUSEX`, `MOUSEY`, `ISACTIVE`, `SAVETEXT`, `LOADTEXT`, `GETTEXTBOX`, `SETTEXTBOX`, `GETDISPLAYLINE` |
| 反射/变量 | `ENUMFUNCBEGINSWITH`, `ENUMFUNCENDSWITH`, `ENUMFUNCWITH`, `ENUMVARBEGINSWITH`, `ENUMVARENDSWITH`, `ENUMVARWITH`, `ENUMMACROBEGINSWITH`, `ENUMMACROENDSWITH`, `ENUMMACROWITH`, `ENUMFILES`, `GETVAR`, `GETVARS`, `SETVAR`, `VARSETEX`, `EXISTFUNCTION`, `ERDNAME` |
| XML | `XML_DOCUMENT`, `XML_RELEASE`, `XML_GET`, `XML_GET_BYNAME`, `XML_SET`, `XML_SET_BYNAME`, `XML_EXIST`, `XML_TOSTR`, `XML_ADDNODE`, `XML_ADDNODE_BYNAME`, `XML_REMOVENODE`, `XML_REMOVENODE_BYNAME`, `XML_REPLACE`, `XML_REPLACE_BYNAME`, `XML_ADDATTRIBUTE`, `XML_ADDATTRIBUTE_BYNAME`, `XML_REMOVEATTRIBUTE`, `XML_REMOVEATTRIBUTE_BYNAME` |
| Map | `MAP_CREATE`, `MAP_EXIST`, `MAP_RELEASE`, `MAP_GET`, `MAP_CLEAR`, `MAP_SIZE`, `MAP_HAS`, `MAP_SET`, `MAP_REMOVE`, `MAP_GETKEYS`, `MAP_TOXML`, `MAP_FROMXML` |
| DataTable | `DT_CREATE`, `DT_EXIST`, `DT_RELEASE`, `DT_NOCASE`, `DT_CLEAR`, `DT_COLUMN_ADD`, `DT_COLUMN_EXIST`, `DT_COLUMN_REMOVE`, `DT_COLUMN_LENGTH`, `DT_ROW_ADD`, `DT_ROW_SET`, `DT_ROW_REMOVE`, `DT_ROW_LENGTH`, `DT_CELL_GET`, `DT_CELL_ISNULL`, `DT_CELL_GETS`, `DT_CELL_SET`, `DT_SELECT` |
| 运行时/内存/声音 | `REGEXPMATCH`, `EXISTSOUND`, `GETMEMORYUSAGE`, `CLEARMEMORY` |

## Graphics、Sprite 与 CBG 的精确公共名

这组单列是为了避免再次把实现类型名误认为脚本 API。

| 公共键 | Creator 映射 | 设计归属 |
| --- | --- | --- |
| `SPRITECREATED`, `SPRITEWIDTH`, `SPRITEHEIGHT`, `SPRITEPOSX`, `SPRITEPOSY` | `SpriteStateMethod` | 查询 Sprite descriptor |
| `SPRITEMOVE`, `SPRITESETPOS` | `SpriteSetPosMethod` | 修改 Sprite 逻辑偏移 |
| `SPRITECREATE`, `SPRITEDISPOSE` | `SpriteCreateMethod`, `SpriteDisposeMethod` | 会话动态 Sprite 生命周期 |
| `SPRITEANIMECREATE`, `SPRITEANIMEADDFRAME` | 对应 Anime methods | 动画 descriptor |
| `SPRITEGETCOLOR` | `SpriteGetColorMethod` | 像素查询 effect |
| `GCREATED`, `GWIDTH`, `GHEIGHT` | `GraphicsStateMethod` | G 槽状态 |
| `GCREATE`, `GCREATEFROMFILE`, `GDISPOSE`, `GCLEAR` | 对应 Graphics methods | G 槽生命周期 |
| `GFILLRECTANGLE`, `GDRAWSPRITE`, `GDRAWG`, `GDRAWGWITHMASK`, `GDRAWGWITHROTATE`, `GDRAWTEXT`, `GROTATE` | 对应 Graphics draw methods | VM owner thread PixelStore；纹理 revision 主线程投影 |
| `GGETCOLOR`, `GSETCOLOR`, `GSETBRUSH`, `GSETFONT`, `GSETPEN`, `GGETFONT`, `GGETFONTSIZE`, `GGETFONTSTYLE`, `GGETTEXTSIZE`, `GGETBRUSH` | 对应 state/style methods | G 状态与查询 |
| `GSAVE`, `GLOAD` | `GraphicsSaveMethod`, `GraphicsLoadMethod` | 有界文件 I/O effect |
| `CBGSETG` | `CBGSetGraphicsMethod` | 以 G 设置 CBG 层 |
| `CBGSETSPRITE` | `CBGSetCIMGMethod` | 以 Sprite 设置 CBG 层；实现类名不是公共名 |
| `CBGCLEAR`, `CBGCLEARBUTTON`, `CBGREMOVERANGE`, `CBGREMOVEBMAP` | 对应 clear/remove methods | 清理 CBG 状态 |
| `CBGSETBMAPG` | `CBGSetBMapGMethod` | 设置按钮位图 |
| `CBGSETBUTTONSPRITE` | `CBGSETButtonSpriteMethod` | 设置普通/选中按钮 Sprite |
| `SETANIMETIMER` | `SetAnimeTimerMethod` | 动画时钟 effect |

## 从快照到可执行生成器

M0-DIA-01 当前 D0 JSON：97 个 profile/setting 命中全部有 owner、目标模块/策略、BehaviorKey 或 CapabilityId 和 fixture ID；326 个指令注册按静态公共/v24/Snake contribution 分开；360 个 Creator key 保留逐公开名；9 个指令/表达式同名键记录当前 `ContainsKey` 指令优先规则。当前 canonical identity 为 `a30d210f...ccdc2`。

这仍只是 D0 的可执行起点：290 个指令与全部 360 个表达式函数仍标为 `legacy.*.unresolved` 或 `game.snake.candidate`。其中 13 个 `SNAKE_*` 指令 handler 出现在 v24/公共贡献，2 个 Snake expression alias 出现在无条件字典；类型名只能提示 provenance，不能代替模块归属 fixture。

[M0-DIA-02 注册快照](generated/dialect-registry-snapshots.json) 在不接入旧 Parser/VM 的前提下建立测试投影：v24 为 290 个指令/358 个表达式函数，Snake 为 326/360，差集为 36 个指令和 2 个函数。snapshot set SHA-256 为 `caad8abf18da81c1658fada3c46f9637b5b5426e8131da2f7390bd5191c4e673`；v24/Snake hash 分别为 `d668d5e...cddbc` / `ff19ade...e0142`。available catalog 加入但不选择 `game.snake` 时 v24 hash 不变；这只证明测试构建器元属性。旧静态构造仍无条件调用两组注册，因此 runtime isolation 明确为 `Failed`，不能借测试投影关闭 D2。

[M0-DIA-03 签名库存](generated/dialect-signature-inventory.json) 进一步固定 descriptor 输入：326 个指令注册来源全部解析且 binding kind 无 Unknown，参数 schema 为 214 `Resolved` / 112 `Conditional`；360 个表达式函数 handler 与 argument source 全部解析，return type 为 358 `Resolved` / 2 `Conditional`，条件项是共享 `GetConfigMethod` 的 `GETCONFIG/GETCONFIGS`。静态体扫描列出 31 个 `InputWaitCandidate`，但 completion/effect 全部仍是 `StaticCandidate + behaviorFixtureStatus=Uncovered`。descriptor set SHA-256 为 `321dc3a2883f7485338894379194c1f230bd13a8667de97a5b68ed7003603e8e`。

112 个条件指令参数并非“缺少随便填一个默认值”：`CALL/CALLFORM`、PRINT 系列等复用同一 handler，实际 ArgBuilder 由构造参数/公开键决定。未来模块 contribution 必须提交已经求值的逐公开键 signature，计划 hash 也必须包含该结果；不能只 hash handler 类型名。

[M0-DIA-04 条件签名解析](generated/dialect-signature-resolution.json) 已用 19 条版本化 `handler + exact/regex` 规则逐 key 求值这 112 项，连同原 214 条唯一项形成 326/326 静态指令参数、0 unresolved。每条规则固定 `expectedMatchCount`，漏匹配、双重匹配、陈旧 source hash、重复 selector、误命中已解析项或选择候选外值都会失败。catalog SHA-256 为 `c51c1f91278156b4896cc6346131d19ddfa2453a30436969db123dcabbbb7042`，resolution set SHA-256 为 `4006b6534a7dcd1decc20c274850b72f8cfd63e75f06a272d3f0a990fa6566b0`。

[M0-DIA-05 函数签名解析](generated/dialect-function-signature-resolution.json) 又以两条 Exact 规则把 `GETCONFIG/GETCONFIGS` 分别求值为 `EraType.Integer/String`，连同 358 条 preserved return 和 360 条已解析 argument 得到 360/360 `CompleteStatic` 函数 signature。catalog SHA-256 为 `c723ed23e5c5bf582a9da9364d2002727e3037b1708566a9e4aef7fbce1f60a6`，resolution set SHA-256 为 `c9ebab5692466dcf046fe2d1b4258c2ddbf8b9116633553fe1f00352884f34ff`。`CompleteStatic` 仍不是行为兼容：参数默认值、错误、restructure、completion/effect 与两侧 fixture 仍 Uncovered。

[M0-DIA-06 有效 flags 解析](generated/dialect-instruction-flag-resolution.json) 把 17 个旧 flag 常量、registration additional flags 和 handler 构造贡献合成为 326/326 逐 key flag 集合：67 direct、136 single、123 by-rule、0 unresolved。35 条规则采用 additive union，既能表达 PRINT 的 K/D/L/W/SINGLE 组合，也能区分 TRYCALL 与 TRYCCALL；TWAIT 的局部 `flag` 和 AWAIT/INPUTMOUSEKEY 注释赋值不进入结果。catalog SHA-256 为 `64375822ddac19f1acf5e7be1594568981e04f8577097c0b5e835751c20a9c39`，resolution set SHA-256 为 `515641ed83c7aaf73717035f112523d42145e1bf1e44e1ba651a42b01dd0021a`。

[M0-DIA-07 归属证据](generated/dialect-ownership-evidence.json) 用固定相对路径、源码 SHA-256 和键数的上游 `FunctionIdentifier.cs`/`Creator.cs` 对照 DIA-01～06 的完整键集。326 个指令中 284 个为 `UpstreamNameMatch`、28 个为显式当前模块候选、14 个仍未决；360 个函数中 243 个为 `UpstreamNameMatch`、117 个仍未决。显式候选细分为 22 个 `game.snake` 与 6 个 `gemuera.v24` 指令；8 个上游同名项同时记录 `CurrentTargetDiffersFromUpstreamCandidate`，必须由 alias/replacement 与行为 fixture 后续裁决。catalog SHA-256 为 `efd15e50...c87c`，当前 evidence set SHA-256 为 `a665945d7910e704de9d13e71c812bc7bc32af6781a3ba6e61ad880ee9a8e749`。

这些数字不能写成“527 项 ownership 已解决”：`UpstreamNameMatch` 只证明公开键 provenance，28 项也只是当前模块候选。DIA-07 仍将全部 686 项的 name comparer/alias/replacement 标为 `Unresolved`，行为与 completion/effect 标为 `Uncovered`，并保持旧 runtime isolation=`Failed`。

[M0-DIA-08 名称查找契约](generated/dialect-name-lookup-contract.json) 对当前旧 lookup 路径补充逐 key 静态证据：326 个指令与 360 个表达式函数存在 9 个同名碰撞，旧 `ContainsKey` 指令优先规则只投影 351 个函数，最终指令 lookup surface 为 677。指令 comparer 在静态初始化时捕获 `Config.ICVariable` 的 `OrdinalIgnoreCase/Ordinal` 选择；函数 registry 为 `Ordinal`，但 `Config.ICFunction` 可在 lookup 前触发 current-culture `ToUpper`，这是未来 D2 必须消除或显式兼容的文化风险。`_Rename.csv` 的 `[[name]]` 是 `EraStreamReader` 在词法分析前执行的 `SourceTextRewrite`，不是 registry alias/replacement。

DIA-08 的 key domain 为 684 个 ASCII uppercase 与 2 个非 ASCII或 mixed-case key；686 项 lookup contract 均已静态定位，但全部 686 项 semantic alias 与 semantic replacement 仍分别为 `Unresolved`。catalog SHA-256 为 `d2980de34652c3932192b16bce95f38475e6f2f5168151ec73c30e6455dde2f6`，当前 contract set SHA-256 为 `169ca8161f8141351a25c665ca08cf176eb91c8a30920d6b9e4878e70eaef65c`；静态报告保持 runtime isolation=`Failed`、`parserVmConsumption=NotConsumed`，该状态只表示报告本身未接线到 legacy Parser/VM 行为。独立 startup/parser 已消费窄 `CompatibilityPlan` descriptor presence/ownership guard，验证 plan identity/profile/hash 与 legacy descriptor presence/module ownership，但不替换 legacy handler 或执行 typed policy，不能据此关闭 D1/D2。

[M0-DIA-09 模块可见性证据](generated/dialect-module-visibility.json) 仅对 DIA-07 的 131 个 ownership `Unresolved` 项做静态投影成员关系分区：123 个 `V24VisibleCandidate` 同时存在于 v24/Snake，8 个 `SnakeOnlyCandidate` 只存在于 Snake（指令 8/6，表达式函数 115/2），0 个未决项缺席 Snake。候选报告保留 v24/Snake projection moduleId/currentContribution 与 DIA-08 lookup contract；这尤其防止把 `CALLSHARP`、TOOLTIP 或两个中文函数的投影差异错误升级为最终 owner 或 replacement。catalog SHA-256 为 `4fffa254...d9574e`，当前 visibility set SHA-256 为 `bc2b54704d2c4252f4d910eca253f9ec5613bc16105266383ad0c901e278bfdf`；静态报告的 `parserVmConsumption=NotConsumed` 只表示报告本身未接线到 legacy Parser/VM 行为。独立 startup/parser 已消费窄 `CompatibilityPlan` descriptor presence/ownership guard，验证 plan identity/profile/hash 与 legacy descriptor presence/module ownership，但不替换 legacy handler 或执行 typed policy；全部 131 项的 ownership、alias/replacement 仍是 `Unresolved`，行为与 completion/effect 仍是 `Uncovered`。

[M0-DIA-10 会话计划静态预检](generated/dialect-plan-preflight.json) 不添加新的 instruction/function descriptor，也不把 DIA-03～09 的结论接入旧解释器；它只固化 `v24pure`→`V24Pure`/v24=290/358、`snake`→`Snake`/snake=326/360 的离线映射。每个 profile 的 semantic hash 只覆盖静态投影语义，完整 preflight set hash 保存来源/会话 inventory/catalog 可复现证据；`SnakeModernMobile` 明确 `Uncovered`。因此该报告不能推导 owner、replacement、行为兼容、runtime plan 或 Parser/VM consumption。

生成器输出 `instruction-inventory.json`，每条至少含：公开名、kind、源码文件/符号、参数 builder 或 method 参数、返回类型、extension/method-safe flags、大小写策略、同步/等待、Core effect 类别和状态。关闭 P0-01/P0-06 时必须满足：

1. 文件哈希或 commit 与报告一致；三个源集合重新抽取成功。
2. 每个 `FunctionCode` 被分类为显式注册、循环注册、parser-only 或未注册；不得只有总数。
3. Creator 的 248 个键逐项解析方法签名；同实现类复用不能合并公共键。
4. 源集合与兼容矩阵做双向差集；任何新增、删除、重命名都让 CI 失败。
5. `Mapped` 仍需最小执行 fixture；只有 baseline/target 状态、输出、错误和时序全部相同时才能升级 `Compatible`。

## 本快照的限制

- 正则库存没有求值条件编译、构造器分支和运行时覆盖；它只证明文本基线集合。
- Creator 表的领域归类是设计视图，不是原版模块边界。
- 已生成 D0 JSON、326/326 指令参数/flags、360/360 函数参数/返回静态解析、686 项归属证据与当前 lookup contract；但最终 ownership、未来 D2 comparer/normalizer、686 项 semantic alias/replacement，以及 defaults/errors/restructure/effect/completion 行为的 upstream/target 逐项差分尚未关闭，因此 [CompatibilityMatrix](CompatibilityMatrix.md) 仍不含全集 `Compatible` 声明。
The ParserMediator descriptor-consumption source change and the StackList canary reset invalidated previous downstream identities. Regenerated reports are authoritative for the current tree (`DIA-01=a30d210f...ccdc2`, 97 branch hits; `DIA-03=d347bdd7...2fa9`; `DIA-04=2f9995f8...80e2`; `DIA-05=18f2e9f4...223e`; `DIA-06=16f8ecab...2717`; `DIA-07=a665945d...e749`; `DIA-08=169ca816...f65c`; `DIA-09=bc2b5470...bfdf`; `DIA-10=d5cc089f...256b`; `DIA-11=0960994b...bd71d`; `DIA-12=e9ffd094...8c46`; `DIA-13=766c3f84...98c4`; `DIA-14=beedf972...c6b`; `DIA-15=2213bf38...e012`; `DIA-16=2b7361ad...ea75`; `DIA-17=1fed13e5...73c4`) and all downstream DIA reports were regenerated before their contracts were rerun.

The later route-adapter regeneration supersedes the DIA-08/09 shorthand above: generated DIA-08 contract is `e1f84151...98056` and DIA-09 visibility is `755289bb...40ba29`.
