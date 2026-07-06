# LunarGuard

The project was made entirely with the help of AI
```
  _                                       ____                              _
 | |      _   _   _ __     __ _   _ __   / ___|  _   _    __ _   _ __    __| |
 | |     | | | | | '_ \   / _` | | '__| | |  _  | | | |  / _` | | '__|  / _` |
 | |___  | |_| | | | | | | (_| | | |    | |_| | | |_| | | (_| | | |    | (_| |
 |_____|  \__,_| |_| |_|  \__,_| |_|     \____|  \__,_|  \__,_| |_|     \__,_|
```

## Changelog

Полный список изменений — в [CHANGELOG.md](CHANGELOG.md).

## Features

| Pass | Description | Status |
|------|-------------|--------|
| Variable Renaming | Renames locals to unreadable hex identifiers | ✅ |
| String Encryption | Encrypts string literals with dynamic runtime decryption via `load()` | ✅ |
| Number Encoding | Obfuscates numeric literals using arithmetic expressions (add/sub, mul/div, nested) | ✅ |
| Dead Code Injection | Injects unreachable junk statements (8 templates, 5 junk expression forms) | ✅ |
| Control Flow Obfuscation | Wraps statements in opaque predicates and nested `do` blocks | ✅ |
| Expression Splitting | Splits complex expressions into temp local variables | ✅ |
| Anti-Debug | Two-layer debug detection (library presence + `debug.getinfo` detour check) | ✅ |
| Bytecode Virtualization | Converts code to custom VM bytecode with runtime interpreter | ✅ |

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
- Windows, Linux, or macOS

## Build

```bash
git clone https://github.com/zlUnO/LunarGuard.git
cd LunarGuard
dotnet build
```

## Usage

### CLI

```bash
dotnet run --project LunarGuard.CLI -- obfuscate <script.lua> [options]
```

### GUI (Windows)

```bash
dotnet run --project LunarGuard.GUI
```

### Examples

```bash
# Obfuscate with all passes enabled (default)
dotnet run --project LunarGuard.CLI -- obfuscate script.lua

# Specify output file
dotnet run --project LunarGuard.CLI -- obfuscate script.lua -o output.lua

# Disable specific passes
dotnet run --project LunarGuard.CLI -- obfuscate script.lua --no-vm --no-antidebug

# Set custom string encryption key
dotnet run --project LunarGuard.CLI -- obfuscate script.lua --key "mySecretKey"

# Adjust dead code injection volume
dotnet run --project LunarGuard.CLI -- obfuscate script.lua --deadcount 10

# Verbose mode (shows enabled/disabled passes)
dotnet run --project LunarGuard.CLI -- obfuscate script.lua -v
```

### Options

| Option | Alias | Description |
|--------|-------|-------------|
| `[input]` | | Input Lua script file path |
| `--output` | `-o` | Output file path (default: `input.obfuscated.lua`) |
| `--no-rename` | | Disable variable renaming |
| `--no-strings` | | Disable string encryption |
| `--no-numbers` | | Disable number encoding |
| `--no-deadcode` | | Disable dead code injection |
| `--no-cflow` | | Disable control flow obfuscation |
| `--no-split` | | Disable expression splitting |
| `--no-antidebug` | | Disable anti-debug checks |
| `--no-vm` | | Disable bytecode virtualization |
| `--key` | | String encryption key (default: auto-generated) |
| `--deadcount` | | Number of dead code blocks (default: 5) |
| `--verbose` | `-v` | Show detailed pass information |

### Aliases

- `obfuscate` → `ob`, `protect`
- `info` → `about`

## Run Tests

```bash
dotnet test
```

## Roadmap

- [ ] **VM extension**: handle `RepeatStmt`, `ForGenericStmt`, `DoStmt`, `LabelStmt`, `GotoStmt`, nested `FunctionDeclStmt` in bytecode compiler
- [ ] **Fuzzing tests**: random Lua fragments for parser + obfuscator robustness
- [ ] **String encryption**: support single-character strings (currently skipped)
- [ ] **Code protection**: integrity checks, anti-tamper
- [ ] **AST-level optimization**: constant folding, dead code elimination before obfuscation

## Project Structure

```
LunarGuard/
├── LunarGuard.CLI/        # Spectre.Console command-line interface
├── LunarGuard.Core/        # Core obfuscation engine
│   ├── AST/                # Lua 5.1 abstract syntax tree
│   ├── CodeGen/            # Lua source code writer
│   ├── Obfuscation/        # Obfuscation passes
│   │   └── Virtualization/ # Bytecode VM compiler + interpreter
│   └── Syntax/             # Lexer + parser
├── LunarGuard.GUI/         # WPF desktop interface (Windows)
└── LunarGuard.Tests/       # xUnit tests
```

## Obfuscation Pipeline

Passes run in this order:

```
Input → RenamePass → DeadCodePass → StringEncryptPass → ControlFlowPass
       → ExpressionSplitPass → AntiDebugPass → NumberEncodePass
       → VirtualizationPass → Output
```

## License

MIT
