extends Node

# 简单的 SQL 测试脚本
# 用法：在 Godot 编辑器中，将此脚本附加到一个节点并运行场景

func _ready():
	print("=== SQL Test Start ===")
	print("Platform: ", OS.get_name())
	print("Base Directory: ", OS.get_executable_path().get_base_dir())

	# 等待一帧，确保 Emuera 系统初始化
	await get_tree().process_frame

	test_sql_init()

	print("=== SQL Test End ===")

func test_sql_init():
	print("\n--- Testing SQL Initialization ---")

	# 这里我们只能测试 Godot 端的环境
	# 真正的 SQL 测试需要在 Emuera 核心启动后进行

	# 检查 SQLite 库文件是否存在
	var base_dir = OS.get_executable_path().get_base_dir()
	print("Checking for SQLite libraries...")

	if OS.get_name() == "Windows":
		var dll_path = base_dir + "/e_sqlite3.dll"
		if FileAccess.file_exists(dll_path):
			print("✓ Found: ", dll_path)
		else:
			print("✗ Not found: ", dll_path)
			# 检查 runtimes 目录
			var runtime_path = base_dir + "/runtimes/win-x64/native/e_sqlite3.dll"
			if FileAccess.file_exists(runtime_path):
				print("✓ Found in runtimes: ", runtime_path)

	elif OS.get_name() == "Android":
		print("Android platform detected")
		print("Library should be loaded from APK lib directory")

	print("\nNote: Full SQL test requires running the Emuera core")
	print("Check Godot console for SQL initialization logs when game starts")
