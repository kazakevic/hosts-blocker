# Hosts Blocker

A small Windows GUI for blocking specific websites through the `hosts` file — built for the
case where you keep opening a news site out of habit and want that to just stop working.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4) ![Windows](https://img.shields.io/badge/platform-Windows-0078D4)

## What it does

- **Blocks exact hostnames, not subtrees.** Adding `example.com` blocks `example.com` (and
  `www.example.com` if you leave the `+ www` toggle on) — nothing else. `mail.example.com`
  keeps working.
- **Toggle instead of delete.** Flipping a site off comments its line out, so re-enabling it
  later is one click.
- **Leaves the rest of your hosts file alone.** Only lines inside its own marked block are
  touched; everything else — including the section Docker Desktop rewrites on its own — is
  written back byte for byte, preserving the UTF-8 BOM and CRLF line endings.
- **No sidecar state.** Which sites are blocked, and whether each is on or off, lives in the
  hosts file itself, so editing it by hand doesn't confuse the app.
- Applies changes atomically (temp file + replace, leaving a `.bak`) and flushes the DNS cache.

## Running it

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download) to build:

```
dotnet build HostsBlocker
HostsBlocker\bin\Debug\net8.0-windows\HostsBlocker.exe
```

Or grab a prebuilt `.exe` from the [Releases](../../releases) page — no runtime install needed.

Writing to `%SystemRoot%\System32\drivers\etc\hosts` requires administrator rights, so the app
requests elevation and Windows will show a UAC prompt when it starts.

## Notes

Hosts-file blocking is a speed bump for habits, not a security control — it is trivially
bypassed and does not cover DNS-over-HTTPS resolvers that some browsers enable by default.

## License

MIT
