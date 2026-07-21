// Our Winter Car — universal installer (Actually Portable Executable / Cosmopolitan).
//
// One binary installs the co-op mod on both Windows and Linux. It carries both
// self-contained launcher builds inside its own zip store (/zip/win, /zip/linux)
// and, at run time:
//   1. detects the host OS,
//   2. extracts the matching launcher into a per-user app folder,
//   3. delegates the real mod install to that launcher's already-tested headless
//      path — `WinterMPLauncher --install-mod --silent` (installs BepInEx +
//      plugins, patches BepInEx.cfg + mainData, seeds the FastBoot profile),
//   4. opens the launcher GUI for host/join + save-backups.
//
// The launcher stays on disk after install, so ongoing repair/host/join/backups
// use the exact same tool players get from the Windows/Linux downloads today.
//
// Build: tools/build-ape-installer.sh (bundles the payload via cosmocc + zip).

#include <cosmo.h>
#include <libc/dce.h>

#include <errno.h>
#include <limits.h>
#include <spawn.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <sys/wait.h>
#include <unistd.h>

extern char **environ;

#define PRODUCT "Our Winter Car"
#define APPDIR_NAME "OurWinterCar"

// Exit codes mirror WinterMP.Launcher CliInstallRunner so scripts can branch on them.
enum {
    EXIT_OK = 0,
    EXIT_GAME_NOT_FOUND = 1,
    EXIT_INSTALL_FAILED = 2,
    EXIT_PAYLOAD_MISSING = 3,
    EXIT_UNSUPPORTED_OS = 4,
    EXIT_EXTRACT_FAILED = 5,
};

static const char *platform_key(void) {
    if (IsWindows()) return "win";
    if (IsLinux()) return "linux";
    return NULL;  // macOS/BSD: MWC is a Windows game with no supported path there.
}

static const char *launcher_exe(void) {
    return IsWindows() ? "WinterMPLauncher.exe" : "WinterMPLauncher";
}

// Per-user install location. Kept out of the game folder so a game reinstall or
// Steam "verify files" can't wipe the launcher, and so no admin rights are needed.
static void resolve_install_dir(char *out, size_t n) {
    const char *base;
    if (IsWindows()) {
        base = getenv("LOCALAPPDATA");
        if (!base || !*base) base = getenv("APPDATA");
        if (!base || !*base) base = getenv("USERPROFILE");
        if (!base || !*base) base = ".";
    } else {
        base = getenv("XDG_DATA_HOME");
        if (base && *base) {
            snprintf(out, n, "%s/%s", base, APPDIR_NAME);
            return;
        }
        const char *home = getenv("HOME");
        if (!home || !*home) home = ".";
        snprintf(out, n, "%s/.local/share/%s", home, APPDIR_NAME);
        return;
    }
    snprintf(out, n, "%s/%s", base, APPDIR_NAME);
}

// mkdir -p, tolerant of both slash styles and of a leading "C:" drive component.
static int make_dirs(const char *path) {
    char tmp[PATH_MAX];
    size_t len = strlen(path);
    if (len == 0 || len >= sizeof tmp) return -1;
    memcpy(tmp, path, len + 1);

    for (size_t i = 1; i < len; i++) {
        if (tmp[i] == '/' || tmp[i] == '\\') {
            char sep = tmp[i];
            tmp[i] = '\0';
            if (tmp[i - 1] != ':')  // don't try to mkdir a bare "C:" root
                mkdir(tmp, 0755);   // ignore EEXIST and drive-root failures
            tmp[i] = sep;
        }
    }
    if (mkdir(path, 0755) != 0 && errno != EEXIST) return -1;
    return 0;
}

static int make_parent_dirs(const char *file_path) {
    char tmp[PATH_MAX];
    snprintf(tmp, sizeof tmp, "%s", file_path);
    char *slash = strrchr(tmp, '/');
    char *back = strrchr(tmp, '\\');
    if (back > slash) slash = back;
    if (!slash) return 0;
    *slash = '\0';
    return make_dirs(tmp);
}

static int copy_stream(const char *src, const char *dst) {
    FILE *in = fopen(src, "rb");
    if (!in) return -1;
    if (make_parent_dirs(dst) != 0) { fclose(in); return -1; }
    FILE *out = fopen(dst, "wb");
    if (!out) { fclose(in); return -1; }

    char buf[1 << 16];
    size_t r;
    int rc = 0;
    while ((r = fread(buf, 1, sizeof buf, in)) > 0) {
        if (fwrite(buf, 1, r, out) != r) { rc = -1; break; }
    }
    if (ferror(in)) rc = -1;
    fclose(in);
    if (fclose(out) != 0) rc = -1;
    return rc;
}

// Copies every file listed in /zip/<key>.manifest from the zip store to destroot.
static int extract_launcher(const char *key, const char *destroot) {
    char manifest_path[PATH_MAX];
    snprintf(manifest_path, sizeof manifest_path, "/zip/%s.manifest", key);
    FILE *m = fopen(manifest_path, "rb");
    if (!m) {
        fprintf(stderr, "  embedded launcher missing (%s) — corrupt download?\n", manifest_path);
        return -1;
    }

    char rel[PATH_MAX];
    long copied = 0, failed = 0;
    while (fgets(rel, sizeof rel, m)) {
        size_t L = strlen(rel);
        while (L && (rel[L - 1] == '\n' || rel[L - 1] == '\r' || rel[L - 1] == ' '))
            rel[--L] = '\0';
        if (L == 0) continue;

        char src[PATH_MAX], dst[PATH_MAX];
        snprintf(src, sizeof src, "/zip/%s/%s", key, rel);
        snprintf(dst, sizeof dst, "%s/%s", destroot, rel);
        if (copy_stream(src, dst) != 0) {
            fprintf(stderr, "  failed to write %s\n", rel);
            failed++;
        } else {
            copied++;
        }
    }
    fclose(m);

    printf("  extracted %ld files\n", copied);
    return failed == 0 ? 0 : -1;
}

// Runs argv and waits; returns the child's exit code, or -1 if it couldn't start.
static int run_and_wait(const char *const argv[]) {
    pid_t pid;
    int rc = posix_spawn(&pid, argv[0], NULL, NULL, (char *const *)argv, environ);
    if (rc != 0) { errno = rc; return -1; }
    int status;
    if (waitpid(pid, &status, 0) < 0) return -1;
    return WIFEXITED(status) ? WEXITSTATUS(status) : -1;
}

// Starts argv without waiting (the GUI keeps running after the installer exits).
static int spawn_detached(const char *const argv[]) {
    pid_t pid;
    int rc = posix_spawn(&pid, argv[0], NULL, NULL, (char *const *)argv, environ);
    return rc == 0 ? 0 : -1;
}

static void usage(const char *argv0) {
    printf("%s universal installer\n\n", PRODUCT);
    printf("Usage: %s [options]\n\n", argv0);
    printf("  --game-dir <path>   Install into a specific My Winter Car folder\n");
    printf("  --dir <path>        Install the launcher into <path> (default: per-user app folder)\n");
    printf("  --no-launch         Install only; do not open the launcher afterwards\n");
    printf("  -h, --help          Show this help\n");
}

int main(int argc, char *argv[]) {
    const char *game_dir = NULL;
    const char *install_override = NULL;
    int no_launch = 0;

    for (int i = 1; i < argc; i++) {
        if (!strcmp(argv[i], "--game-dir") && i + 1 < argc) {
            game_dir = argv[++i];
        } else if (!strcmp(argv[i], "--dir") && i + 1 < argc) {
            install_override = argv[++i];
        } else if (!strcmp(argv[i], "--no-launch")) {
            no_launch = 1;
        } else if (!strcmp(argv[i], "-h") || !strcmp(argv[i], "--help")) {
            usage(argv[0]);
            return EXIT_OK;
        } else {
            fprintf(stderr, "Unknown argument: %s\n", argv[i]);
            usage(argv[0]);
            return EXIT_INSTALL_FAILED;
        }
    }

    const char *key = platform_key();
    if (!key) {
        fprintf(stderr,
                "%s runs on Windows and Linux (Steam/Proton) only — this OS is not supported.\n",
                PRODUCT);
        return EXIT_UNSUPPORTED_OS;
    }

    char install_dir[PATH_MAX];
    if (install_override && *install_override)
        snprintf(install_dir, sizeof install_dir, "%s", install_override);
    else
        resolve_install_dir(install_dir, sizeof install_dir);

    printf("== %s installer ==\n", PRODUCT);
    printf("Platform: %s\n", IsWindows() ? "Windows" : "Linux (Steam/Proton)");
    printf("Launcher target: %s\n\n", install_dir);

    printf("[1/3] Unpacking launcher...\n");
    if (make_dirs(install_dir) != 0) {
        fprintf(stderr, "Could not create %s (%s)\n", install_dir, strerror(errno));
        return EXIT_EXTRACT_FAILED;
    }
    if (extract_launcher(key, install_dir) != 0)
        return EXIT_EXTRACT_FAILED;

    char launcher_path[PATH_MAX];
    snprintf(launcher_path, sizeof launcher_path, "%s/%s", install_dir, launcher_exe());
    if (!IsWindows())
        chmod(launcher_path, 0755);  // self-contained .NET apphost needs the exec bit

    printf("[2/3] Installing the mod into My Winter Car...\n");
    const char *install_argv[8];
    int n = 0;
    install_argv[n++] = launcher_path;
    install_argv[n++] = "--install-mod";
    install_argv[n++] = "--silent";
    if (game_dir) { install_argv[n++] = "--game-dir"; install_argv[n++] = game_dir; }
    install_argv[n] = NULL;

    int code = run_and_wait(install_argv);
    if (code < 0) {
        fprintf(stderr, "Could not start the launcher (%s).\n", strerror(errno));
        return EXIT_INSTALL_FAILED;
    }

    switch (code) {
        case EXIT_OK:
            printf("  Mod installed.\n");
            break;
        case EXIT_GAME_NOT_FOUND:
            printf("  My Winter Car was not found automatically.\n"
                   "  Opening the launcher so you can point it at your game folder (Settings).\n");
            break;
        case EXIT_PAYLOAD_MISSING:
            fprintf(stderr, "  Bundled mod files are missing — this installer download is corrupt.\n");
            return EXIT_PAYLOAD_MISSING;
        default:
            fprintf(stderr, "  Install reported an error (code %d). See the launcher's last-install log.\n", code);
            // Still open the GUI so the user can retry / read the message.
            break;
    }

    if (no_launch) {
        printf("\n[3/3] Done. Launcher installed at:\n  %s\n", launcher_path);
        return code;
    }

    printf("[3/3] Opening %s...\n", PRODUCT);
    const char *gui_argv[] = { launcher_path, NULL };
    if (spawn_detached(gui_argv) != 0)
        fprintf(stderr, "  Could not open the launcher — run it manually:\n  %s\n", launcher_path);

    return code;
}
