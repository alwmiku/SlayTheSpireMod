"""
StS2 Mod Build & Deploy Script
------------------------------
Usage: python build.py [--no-deploy]
"""

import subprocess
import shutil
import os
import sys

PROJ_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "StS2Mod")
MOD_NAME = "ChargeDrawMod"
GAME_MODS = r"D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods"
TOOLS_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "tools")

def step(msg):
    print(f"\n[STEP] {msg}")

def fail(msg):
    print(f"[FAIL] {msg}")
    sys.exit(1)

# 1. Clean
step("Cleaning previous build...")
for sub in ["bin", "obj"]:
    path = os.path.join(PROJ_DIR, sub)
    if os.path.exists(path):
        shutil.rmtree(path)

# 2. Build
step("Building C# project (net9.0)...")
csproj = os.path.join(PROJ_DIR, f"{MOD_NAME}.csproj")
if not os.path.exists(csproj):
    fail(f"Project file not found: {csproj}")
result = subprocess.run(
    ["dotnet", "build", csproj, "-c", "Debug", "--nologo", "-v", "q"],
    capture_output=True, text=True
)
if result.returncode != 0:
    print(result.stdout)
    print(result.stderr)
    fail("Build failed")

# Find DLL
dll_path = None
for root, dirs, files in os.walk(os.path.join(PROJ_DIR, "bin")):
    for f in files:
        if f == f"{MOD_NAME}.dll":
            dll_path = os.path.join(root, f)
            break
if not dll_path:
    fail(f"{MOD_NAME}.dll not found in build output")
print(f"  -> {dll_path}")

# 3. Pack PCK
step("Packing .pck...")
manifest = os.path.join(PROJ_DIR, "mod_manifest.json")
pck_output = os.path.join(PROJ_DIR, f"{MOD_NAME}.pck")
pck_builder = os.path.join(TOOLS_DIR, "pck_builder.py")
result = subprocess.run(
    ["python", pck_builder, pck_output, manifest],
    capture_output=True, text=True
)
if result.returncode != 0:
    print(result.stdout)
    print(result.stderr)
    fail("PCK packing failed")
print(f"  -> {pck_output} ({os.path.getsize(pck_output)} bytes)")

# 4. Deploy
if "--no-deploy" in sys.argv:
    print("\n[INFO] Skipping deploy (--no-deploy)")
else:
    step("Deploying to game mods directory...")
    if not os.path.exists(GAME_MODS):
        fail(f"Game mods dir not found: {GAME_MODS}\n       Install STS2 and run it at least once.")
    shutil.copy2(dll_path, os.path.join(GAME_MODS, f"{MOD_NAME}.dll"))
    shutil.copy2(pck_output, os.path.join(GAME_MODS, f"{MOD_NAME}.pck"))
    # 独立 JSON 清单是 ModManager 发现模组的入口
    json_manifest = os.path.join(PROJ_DIR, f"{MOD_NAME}.json")
    if os.path.exists(json_manifest):
        shutil.copy2(json_manifest, os.path.join(GAME_MODS, f"{MOD_NAME}.json"))
    print(f"  -> {GAME_MODS}\\{MOD_NAME}.dll")
    print(f"  -> {GAME_MODS}\\{MOD_NAME}.pck")
    print(f"  -> {GAME_MODS}\\{MOD_NAME}.json")

print(f"\n[OK] Build complete.")
