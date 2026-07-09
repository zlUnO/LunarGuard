using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using Spectre.Console;
using Spectre.Console.Cli;
using LunarGuard.Core;
using LunarGuard.Core.Obfuscation;

namespace LunarGuard.CLI;

public static class Program
{
    public static int Main(string[] args)
    {
        AnsiConsole.Write(
            new FigletText("LunarGuard")
                .LeftJustified()
                .Color(Color.Purple));

        AnsiConsole.MarkupLine("[grey]Lua Script Protector — Next Generation Obfuscation[/]");
        AnsiConsole.MarkupLine($"[grey]Version 2.0.0 | {DateTime.Now:yyyy-MM-dd}[/]\n");

        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("LunarGuard");
            config.AddCommand<ObfuscateCommand>("obfuscate")
                .WithAlias("ob")
                .WithAlias("protect")
                .WithDescription("Obfuscate a Lua script");
            config.AddCommand<InfoCommand>("info")
                .WithAlias("about")
                .WithDescription("Show information about LunarGuard");
        });

        return app.Run(args);
    }
}

public class ObfuscateSettings : CommandSettings
{
    [Description("Input Lua script file path or directory (with --dir)")]
    [CommandArgument(0, "[input]")]
    public string? Input { get; set; }

    [Description("Output file path (default: input.obfuscated.lua)")]
    [CommandOption("-o|--output")]
    public string? Output { get; set; }

    [Description("Obfuscation preset: debug, fast, balanced, max (default: balanced)")]
    [CommandOption("--preset")]
    public string? Preset { get; set; }

    [Description("Process all .lua files in a directory")]
    [CommandOption("--dir")]
    public bool BatchMode { get; set; }

    [Description("Disable variable renaming")]
    [CommandOption("--no-rename")]
    public bool NoRename { get; set; }

    [Description("Disable string encryption")]
    [CommandOption("--no-strings")]
    public bool NoStrings { get; set; }

    [Description("Disable number encoding")]
    [CommandOption("--no-numbers")]
    public bool NoNumbers { get; set; }

    [Description("Disable dead code injection")]
    [CommandOption("--no-deadcode")]
    public bool NoDeadCode { get; set; }

    [Description("Disable control flow obfuscation")]
    [CommandOption("--no-cflow")]
    public bool NoControlFlow { get; set; }

    [Description("Disable expression splitting")]
    [CommandOption("--no-split")]
    public bool NoSplit { get; set; }

    [Description("Disable anti-debug")]
    [CommandOption("--no-antidebug")]
    public bool NoAntiDebug { get; set; }

    [Description("Disable bytecode virtualization")]
    [CommandOption("--no-vm")]
    public bool NoVm { get; set; }

    [Description("Disable AST optimization")]
    [CommandOption("--no-optimize")]
    public bool NoOptimize { get; set; }

    [Description("Disable string splitting")]
    [CommandOption("--no-splitstrings")]
    public bool NoSplitStrings { get; set; }

    [Description("Disable opaque predicates")]
    [CommandOption("--no-opaque")]
    public bool NoOpaque { get; set; }

    [Description("Disable anti-tamper")]
    [CommandOption("--no-antitamper")]
    public bool NoAntiTamper { get; set; }

    [Description("String encryption key")]
    [CommandOption("--key")]
    public string? StringKey { get; set; }

    [Description("Number of dead code blocks")]
    [CommandOption("--deadcount")]
    public int? DeadCodeCount { get; set; }

    [Description("Show detailed output")]
    [CommandOption("-v|--verbose")]
    public bool Verbose { get; set; }
}

public class ObfuscateCommand : Command<ObfuscateSettings>
{
    protected override int Execute(CommandContext context, ObfuscateSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(settings.Input))
        {
            AnsiConsole.MarkupLine("[red]No input file specified![/]");
            AnsiConsole.MarkupLine("Usage: [yellow]LunarGuard obfuscate <script.lua> [options][/]");
            AnsiConsole.MarkupLine("       [yellow]LunarGuard obfuscate <dir> --dir[/]");
            return 1;
        }

        if (settings.BatchMode)
        {
            return ProcessDirectory(settings);
        }

        if (!File.Exists(settings.Input))
        {
            AnsiConsole.MarkupLine($"[red]File not found: {settings.Input}[/]");
            return 1;
        }

        return ProcessSingleFile(settings.Input, settings);
    }

    private int ProcessDirectory(ObfuscateSettings settings)
    {
        if (!Directory.Exists(settings.Input))
        {
            AnsiConsole.MarkupLine($"[red]Directory not found: {settings.Input}[/]");
            return 1;
        }

        var files = Directory.GetFiles(settings.Input, "*.lua");
        if (files.Length == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No .lua files found in directory.[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"[green]Found {files.Length} .lua files[/]\n");

        var success = 0;
        var failed = 0;

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            AnsiConsole.Markup($"  [cyan]{fileName}[/] ... ");

            try
            {
                ProcessSingleFile(file, settings, silent: true);
                AnsiConsole.MarkupLine("[green]OK[/]");
                success++;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]FAIL: {ex.Message}[/]");
                failed++;
            }
        }

        AnsiConsole.MarkupLine($"\n[bold]Done: {success} succeeded, {failed} failed[/]");
        return failed > 0 ? 1 : 0;
    }

    private int ProcessSingleFile(string inputPath, ObfuscateSettings settings, bool silent = false)
    {
        var fileInfo = new FileInfo(inputPath);
        const long maxSize = 10L * 1024 * 1024;
        if (fileInfo.Length > maxSize)
        {
            if (!silent) AnsiConsole.MarkupLine($"[red]Input file too large ({fileInfo.Length:N0} bytes).[/]");
            return 1;
        }

        var source = File.ReadAllText(inputPath);

        if (!silent)
        {
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("purple"))
                .Start("Processing...", ctx =>
                {
                    ctx.Status("Parsing Lua script...");
                    Thread.Sleep(50);
                    ctx.Status("Applying obfuscation passes...");
                    Thread.Sleep(100);
                    ctx.Status("Finalizing...");
                    Thread.Sleep(50);
                });
        }

        var options = BuildOptions(settings);

        if (settings.Verbose && !silent)
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("[yellow]Pass[/]")
                .AddColumn("[yellow]Status[/]");

            var passes = new (string, bool)[]
            {
                ("AST Optimization", options.OptimizeAst),
                ("Variable Renaming", options.RenameVariables),
                ("String Splitting", options.SplitStrings),
                ("Opaque Predicates", options.OpaquePredicates),
                ("String Encryption", options.EncryptStrings),
                ("Number Encoding", options.EncodeNumbers),
                ("Dead Code Injection", options.InjectDeadCode),
                ("Control Flow Obfuscation", options.ObfuscateControlFlow),
                ("Expression Splitting", options.SplitExpressions),
                ("Anti-Debug", options.AntiDebug),
                ("Anti-Tamper", options.AntiTamper),
                ("Bytecode Virtualization", options.Virtualize),
            };

            foreach (var (name, enabled) in passes)
                table.AddRow(name, enabled ? "[green]Enabled[/]" : "[grey]Disabled[/]");

            AnsiConsole.Write(table);
        }

        var sw = Stopwatch.StartNew();

        try
        {
            var processor = new LunarGuardProcessor();
            var result = processor.Process(source, options);
            sw.Stop();

            var outputPath = settings.Output ?? Path.ChangeExtension(inputPath, ".obfuscated.lua");
            File.WriteAllText(outputPath, result);

            if (!silent)
            {
                var resultTable = new Table()
                    .Border(TableBorder.Rounded)
                    .AddColumn("[yellow]Metric[/]")
                    .AddColumn("[yellow]Value[/]");

                var originalSize = source.Length;
                var obfuscatedSize = result.Length;
                var ratio = originalSize > 0 ? (double)obfuscatedSize / originalSize : 0;

                resultTable.AddRow("Input file", $"[cyan]{Path.GetFileName(inputPath)}[/]");
                resultTable.AddRow("Output file", $"[cyan]{Path.GetFileName(outputPath)}[/]");
                resultTable.AddRow("Original size", $"{originalSize:N0} bytes");
                resultTable.AddRow("Obfuscated size", $"{obfuscatedSize:N0} bytes");
                resultTable.AddRow("Blow-up ratio", $"[yellow]{ratio:F2}x[/]");
                resultTable.AddRow("Processing time", $"[green]{sw.ElapsedMilliseconds} ms[/]");

                AnsiConsole.Write(resultTable);
                AnsiConsole.MarkupLine($"\n[bold green]✔ Successfully obfuscated![/] Output: [underline]{outputPath}[/]");
            }

            return 0;
        }
        catch (Exception ex)
        {
            sw.Stop();
            if (!silent) AnsiConsole.WriteException(ex);
            return 1;
        }
    }

    private static ObfuscationOptions BuildOptions(ObfuscateSettings settings)
    {
        ObfuscationOptions options;

        if (!string.IsNullOrEmpty(settings.Preset))
        {
            options = settings.Preset.ToLower() switch
            {
                "debug" => ObfuscationOptions.FromPreset(Preset.Debug),
                "fast" => ObfuscationOptions.FromPreset(Preset.Fast),
                "max" => ObfuscationOptions.FromPreset(Preset.Max),
                _ => ObfuscationOptions.FromPreset(Preset.Balanced),
            };
        }
        else
        {
            options = new ObfuscationOptions();
        }

        if (settings.NoRename) options.RenameVariables = false;
        if (settings.NoStrings) options.EncryptStrings = false;
        if (settings.NoNumbers) options.EncodeNumbers = false;
        if (settings.NoDeadCode) options.InjectDeadCode = false;
        if (settings.NoControlFlow) options.ObfuscateControlFlow = false;
        if (settings.NoSplit) options.SplitExpressions = false;
        if (settings.NoAntiDebug) options.AntiDebug = false;
        if (settings.NoVm) options.Virtualize = false;
        if (settings.NoOptimize) options.OptimizeAst = false;
        if (settings.NoSplitStrings) options.SplitStrings = false;
        if (settings.NoOpaque) options.OpaquePredicates = false;
        if (settings.NoAntiTamper) options.AntiTamper = false;

        if (settings.StringKey != null) options.StringKey = settings.StringKey;
        if (settings.DeadCodeCount.HasValue) options.DeadCodeBlocks = settings.DeadCodeCount.Value;

        return options;
    }
}

public class InfoCommand : Command<InfoSettings>
{
    protected override int Execute(CommandContext context, InfoSettings settings, CancellationToken cancellationToken)
    {
        var info = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[yellow]Property[/]")
            .AddColumn("[yellow]Value[/]");

        info.AddRow("Name", "[bold purple]LunarGuard[/]");
        info.AddRow("Version", "2.0.0");
        info.AddRow("Description", "Next-generation Lua script obfuscation engine");
        info.AddRow("Target", "Lua 5.1 (GameSense, Neverlose, CS:GO scripts)");
        info.AddRow("Author", "github.com/lunarguard");
        info.AddRow("Tech", ".NET 9 + Spectre.Console");

        AnsiConsole.Write(info);

        AnsiConsole.MarkupLine("\n[bold]Available Commands:[/]");
        AnsiConsole.MarkupLine("  [green]obfuscate[/] [input]       Obfuscate a Lua script");
        AnsiConsole.MarkupLine("  [green]obfuscate[/] [dir] --dir   Batch process a directory");
        AnsiConsole.MarkupLine("  [green]info[/]                     Show this information");

        AnsiConsole.MarkupLine("\n[bold]Available Presets:[/]");
        AnsiConsole.MarkupLine("  [green]--preset debug[/]       Minimal protection, fastest");
        AnsiConsole.MarkupLine("  [green]--preset fast[/]        Basic protection, no VM");
        AnsiConsole.MarkupLine("  [green]--preset balanced[/]    All passes, VM disabled (default)");
        AnsiConsole.MarkupLine("  [green]--preset max[/]         Maximum protection, full VM");

        AnsiConsole.MarkupLine("\n[bold]Available Passes (12 total):[/]");
        var passes = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[yellow]Pass[/]")
            .AddColumn("[yellow]Flag[/]")
            .AddColumn("[yellow]Description[/]");

        passes.AddRow("AST Optimization", "--no-optimize", "Constant folding + dead code elimination");
        passes.AddRow("Variable Renaming", "--no-rename", "Renames locals to unreadable names");
        passes.AddRow("String Splitting", "--no-splitstrings", "Splits strings into chunks");
        passes.AddRow("Opaque Predicates", "--no-opaque", "Always-true branch conditions");
        passes.AddRow("String Encryption", "--no-strings", "Encrypts string literals with XOR cipher");
        passes.AddRow("Number Encoding", "--no-numbers", "Obfuscates numeric literals");
        passes.AddRow("Dead Code Injection", "--no-deadcode", "Injects unreachable junk code");
        passes.AddRow("Control Flow", "--no-cflow", "Wraps code in opaque predicates");
        passes.AddRow("Expression Splitting", "--no-split", "Splits complex expressions into locals");
        passes.AddRow("Anti-Debug", "--no-antidebug", "Blocks debugger/detection tools");
        passes.AddRow("Anti-Tamper", "--no-antitamper", "Code integrity checks at runtime");
        passes.AddRow("Bytecode VM", "--no-vm", "Converts code to custom VM bytecode");

        AnsiConsole.Write(passes);

        return 0;
    }
}

public class InfoSettings : CommandSettings { }
