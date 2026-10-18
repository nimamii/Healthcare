#!/usr/bin/env node
/**
 * ============================================================================
 *  DO NOT USE THIS AS A SUBSTITUTE FOR `dotnet build`.
 * ============================================================================
 *
 * This was written while the build could not run, as an attempt to approximate
 * one specific check: does project A see the types it uses from project B.
 *
 * It was tested and rejected. Findings from that test:
 *
 *   - It DID catch an unimplemented interface and an unknown type name.
 *   - It did NOT catch accessibility violations — making a cross-assembly
 *     member `internal` is exactly the bug it missed, and that bug reached
 *     review before being found by a human pass.
 *   - It produces heavy false positives on member names, because PascalCase
 *     members (`Hash`, `TokenHash`, `Fact`) are indistinguishable from types
 *     without full semantic analysis.
 *   - It cannot see framework types, so it needs an allow-list that would
 *     silently hide any real error sharing a name with a BCL type.
 *
 * A checker you cannot trust is worse than no checker: it reports "clean"
 * while missing the class of error you care about. This file is kept only so
 * the decision is recorded, and it is excluded from the Docker build.
 *
 * The real answer is the one in docs/LOCAL-DEVELOPMENT.md: the build runs
 * inside a container, so no .NET SDK is needed on the host.
 *
 * ============================================================================
 */

process.exitCode = 0;
console.log('This checker was evaluated and rejected. See the header comment.');
console.log('Run the real build instead:');
console.log('');
console.log('  docker run --rm -v "$PWD:/src" -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet build');
