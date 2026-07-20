// @category RGH

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import java.io.BufferedWriter;
import java.io.File;
import java.io.FileOutputStream;
import java.io.OutputStreamWriter;
import java.nio.charset.StandardCharsets;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

public class ExportSymbols extends GhidraScript {

    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length < 1) {
            println("Usage: ExportSymbols <output-json> [module-name]");
            return;
        }

        File outFile = new File(args[0]);
        File parent = outFile.getParentFile();
        if (parent != null && !parent.exists() && !parent.mkdirs()) {
            println("Unable to create output directory: " + parent.getAbsolutePath());
            return;
        }

        String moduleHint = args.length > 1 ? args[1] : "";
        String sourcePath = resolveSourcePath();
        String moduleName = resolveModuleName(sourcePath, moduleHint);
        long imageBase = currentProgram.getImageBase().getOffset();

        List<SymbolEntry> symbols = new ArrayList<>();
        FunctionIterator functions = currentProgram.getListing().getFunctions(true);
        while (functions.hasNext()) {
            monitor.checkCancelled();
            Function function = functions.next();
            Address entryPoint = function.getEntryPoint();
            long rva = entryPoint.getOffset() - imageBase;
            if (rva < 0) {
                rva = entryPoint.getOffset();
            }

            String name = function.getName();
            if (name == null || name.trim().isEmpty()) {
                name = String.format(Locale.ROOT, "sub_%08X", entryPoint.getOffset());
            }

            symbols.add(new SymbolEntry(name, rva, "function"));
        }

        writeJson(outFile, sourcePath, moduleName, symbols);
        println("Exported " + symbols.size() + " function symbols to " + outFile.getAbsolutePath() + ".");
    }

    private String resolveSourcePath() {
        String executablePath = currentProgram.getExecutablePath();
        if (executablePath != null && !executablePath.isBlank()) {
            return executablePath;
        }

        String programName = currentProgram.getName();
        if (programName != null && !programName.isBlank()) {
            return programName;
        }

        return "unknown";
    }

    private static String resolveModuleName(String sourcePath, String explicitHint) {
        String moduleName = normalizeStem(explicitHint);
        if (!moduleName.isEmpty()) {
            return moduleName;
        }

        moduleName = normalizeStem(sourcePath);
        if (!moduleName.isEmpty()) {
            return moduleName;
        }

        return "unknown";
    }

    private static String normalizeStem(String value) {
        if (value == null || value.trim().isEmpty()) {
            return "";
        }

        String name = new File(value).getName();
        int dotIndex = name.lastIndexOf('.');
        if (dotIndex > 0) {
            name = name.substring(0, dotIndex);
        }

        return name.trim();
    }

    private static void writeJson(File outFile, String sourcePath, String moduleName, List<SymbolEntry> symbols) throws Exception {
        StringBuilder builder = new StringBuilder();
        builder.append("{\n");
        builder.append("  \"SchemaVersion\": 1,\n");
        builder.append("  \"Source\": ");
        appendJsonString(builder, sourcePath);
        builder.append(",\n");
        builder.append("  \"Module\": ");
        appendJsonString(builder, moduleName);
        builder.append(",\n");
        builder.append("  \"GeneratedUtc\": ");
        appendJsonString(builder, Instant.now().toString());
        builder.append(",\n");
        builder.append("  \"Symbols\": [\n");
        for (int index = 0; index < symbols.size(); index++) {
            SymbolEntry symbol = symbols.get(index);
            builder.append("    {\n");
            builder.append("      \"Name\": ");
            appendJsonString(builder, symbol.name);
            builder.append(",\n");
            builder.append("      \"Rva\": ").append(symbol.rva).append(",\n");
            builder.append("      \"Type\": ");
            appendJsonString(builder, symbol.type);
            builder.append("\n");
            builder.append("    }");
            if (index < symbols.size() - 1) {
                builder.append(",");
            }
            builder.append("\n");
        }
        builder.append("  ]\n");
        builder.append("}\n");

        try (BufferedWriter writer = new BufferedWriter(new OutputStreamWriter(new FileOutputStream(outFile), StandardCharsets.UTF_8))) {
            writer.write(builder.toString());
        }
    }

    private static void appendJsonString(StringBuilder builder, String value) {
        builder.append('"');
        if (value != null) {
            for (int index = 0; index < value.length(); index++) {
                char ch = value.charAt(index);
                switch (ch) {
                    case '\\':
                        builder.append("\\\\");
                        break;
                    case '"':
                        builder.append("\\\"");
                        break;
                    case '\b':
                        builder.append("\\b");
                        break;
                    case '\f':
                        builder.append("\\f");
                        break;
                    case '\n':
                        builder.append("\\n");
                        break;
                    case '\r':
                        builder.append("\\r");
                        break;
                    case '\t':
                        builder.append("\\t");
                        break;
                    default:
                        if (ch < 0x20) {
                            builder.append(String.format(Locale.ROOT, "\\u%04X", (int) ch));
                        }
                        else {
                            builder.append(ch);
                        }
                        break;
                }
            }
        }
        builder.append('"');
    }

    private static final class SymbolEntry {
        private final String name;
        private final long rva;
        private final String type;

        private SymbolEntry(String name, long rva, String type) {
            this.name = name;
            this.rva = rva;
            this.type = type;
        }
    }
}
