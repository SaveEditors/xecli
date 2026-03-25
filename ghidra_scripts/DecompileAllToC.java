// @category RGH

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileOptions;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.mem.MemoryBlock;
import ghidra.program.model.address.Address;
import java.io.BufferedWriter;
import java.io.File;
import java.io.FileOutputStream;
import java.io.OutputStreamWriter;
import java.nio.charset.StandardCharsets;

public class DecompileAllToC extends GhidraScript {

    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length < 1) {
            println("Usage: DecompileAllToC <output-dir> [max-functions] [timeout-sec]");
            return;
        }

        File outDir = new File(args[0]);
        if (!outDir.exists() && !outDir.mkdirs()) {
            println("Unable to create output directory: " + outDir.getAbsolutePath());
            return;
        }

        int maxFunctions = parseOptionalInt(args, 1, -1);
        int timeoutSec = parseOptionalInt(args, 2, 60);

        DecompInterface decompiler = new DecompInterface();
        DecompileOptions options = new DecompileOptions();
        options.grabFromProgram(currentProgram);
        decompiler.setOptions(options);
        decompiler.toggleCCode(true);
        decompiler.toggleSyntaxTree(false);

        if (!decompiler.openProgram(currentProgram)) {
            println("Unable to initialize decompiler: " + decompiler.getLastMessage());
            return;
        }

        int count = 0;
        int skipped = 0;
        FunctionIterator it = currentProgram.getListing().getFunctions(true);
        while (it.hasNext()) {
            monitor.checkCancelled();
            Function function = it.next();
            if (!isExecutable(function)) {
                skipped++;
                continue;
            }

            Instruction entryInstruction = currentProgram.getListing().getInstructionAt(function.getEntryPoint());
            if (entryInstruction == null) {
                skipped++;
                continue;
            }

            DecompileResults results = decompiler.decompileFunction(function, timeoutSec, monitor);
            if (results == null || results.getDecompiledFunction() == null) {
                skipped++;
                continue;
            }

            String c = results.getDecompiledFunction().getC();
            if (c == null || c.isEmpty()) {
                skipped++;
                continue;
            }
            if (c.contains("halt_baddata") || c.contains("Bad instruction")) {
                skipped++;
                continue;
            }

            String filename = sanitize(function.getName() + "_" + function.getEntryPoint());
            File outFile = new File(outDir, filename + ".c");
            try (BufferedWriter writer = new BufferedWriter(new OutputStreamWriter(
                    new FileOutputStream(outFile), StandardCharsets.UTF_8))) {
                writer.write(c);
            }

            count++;
            if (maxFunctions > 0 && count >= maxFunctions) {
                break;
            }
        }

        decompiler.dispose();
        println("Decompiled " + count + " functions to " + outDir.getAbsolutePath() + " (skipped " + skipped + ").");
    }

    private boolean isExecutable(Function function) {
        Address addr = function.getEntryPoint();
        MemoryBlock block = currentProgram.getMemory().getBlock(addr);
        if (block == null) {
            return false;
        }
        return block.isExecute();
    }

    private static int parseOptionalInt(String[] args, int index, int fallback) {
        if (args.length <= index) {
            return fallback;
        }
        try {
            return Integer.parseInt(args[index]);
        }
        catch (NumberFormatException ex) {
            return fallback;
        }
    }

    private static String sanitize(String value) {
        return value.replaceAll("[^A-Za-z0-9._-]", "_");
    }
}
