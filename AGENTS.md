# AGENTS.md

## Project

.NET 10 solution with 3 projects — a LINQ learning exercise.

```
src/exercises/           Console app (Program.cs entrypoint)
src/exercises.Lib/       Class library (Order, OrderItem, Greeter)
tests/exercises.Tests/   xUnit tests
```

## Commands

```sh
dotnet build exercises.sln          # build all (also works with .slnx)
dotnet test tests/exercises.Tests/  # run all tests
```

## Conventions

- **Test files** — every `[Fact]` **must** have detailed ELI5 comments explaining what the code does and why, as if to a 5-year-old. No `[Fact]` without them.
- **Domain models** — you may modify `Order`, `OrderItem`, or any model as needed to fulfill test requirements.
- **Solution files** — both `exercises.sln` (VSCode compat) and `exercises.slnx` (.NET 10 native) exist; use `.sln` when the C# extension is involved.
- **Layout** — `src/` + `tests/` is required. Putting a `.csproj` subdirectory under another project causes .NET 10 SDK to auto-include its `.cs` files in the parent compilation, breaking the build.
- **Framework** — `net10.0`, `ImplicitUsings` enabled, `Nullable` enabled.
- **Tests** — xUnit v2.9.3, Microsoft.NET.Test.Sdk v17.14.1, coverlet.collector v6.0.4.
- **No CI / no pre-commit / no linter / no typecheck** beyond `dotnet build`.
