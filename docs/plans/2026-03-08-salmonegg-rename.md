# SalmonEgg Rename Implementation Plan

> I'm using the writing-plans skill to create the implementation plan.
> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Ensure the entire repository consistently uses the `SalmonEgg` name across solutions, projects, source code, docs, scripts, and published assets without altering functionality.

**Architecture:** The rename flows through the existing layered structure (UI project, src/Application/Domain/Infrastructure layers, tests) and keeps the MVVM + Uno Platform architecture intact while only touching metadata/names.

**Tech Stack:** .NET 10+ (Uno Platform), MSBuild/`dotnet` CLI, PowerShell, Git.

---

### Task 1: Ensure solutions and UI project use SalmonEgg

**Files:**
- `SalmonEgg.sln`
- `SalmonEgg/SalmonEgg/SalmonEgg.csproj`

**Step 1:** Confirm the main solution is `SalmonEgg.sln` and references `SalmonEgg/SalmonEgg/SalmonEgg.csproj`.

### Task 2: Ensure shared projects and tests use SalmonEgg

**Files:**
- `src/SalmonEgg.Domain/SalmonEgg.Domain.csproj`
- `src/SalmonEgg.Application/SalmonEgg.Application.csproj`
- `src/SalmonEgg.Infrastructure/SalmonEgg.Infrastructure.csproj`
- `tests/SalmonEgg.*/*.csproj`

**Step 1:** Confirm each `.csproj` references the renamed project paths and assemblies.

### Task 3: Replace code-level references

**Files:** All `.cs`, `.xaml`, `.json`, `.xml`, `.md`, `.sh`, `.bat`, YAML, and config files tracked in Git that previously contained legacy product names.

**Step 1:** Run repo-wide `git grep` checks to ensure no legacy name remains (including case variants).
**Step 2:** Manually verify rename-sensitive items (DI entrypoints, namespaces, XAML class names, runtime storage paths).
**Step 3:** Verify MSIX tooling uses `SalmonEgg` project paths and certificate subject.

### Task 4: Refresh docs, scripts, and CI workflows

**Files:** README.md, BUILD_GUIDE.md, docs/*.md (SETUP, USER, architecture, release, coding standards, etc.), `.github/workflows/ci.yml`, `build.{sh,bat}`, `run.{sh,bat}`, packaging scripts.

**Step 1:** Update textual references, headings, and commands so they cite `SalmonEgg` (including asset names like `SalmonEgg-wasm.zip`).

**Step 2:** Ensure workflows and scripts run `dotnet ... SalmonEgg.sln` and artifact names align with `SalmonEgg`.

**Step 3:** Update release documentation to use `%LOCALAPPDATA%\SalmonEgg` and new package names.

**Step 4:** Review `artifacts/msix/*` folder names referenced by docs or scripts and rename if those artifacts are committed (or update docs to mention the new names without renaming the artifacts folder if it is generated output).

### Task 5: Validate and tidy

**Files:** `SalmonEgg.sln`, `docs`, script updates, renamed project files.

**Step 1:** Run `dotnet clean SalmonEgg.sln`.

**Step 2:** Run `dotnet build SalmonEgg.sln --configuration Release` and confirm no errors.

**Step 3:** Run `dotnet test SalmonEgg.sln`.

**Step 4:** Run repo-wide `git grep` checks to prove no legacy identifiers remain.

**Step 5:** Run `git status` to review changes and prepare for commit.
