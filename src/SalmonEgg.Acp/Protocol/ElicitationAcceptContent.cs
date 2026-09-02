using System;
using System.Collections.Generic;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// The content object submitted with an accepted elicitation, keyed by form field name.
    /// </summary>
    /// <remarks>
    /// The client is the party that knows which primitive JSON type each field carries, so the value is
    /// built here rather than inferred later from a stringly-typed dictionary: a multi-select field must
    /// leave as a JSON array and an integer field as a JSON number, otherwise the Agent's own
    /// schema re-validation rejects it.
    /// </remarks>
    public sealed class ElicitationAcceptContent
    {
        private readonly Dictionary<string, ElicitationContentValue> _values =
            new(StringComparer.Ordinal);

        /// <summary>
        /// The field values collected so far.
        /// </summary>
        public IReadOnlyDictionary<string, ElicitationContentValue> Values => _values;

        /// <summary>
        /// Sets a string field.
        /// </summary>
        /// <param name="field">The field name from the requested schema.</param>
        /// <param name="value">The submitted value.</param>
        public ElicitationAcceptContent SetString(string field, string value)
            => Set(field, ElicitationContentValue.FromString(value));

        /// <summary>
        /// Sets an integer field.
        /// </summary>
        /// <param name="field">The field name from the requested schema.</param>
        /// <param name="value">The submitted value.</param>
        public ElicitationAcceptContent SetInteger(string field, long value)
            => Set(field, ElicitationContentValue.FromInteger(value));

        /// <summary>
        /// Sets a floating-point field.
        /// </summary>
        /// <param name="field">The field name from the requested schema.</param>
        /// <param name="value">The submitted value.</param>
        public ElicitationAcceptContent SetNumber(string field, double value)
            => Set(field, ElicitationContentValue.FromNumber(value));

        /// <summary>
        /// Sets a boolean field.
        /// </summary>
        /// <param name="field">The field name from the requested schema.</param>
        /// <param name="value">The submitted value.</param>
        public ElicitationAcceptContent SetBoolean(string field, bool value)
            => Set(field, ElicitationContentValue.FromBoolean(value));

        /// <summary>
        /// Sets a multi-select field.
        /// </summary>
        /// <param name="field">The field name from the requested schema.</param>
        /// <param name="values">The selected values.</param>
        public ElicitationAcceptContent SetStringArray(string field, IEnumerable<string> values)
            => Set(field, ElicitationContentValue.FromStringArray(values));

        /// <summary>
        /// Sets a field to an already-built value.
        /// </summary>
        /// <param name="field">The field name from the requested schema.</param>
        /// <param name="value">The submitted value.</param>
        public ElicitationAcceptContent Set(string field, ElicitationContentValue value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(field);
            ArgumentNullException.ThrowIfNull(value);

            _values[field] = value;
            return this;
        }

        /// <summary>
        /// Projects the collected values into the wire dictionary, or <c>null</c> when nothing was
        /// collected, so an empty submission is written as omitted content rather than an empty object.
        /// </summary>
        public Dictionary<string, ElicitationContentValue>? ToWireContent()
            => _values.Count == 0 ? null : new Dictionary<string, ElicitationContentValue>(_values, StringComparer.Ordinal);
    }
}
