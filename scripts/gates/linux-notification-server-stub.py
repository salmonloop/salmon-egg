#!/usr/bin/env python3
"""A minimal org.freedesktop.Notifications server for the Linux notification gate.

Claims the well-known name on whatever bus DBUS_SESSION_BUS_ADDRESS points at, answers
GetCapabilities and Notify per the Desktop Notifications specification, and appends every call to a
JSON-lines file. The gate asserts on that file, so the evidence is what the app actually put on the
session bus rather than what a mock recorded in-process.

Prints "READY <unique-name>" on stdout once the name is owned.
"""
import json
import sys

from jeepney import DBusAddress, MessageType, new_error, new_method_call, new_method_return
from jeepney.io.blocking import open_dbus_connection
from jeepney.low_level import HeaderFields

NAME = "org.freedesktop.Notifications"
DBUS = DBusAddress(
    "/org/freedesktop/DBus",
    bus_name="org.freedesktop.DBus",
    interface="org.freedesktop.DBus",
)

# org.freedesktop.DBus.RequestName reply codes that mean this process owns the name.
PRIMARY_OWNER = 1
ALREADY_OWNER = 4


def main() -> int:
    record_path = sys.argv[1]
    connection = open_dbus_connection()

    reply = connection.send_and_get_reply(
        new_method_call(DBUS, "RequestName", "su", (NAME, 4)))
    if reply.body[0] not in (PRIMARY_OWNER, ALREADY_OWNER):
        print(f"RequestName for {NAME} returned {reply.body[0]}", file=sys.stderr)
        return 1

    print(f"READY {connection.unique_name}", flush=True)

    def record(entry: dict) -> None:
        with open(record_path, "a", encoding="utf-8") as handle:
            handle.write(json.dumps(entry) + "\n")
            handle.flush()

    next_id = 1
    while True:
        message = connection.receive()
        if message.header.message_type is not MessageType.method_call:
            continue

        member = message.header.fields.get(HeaderFields.member)
        interface = message.header.fields.get(HeaderFields.interface)

        if member == "GetCapabilities":
            record({"call": "GetCapabilities"})
            connection.send(new_method_return(message, "as", (["body", "actions"],)))
        elif member == "Notify":
            (app_name, replaces_id, _app_icon, summary,
             body, actions, hints, timeout) = message.body
            # The spec says a non-zero replaces_id reuses that id; otherwise assign a fresh one.
            assigned_id = replaces_id if replaces_id else next_id
            if not replaces_id:
                next_id += 1
            record({
                "call": "Notify",
                "app_name": app_name,
                "replaces_id": replaces_id,
                "assigned_id": assigned_id,
                "summary": summary,
                "body": body,
                "actions": list(actions),
                "hint_keys": sorted(dict(hints).keys()),
                "timeout": timeout,
            })
            connection.send(new_method_return(message, "u", (assigned_id,)))
        elif member == "Introspect":
            connection.send(new_method_return(message, "s", ("<node/>",)))
        else:
            connection.send(new_error(
                message,
                "org.freedesktop.DBus.Error.UnknownMethod",
                "s",
                (f"{interface}.{member}",)))


if __name__ == "__main__":
    sys.exit(main())
