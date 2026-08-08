using System;
using System.Linq;
using System.Text;

namespace SalmonEgg.Acp.JsonRpc
{
    /// <summary>
    /// The single definition of what counts as an inbound ACP frame, shared by every transport.
    /// </summary>
    /// <remarks>
    /// ACP carries individual JSON-RPC requests, notifications, or responses — never a batch — so a
    /// frame must begin with '{'. Anything else was never an ACP message: over stdio the agent wrote
    /// diagnostics to the stream reserved for the protocol (the spec directs those to stderr), and
    /// over a bridged transport the same line arrives verbatim as a text frame.
    ///
    /// This lives at the protocol layer rather than in one transport because the test is
    /// transport-agnostic. Only stdio can additionally say "this belonged on stderr", but every
    /// transport needs the same answer to "is this a frame at all" — otherwise a bridge that relays
    /// an agent's stdout reintroduces the defect the stdio path already guards.
    /// </remarks>
    public static class AcpFrame
    {
        /// <summary>
        /// U+FEFF. Not whitespace to <see cref="string.IsNullOrWhiteSpace(string?)"/>, so a line
        /// containing only this would otherwise be dispatched as a message.
        /// </summary>
        private const char ByteOrderMark = '﻿';

        /// <summary>
        /// Strips a leading byte order mark, which RFC 8259 §8.1 forbids emitting but explicitly
        /// permits parsers to "ignore ... rather than treating it as an error".
        /// </summary>
        public static string StripByteOrderMark(string message)
            => string.IsNullOrEmpty(message) ? message : message.TrimStart(ByteOrderMark);

        /// <summary>
        /// True when <paramref name="message"/> carries no content a transport should dispatch:
        /// empty, whitespace, or nothing but byte order marks.
        /// </summary>
        public static bool IsBlank(string? message)
            => string.IsNullOrEmpty(message) || string.IsNullOrWhiteSpace(StripByteOrderMark(message));

        /// <summary>
        /// True when <paramref name="message"/> looks like an ACP frame, i.e. its first
        /// non-whitespace character (after any byte order mark) opens a JSON object.
        /// </summary>
        /// <remarks>
        /// A shape test, not a validity test: a frame that looks like one but fails to parse is a
        /// genuine protocol error and is reported as such. This only separates "the peer intended to
        /// send a message" from "this was never a message".
        /// </remarks>
        public static bool LooksLikeFrame(string? message)
        {
            if (IsBlank(message))
            {
                return false;
            }

            var payload = StripByteOrderMark(message!).AsSpan().TrimStart();
            return payload.Length > 0 && payload[0] == '{';
        }

        /// <summary>
        /// Renders a rejected message plus its leading bytes so the cause is identifiable from logs
        /// alone.
        /// </summary>
        /// <remarks>
        /// A byte order mark, U+FFFD from a decode failure, a private-use glyph, and plain agent
        /// logging all produce the identical parser message and differ only in their leading bytes,
        /// so recording length alone cannot locate the cause.
        /// </remarks>
        public static string Describe(string? message, int maxChars = 120)
        {
            if (string.IsNullOrEmpty(message))
            {
                return "<empty>";
            }

            string text;
            if (message.Length <= maxChars)
            {
                text = message;
            }
            else
            {
                // Step back off a surrogate pair: this renders malformed input, so a split pair
                // would put a lone surrogate in the log — the very noise this is meant to resolve.
                var cut = maxChars;
                if (char.IsLowSurrogate(message[cut]))
                {
                    cut--;
                }

                text = string.Concat(message.AsSpan(0, cut), "…");
            }

            // Only the first few bytes are wanted, so encode a short prefix rather than the whole
            // message — this runs on an error path an agent may be flooding.
            const int HexBytes = 8;
            var prefix = message.Length <= HexBytes ? message : message[..HexBytes];
            var hex = string.Join(
                ' ',
                Encoding.UTF8.GetBytes(prefix).Take(HexBytes).Select(b => b.ToString("X2")));
            return $"{text} [hex: {hex}]";
        }
    }
}
