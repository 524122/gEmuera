# SQL 功能测试指南

## 快速测试步骤

### 方法 1：运行完整游戏（推荐）

1. **启动游戏**
   - 在 Godot 编辑器按 F5 或点击"运行项目"
   - 选择测试游戏目录：`E:\MyCode\eraCode\eratw-sub-modding-develop`

2. **观察 Godot 控制台输出**
   - 查找以下日志：
   ```
   [INFO] SQL: Initializing SQLite runtime...
   [INFO] SQL: Platform: Windows, BaseDirectory: ...
   [INFO] SQL: SQLite runtime initialized successfully
   ```

3. **游戏启动后，触发 SQL 调用**
   - 游戏会在 `QOL_DB_INIT` 函数中调用 SQL_CONNECT
   - 观察是否出现错误

### 方法 2：查看日志文件

1. **运行游戏后，检查日志文件**
   - 位置：项目根目录或游戏目录
   - 文件名：`gemuera_*.log` 或 `emuera.log`

2. **搜索关键词**
   ```
   grep -i "sql" gemuera_*.log
   grep -i "sqlite" gemuera_*.log
   ```

### 预期的成功日志

```
[INFO] SQL: Initializing SQLite runtime...
[INFO] SQL: Platform: Windows, BaseDirectory: E:\MyCode\gEmuera-future\.godot\mono\temp\bin\Debug
[DEBUG] SQL: Resolving SQLite native library: e_sqlite3
[DEBUG] SQL: Trying candidate: E:\MyCode\gEmuera-future\.godot\mono\temp\bin\Debug\e_sqlite3
[DEBUG] SQL: Trying candidate: E:\MyCode\gEmuera-future\.godot\mono\temp\bin\Debug\runtimes\win-x64\native\e_sqlite3.dll
[INFO] SQL: Successfully loaded SQLite native library from: ...
[INFO] SQL: SQLite runtime initialized successfully
[INFO] SQL: Connecting to database: QOL_DISH_DB
[DEBUG] SQL: Connection string: Data Source=plugins/qol_data.db
[INFO] SQL: Database 'QOL_DISH_DB' connected successfully
```

### 如果看到错误

**错误 1：库加载失败**
```
[ERROR] SQL: Failed to load SQLite native library: e_sqlite3
```
→ 可能是构建输出目录缺少 DLL，需要重新构建项目

**错误 2：数据库文件未找到**
```
[ERROR] SQL: Failed to connect database 'QOL_DISH_DB': ...
```
→ 检查游戏目录是否有 `plugins/qol_data.db` 文件

**错误 3：连接字符串错误**
```
SQL_CONNECT: database name is empty
```
→ 这是 ERB 脚本参数问题（之前诊断的问题 1）

---

## Android APK 测试步骤

### 1. 导出 APK

1. Godot 编辑器：项目 → 导出
2. 选择 Android 预设
3. 确认勾选了 "Export with Debug"（首次测试）
4. 导出 APK

### 2. 安装并运行

```bash
# 安装 APK
adb install gemuera.apk

# 运行并查看日志
adb logcat | grep -E "SQL|SQLite|gemuera"
```

### 3. 导出诊断包（如果出错）

在游戏内：
1. 打开诊断面板
2. 导出诊断包
3. 使用 `adb pull` 获取日志文件

---

## 常见问题

### Q: 看到编辑器警告 "Attempting to make child window exclusive"
A: 这是 Godot 编辑器 UI 的警告，不影响游戏功能，可以忽略。

### Q: 没有看到任何 SQL 日志
A: 检查 `config.toml` 是否开启了日志：
```toml
[logging]
enabled = true
```

### Q: SQLite 初始化成功，但连接失败
A: 检查：
1. 游戏目录路径是否正确
2. `plugins/` 目录是否存在
3. 数据库文件权限

---

## 下一步

测试后请告诉我：
1. 是否看到 "SQLite runtime initialized successfully"
2. 是否有任何错误日志
3. 游戏是否正常运行
