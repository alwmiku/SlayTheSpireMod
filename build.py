"""
StS2 模组构建与部署脚本
----------------------
用法：python build.py [--no-deploy]
加上 --no-deploy 时只构建 DLL 和 PCK，不复制到游戏 mods 目录。
"""

import subprocess
import shutil
import os
import sys
import tempfile

PROJ_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "StS2Mod")
MOD_NAME = "ChargeDrawMod"
GAME_MODS = r"D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods"
TOOLS_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "tools")

def step(msg):
    print(f"\n[步骤] {msg}")

def fail(msg):
    print(f"[失败] {msg}")
    sys.exit(1)

# 1. 清理旧构建产物，避免上一次的 obj/bin 影响本次结果。
step("清理旧构建产物...")
for sub in ["bin", "obj"]:
    path = os.path.join(PROJ_DIR, sub)
    if os.path.exists(path):
        shutil.rmtree(path)

# 2. 编译 C# 项目。
step("编译 C# 项目（net9.0）...")
# 历史上项目文件名改过，所以这里同时兼容 ChargeDrawMod.csproj 和 StS2Mod.csproj。
csproj_candidates = [
    os.path.join(PROJ_DIR, f"{MOD_NAME}.csproj"),
    os.path.join(PROJ_DIR, "StS2Mod.csproj"),
]
csproj = next((path for path in csproj_candidates if os.path.exists(path)), None)
if not csproj:
    fail("没有找到 C# 项目文件")
result = subprocess.run(
    ["dotnet", "build", csproj, "-c", "Debug", "--nologo", "-v", "q"],
    # 显式指定 UTF-8，避免中文编译输出在 Windows 控制台里被解码成乱码。
    capture_output=True, text=True, encoding="utf-8", errors="replace"
)
if result.returncode != 0:
    print(result.stdout)
    print(result.stderr)
    fail("C# 编译失败")

# 查找编译出的模组 DLL。
dll_path = None
for root, dirs, files in os.walk(os.path.join(PROJ_DIR, "bin")):
    for f in files:
        if f == f"{MOD_NAME}.dll":
            dll_path = os.path.join(root, f)
            break
if not dll_path:
    fail(f"构建输出中没有找到 {MOD_NAME}.dll")
print(f"  -> {dll_path}")

# 3. 打包 PCK。
step("打包 .pck...")
manifest = os.path.join(PROJ_DIR, "mod_manifest.json")
pck_output = os.path.join(PROJ_DIR, f"{MOD_NAME}.pck")
pck_builder = os.path.join(TOOLS_DIR, "pck_builder.py")
with tempfile.TemporaryDirectory() as staging:
    # PCK 里需要有 ChargeDrawMod/ 这一级目录，否则游戏侧按模组名查资源时会找不到。
    mod_root = os.path.join(staging, MOD_NAME)
    os.makedirs(mod_root, exist_ok=True)
    shutil.copy2(manifest, os.path.join(mod_root, "mod_manifest.json"))

    # localization 必须进 PCK，角色名和卡牌文本才会被游戏加载。
    loc_src = os.path.join(PROJ_DIR, "localization")
    if os.path.isdir(loc_src):
        shutil.copytree(loc_src, os.path.join(mod_root, "localization"))

    result = subprocess.run(
        [sys.executable, pck_builder, pck_output, staging],
        capture_output=True, text=True, encoding="utf-8", errors="replace"
    )
    if result.returncode != 0:
        print(result.stdout)
        print(result.stderr)
        fail("PCK 打包失败")
print(f"  -> {pck_output} ({os.path.getsize(pck_output)} 字节)")

# 4. 可选部署。
if "--no-deploy" in sys.argv:
    print("\n[信息] 已跳过部署（--no-deploy）")
else:
    step("部署到游戏 mods 目录...")
    if not os.path.exists(GAME_MODS):
        fail(f"没有找到游戏 mods 目录：{GAME_MODS}\n       请先安装 STS2，并至少启动一次游戏。")
    shutil.copy2(dll_path, os.path.join(GAME_MODS, f"{MOD_NAME}.dll"))
    shutil.copy2(pck_output, os.path.join(GAME_MODS, f"{MOD_NAME}.pck"))
    # 独立 JSON 清单是 ModManager 发现模组的入口
    json_manifest = os.path.join(PROJ_DIR, f"{MOD_NAME}.json")
    if os.path.exists(json_manifest):
        shutil.copy2(json_manifest, os.path.join(GAME_MODS, f"{MOD_NAME}.json"))
    print(f"  -> {GAME_MODS}\\{MOD_NAME}.dll")
    print(f"  -> {GAME_MODS}\\{MOD_NAME}.pck")
    print(f"  -> {GAME_MODS}\\{MOD_NAME}.json")

print(f"\n[完成] 构建完成。")
