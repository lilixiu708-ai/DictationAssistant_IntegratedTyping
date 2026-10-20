// SPDX-License-Identifier: AGPL-3.0-or-later
// Modified: 2026-10-20
// Keep managed .NET files in Resources while exposing a native app executable.
#include <errno.h>
#include <limits.h>
#include <mach-o/dyld.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

int main(int argc, char **argv)
{
    (void)argc;
    char executable[PATH_MAX];
    char resolved[PATH_MAX];
    uint32_t capacity = sizeof executable;
    if (_NSGetExecutablePath(executable, &capacity) != 0 ||
        realpath(executable, resolved) == NULL) {
        fprintf(stderr, "Cannot locate the application launcher.\n");
        return 1;
    }
    char *separator = strrchr(resolved, '/');
    if (separator == NULL) {
        fprintf(stderr, "Invalid application launcher path.\n");
        return 1;
    }
    *separator = '\0';
    char apphost[PATH_MAX];
    int length = snprintf(apphost, sizeof apphost,
                          "%s/../Resources/runtime/DictationAssistant.App", resolved);
    if (length < 0 || (size_t)length >= sizeof apphost) {
        fprintf(stderr, "The application path is too long.\n");
        return 1;
    }
    argv[0] = apphost;
    execv(apphost, argv);
    fprintf(stderr, "Cannot start DictationAssistant: %s\n", strerror(errno));
    return 1;
}
