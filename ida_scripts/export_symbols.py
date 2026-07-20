import json
import os
import traceback
from datetime import datetime, timezone

import ida_auto
import idautils
import ida_nalt
import idc


def write_json(path, payload):
    directory = os.path.dirname(path)
    if directory:
        os.makedirs(directory, exist_ok=True)

    temp_path = path + ".tmp"
    with open(temp_path, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2)
    os.replace(temp_path, path)


def normalize_stem(value):
    if not value:
        return ""
    return os.path.splitext(os.path.basename(value))[0].strip()


def resolve_module_name(source_path, explicit_hint):
    for candidate in (source_path, explicit_hint):
        stem = normalize_stem(candidate)
        if stem:
            return stem
    return "unknown"


def main():
    if len(idc.ARGV) < 2:
        raise RuntimeError("usage: export_symbols.py <output-json> [module-name]")

    output_path = idc.ARGV[1]
    explicit_module = idc.ARGV[2] if len(idc.ARGV) > 2 else ""

    ida_auto.auto_wait()

    source_path = ida_nalt.get_input_file_path() or idc.get_idb_path()
    module_name = resolve_module_name(source_path, explicit_module)
    image_base = ida_nalt.get_imagebase()

    symbols = []
    for ea in idautils.Functions():
        name = idc.get_func_name(ea) or idc.get_name(ea) or f"sub_{ea:08X}"
        rva = ea - image_base
        if rva < 0:
            rva = ea
        symbols.append({
            "Name": name,
            "Rva": rva,
            "Type": "function",
        })

    payload = {
        "SchemaVersion": 1,
        "Source": source_path or idc.get_idb_path(),
        "Module": module_name,
        "GeneratedUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "Symbols": symbols,
    }
    write_json(output_path, payload)
    return 0


exit_code = 0
try:
    exit_code = main()
except Exception as exc:
    print(str(exc))
    print(traceback.format_exc())
    exit_code = 1

idc.qexit(exit_code)
