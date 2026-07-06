namespace LunarGuard.Core.Obfuscation;

public class ObfuscationOptions
{
    public bool RenameVariables { get; set; } = true;
    public bool EncryptStrings { get; set; } = true;
    public bool EncodeNumbers { get; set; } = true;
    public bool InjectDeadCode { get; set; } = true;
    public bool ObfuscateControlFlow { get; set; } = true;
    public bool SplitExpressions { get; set; } = true;
    public bool AntiDebug { get; set; } = true;
    public bool Virtualize { get; set; } = true;

    public string StringKey { get; set; } = "lx9zq4k7";
    public int DeadCodeBlocks { get; set; } = 5;
    public string RenamePrefix { get; set; } = "";
}
