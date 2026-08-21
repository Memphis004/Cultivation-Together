# Workspace layout

Two separate, independently-run workspaces, plus one folder that isn't a
project at all - just the source both of them need a copy of.

```
Shared/          canonical source - not a buildable project, just files
UnityProject/    open this folder directly in Unity Hub
McpBridge/       a .NET console app - build/run with `dotnet`, not Unity
sync-shared.sh   copies Shared/*.cs into both of the above
```

## Why Shared/ isn't a real package (yet)

`GameMessages.cs` and `SectEconomyState.cs` are used by both the Unity game
and the external MCP bridge process, but Unity doesn't consume NuGet
packages and the schema is still changing every session - setting up a
proper shared class library + compiled plugin DLL right now would mean
rebuilding and re-copying a DLL every time a field changes. Instead,
`sync-shared.sh` just copies the `.cs` files into both projects' `Shared/`
folders. **Edit the files in the top-level `Shared/` folder, not the copies
inside `UnityProject/` or `McpBridge/`** - those get overwritten next sync.

```
./sync-shared.sh
```

Revisit this once the schema stabilizes - a real shared `netstandard2.1`
class library, project-referenced by `McpBridge` and built to a plugin DLL
for `UnityProject/Assets/Plugins/`, is the correct long-term setup.

## UnityProject/

Open this folder in Unity Hub as-is. On first open, Unity will try to
resolve the package manifest and fail on the missing dependency versions -
that's expected, see below.

**Packages/manifest.json** has the OpenUPM scoped registry wired up, but
the actual VContainer/MessagePipe dependency entries are left out on
purpose (see McpBridge.csproj note below for why - same reasoning). Add
them with [openupm-cli](https://openupm.com/docs/getting-started-cli.html)
from the project root so you get real, currently-published versions instead
of numbers I guessed:

```
openupm add jp.hadashikick.vcontainer
openupm add com.cysharp.messagepipe
openupm add com.cysharp.messagepipe.vcontainer
openupm add com.cysharp.messagepipe.interprocess
openupm add com.cysharp.unitask
```

**What's actually implemented**: `GameLifetimeScope` wires the DI graph
end to end (MessagePipe bus + TCP interprocess + four subsystems +
`SectStateQueryHandler`). `TimeSystem` has real publish calls.
`DiscipleSystem`, `ResourceCraftingSystem`, `BuildingSystem` are stubs with
no gameplay logic yet - only there so the DI graph resolves.
`SectStateProvider` returns `MockSectData.Create()` - swap this for real
aggregation once the subsystems above hold live state.

**Known compile blocker**: `SectStateQueryHandler.InvokeAsync` calls
`state.ToByteArray()` on `SectEconomyState`. That method doesn't exist yet -
`SectEconomyState.cs` is still the plain hand-written mirror of
`economy.proto`, not protoc-generated output. Either run `economy.proto`
through `protoc`/`Grpc.Tools` to generate a real `SectEconomyState` with
protobuf serialization, or write a temporary `ToByteArray()`/`Parse()` pair
by hand (e.g. backed by `System.Text.Json` or MessagePack) to unblock
compilation before that's done.

## McpBridge/

A plain .NET console app, not part of the Unity project. Build and run it
separately:

```
cd McpBridge
dotnet restore
dotnet run
```

**Package versions are pinned** (`MessagePipe`/`MessagePipe.Interprocess`
1.8.2, `ModelContextProtocol` 2.2.0, `Microsoft.Extensions.Hosting` 10.0.11)
- resolved via `dotnet add package <name>`, not guessed.

**Before this runs against real data**: `SectQueryTools.GetSectState`
returns the raw snapshot bytes as base64 - it needs a decode step (protobuf
→ JSON) once `economy.proto` codegen exists, so the AI GM client gets
readable state instead of an opaque blob. `SectActionTools.ExecuteDecision`
is a stub (`throw new NotImplementedException()`) - the write path back to
Unity isn't wired up yet, only the read path is.

**Connecting the two**: both sides currently assume Unity hosts the TCP
endpoint at `127.0.0.1:3215` and the bridge connects as a client (see
`GameLifetimeScope`'s `interprocessHost`/`interprocessPort` fields and the
matching `AddTcpInterprocess("127.0.0.1", 3215)` call in `Program.cs`).
Start the Unity game first, then run the bridge.

## Order of operations to get something running end to end

1. `./sync-shared.sh`
2. Fix the `ToByteArray()` compile blocker (hand-written stub is fastest for now)
3. Open `UnityProject/` in Unity, resolve packages via openupm-cli, press Play
4. `cd McpBridge && dotnet add package MessagePipe && dotnet add package MessagePipe.Interprocess && dotnet add package ModelContextProtocol && dotnet add package Microsoft.Extensions.Hosting && dotnet run`
5. Confirm the bridge connects (no TCP connection errors in its console) - `get_sect_state` should return the mock data's base64 blob
