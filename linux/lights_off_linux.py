#!/usr/bin/env python3
"""Experimental Lights Off prototype for Linux X11 sessions."""

from __future__ import annotations

import asyncio
import os
import shutil
import subprocess
import threading
import tkinter as tk
from tkinter import messagebox

try:
    from dbus_next import BusType
    from dbus_next.aio import MessageBus
except ImportError:
    BusType = None
    MessageBus = None


class Login1Inhibitor:
    """Owns a logind inhibitor FD so the OS releases it when this app exits."""

    def __init__(self) -> None:
        self.loop = asyncio.new_event_loop()
        self.thread = threading.Thread(target=self.loop.run_forever, daemon=True)
        self.bus = None
        self.fd = None

    async def _connect(self) -> None:
        if self.bus is not None:
            await self._disconnect()
        self.bus = await MessageBus(
            bus_type=BusType.SYSTEM, negotiate_unix_fd=True
        ).connect()
        introspection = await self.bus.introspect(
            "org.freedesktop.login1", "/org/freedesktop/login1"
        )
        obj = self.bus.get_proxy_object(
            "org.freedesktop.login1", "/org/freedesktop/login1", introspection
        )
        manager = obj.get_interface("org.freedesktop.login1.Manager")
        self.fd = await manager.call_inhibit(
            "idle:sleep",
            "Lights Off",
            "Keep the PC awake during a Lights Off session",
            "block",
        )

    async def _disconnect(self) -> None:
        if self.fd is not None:
            os.close(int(self.fd))
            self.fd = None
        if self.bus is not None:
            self.bus.disconnect()
            self.bus = None

    def start(self) -> None:
        if MessageBus is None:
            raise RuntimeError("Missing dbus-next. Install the Linux requirements first.")
        if not self.thread.is_alive():
            self.thread.start()
        future = asyncio.run_coroutine_threadsafe(self._connect(), self.loop)
        future.result(timeout=8)

    def stop(self) -> None:
        if not self.thread.is_alive():
            return
        future = asyncio.run_coroutine_threadsafe(self._disconnect(), self.loop)
        future.result(timeout=5)

    def close(self) -> None:
        self.stop()
        if self.thread.is_alive():
            self.loop.call_soon_threadsafe(self.loop.stop)
            self.thread.join(timeout=2)


class LightsOffLinux:
    BG = "#121316"
    CARD = "#1e2028"
    BORDER = "#2e323e"
    TEXT = "#f1f2f4"
    MUTED = "#a5a8b2"
    PURPLE = "#7168e8"
    GREEN = "#43bd83"
    RED = "#dc626b"

    def __init__(self, root: tk.Tk) -> None:
        self.root = root
        self.inhibitor = Login1Inhibitor()
        self.keep_awake = False
        self.root.title("Lights Off")
        self.root.configure(bg=self.BG)
        self.root.resizable(False, False)
        self.root.protocol("WM_DELETE_WINDOW", self.close)
        self._build_ui()
        self._check_session()

    def _build_ui(self) -> None:
        outer = tk.Frame(self.root, bg=self.BG, padx=18, pady=16)
        outer.pack(fill="both", expand=True)

        header = tk.Frame(outer, bg=self.BG)
        header.pack(fill="x", pady=(0, 14))
        tk.Label(header, text="Lights Off", bg=self.BG, fg=self.TEXT,
                 font=("TkDefaultFont", 16, "bold")).pack(side="left")
        self.status = tk.Label(header, text="Ready", bg=self.BG, fg=self.GREEN,
                               font=("TkDefaultFont", 9, "bold"))
        self.status.pack(side="right", pady=5)

        monitor = self._card(outer)
        tk.Label(monitor, text="Turn monitors off", bg=self.CARD, fg=self.TEXT,
                 font=("TkDefaultFont", 11, "bold")).pack(anchor="w")
        tk.Label(monitor, text="Requests real standby through X11 DPMS.", bg=self.CARD,
                 fg=self.MUTED, font=("TkDefaultFont", 9)).pack(anchor="w", pady=(4, 10))
        self.off_button = tk.Button(
            monitor, text="Turn monitors off now", command=self.turn_displays_off,
            bg=self.PURPLE, fg="white", activebackground="#5f57cf",
            activeforeground="white", relief="flat", padx=12, pady=8,
            cursor="hand2", font=("TkDefaultFont", 10, "bold"),
        )
        self.off_button.pack(fill="x")

        awake = self._card(outer)
        top = tk.Frame(awake, bg=self.CARD)
        top.pack(fill="x")
        tk.Label(top, text="Keep PC awake", bg=self.CARD, fg=self.TEXT,
                 font=("TkDefaultFont", 11, "bold")).pack(side="left")
        tk.Label(awake, text="Prevents automatic idle sleep. Displays are unaffected.",
                 bg=self.CARD, fg=self.MUTED,
                 font=("TkDefaultFont", 9), wraplength=340, justify="left").pack(
                     anchor="w", pady=(4, 10))
        self.awake_button = tk.Button(
            awake, text="Keep PC awake", command=self.toggle_keep_awake,
            bg=self.CARD, fg=self.TEXT, activebackground="#292c36",
            activeforeground=self.TEXT, relief="flat", padx=12, pady=8,
            cursor="hand2", font=("TkDefaultFont", 10, "bold"),
            highlightthickness=1, highlightbackground=self.BORDER,
        )
        self.awake_button.pack(fill="x")

        tk.Label(outer, text="Experimental Linux X11 prototype", bg=self.BG,
                 fg=self.MUTED, font=("TkDefaultFont", 8)).pack(anchor="w", pady=(2, 0))

    def _card(self, parent: tk.Widget) -> tk.Frame:
        card = tk.Frame(parent, bg=self.CARD, padx=14, pady=12,
                        highlightthickness=1, highlightbackground=self.BORDER)
        card.pack(fill="x", pady=(0, 10))
        return card

    def _check_session(self) -> None:
        if os.environ.get("WAYLAND_DISPLAY"):
            self.off_button.configure(state="disabled")
            self.status.configure(text="Wayland unsupported", fg="#e2ad4f")
        elif not os.environ.get("DISPLAY"):
            self.off_button.configure(state="disabled")
            self.status.configure(text="No X11 display", fg="#e2ad4f")
        elif not shutil.which("xset"):
            self.off_button.configure(state="disabled")
            self.status.configure(text="Install x11-xserver-utils", fg="#e2ad4f")
        else:
            result = subprocess.run(["xset", "-q"], capture_output=True, text=True)
            if result.returncode != 0 or "DPMS" not in result.stdout:
                self.off_button.configure(state="disabled")
                self.status.configure(text="DPMS unavailable", fg="#e2ad4f")

    def turn_displays_off(self) -> None:
        result = subprocess.run(["xset", "dpms", "force", "off"],
                                capture_output=True, text=True)
        if result.returncode:
            messagebox.showerror("Lights Off", result.stderr.strip() or
                                 "The X11 display server rejected the DPMS request.")
        else:
            self.status.configure(text="Standby requested", fg=self.PURPLE)

    def toggle_keep_awake(self) -> None:
        if self.keep_awake:
            try:
                self.inhibitor.stop()
                self.keep_awake = False
                self.awake_button.configure(text="Keep PC awake", fg=self.TEXT)
                self.status.configure(text="Normal sleep settings", fg=self.GREEN)
            except Exception as exc:
                messagebox.showerror("Could not release keep-awake", str(exc))
            return
        try:
            self.inhibitor.start()
            self.keep_awake = True
            self.awake_button.configure(text="Stop keeping PC awake", fg=self.GREEN)
            self.status.configure(text="PC sleep prevented", fg=self.GREEN)
        except Exception as exc:
            messagebox.showerror(
                "Could not keep the PC awake",
                f"logind did not grant a sleep inhibitor.\n\n{exc}",
            )

    def close(self) -> None:
        try:
            self.inhibitor.close()
        except Exception:
            pass
        self.root.destroy()


def main() -> None:
    root = tk.Tk()
    LightsOffLinux(root)
    root.mainloop()


if __name__ == "__main__":
    main()
