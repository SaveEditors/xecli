import json
import os
import re
import traceback

import ida_auto
import ida_hexrays
import idautils
import idc


def sanitize(name):
    sanitized = re.sub(r"[^0-9A-Za-z._-]+", "_", name)
    return sanitized or "sub"


def write_json(path, payload):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2)


def main():
    if len(idc.ARGV) < 4:
        raise RuntimeError("usage: decompile_xex.py <out-dir> <max-functions> <result-json>")

    out_dir = idc.ARGV[1]
    max_functions = int(idc.ARGV[2])
    result_path = idc.ARGV[3]

    os.makedirs(out_dir, exist_ok=True)
    ida_auto.auto_wait()
    if not ida_hexrays.init_hexrays_plugin():
        raise RuntimeError("Hex-Rays decompiler is not available")

    files = []
    errors = []
    attempted = 0
    decompiled = 0
    for ea in idautils.Functions():
        if max_functions > 0 and attempted >= max_functions:
            break

        attempted += 1
        function_name = idc.get_func_name(ea) or f"sub_{ea:08X}"
        file_name = f"{ea:08X}_{sanitize(function_name)}.c"
        output_path = os.path.join(out_dir, file_name)
        try:
            cfunc = ida_hexrays.decompile(ea)
            with open(output_path, "w", encoding="utf-8") as handle:
                handle.write(str(cfunc))
                handle.write("\n")
            files.append(output_path)
            decompiled += 1
        except Exception as exc:
            errors.append(f"0x{ea:08X} {function_name}: {exc}")

    write_json(
        result_path,
        {
            "database": idc.get_idb_path(),
            "outputDirectory": out_dir,
            "backend": "batch",
            "decompiled": decompiled,
            "failed": len(errors),
            "files": files,
            "errors": errors,
        },
    )
    return 0 if decompiled > 0 else 1


exit_code = 0
try:
    exit_code = main()
except Exception as exc:
    fallback = idc.ARGV[3] if len(idc.ARGV) > 3 else os.path.join(os.getcwd(), "ida-decompile-error.json")
    write_json(
        fallback,
        {
            "backend": "batch",
            "error": str(exc),
            "traceback": traceback.format_exc(),
        },
    )
    exit_code = 1

idc.qexit(exit_code)
