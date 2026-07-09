# Changelog

All notable changes to LunarGuard are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/).

## [2.0.0] - 2026-07-09

### Fixed — Bytecode Virtualization (the headline of this release)

The custom VM was producing scripts that crashed or returned wrong values.
The following were corrected:

- **Lexer integer typing.** Numeric literals were boxed as `double`
  through a ternary `condition ? double.Parse(…) : long.Parse(…)`, so every
  integer became `double` and `LOADK` operands bypassed the operand encryption,
  then got corrupted at runtime. Integer literals are now stored as `long`.
- **Comparison operand order.** `LT`/`GT`/`LEQ`/`GEQ` popped operands in the
  wrong order (`a < b` where `a` was the top-of-stack value). Now `b < a`,
  matching Lua's stack convention. This also unmasked the next bug.
- **Jump target off-by-one.** Lua's program counter is 1-indexed while the
  bytecode list is 0-indexed. Every `JZ`/`JMP`/loop-back/branch-end patch
  (and the short-circuit `JNZ`) now adds `+1`. Previously jumps landed on the
  wrong opcode, often executing `RET` early.
- **Recursive / local calls.** Function calls now resolve via `GETL` (register)
  when the callee is a local, instead of always `GETG` (global). The
  function's own value is also stored into its VM register before the interpreter
  runs, so recursion works (`fib(10)` now returns `55`).
- **CALL register corruption.** The `CALL` handler saves and restores the VM
  register table across recursive invocations.
- **Multiple return values.** The generated wrapper now drains the shared VM
  stack and returns every value, instead of returning only the top one
  (`calculate(10,20)` now yields `30, 200` rather than `200, nil`).
- **Upvalue guard.** Functions that capture an outer-scope local are skipped
  by the VM (the register model has no closure support) instead of resolving
  captured locals to `nil`.

### Fixed — other passes

- **String Encryption.** Decoders are now emitted at the very top of the root
  block, guaranteeing they are assigned before any consumer. They used to be
  inserted after leading `if` wrappers injected by other passes, leaving
  references as `nil` globals at runtime.
- **Opaque Predicates.** Declaration statements (`local …`) are no longer
  wrapped in an opaque `if` block, which had restricted the new binding's
  scope and broken sibling references (e.g. `local player = {}` followed by
  `function player:takeDamage`).
- **Integrity check.** The `INTEG_CHECK` expected-length constant was
  off-by-one relative to the two appended elements.

### Verified

- `fib(10)` → `55`; `calculate(10,20)` → `30, 200`; `greet("World")`
  → `Hello, World!` across VM-only, partial and full preset configurations.
- Full obfuscation pipeline runs clean on the bundled `test_script.lua`.
- 53 unit tests pass.

## [1.1.1] - 2026-07-06

### Fixed

- `ExpressionSplitPass` cast bug on `FunctionCallStmt.Call`.
- `StringEncryptPass` Lua 5.1 compatibility (`loadstring` signature,
  decoder arithmetic, scattered-decoder placement).
- `NumberEncodePass` nested-math inversion and `VirtualizationPass` Lua 5.1
  syntax (`loadstring`, `unpack`, `GETL` vs `GETG`).

## [1.1.0] - 2026-07-06

### Added

- WPF GUI (`LunarGuard.GUI`) with glassmorphism UI and a FAQ.
- SHA-256 verification of `LunarGuard.dll` against a pinned GitHub hash.
- Solution file wiring Core / CLI / Tests / GUI.
