using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Acp.Plan
{
    /// <summary>
    /// Plan.
    /// Represents an agent's action plan, containing a sequence of plan entries.
    /// </summary>
    public sealed record Plan : AcpProtocolObject
    {
        private readonly List<PlanEntry> _entries = new();

        /// <summary>
        /// The list of plan entries.
        /// </summary>
        [JsonRequired]
        [JsonPropertyName("entries")]
        public List<PlanEntry> Entries
        {
            get => _entries;
            init => _entries = ValidateEntries(value);
        }

        /// <summary>
        /// Creates a new plan instance.
        /// </summary>
        public Plan()
        {
        }

        /// <summary>
        /// Creates a new plan instance.
        /// </summary>
        /// <param name="entries">The list of plan entries</param>
        public Plan(List<PlanEntry> entries)
        {
            Entries = ValidateEntries(entries);
        }

        /// <summary>
        /// Returns a snapshot of the plan with one new entry appended (the current instance is not modified).
        /// </summary>
        public Plan WithEntry(string content, PlanEntryStatus? status = null, PlanEntryPriority? priority = null)
        {
            var entries = new List<PlanEntry>(Entries)
            {
                new PlanEntry(
                    content,
                    status ?? PlanEntryStatus.Pending,
                    priority ?? PlanEntryPriority.Medium)
            };
            return this with { Entries = entries };
        }

        /// <summary>
        /// Gets all pending entries.
        /// </summary>
        public List<PlanEntry> GetPendingEntries()
            => Entries.FindAll(e => e.Status == PlanEntryStatus.Pending);

        /// <summary>
        /// Gets all in-progress entries.
        /// </summary>
        public List<PlanEntry> GetInProgressEntries()
            => Entries.FindAll(e => e.Status == PlanEntryStatus.InProgress);

        /// <summary>
        /// Gets all completed entries.
        /// </summary>
        public List<PlanEntry> GetCompletedEntries()
            => Entries.FindAll(e => e.Status == PlanEntryStatus.Completed);

        internal static List<PlanEntry> ValidateEntries(List<PlanEntry>? entries)
        {
            if (entries is null)
            {
                throw new JsonException("Plan entries must not be null.");
            }

            foreach (var entry in entries)
            {
                if (entry is null)
                {
                    throw new JsonException("Plan entries must not contain null items.");
                }
            }

            return entries;
        }
    }

    /// <summary>
    /// Plan entry.
    /// Represents a specific task or step within a plan.
    /// </summary>
    public sealed record PlanEntry : AcpProtocolObject
    {
        private readonly string _content = string.Empty;

        /// <summary>
        /// The content description of the entry.
        /// </summary>
        [JsonRequired]
        [JsonPropertyName("content")]
        public string Content
        {
            get => _content;
            init => _content = value ?? throw new JsonException("Plan entry content must not be null.");
        }

        /// <summary>
        /// The current status of the entry.
        /// </summary>
        [JsonRequired]
        [JsonPropertyName("status")]
        public PlanEntryStatus Status { get; init; } = PlanEntryStatus.Pending;

        /// <summary>
        /// The priority of the entry.
        /// </summary>
        [JsonRequired]
        [JsonPropertyName("priority")]
        public PlanEntryPriority Priority { get; init; } = PlanEntryPriority.Medium;

        /// <summary>
        /// Creates a new plan entry instance.
        /// </summary>
        public PlanEntry()
        {
        }

        /// <summary>
        /// Creates a new plan entry instance.
        /// </summary>
        /// <param name="content">The entry content</param>
        /// <param name="status">The entry status</param>
        /// <param name="priority">The entry priority</param>
        public PlanEntry(string content, PlanEntryStatus? status = null, PlanEntryPriority? priority = null)
        {
            Content = content ?? throw new JsonException("Plan entry content must not be null.");
            Status = status ?? PlanEntryStatus.Pending;
            Priority = priority ?? PlanEntryPriority.Medium;
        }

        /// <summary>
        /// Returns a snapshot of the entry marked as in progress (the current instance is not modified).
        /// </summary>
        public PlanEntry Started()
            => this with { Status = PlanEntryStatus.InProgress };

        /// <summary>
        /// Returns a snapshot of the entry marked as completed (the current instance is not modified).
        /// </summary>
        public PlanEntry Completed()
            => this with { Status = PlanEntryStatus.Completed };
    }


    /// <summary>
    /// The status of a plan entry.
    /// </summary>
    /// <remarks>
    /// Modeled as an extensible value type rather than a closed enum, so that unknown wire values are
    /// preserved losslessly and round-trip intact, matching the authoritative ACP schema
    /// (<c>PlanEntryStatus</c> is <c>#[non_exhaustive]</c> with an untagged <c>Other(String)</c> fallback).
    /// Per the ACP extension contract, unknown values that do not start with <c>_</c> are reserved for future
    /// ACP variants and must not be rejected by a client.
    /// </remarks>
    [JsonConverter(typeof(PlanEntryStatusJsonConverter))]
    public readonly struct PlanEntryStatus : IEquatable<PlanEntryStatus>
    {
        private readonly string? _value;

        /// <summary>
        /// Creates a plan entry status carrying the given wire value.
        /// </summary>
        /// <param name="value">The protocol string value.</param>
        public PlanEntryStatus(string value)
        {
            _value = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// The entry has been created but has not started yet.
        /// </summary>
        public static PlanEntryStatus Pending { get; } = new("pending");

        /// <summary>
        /// The entry is currently in progress.
        /// </summary>
        public static PlanEntryStatus InProgress { get; } = new("in_progress");

        /// <summary>
        /// The entry completed successfully.
        /// </summary>
        public static PlanEntryStatus Completed { get; } = new("completed");

        /// <summary>
        /// The entry was cancelled before completion.
        /// </summary>
        public static PlanEntryStatus Cancelled { get; } = new("cancelled");

        /// <summary>
        /// The wire value carried by this status.
        /// </summary>
        public string Value => _value ?? string.Empty;

        /// <inheritdoc />
        public bool Equals(PlanEntryStatus other) => string.Equals(_value, other._value, StringComparison.Ordinal);

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is PlanEntryStatus other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);

        /// <summary>
        /// Determines whether two statuses carry the same wire value.
        /// </summary>
        public static bool operator ==(PlanEntryStatus left, PlanEntryStatus right) => left.Equals(right);

        /// <summary>
        /// Determines whether two statuses carry different wire values.
        /// </summary>
        public static bool operator !=(PlanEntryStatus left, PlanEntryStatus right) => !left.Equals(right);

        /// <inheritdoc />
        public override string ToString() => Value;
    }

    /// <summary>
    /// The priority of a plan entry.
    /// </summary>
    /// <remarks>
    /// Modeled as an extensible value type rather than a closed enum, so that unknown wire values are
    /// preserved losslessly and round-trip intact, matching the authoritative ACP schema
    /// (<c>PlanEntryPriority</c> is <c>#[non_exhaustive]</c> with an untagged <c>Other(String)</c> fallback).
    /// Per the ACP extension contract, unknown values that do not start with <c>_</c> are reserved for future
    /// ACP variants and must not be rejected by a client.
    /// </remarks>
    [JsonConverter(typeof(PlanEntryPriorityJsonConverter))]
    public readonly struct PlanEntryPriority : IEquatable<PlanEntryPriority>
    {
        private readonly string? _value;

        /// <summary>
        /// Creates a plan entry priority carrying the given wire value.
        /// </summary>
        /// <param name="value">The protocol string value.</param>
        public PlanEntryPriority(string value)
        {
            _value = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Low priority.
        /// </summary>
        public static PlanEntryPriority Low { get; } = new("low");

        /// <summary>
        /// Medium priority.
        /// </summary>
        public static PlanEntryPriority Medium { get; } = new("medium");

        /// <summary>
        /// High priority.
        /// </summary>
        public static PlanEntryPriority High { get; } = new("high");

        /// <summary>
        /// The wire value carried by this priority.
        /// </summary>
        public string Value => _value ?? string.Empty;

        /// <inheritdoc />
        public bool Equals(PlanEntryPriority other) => string.Equals(_value, other._value, StringComparison.Ordinal);

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is PlanEntryPriority other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);

        /// <summary>
        /// Determines whether two priorities carry the same wire value.
        /// </summary>
        public static bool operator ==(PlanEntryPriority left, PlanEntryPriority right) => left.Equals(right);

        /// <summary>
        /// Determines whether two priorities carry different wire values.
        /// </summary>
        public static bool operator !=(PlanEntryPriority left, PlanEntryPriority right) => !left.Equals(right);

        /// <inheritdoc />
        public override string ToString() => Value;
    }

    internal sealed class PlanEntryStatusJsonConverter : JsonConverter<PlanEntryStatus>
    {
        public override PlanEntryStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("Plan entry status must be a string.");
            }

            return new PlanEntryStatus(reader.GetString()!);
        }

        public override void Write(Utf8JsonWriter writer, PlanEntryStatus value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Value);
        }
    }

    internal sealed class PlanEntryPriorityJsonConverter : JsonConverter<PlanEntryPriority>
    {
        public override PlanEntryPriority Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("Plan entry priority must be a string.");
            }

            return new PlanEntryPriority(reader.GetString()!);
        }

        public override void Write(Utf8JsonWriter writer, PlanEntryPriority value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Value);
        }
    }
}
