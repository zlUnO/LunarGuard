namespace LunarGuard.Core.Obfuscation;

public enum Preset
{
    Debug,
    Fast,
    Balanced,
    Max
}

public class ObfuscationOptions
{
    public Preset Preset { get; set; } = Preset.Balanced;

    public bool RenameVariables { get; set; } = true;
    public bool EncryptStrings { get; set; } = true;
    public bool EncodeNumbers { get; set; } = true;
    public bool InjectDeadCode { get; set; } = true;
    public bool ObfuscateControlFlow { get; set; } = true;
    public bool SplitExpressions { get; set; } = true;
    public bool AntiDebug { get; set; } = true;
    public bool Virtualize { get; set; } = true;

    public bool OptimizeAst { get; set; } = true;
    public bool SplitStrings { get; set; } = true;
    public bool OpaquePredicates { get; set; } = true;
    public bool AntiTamper { get; set; } = true;

    public string StringKey { get; set; } = "lx9zq4k7";
    public int DeadCodeBlocks { get; set; } = 5;
    public string RenamePrefix { get; set; } = "";
    public int StringSplitMinLen { get; set; } = 6;

    public static ObfuscationOptions FromPreset(Preset preset) => preset switch
    {
        Preset.Debug => new ObfuscationOptions
        {
            Preset = Preset.Debug,
            RenameVariables = false,
            EncryptStrings = false,
            EncodeNumbers = false,
            InjectDeadCode = false,
            ObfuscateControlFlow = false,
            SplitExpressions = false,
            AntiDebug = false,
            Virtualize = false,
            OptimizeAst = false,
            SplitStrings = false,
            OpaquePredicates = false,
            AntiTamper = false,
        },
        Preset.Fast => new ObfuscationOptions
        {
            Preset = Preset.Fast,
            RenameVariables = true,
            EncryptStrings = true,
            EncodeNumbers = true,
            InjectDeadCode = true,
            ObfuscateControlFlow = false,
            SplitExpressions = true,
            AntiDebug = false,
            Virtualize = false,
            OptimizeAst = true,
            SplitStrings = false,
            OpaquePredicates = false,
            AntiTamper = false,
            DeadCodeBlocks = 2,
        },
        Preset.Max => new ObfuscationOptions
        {
            Preset = Preset.Max,
            RenameVariables = true,
            EncryptStrings = true,
            EncodeNumbers = true,
            InjectDeadCode = true,
            ObfuscateControlFlow = true,
            SplitExpressions = true,
            AntiDebug = true,
            Virtualize = true,
            OptimizeAst = true,
            SplitStrings = true,
            OpaquePredicates = true,
            AntiTamper = true,
            DeadCodeBlocks = 20,
        },
        _ => new ObfuscationOptions(),
    };
}
