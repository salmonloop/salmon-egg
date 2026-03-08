# SalmonEgg Rename Design

## Background

The project has been renamed end-to-end to **SalmonEgg**, covering solution/project names, folder layout, namespaces, textual references, scripts, and published assets. No user-facing functionality should change during the rename.

## Goals

- Ensure no legacy identifiers remain (paths, solutions, csproj metadata, namespaces, docs, CI, scripts).
- Ensure user-facing app name, package identity, and runtime data paths align with `SalmonEgg`.
- Keep the existing project layout, cross-project references, and build/test workflows working after the rename.

## Steps

1. Rename files & folders so solutions/projects and directory layout use `SalmonEgg`.
2. Update `.sln`, `.csproj`, and shared config metadata so project references point at the renamed directories while still building the same targets.
3. Replace text inside code, XAML, docs, and scripts so every namespace, literal, setting, or asset name uses `SalmonEgg`.
4. Update README/BUILD/SETUP/USER/architectural docs, GitHub Actions, `run.bat`/`run.sh`, and any release automation to reference the new paths and artifact names.
5. Validate the rename via `dotnet clean`, `dotnet build SalmonEgg.sln`, `dotnet test SalmonEgg.sln`, and repo-wide `git grep` to confirm no legacy name occurrences remain.

## Validation & Risks

- Risk: Generated artifacts (.csproj.user, obj/bin outputs) may still mention the legacy name; mitigate by cleaning the repo and excluding generated files from commits.
- Risk: Tests such as `UiConventionsTests` may hardcode the old folder layout; update those helpers to load `SalmonEgg` paths.

## Next Steps

1. Run `dotnet build SalmonEgg.sln` and `dotnet test SalmonEgg.sln` in an environment with working NuGet connectivity.
2. Run repo-wide `git grep` checks to confirm no legacy identifiers remain.
