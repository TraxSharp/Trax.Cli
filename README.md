# Trax.Cli

[![Build](https://github.com/TraxSharp/Trax.Cli/actions/workflows/nuget_release.yml/badge.svg?branch=main)](https://github.com/TraxSharp/Trax.Cli/actions/workflows/nuget_release.yml?query=branch%3Amain)
[![NuGet](https://img.shields.io/nuget/v/Trax.Cli)](https://www.nuget.org/packages/Trax.Cli)
[![codecov](https://codecov.io/gh/TraxSharp/Trax.Cli/branch/main/graph/badge.svg)](https://codecov.io/gh/TraxSharp/Trax.Cli)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/TraxSharp/Trax.Cli/blob/main/LICENSE)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs/reference/cli)

> Part of [Trax](https://github.com/TraxSharp): business logic you can call, schedule, or serve as an API, with every
> run recorded in your Postgres. [Docs](https://traxsharp.net/docs) · [Getting started](https://traxsharp.net/docs/getting-started) · [All repos](https://github.com/TraxSharp)

`Trax.Cli` is the `trax` .NET tool. `trax generate` scaffolds a hub and a library of Trax trains from an OpenAPI or
GraphQL schema, and `trax machine` generates state-machine code. The hub comes from the `trax-hub` template in
[Trax.Samples](https://github.com/TraxSharp/Trax.Samples); the trains are typed pipelines of small steps (junctions)
whose bodies you fill in.

## Install

`trax generate` runs `dotnet new trax-hub`, so install the templates first:

```bash
dotnet new install Trax.Samples.Templates
```

Then install the tool (it needs the .NET 10 SDK), either as a local tool pinned in the repository's manifest or globally:

```bash
dotnet new tool-manifest            # once per repository
dotnet tool install Trax.Cli        # run it as: dotnet trax

dotnet tool install --global Trax.Cli   # run it as: trax
```

## Generate trains from a schema

```bash
trax generate --schema ./schema.graphql --output ./Payments --name Payments
trax generate --schema ./payments-api.yaml --output ./Payments --name Payments
```

| Option | Required | What it does |
|---|---|---|
| `--schema` | yes | The schema file: `.graphql` or `.gql` SDL, or an OpenAPI `.json`, `.yaml` or `.yml` |
| `--output` | yes | The directory to create |
| `--name` | yes | The project name, used for namespaces and the csproj names |
| `--type` | no | `graphql` or `openapi`, when the extension does not say |
| `--force` | no | Replace an existing output directory once generation has succeeded. Refused for the current directory, its parents, and any directory holding a git repository |

OpenAPI documents must be OpenAPI 3.0. OpenAPI 3.1 documents are not read yet.

GraphQL query fields and OpenAPI `GET` operations become `[TraxQuery]` trains. Mutation fields and `POST`, `PUT`,
`PATCH` and `DELETE` operations become `[TraxMutation]` trains. Operations are grouped by noun, so `createPlayer` and
`getPlayer` both land under `Players/`:

```
Payments/
├── Payments.Hub/            # dotnet new trax-hub, with a ProjectReference to the trains library
│   └── Program.cs           # AddMediator also scans the trains assembly
└── Payments.Trains/
    ├── ManifestNames.cs     # a const string per operation, for scheduling
    ├── Models/              # shared types and enums from the schema
    └── Trains/Players/CreatePlayer/
        ├── ICreatePlayerTrain.cs
        ├── CreatePlayerTrain.cs     # carries [TraxMutation(...)] and [TraxAllowAnonymous]
        ├── CreatePlayerInput.cs
        └── Junctions/CreatePlayerJunction.cs
```

The attributes go on the train class, not the interface. Each train is marked `[TraxAllowAnonymous]` so the scaffold
runs as generated; replace it with `[TraxAuthorize(...)]` once authentication is wired up. Each junction throws
`NotImplementedException` under a `TODO` comment, and for OpenAPI sources the comment names the original method and path.

## After generating

```bash
cd Payments/Payments.Hub
dotnet restore
# fill in the junctions in Payments.Trains (search for TODO)
dotnet run
```

The hub serves GraphQL at `http://localhost:5000/trax/graphql` and mounts the Trax dashboard in Development. Current
Trax.Dashboard releases refuse to start a host that has not said who may use the dashboard, so the hub starts once its
dashboard authorization is configured: see [dashboard options](https://traxsharp.net/docs/sdk-reference/dashboard-api/dashboard-options).

## State machines

`trax machine` scaffolds a machine, then exports its IR, TypeScript twin and differential corpus from the compiled
assembly. `generate` and `check` need Node.js 22 or later on `PATH` for the twin and corpus.

```bash
trax machine new checkout --output ./Machines --namespace MyApp.Machines --with-effect

trax machine generate --assembly ./bin/MyApp.dll \
  --ir-out ./machines/checkout --twin-out ./web/src/app/checkout --corpus-out ./machines/checkout \
  --engine-src ./vendor/state-machine/src

# same options; exits 1 if a committed artifact is stale, for CI
trax machine check --assembly ./bin/MyApp.dll ...
```

`trax machine <command> --help` lists every option.

## Where this fits

Trax is split into layers, one repo each. Take the ones you need; the trains you wrote do not change. **You are here: Trax.Cli.**

| Repo | What it adds |
|---|---|
| [Trax.Core](https://github.com/TraxSharp/Trax.Core) | Trains, junctions and the chain, with no database and no DI container |
| [Trax.Effect](https://github.com/TraxSharp/Trax.Effect) | A recorded run for every execution (Postgres, SQLite or in memory), DI, effect providers, the state-machine engine |
| [Trax.Mediator](https://github.com/TraxSharp/Trax.Mediator) | The train bus: run a train by handing over its input, with every chain checked at startup |
| [Trax.Scheduler](https://github.com/TraxSharp/Trax.Scheduler) | Cron and interval schedules, retries, dead letters, and workers on other machines or in Lambda |
| [Trax.Api](https://github.com/TraxSharp/Trax.Api) | GraphQL generated from your trains, with authentication, audit and typed clients |
| [Trax.Dashboard](https://github.com/TraxSharp/Trax.Dashboard) | A Blazor Server UI for runs, schedules and dead letters, mounted in your app |
| **[Trax.Cli](https://github.com/TraxSharp/Trax.Cli)** | **The `trax` tool: scaffold a hub and trains from an OpenAPI or GraphQL schema, and state-machine codegen** |
| [Trax.Samples](https://github.com/TraxSharp/Trax.Samples) | Complete sample apps, and the `trax-api`, `trax-scheduler` and `trax-hub` templates |

Docs live in [Trax.Docs](https://github.com/TraxSharp/Trax.Docs) and are published at [traxsharp.net/docs](https://traxsharp.net/docs).

## Documentation

- [CLI reference](https://traxsharp.net/docs/reference/cli)
- [Project templates](https://traxsharp.net/docs/reference/templates)
- [State machines](https://traxsharp.net/docs/statemachine) and the [codegen pipeline](https://traxsharp.net/docs/statemachine/codegen-pipeline)
- [Dashboard](https://traxsharp.net/docs/dashboard)

## Contributing

Read [AGENTS.md](https://github.com/TraxSharp/Trax.Cli/blob/main/AGENTS.md) before changing code. Report vulnerabilities
privately as described in [SECURITY.md](https://github.com/TraxSharp/Trax.Cli/blob/main/SECURITY.md).

## License

MIT. There is no commercial edition, and there will not be one.

Trax is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
