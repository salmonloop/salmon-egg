# Config Persistence (YAML) Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the current JSON-based `ConfigurationManager` with a versioned, atomic, YAML-based config system (`SalmonEgg/config`) plus secure secret storage for sensitive fields, per `docs/SPEC-CONFIG-PERSISTENCE-YAML.md`.

**Architecture:** Persist non-sensitive server config to per-server YAML files and UI preferences to a global YAML file; persist secrets to `ISecureStorage` with stable key prefixes; enforce schema version rules (read older, read-only newer), ignore unknown fields, and fall back on unknown enum values.

**Tech Stack:** .NET, `YamlDotNet`, existing `ISecureStorage`, xUnit.

---

### Task 1: Define persistence DTOs and conversions

**Files:**
- Create: `src/SalmonEgg.Infrastructure/Configuration/Model/AppConfigYamlV1.cs`
- Create: `src/SalmonEgg.Infrastructure/Configuration/Model/ServerConfigYamlV1.cs`
- Modify: `src/SalmonEgg.Domain/Models/AuthenticationConfig.cs`
- Modify: `src/SalmonEgg.Application/Validators/ServerConfigurationValidator.cs`

**Steps:**
1. Add `AuthenticationMode` concept (`none` | `bearer_token` | `api_key`) to in-memory model without allowing secrets to ever serialize to YAML.
2. Implement mapping between domain `ServerConfiguration` and YAML DTO, including snake_case fields and `*_seconds` naming.

### Task 2: Implement atomic YAML file IO + schema checks

**Files:**
- Create: `src/SalmonEgg.Infrastructure/Configuration/AtomicFile.cs`
- Create: `src/SalmonEgg.Infrastructure/Configuration/YamlSerializerFactory.cs`
- Create: `src/SalmonEgg.Infrastructure/Configuration/YamlEnumConverters.cs`

**Steps:**
1. Write `AtomicFile.WriteUtf8AtomicAsync()` (temp write + fsync/flush + replace/rename).
2. Build YamlDotNet serializer/deserializer configured to ignore unknown fields.
3. Add tolerant enum parsing (unknown values => default + warning hook).

### Task 3: Implement stores (servers + app settings) and secret store

**Files:**
- Create: `src/SalmonEgg.Infrastructure/Configuration/IConfigurationStore.cs`
- Create: `src/SalmonEgg.Infrastructure/Configuration/IAppSettingsStore.cs`
- Create: `src/SalmonEgg.Infrastructure/Configuration/IConfigurationSecretStore.cs`
- Create: `src/SalmonEgg.Infrastructure/Configuration/YamlConfigurationStore.cs`
- Create: `src/SalmonEgg.Infrastructure/Configuration/YamlAppSettingsStore.cs`
- Create: `src/SalmonEgg.Infrastructure/Configuration/SecureConfigurationSecretStore.cs`

**Steps:**
1. Persist servers under `SalmonEgg/config/servers/<id>.yaml`.
2. Persist app settings under `SalmonEgg/config/app.yaml`.
3. Persist secrets with keys: `salmonegg/config/<serverId>/token` and `salmonegg/config/<serverId>/apiKey`.
4. Refuse writes if existing file schema is newer than supported.

### Task 4: Replace `ConfigurationManager` with a clean service façade

**Files:**
- Create: `src/SalmonEgg.Infrastructure/Configuration/ConfigurationService.cs`
- Modify: `src/SalmonEgg.Domain/Services/IConfigurationService.cs` (only if needed)
- Modify: `SalmonEgg/SalmonEgg/DependencyInjection.cs`
- Modify: `src/SalmonEgg.Infrastructure/Storage/SecureStorage.cs`
- Delete: `src/SalmonEgg.Infrastructure/Storage/ConfigurationManager.cs` (or keep but stop registering)

**Steps:**
1. Implement `IConfigurationService` on top of YAML store + secret store.
2. Switch all app-data roots to `SalmonEgg/` (including secure storage folder and logging root).

### Task 5: Persist UI preferences to `app.yaml`

**Files:**
- Create: `src/SalmonEgg.Domain/Services/IAppSettingsService.cs`
- Create: `src/SalmonEgg.Domain/Models/AppSettings.cs`
- Create: `src/SalmonEgg.Infrastructure/Configuration/AppSettingsService.cs`
- Modify: `SalmonEgg/SalmonEgg/Presentation/ViewModels/SettingsViewModel.cs`
- Modify: `SalmonEgg/SalmonEgg/DependencyInjection.cs`

**Steps:**
1. Load settings at startup and apply to `SettingsViewModel`.
2. Debounce saves on changes (avoid frequent IO).

### Task 6: Tests (RED → GREEN)

**Files:**
- Modify: `tests/SalmonEgg.Infrastructure.Tests/Storage/ConfigurationManagerTests.cs` (rename/retarget)
- Modify: `tests/SalmonEgg.IntegrationTests/ConfigurationIntegrationTests.cs`

**Test cases:**
1. Round-trip server config: YAML contains no token/apiKey; secrets load correctly.
2. Unknown YAML fields: ignored.
3. Unknown enum value: falls back to default.
4. Delete server: YAML file removed + secrets removed.
5. App settings: round-trip to `app.yaml`.
