# 变量系统

## 概述

ERA 游戏的变量系统极其庞大，支持多维数组、角色变量、局部变量、引用变量和用户自定义变量。所有变量由 `VariableData` 统一分配存储，通过 `VariableToken` 提供类型安全的读写接口。

## 核心类

| 类 | 职责 |
|----|------|
| `VariableData` | 全局变量存储容器，分配/持有所有变量内存 |
| `VariableToken` | 变量访问抽象，每个变量名对应一个 Token |
| `VariableEvaluator` | 高级变量操作（角色管理、存档序列化） |
| `VariableLocal` | 函数局部变量/参数管理 |
| `CharacterData` | 单个角色的数据容器 |
| `IdentifierDictionary` | 变量名 → Token 的映射字典 |

## VariableToken 继承体系

```
VariableToken (抽象基类)
├── Int1DVariableToken         // FLAG[n], ITEM[n], MONEY[n]
├── Str1DVariableToken         // STR[n], TSTR[n], SAVESTR[n]
├── Int2DVariableToken         // DA[x,y], DB[x,y]
├── Int3DVariableToken         // TA[x,y,z]
├── CharaIntVariableToken      // NO, ISASSI (角色标量)
├── CharaInt1DVariableToken    // ABL[n], TALENT[n], EXP[n]
├── CharaStrVariableToken      // NAME, CALLNAME (角色字符串标量)
├── CharaStr1DVariableToken    // CSTR[n]
├── CharaInt2DVariableToken    // CDFLAG[x,y]
├── Str1DConstantToken         // ABLNAME[n] (只读CSV名)
├── IntConstantToken           // GAMEBASE_AUTHOR 等常量
├── StrConstantToken           // GAMEBASE_TITLE 等
├── LocalVariableToken         // LOCAL[n], LOCALS[n], ARG[n], ARGS[n]
├── UserDefinedVariableToken   // #DIM 用户自定义
├── ReferenceToken             // REF 引用类型
└── 特殊 Token
    ├── RandToken              // RAND:n (读取返回随机数)
    ├── CHARANUM_Token         // CHARANUM (返回角色数量)
    └── LINECOUNT_Token        // LINECOUNT (显示行数)
```

## 变量分类

### 系统全局变量

| 变量 | 类型 | 维度 | 存档 | 说明 |
|------|------|------|------|------|
| RESULT | Int64 | 标量 | 否 | 函数返回值 |
| RESULTS | String | 标量 | 否 | 字符串返回值 |
| TARGET | Int64 | 标量 | 否 | 当前目标角色 |
| MASTER | Int64 | 标量 | 否 | 主人角色编号 |
| FLAG | Int64[] | 10000 | 是 | 通用标志 |
| TFLAG | Int64[] | 10000 | 否 | 临时标志 |
| ITEM | Int64[] | 变长 | 是 | 物品数量 |
| MONEY | Int64[] | 变长 | 是 | 金钱 |
| PBAND | Int64[] | 变长 | 是 | 调教带 |

### 角色变量 (每个角色独立一份)

| 变量 | 类型 | 说明 |
|------|------|------|
| NO | Int64 | CSV 角色编号 |
| NAME | String | 角色名 |
| CALLNAME | String | 昵称 |
| BASE[n] | Int64[] | 基础能力 |
| MAXBASE[n] | Int64[] | 能力上限 |
| ABL[n] | Int64[] | 能力值 |
| TALENT[n] | Int64[] | 素质 |
| EXP[n] | Int64[] | 经验值 |
| PALAM[n] | Int64[] | 参数 |
| JUEL[n] | Int64[] | 珠 |
| CFLAG[n] | Int64[] | 角色标志 |
| RELATION[n] | Int64[] | 关系 |
| EQUIP[n] | Int64[] | 装备 |

### 局部变量

| 变量 | 作用域 | 说明 |
|------|--------|------|
| LOCAL[n] | 函数内 | 局部整型 |
| LOCALS[n] | 函数内 | 局部字符串 |
| ARG[n] | 函数内 | 函数参数(整型) |
| ARGS[n] | 函数内 | 函数参数(字符串) |

### 用户自定义变量 (#DIM/#DIMS)

```erb
#DIM DYNAMIC myCounter, 100      ; 动态一维整型[100]
#DIMS SAVEDATA myStrings, 10     ; 可存档字符串[10]
#DIM CHARADATA myCData, 5        ; 角色变量[5]
#DIM REF refVar                  ; 引用类型
#DIM CONST MY_CONST = 42         ; 编译期常量
```

修饰符：
- `DYNAMIC` - 每次函数调用重初始化
- `STATIC` - 跨调用保持 (默认)
- `SAVEDATA` - 参与存档
- `GLOBAL` - 全局存档 (跨存档共享)
- `CHARADATA` - 角色变量
- `CONST` - 常量
- `REF` - 引用传递

## 变量访问模式

### 索引语法

```erb
FLAG:3           ; 一维: FLAG[3]
ABL:TARGET:0     ; 角色变量: 角色[TARGET].ABL[0]
DA:2:5           ; 二维: DA[2,5]
CDFLAG:0:3:7     ; 角色二维: 角色[0].CDFLAG[3,7]
```

### VariableToken 接口

```csharp
abstract class VariableToken
{
    VariableCode Code;
    int Dimension;         // 0=标量, 1=1D, 2=2D, 3=3D
    bool IsInteger;
    bool IsString;
    bool IsCharacterData;  // 角色变量
    bool IsConst;
    bool IsSavedata;
    bool IsGlobal;

    abstract Int64 GetIntValue(ExpressionMediator exm, Int64[] arguments);
    abstract string GetStrValue(ExpressionMediator exm, Int64[] arguments);
    abstract void SetValue(Int64 value, Int64[] arguments);
    abstract void SetValue(string value, Int64[] arguments);
}
```

### 角色变量寻址

角色编号是动态的 (按 `characterList` 顺序)：
```
ABL:TARGET:0
  ↓
arguments[0] = TARGET 值 (角色索引)
arguments[1] = 0 (ABL 下标)
  ↓
CharacterData chara = VariableData.characterList[TARGET]
return chara.intDataArray[ABL_OFFSET][0]
```

## CharacterData 结构

```csharp
class CharacterData
{
    Int64[][] intDataArray;      // ABL[], TALENT[], EXP[], PALAM[], ...
    string[][] strDataArray;    // CSTR[] 等
    Int64[,][] int2DDataArray;  // CDFLAG[,] 等
    // 用户自定义角色变量
}
```

角色操作：
- `ADDCHARA n` - 从 CSV 编号 n 创建角色，添加到列表末尾
- `DELCHARA n` - 删除索引 n 的角色
- `PICKUPCHARA n...` - 重排角色顺序
- `SWAPCHARA i, j` - 交换两个角色位置

## 稀疏数组优化

对于大型稀疏变量 (多数元素为默认值 0)，使用 `SparseArray<T>`：

```csharp
class SparseArray<T>
{
    Dictionary<int, T> storage;   // 只存非默认值
    T defaultValue;

    T this[int index]
    {
        get => storage.TryGetValue(index, out var v) ? v : defaultValue;
        set { if (Equals(value, defaultValue)) storage.Remove(index); else storage[index] = value; }
    }
}
```

## 存档序列化

### 需存档的变量

1. 所有 `IsSavedata == true` 的系统变量
2. 全部角色数据
3. 用户 `#DIM SAVEDATA` 变量

### GLOBAL 变量

独立于普通存档，所有存档槽共享一份 GLOBAL 数据。

### 序列化格式

- 文本格式 (默认): 行式 key=value
- 二进制格式: `EraBinaryDataWriter` / `EraBinaryDataReader`
- 编码: UTF-8 或 Shift-JIS (配置决定)

## 内存管理注意事项

### 变量总内存估算

典型 ERA 游戏变量内存：
- 全局 FLAG/ITEM/PALAM: ~500KB (Int64[] × 数千)
- 50 个角色 × 每角色 ~100KB: ~5MB
- 用户变量: 变化大，~1-10MB

### 角色变量的生命周期

```
ADDCHARA → new CharacterData → 分配所有角色数组
DELCHARA → 从 characterList 移除 → GC 回收

注意: 所有后续角色的索引会变化!
角色引用应使用 NO (CSV编号) 而非列表索引。
```

### 移动端优化

- 使用 `SparseArray` 减少零值存储
- 角色数据按需分配 (ADDCHARA 时才创建)
- GLOBAL 变量独立文件避免重复序列化
