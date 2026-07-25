# 变量系统

## 概述

XEmuera 的变量系统支持 ERA 游戏引擎定义的丰富变量类型，包括全局变量、角色变量、临时变量、局部变量，以及用户自定义变量。所有变量由 `VariableData` 统一管理，通过 `VariableToken` 进行访问。

## 核心类

### VariableData
变量数据的容器和工厂类，负责：
- 分配和存储所有变量的内存
- 创建各种 `VariableToken` 实例
- 管理角色数据列表 (`characterList`)
- 存档/读档时的数据序列化

### VariableToken (抽象基类)
变量访问的抽象接口，每个变量名对应一个 Token 实例：
```csharp
internal abstract class VariableToken
{
    VariableCode Code;      // 变量代码
    int Dimension;          // 维度 (0-3)
    bool IsInteger;         // 是否整型
    bool IsString;          // 是否字符串型
    bool IsCharacterData;   // 是否角色变量
    bool IsConst;           // 是否常量
    bool IsSavedata;        // 是否需要存档
    bool IsGlobal;          // 是否全局存档
    
    abstract Int64 GetIntValue(ExpressionMediator exm, Int64[] arguments);
    abstract string GetStrValue(ExpressionMediator exm, Int64[] arguments);
    abstract void SetValue(Int64 value, Int64[] arguments);
    abstract void SetValue(string value, Int64[] arguments);
}
```

### VariableToken 类型体系
```
VariableToken (抽象)
├── Int1DVariableToken       # 一维整型 (FLAG, TFLAG, ITEM 等)
├── Str1DVariableToken       # 一维字符串 (STR, TSTR, SAVESTR 等)
├── Int2DVariableToken       # 二维整型 (DA, DB, DC 等)
├── Int3DVariableToken       # 三维整型 (TA, TB)
├── CharaIntVariableToken    # 角色整型标量 (NO, ISASSI)
├── CharaInt1DVariableToken  # 角色一维整型 (BASE, ABL, TALENT 等)
├── CharaStrVariableToken    # 角色字符串标量 (NAME, CALLNAME)
├── CharaStr1DVariableToken  # 角色一维字符串 (CSTR)
├── CharaInt2DVariableToken  # 角色二维整型 (CDFLAG)
├── Str1DConstantToken       # 一维字符串常量 (ABLNAME, ITEMNAME 等)
├── IntConstantToken         # 整型常量 (GAMEBASE_*)
├── StrConstantToken         # 字符串常量 (GAMEBASE_TITLE 等)
├── LocalVariableToken       # 局部变量 (LOCAL, LOCALS, ARG, ARGS)
├── UserDefinedVariableToken # 用户自定义变量 (#DIM)
├── ReferenceToken           # 引用类型变量 (REF)
└── 特殊 Token
    ├── RandToken            # RAND 伪变量（读取时返回随机数）
    ├── CHARANUM_Token       # CHARANUM（返回角色数量）
    ├── LINECOUNT_Token      # LINECOUNT（返回显示行数）
    └── ...
```

## 变量分类

### 系统变量
| 变量名 | 类型 | 说明 |
|--------|------|------|
| RESULT | Int64 | 函数返回值/临时结果 |
| RESULTS | String | 字符串返回值 |
| TARGET | Int64 | 当前目标角色编号 |
| MASTER | Int64 | 主人角色编号 |
| ASSI | Int64 | 助手角色编号 |
| MONEY | Int64[] | 金钱 |
| ITEM | Int64[] | 物品数量 |
| FLAG | Int64[] | 通用标志 |
| TFLAG | Int64[] | 临时标志（不存档） |

### 角色变量
每个角色拥有独立的：
| 变量名 | 说明 |
|--------|------|
| NAME | 角色名 |
| CALLNAME | 昵称 |
| BASE/MAXBASE | 基础能力 |
| ABL | 能力值 |
| TALENT | 素质 |
| EXP | 经验 |
| PALAM | 参数 |
| JUEL | 珠 |
| CFLAG | 角色标志 |
| RELATION | 关系值 |
| EQUIP | 装备 |

### 局部变量
- `LOCAL` / `LOCALS` - 函数内局部变量
- `ARG` / `ARGS` - 函数参数
- 通过 `VariableLocal` 管理，按函数作用域隔离

### 用户自定义变量 (#DIM / #DIMS)
```
#DIM DYNAMIC myVar, 100    ; 动态一维整型数组
#DIMS SAVEDATA myStr, 10   ; 需存档的字符串数组
#DIM CHARADATA myChara, 5  ; 角色变量
#DIM REF refVar            ; 引用类型
```

属性修饰：
- `DYNAMIC` - 每次函数调用重新初始化
- `STATIC` - 函数间保持值（默认）
- `SAVEDATA` - 参与存档
- `GLOBAL` - 全局存档
- `CHARADATA` - 角色变量
- `CONST` - 常量
- `REF` - 引用（传址）

## VariableEvaluator

执行层的变量访问中介，提供高级操作：
- 角色管理（添加/删除/排序角色）
- 存档/读档数据序列化
- 角色数据的格式化输出
- UPCHECK 参数变动计算

## IdentifierDictionary

维护变量名 → VariableToken 的映射：
- `varTokenDic` - 全局变量字典
- `localvarTokenDic` - 局部变量字典
- 用户自定义变量注册
- 名称合法性检查

## 变量存档机制

存档时序列化的变量：
1. 所有带 `IsSavedata` 标记的系统变量
2. 角色数据（全部角色的全部角色变量）
3. 用户自定义的 `SAVEDATA` 变量
4. `GLOBAL` 变量独立存档（跨存档共享）

存档格式：
- 文本格式（默认）或二进制格式
- 可选 UTF-8 或 Shift-JIS 编码
- 二进制格式支持压缩

## CharacterData

单个角色的数据容器：
```csharp
class CharacterData
{
    Int64[][] intDataArray;     // 一维整型数组集合
    string[][] strDataArray;   // 一维字符串数组集合
    Int64[,][] int2DDataArray; // 二维整型数组集合
    // 用户自定义角色变量...
}
```

角色操作：
- `AddChara` - 从 CSV 编号创建角色
- `DelChara` - 删除角色
- `PickUpChara` - 重排角色顺序
- 角色编号是动态的（按 characterList 索引）
