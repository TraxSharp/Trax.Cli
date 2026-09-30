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

```bash
dotnet new install Trax.Samples.Templates   # trax generate builds on the trax-hub template
dotnet tool install --global Trax.Cli       # needs the .NET 10 SDK
```

## Example

```bash
trax generate --schema ./schema.graphql --output ./Payments --name Payments
cd Payments/Payments.Hub && dotnet run

trax machine new checkout --output ./Machines --namespace MyApp.Machines
```

`generate` reads GraphQL SDL or an OpenAPI 3.0 document and writes a hub plus one train per operation, each junction
throwing `NotImplementedException` under a `TODO` for you to fill in. `trax <command> --help` lists every option.

## License

MIT. There is no commercial edition, and there will not be one.

Trax is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
