# Contributing to CrimsonX

Thanks for wanting to help. CrimsonX is source available, not open source, so a
few rules apply before code can be accepted.

## Reporting bugs

Open an issue with:

- what you did, what happened, and what you expected instead
- the CrimsonX version (see `version.json` or the app's About row)
- your distribution, desktop session (X11 / Wayland), CPU architecture, and the
  connection mode (VPN / Proxy), plus whether **Clear Proxy Mode** behaves
  differently
- the output of `crimsonx --doctor`
- the relevant lines from the app's log

## Security issues

Do not open a public issue. See [SECURITY.md](SECURITY.md).

## Sending changes

The source is licensed under the PolyForm Strict License 1.0.0, which does not
permit making changes or new works based on the software. A pull request
therefore cannot be accepted on the licence alone.

If you want to contribute code, write to **CrimsonVPN.Support@proton.me** first
and describe the change. Anything accepted is licensed to the project owner on
terms that allow relicensing, so a contribution cannot hold the project's
licensing hostage later.

## Building and testing

You need the .NET 10 SDK.

```bash
cp Services/AppSecrets.cs.example Services/AppSecrets.cs
```

Fill in your own worker URLs, `WorkerDomainKeyword`, and a fresh 32-byte cache
key plus 16-byte IV, then:

```bash
dotnet restore CrimsonX.csproj
dotnet build CrimsonX.csproj -c Release
```

The test suite is not part of the public repository, so `dotnet test` only works
in the private tree.

`pack-linux.ps1` builds the x64, arm64 and arm release archives. `install/`
holds the installer and desktop entry those archives ship; keep them LF-only —
`.gitattributes` pins that, because a CRLF `install.sh` fails on Linux with
`bad interpreter`.

The proxy engines (Xray, sing-box, amnezia-box, aether, crimsonipsec) are not in
the repository. The app builds and starts without them, but cannot tunnel.

Please keep the existing header block in every source file, match the style of
the surrounding code, and avoid new dependencies unless they are really needed:
each one has to ship a licence text in `Licenses/` and a line in
`THIRD-PARTY-NOTICES.txt`.

Contact: **CrimsonVPN.Support@proton.me**
