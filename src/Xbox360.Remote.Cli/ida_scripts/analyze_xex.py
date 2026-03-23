import json
import os
import traceback

import ida_auto
import ida_funcs
import ida_nalt
import ida_segment
import idc


def write_json(path, payload):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2)


def main():
    if len(idc.ARGV) < 2:
        raise RuntimeError("usage: analyze_xex.py <result-json>")

    result_path = idc.ARGV[1]
    ida_auto.auto_wait()

    try:
        idc.save_database(idc.get_idb_path(), 0)
    except Exception:
        pass

    write_json(
        result_path,
        {
            "database": idc.get_idb_path(),
            "input": ida_nalt.get_input_file_path(),
            "segments": ida_segment.get_segm_qty(),
            "functions": ida_funcs.get_func_qty(),
        },
    )
    return 0


exit_code = 0
try:
    exit_code = main()
except Exception as exc:
    fallback = idc.ARGV[1] if len(idc.ARGV) > 1 else os.path.join(os.getcwd(), "ida-analyze-error.json")
    write_json(
        fallback,
        {
            "error": str(exc),
            "traceback": traceback.format_exc(),
        },
    )
    exit_code = 1

idc.qexit(exit_code)
