"""
STS2 PCK 打包工具
--------------
从文件夹或文件列表生成 Godot 4 .pck（pack version 2），
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


def create_pck(output_path, files_dict, godot_version=(4, 5, 1)):
    """
    创建 Godot 4 .pck 文件。

    Args:
        output_path:  输出 .pck 路径
        files_dict:   {虚拟路径: bytes 内容} 的字典
        godot_version: (major, minor, patch) 版本三元组
    """
    file_entries = []
    data_blocks = []

    for vpath in sorted(files_dict.keys()):
        content = files_dict[vpath]
        if not isinstance(content, bytes):
            content = str(content).encode("utf-8")

        entry_path = vpath.encode("utf-8")
        file_size = len(content)
        file_md5 = hashlib.md5(content).digest()

        file_entries.append((entry_path, file_size, file_md5))
        data_blocks.append(content)

    # 计算偏移量
    index_bytes = bytearray()
    entry_hashes = bytearray()
    header_size = 40  # GDPC(4) + version(4) + godot_ver(16) + reserved(16)
    index_size = (
        sum(4 + len(ep) + 8 + 8 + 16 for ep, _, _ in file_entries) + 16
    )
    data_start = header_size + index_size
    current_offset = 0

    for i, ((entry_path, file_size, file_md5), _) in enumerate(
        zip(file_entries, data_blocks)
    ):
        file_offset = data_start + current_offset
        current_offset += file_size

        index_bytes += struct.pack("<I", len(entry_path))
        index_bytes += entry_path
        index_bytes += struct.pack("<Q", file_offset)
        index_bytes += struct.pack("<Q", file_size)
        index_bytes += file_md5

        entry_hashes += struct.pack("<I", len(entry_path))
        entry_hashes += entry_path
        entry_hashes += struct.pack("<Q", file_offset)
        entry_hashes += struct.pack("<Q", file_size)
        entry_hashes += file_md5

    index_md5 = (
        hashlib.md5(bytes(entry_hashes)).digest()
        if file_entries
        else b"\x00" * 16
    )

    with open(output_path, "wb") as f:
        f.write(b"GDPC")
        f.write(struct.pack("<I", 2))
        f.write(struct.pack("<I", godot_version[0]))
        f.write(struct.pack("<I", godot_version[1]))
        f.write(struct.pack("<I", godot_version[2]))
        f.write(b"\x00" * 16)
        f.write(bytes(index_bytes))
        f.write(index_md5)
        for db in data_blocks:
            f.write(db)


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
