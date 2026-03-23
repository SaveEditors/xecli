import argparse
import json
import os
import re
import traceback

import idapro
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
    parser = argparse.ArgumentParser(description="idalib decompile helper")
    parser.add_argument("--db", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--max", type=int, default=0)
    parser.add_argument("--json-out", required=True)
    args = parser.parse_args()

    os.makedirs(args.out, exist_ok=True)
    idapro.open_database(args.db, True)
    try:
        ida_auto.auto_wait()
        if not ida_hexrays.init_hexrays_plugin():
            raise RuntimeError("Hex-Rays decompiler is not available")

        files = []
        errors = []
        attempted = 0
        decompiled = 0
        for ea in idautils.Functions():
            if args.max > 0 and attempted >= args.max:
                break

            attempted += 1
            function_name = idc.get_func_name(ea) or f"sub_{ea:08X}"
            file_name = f"{ea:08X}_{sanitize(function_name)}.c"
            output_path = os.path.join(args.out, file_name)
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
            args.json_out,
            {
                "database": args.db,
                "outputDirectory": args.out,
                "backend": "idalib",
                "decompiled": decompiled,
                "failed": len(errors),
                "files": files,
                "errors": errors,
            },
        )
        return 0 if decompiled > 0 else 1
    finally:
        idapro.close_database()


exit_code = 0
try:
    exit_code = main()
except Exception as exc:
    fallback = os.path.join(os.getcwd(), "idalib-decompile-error.json")
    try:
        json_out = None
        if "--json-out" in os.sys.argv:
            json_out = os.sys.argv[os.sys.argv.index("--json-out") + 1]
    except Exception:
        json_out = None
    write_json(
        json_out or fallback,
        {
            "backend": "idalib",
            "error": str(exc),
            "traceback": traceback.format_exc(),
        },
    )
    exit_code = 1

raise SystemExit(exit_code)
