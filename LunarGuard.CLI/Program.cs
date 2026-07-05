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
        AnsiConsole.MarkupLine($"[grey]Version 1.0.0 | {DateTime.Now:yyyy-MM-dd}[/]\n");

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
    [Description("Input Lua script file path")]
    [CommandArgument(0, "[input]")]
    public string? Input { get; set; }

    [Description("Output file path (default: input.obfuscated.lua)")]
    [CommandOption("-o|--output")]
    public string? Output { get; set; }

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
            return 1;
        }

        if (!File.Exists(settings.Input))
        {
            AnsiConsole.MarkupLine($"[red]File not found: {settings.Input}[/]");
            return 1;
        }

        var fileInfo = new FileInfo(settings.Input);
        const long maxSize = 10L * 1024 * 1024;
        if (fileInfo.Length > maxSize)
        {
            AnsiConsole.MarkupLine($"[red]Input file too large ({fileInfo.Length:N0} bytes). Maximum allowed: 10 MiB.[/]");
            return 1;
        }

        var source = File.ReadAllText(settings.Input);

        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("purple"))
            .Start("Initializing...", ctx =>
            {
                ctx.Status("Parsing Lua script...");
                Thread.Sleep(100);

                ctx.Status("Applying obfuscation passes...");
                Thread.Sleep(200);

                ctx.Status("Finalizing...");
                Thread.Sleep(100);
            });

        // Build options
        var options = new ObfuscationOptions
        {
            RenameVariables = !settings.NoRename,
            EncryptStrings = !settings.NoStrings,
            EncodeNumbers = !settings.NoNumbers,
            InjectDeadCode = !settings.NoDeadCode,
            ObfuscateControlFlow = !settings.NoControlFlow,
            SplitExpressions = !settings.NoSplit,
            AntiDebug = !settings.NoAntiDebug,
            Virtualize = !settings.NoVm,
            StringKey = settings.StringKey ?? "lx9zq4k7",
            DeadCodeBlocks = settings.DeadCodeCount ?? 5,
        };

        var passesEnabled = new List<(string Name, bool Enabled)>
        {
            ("Variable Renaming", options.RenameVariables),
            ("String Encryption", options.EncryptStrings),
            ("Number Encoding", options.EncodeNumbers),
            ("Dead Code Injection", options.InjectDeadCode),
            ("Control Flow Obfuscation", options.ObfuscateControlFlow),
            ("Expression Splitting", options.SplitExpressions),
            ("Anti-Debug", options.AntiDebug),
            ("Bytecode Virtualization", options.Virtualize),
        };

        if (settings.Verbose)
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("[yellow]Pass[/]")
                .AddColumn("[yellow]Status[/]");

            foreach (var (name, enabled) in passesEnabled)
                table.AddRow(name, enabled ? "[green]Enabled[/]" : "[grey]Disabled[/]");

            AnsiConsole.Write(table);
        }

        var sw = Stopwatch.StartNew();

        try
        {
            var processor = new LunarGuardProcessor();
            var result = processor.Process(source, options);
            sw.Stop();

            var outputPath = settings.Output ?? Path.ChangeExtension(settings.Input, ".obfuscated.lua");
            var outputFull = Path.GetFullPath(outputPath);
            var cwd = Path.GetFullPath(Environment.CurrentDirectory);
            if (!outputFull.StartsWith(cwd, StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine("[red]Output path must be within the current working directory.[/]");
                return 1;
            }
            var outputDir = Path.GetDirectoryName(outputFull);
            if (outputDir != null && !Directory.Exists(outputDir))
            {
                AnsiConsole.MarkupLine($"[red]Output directory does not exist: {outputDir}[/]");
                return 1;
            }
            File.WriteAllText(outputFull, result);

            var resultTable = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("[yellow]Metric[/]")
                .AddColumn("[yellow]Value[/]");

            var originalSize = source.Length;
            var obfuscatedSize = result.Length;
            var ratio = originalSize > 0 ? (double)obfuscatedSize / originalSize : 0;

            resultTable.AddRow("Input file", $"[cyan]{Path.GetFileName(settings.Input)}[/]");
            resultTable.AddRow("Output file", $"[cyan]{Path.GetFileName(outputPath)}[/]");
            resultTable.AddRow("Original size", $"{originalSize:N0} bytes");
            resultTable.AddRow("Obfuscated size", $"{obfuscatedSize:N0} bytes");
            resultTable.AddRow("Blow-up ratio", $"[yellow]{ratio:F2}x[/]");
            resultTable.AddRow("Processing time", $"[green]{sw.ElapsedMilliseconds} ms[/]");

            AnsiConsole.Write(resultTable);

            AnsiConsole.MarkupLine($"\n[bold green]✔ Successfully obfuscated![/] Output: [underline]{outputPath}[/]");

            return 0;
        }
        catch (Exception ex)
        {
            sw.Stop();
            AnsiConsole.WriteException(ex);
            return 1;
        }
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
        info.AddRow("Version", "1.0.0");
        info.AddRow("Description", "Next-generation Lua script obfuscation engine");
        info.AddRow("Target", "Lua 5.1 (GameSense, Neverlose, CS:GO scripts)");
        info.AddRow("Author", "github.com/lunarguard");
        info.AddRow("Tech", ".NET 9 + Spectre.Console");

        AnsiConsole.Write(info);

        AnsiConsole.MarkupLine("\n[bold]Available Commands:[/]");
        AnsiConsole.MarkupLine("  [green]obfuscate[/] [input]    Obfuscate a Lua script");
        AnsiConsole.MarkupLine("  [green]info[/]                Show this information");

        AnsiConsole.MarkupLine("\n[bold]Available Passes:[/]");
        var passes = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[yellow]Pass[/]")
            .AddColumn("[yellow]Flag[/]")
            .AddColumn("[yellow]Description[/]");

        passes.AddRow("Variable Renaming", "--no-rename", "Renames locals to unreadable names");
        passes.AddRow("String Encryption", "--no-strings", "Encrypts string literals with XOR cipher");
        passes.AddRow("Number Encoding", "--no-numbers", "Obfuscates numeric literals");
        passes.AddRow("Dead Code Injection", "--no-deadcode", "Injects unreachable junk code");
        passes.AddRow("Control Flow", "--no-cflow", "Wraps code in opaque predicates");
        passes.AddRow("Expression Splitting", "--no-split", "Splits complex expressions into locals");
        passes.AddRow("Anti-Debug", "--no-antidebug", "Blocks debugger/detection tools");
        passes.AddRow("Bytecode VM", "--no-vm", "Converts code to custom VM bytecode");

        AnsiConsole.Write(passes);

        return 0;
    }
}

public class InfoSettings : CommandSettings { }
