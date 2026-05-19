"""
STS2 PCK 打包工具
--------------
从文件夹或文件列表生成 Godot 4.5 .pck（pack version 3），
用于杀戮尖塔 2 模组部署。不依赖 Godot 编辑器。

用法:
  python pck_builder.py <输出.pck> <输入目录>
  python pck_builder.py <输出.pck> <文件1> <文件2> ...

示例:
  python pck_builder.py mod.pck ./StS2Mod/build/
"""

import struct
import hashlib
import os
import sys

PACK_FORMAT_VERSION = 3
PACK_REL_FILEBASE = 1 << 1
DEFAULT_ALIGNMENT = 32


def _padding(alignment, position):
    """返回把 position 补齐到 alignment 所需的 0 字节数量。"""
    rest = position % alignment
    return 0 if rest == 0 else alignment - rest


def create_pck(output_path, files_dict, godot_version=(4, 5, 1)):
    """
    创建 Godot 4.5 .pck 文件。

    Args:
        output_path:  输出 .pck 路径
        files_dict:   {虚拟路径: bytes 内容} 的字典
        godot_version: (major, minor, patch) 版本三元组
    """
    file_entries = []

    for vpath in sorted(files_dict.keys()):
        content = files_dict[vpath]
        if not isinstance(content, bytes):
            content = str(content).encode("utf-8")

        # Godot 的 PCK 目录里不带 res:// 前缀，反斜杠也必须统一成斜杠。
        clean_path = vpath.replace("\\", "/")
        if clean_path.startswith("res://"):
            clean_path = clean_path[len("res://"):]

        file_entries.append({
            "path": clean_path,
            "content": content,
            "size": len(content),
            "md5": hashlib.md5(content).digest(),
            "offset": 0,
        })

    with open(output_path, "wb") as f:
        # Godot 4.5 当前使用 PCK V3。V3 头部和旧 V2 最大的区别是：
        # 1. 头部中有 pack_flags、file_base、dir_offset。
        # 2. 目录放在文件数据之后，不再紧跟在头部后面。
        # 3. 每个目录项末尾多一个 flags 字段。
        # 少写这些字段时，Godot 会把二进制 MD5/内容错当成 UTF-8 路径解析，
        # 启动日志里就会出现 Unicode parsing error，严重时直接卡在加载 PCK。
        f.write(b"GDPC")
        f.write(struct.pack("<I", PACK_FORMAT_VERSION))
        f.write(struct.pack("<I", godot_version[0]))
        f.write(struct.pack("<I", godot_version[1]))
        f.write(struct.pack("<I", godot_version[2]))
        f.write(struct.pack("<I", PACK_REL_FILEBASE))

        file_base_offset = f.tell()
        f.write(struct.pack("<Q", 0))
        dir_offset_offset = f.tell()
        f.write(struct.pack("<Q", 0))
        f.write(b"\x00" * 64)

        f.write(b"\x00" * _padding(DEFAULT_ALIGNMENT, f.tell()))
        file_base = f.tell()

        f.seek(file_base_offset)
        f.write(struct.pack("<Q", file_base))
        f.seek(file_base)

        for entry in file_entries:
            entry["offset"] = f.tell()
            f.write(entry["content"])
            f.write(b"\x00" * _padding(DEFAULT_ALIGNMENT, f.tell()))

        f.write(b"\x00" * _padding(DEFAULT_ALIGNMENT, f.tell()))
        dir_offset = f.tell()

        f.seek(dir_offset_offset)
        f.write(struct.pack("<Q", dir_offset))
        f.seek(dir_offset)

        f.write(struct.pack("<I", len(file_entries)))
        for entry in file_entries:
            path_bytes = entry["path"].encode("utf-8")
            path_pad = _padding(4, len(path_bytes))
            f.write(struct.pack("<I", len(path_bytes) + path_pad))
            f.write(path_bytes)
            f.write(b"\x00" * path_pad)
            f.write(struct.pack("<Q", entry["offset"] - file_base))
            f.write(struct.pack("<Q", entry["size"]))
            f.write(entry["md5"])
            f.write(struct.pack("<I", 0))


def _collect_files(root):
    """递归收集目录下所有文件的 {相对路径: bytes}"""
    result = {}
    for dirpath, _, filenames in os.walk(root):
        for fn in filenames:
            full = os.path.join(dirpath, fn)
            rel = os.path.relpath(full, root).replace("\\", "/")
            with open(full, "rb") as fh:
                result[rel] = fh.read()
    return result


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)

    output = sys.argv[1]
    inputs = sys.argv[2:]

    files = {}
    for inp in inputs:
        if os.path.isdir(inp):
            files.update(_collect_files(inp))
        elif os.path.isfile(inp):
            vpath = os.path.basename(inp)
            with open(inp, "rb") as fh:
                files[vpath] = fh.read()
        else:
            print(f"警告: 路径不存在，跳过: {inp}", file=sys.stderr)

    create_pck(output, files)
    print(f"[OK] {output}  ({os.path.getsize(output)} bytes, {len(files)} files)")


if __name__ == "__main__":
    main()
