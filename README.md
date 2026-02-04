# DWX ZeroMQ gRPC Connector

A gRPC server to connect with the [DWX ZeroMQ Connector EA](https://github.com/darwinex/dwx-zeromq-connector) using ZeroMQ.

## Requirements

- .Net 8.0
- Visual Studio C++ Redistributable

## Running the server

If you want to use a mock ZeroMQ client, in the `DWX_ZMQ_gRPC_Connector` run:

```bash
dotnet run
```

If you want to connect to the MetaTrader 4 terminal EA, ensure it and the DWX EA is running and execute:

```bash
dotnet run --environment Production
```

## Example client

Ensure the server is running beforehand, and run

```bash
dotnet run
```

in the `MarketData_gRPC_Client` folder to start the example gRPC client.
