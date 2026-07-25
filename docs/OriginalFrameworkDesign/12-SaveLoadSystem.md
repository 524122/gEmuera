# 存档与数据持久化

## 存档架构

```
用户触发存档 (ERB: SAVEDATA)
  ↓
Process → VariableEvaluator.SaveData()
  ├── 序列化系统变量 (FLAG, ITEM, MONEY, ...)
  ├── 序列化角色数据 (全部 CharacterData)
  ├── 序列化用户自定义 SAVEDATA 变量
  └── 写入 dat/ 目录
  ↓
存档文件 (dat/save{N}.sav)
```

## 存档数据范围

### 需要存档的变量

| 变量类别 | 标志 | 示例 |
|---------|------|------|
| 系统持久变量 | `IsSavedata = true` | FLAG, ITEM, MONEY |
| 角色数据 | 全量序列化 | ABL, TALENT, CFLAG |
| 用户自定义 SAVEDATA | `#DIM SAVEDATA` | 脚本自定义变量 |
| 全局变量 | `IsGlobal = true` | GLOBAL (独立存档) |

### 不存档的变量

| 变量类别 | 说明 |
|---------|------|
| TFLAG, TSTR | 临时变量，每次重启清零 |
| LOCAL, LOCALS | 函数局部变量 |
| ARG, ARGS | 函数参数 |
| RESULT, RESULTS | 临时返回值 |
| RAND | 伪变量 (每次读取返回随机值) |

## 存档格式

### 文本格式 (默认)

```
// dat/save0.sav (示意)
version:1930
DAY:3
MONEY:0:50000
FLAG:0:1
FLAG:3:100
...
CHARANUM:5
CHAR:0:NAME:アリス
CHAR:0:ABL:0:50
CHAR:0:ABL:1:30
...
```

### 二进制格式

- `EraBinaryDataWriter` / `EraBinaryDataReader` 处理
- 更小的文件体积
- 更快的读写速度
- 可选压缩

## GLOBAL 存档

全局变量独立于普通存档，跨存档槽共享：

```
dat/global.sav
├── GLOBAL 整型数组
├── GLOBALS 字符串数组
└── 用户定义 #DIM GLOBAL 变量
```

## Godot 侧的注意事项

### 文件路径

```csharp
// 存档目录在游戏根目录/dat下
string savePath = Path.Combine(Program.ExeDir, "dat", $"save{slot}.sav");

// 移动端使用外部存储路径
// 通过 Program.ExeDir 统一处理
```

### 线程安全

存档操作发生在后台线程 (EmueraThread)：
- 写入文件不阻塞主线程
- 但大存档可能短暂占用 CPU
- 读档时会清空并重建所有变量数据

### 存档槽管理

```csharp
Config.SaveDataNos  // 存档槽数量 (默认20)
```

## 存档流程时序

```
ERB: SAVEDATA 0, "存档说明"
  ↓
Instruction.DoInstruction()
  ↓
VariableEvaluator.SaveTo(slot, description)
  ├── 写入存档描述
  ├── 写入游戏版本
  ├── 写入当前时间
  ├── 序列化 VariableData 中所有 IsSavedata 变量
  ├── 序列化所有 CharacterData
  └── flush 并关闭文件

ERB: LOADDATA 0
  ↓
VariableEvaluator.LoadFrom(slot)
  ├── 读取并验证版本
  ├── 重置所有变量为默认值
  ├── 逐项反序列化
  ├── 重建 characterList
  └── 恢复执行状态
```

## 向后兼容

```csharp
// 读取时使用安全访问，新字段使用默认值
value = dict.GetValueOrDefault("key", defaultValue);

// 存档版本号检查
if (saveVersion < currentVersion)
{
    // 迁移逻辑
}
```
