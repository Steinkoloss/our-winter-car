# Universal installer (APE / Cosmopolitan)

`ourwintercar-installer.c` compiles to **one Actually Portable Executable** —
`dist/OurWinterCar-Installer.com` — that installs the mod on **both Windows and
Linux** from a single file.

## What it does

.NET can't be compiled to APE, so this is a tiny native **bootstrapper**, not a
rewrite of the launcher. At run time it:

1. detects the host OS (`IsWindows()` / `IsLinux()` from Cosmopolitan),
2. extracts the OS-matched self-contained launcher — both `win-x64` and
   `linux-x64` builds live in the file's own zip store (`/zip/win`, `/zip/linux`),
3. runs `WinterMPLauncher --install-mod --silent`, the launcher's existing,
   already-Windows-tested headless install (BepInEx + plugins + `BepInEx.cfg` +
   `mainData` patch + FastBoot profile — see `CliInstallRunner` / `BepInExInstaller`),
4. opens the launcher GUI for host/join + save-backups.

The launcher stays on disk, so ongoing repair/host/join/backups use the exact
same tool as the Windows/Linux downloads. The mod payload it installs is
platform-neutral (net35 DLLs + BepInEx run identically under Windows and Proton).

## Build

```bash
export COSMOCC=~/cosmocc/bin/cosmocc      # see toolchain setup below
./tools/build-ape-installer.sh            # -> dist/OurWinterCar-Installer.com
```

Needs the .NET SDK and a My Winter Car install (`Directory.Build.props.user`) so
the net35 payload DLLs build, same as any release.

## Toolchain setup (Cosmopolitan)

```bash
mkdir -p ~/cosmocc && cd ~/cosmocc
curl -fsSL https://cosmo.zip/pub/cosmocc/cosmocc.zip -o cosmocc.zip
unzip -q cosmocc.zip
```

### ⚠ Wine `binfmt_misc` gotcha (gaming distros: Arch/CachyOS, etc.)

APE binaries start with the `MZ` header. If the system has a `binfmt_misc` entry
that routes `MZ` to Wine (Steam/Proton machines often do — check
`/proc/sys/fs/binfmt_misc/DOSWin`), then **the cosmocc toolchain binaries
themselves get hijacked into Wine and the build hangs for minutes**.

Fix without root — convert the toolchain to native ELF in place (leaves your
system Wine setup untouched):

```bash
cd ~/cosmocc
find bin libexec -type f | while read -r f; do
  [ "$(head -c2 "$f" | xxd -p)" = 4d5a ] || continue   # APE only
  case "$f" in */assimilate) continue;; esac
  sh bin/assimilate -c -e -x "$f"                       # run via sh to dodge binfmt
done
```

The **output** `.com` is still a portable APE. On a machine with the Wine
`MZ→wine` rule, run it via `sh ./OurWinterCar-Installer.com` (or register the APE
loader per the cosmocc README). End users on such distros need the same
`sh ./...` fallback — worth calling out in player docs.
