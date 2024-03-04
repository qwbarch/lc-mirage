# -*- mode: python ; coding: utf-8 -*-

import sys ; sys.setrecursionlimit(sys.getrecursionlimit() * 5)
from PyInstaller.utils.hooks import collect_data_files
from PyInstaller.utils.hooks import copy_metadata
from PyInstaller.utils.hooks import collect_submodules

datas = []
datas += collect_data_files("torch")
datas += copy_metadata("nltk")
datas += copy_metadata("transformers")
datas += copy_metadata("torch")
datas += copy_metadata("tqdm")
datas += copy_metadata("regex")
datas += copy_metadata("requests")
datas += copy_metadata("packaging")
datas += copy_metadata("filelock")
datas += copy_metadata("numpy")
datas += copy_metadata("tokenizers")
datas += copy_metadata("huggingface-hub")
datas += copy_metadata("pyyaml")
datas += copy_metadata("safetensors")

hiddenimports = collect_submodules("sklearn") 
hiddenimports.append("nltk")
hiddenimports.append("torch")
hiddenimports.append("torch.utils")
hiddenimports.append("tqdm")
hiddenimports.append("regex")
hiddenimports.append("sacremoses")
hiddenimports.append("requests")
hiddenimports.append("packaging")
hiddenimports.append("filelock")
hiddenimports.append("numpy")
hiddenimports.append("tokenizers")
hiddenimports.append("huggingface-hub")
hiddenimports.append("sklearn.utils._cython_blas")
hiddenimports.append("sklearn.neighbors.typedefs")
hiddenimports.append("sklearn.neighbors.quad_tree")
hiddenimports.append("sklearn.tree")
hiddenimports.append("sklearn.trees._utils")
hiddenimports.append("sklearn.metrics._pairwise_distances_reduction._dataset_pair")

a = Analysis(
    ["main.py"],
    pathex=[],
    binaries=[],
    datas=datas,
    hiddenimports=hiddenimports,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name="main",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    console=True,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)
coll = COLLECT(
    exe,
    a.binaries,
    a.datas,
    strip=False,
    upx=True,
    upx_exclude=[],
    name="main",
)
